using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using StrategyOps.Contracts.V1.Issues;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Contracts.V1.Sagas;
using StrategyOps.Contracts.V1.Risks;
using StrategyOps.Risk.Api.Domain;
using StrategyOps.Risk.Api.Features.Consumers;
using StrategyOps.Risk.Api.Infrastructure;
using RiskEntry = StrategyOps.Risk.Api.Domain.Risk;

namespace StrategyOps.Messaging.Tests.Choreography;

public class RiskServiceConsumerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private static ProvisionRiskRegister AnInitiation(Guid projectId) => new()
    {
        ProjectId = projectId,
        ProjectCode = "PRJ-0007",
        CorrelationId = "corr-abc"
    };

    [Fact]
    public async Task InitiatingAProject_ProvisionsARegisterAndConfirmsBackToTheSaga()
    {
        await using var host = await ConsumerHost<RiskDbContext>.StartAsync(
            bus => bus.AddConsumer<ProvisionRiskRegisterConsumer>(), Now);

        var projectId = Guid.NewGuid();
        await host.PublishAsync(AnInitiation(projectId), Guid.NewGuid());
        await host.Harness.Consumed.Any<ProvisionRiskRegister>();

        var register = await host.QueryAsync(db => db.Registers.SingleOrDefaultAsync(r => r.ProjectId == projectId));
        register.ShouldNotBeNull();
        register.Status.ShouldBe(RiskRegisterStatus.Active);

        // The confirmation matters as much as the work: without it the saga waits forever.
        var confirmations = await host.QueryAsync(db => db.OutboxMessages
            .CountAsync(m => m.Type == typeof(RiskRegisterProvisioned).FullName));
        confirmations.ShouldBe(1);
    }

    [Fact]
    public async Task ARedeliveredInitiation_DoesNotCreateASecondRegister()
    {
        await using var host = await ConsumerHost<RiskDbContext>.StartAsync(
            bus => bus.AddConsumer<ProvisionRiskRegisterConsumer>(), Now);

        var projectId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await host.PublishAsync(AnInitiation(projectId), messageId);
        await host.Harness.Consumed.Any<ProvisionRiskRegister>();
        await host.PublishAsync(AnInitiation(projectId), messageId);

        await Task.Delay(200);

        var registers = await host.QueryAsync(db => db.Registers.CountAsync(r => r.ProjectId == projectId));
        registers.ShouldBe(1);
    }

    [Fact]
    public async Task AnInitiationWithADifferentMessageId_StillProvisionsOnlyOneRegisterAndReconfirms()
    {
        await using var host = await ConsumerHost<RiskDbContext>.StartAsync(
            bus => bus.AddConsumer<ProvisionRiskRegisterConsumer>(), Now);

        var projectId = Guid.NewGuid();

        await host.PublishAsync(AnInitiation(projectId), Guid.NewGuid());
        await host.Harness.Consumed.Any<ProvisionRiskRegister>();
        await host.PublishAsync(AnInitiation(projectId), Guid.NewGuid());

        await Task.Delay(200);

        var registers = await host.QueryAsync(db => db.Registers.CountAsync(r => r.ProjectId == projectId));
        registers.ShouldBe(1, "the unique index on ProjectId is the real guarantee");

        // It re-confirms rather than staying silent, so a saga that missed the first
        // confirmation can still make progress.
        var confirmations = await host.QueryAsync(db => db.OutboxMessages
            .CountAsync(m => m.Type == typeof(RiskRegisterProvisioned).FullName));
        confirmations.ShouldBe(2);
    }

    [Fact]
    public async Task AFailedInitiation_CompensatesByRemovingTheRegisterAndItsRisks()
    {
        await using var host = await ConsumerHost<RiskDbContext>.StartAsync(
            bus =>
            {
                bus.AddConsumer<ProvisionRiskRegisterConsumer>();
                bus.AddConsumer<WithdrawRiskRegisterConsumer>();
            },
            Now);

        var projectId = Guid.NewGuid();
        await host.PublishAsync(AnInitiation(projectId), Guid.NewGuid());
        await host.Harness.Consumed.Any<ProvisionRiskRegister>();

        var registerId = await host.QueryAsync(db => db.Registers
            .Where(r => r.ProjectId == projectId)
            .Select(r => r.Id)
            .SingleAsync());

        await host.SeedAsync(async db =>
        {
            db.Risks.Add(RiskEntry.Raise(registerId, "A risk raised before the failure", "Supplier", 3, 3, "R. Owner", Now));
            await Task.CompletedTask;
        });

        await host.PublishAsync(
            new WithdrawRiskRegister { ProjectId = projectId, CorrelationId = "corr-abc" },
            Guid.NewGuid());

        await host.Harness.Consumed.Any<WithdrawRiskRegister>();

        (await host.QueryAsync(db => db.Registers.CountAsync(r => r.ProjectId == projectId))).ShouldBe(0);
        (await host.QueryAsync(db => db.Risks.CountAsync(r => r.RegisterId == registerId))).ShouldBe(0);

        var withdrawn = await host.QueryAsync(db => db.OutboxMessages
            .CountAsync(m => m.Type == typeof(RiskRegisterWithdrawn).FullName));
        withdrawn.ShouldBe(1);
    }

    [Fact]
    public async Task CompensationWithNothingToUndo_StillConfirmsBackToTheSaga()
    {
        await using var host = await ConsumerHost<RiskDbContext>.StartAsync(
            bus => bus.AddConsumer<WithdrawRiskRegisterConsumer>(), Now);

        await host.PublishAsync(
            new WithdrawRiskRegister { ProjectId = Guid.NewGuid() },
            Guid.NewGuid());

        (await host.Harness.Consumed.Any<WithdrawRiskRegister>()).ShouldBeTrue();

        // Nothing was removed, but the saga is waiting on this leg's confirmation. A
        // consumer that answers only when it had work to do hangs the saga in exactly the
        // cases that are hardest to reproduce.
        await Eventually.IsTrueAsync(
            async () => await host.QueryAsync(db => db.OutboxMessages
                .AnyAsync(m => m.Type == typeof(RiskRegisterWithdrawn).FullName)),
            "the withdrawal is confirmed even though there was nothing to withdraw");
    }

    [Fact]
    public async Task ResolvingTheIssue_ClosesTheRiskItCameFrom()
    {
        await using var host = await ConsumerHost<RiskDbContext>.StartAsync(
            bus => bus.AddConsumer<CloseRiskOnIssueResolvedConsumer>(), Now);

        var projectId = Guid.NewGuid();
        var register = RiskRegister.Provision(projectId, "PRJ-0007", Now);
        var risk = RiskEntry.Raise(register.Id, "Supplier deadline", "Supplier", 5, 5, "R. Owner", Now);
        risk.Escalate("Supplier confirmed they will miss the date", Now);

        await host.SeedAsync(async db =>
        {
            db.Registers.Add(register);
            db.Risks.Add(risk);
            await Task.CompletedTask;
        });

        await host.PublishAsync(
            new IssueResolved { IssueId = Guid.NewGuid(), ProjectId = projectId, OriginRiskId = risk.Id },
            Guid.NewGuid());

        await host.Harness.Consumed.Any<IssueResolved>();

        var closed = await host.QueryAsync(db => db.Risks.SingleAsync(r => r.Id == risk.Id));
        closed.Status.ShouldBe(RiskStatus.Closed);
    }

    [Fact]
    public async Task ResolvingAnIssueThatCameFromNoRisk_ChangesNothing()
    {
        await using var host = await ConsumerHost<RiskDbContext>.StartAsync(
            bus => bus.AddConsumer<CloseRiskOnIssueResolvedConsumer>(), Now);

        await host.PublishAsync(
            new IssueResolved { IssueId = Guid.NewGuid(), ProjectId = Guid.NewGuid(), OriginRiskId = null },
            Guid.NewGuid());

        (await host.Harness.Consumed.Any<IssueResolved>()).ShouldBeTrue();
        (await host.QueryAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task ClosingAProject_ClosesItsRiskRegister()
    {
        await using var host = await ConsumerHost<RiskDbContext>.StartAsync(
            bus => bus.AddConsumer<CloseRiskRegisterConsumer>(), Now);

        var projectId = Guid.NewGuid();
        await host.SeedAsync(async db =>
        {
            db.Registers.Add(RiskRegister.Provision(projectId, "PRJ-0007", Now));
            await Task.CompletedTask;
        });

        await host.PublishAsync(new ProjectClosed { ProjectId = projectId, Code = "PRJ-0007" }, Guid.NewGuid());
        await host.Harness.Consumed.Any<ProjectClosed>();

        var register = await host.QueryAsync(db => db.Registers.SingleAsync(r => r.ProjectId == projectId));
        register.Status.ShouldBe(RiskRegisterStatus.Closed);
    }
}
