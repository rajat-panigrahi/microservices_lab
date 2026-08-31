using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Contracts.V1.Sagas;
using StrategyOps.Projects.Api.Domain;
using StrategyOps.Projects.Api.Infrastructure;

namespace StrategyOps.Projects.Api.Features.Consumers;

/// <summary>
/// The saga's failure outcome, arriving only after every successful leg has been compensated.
/// </summary>
/// <remarks>
/// The project lands in InitiationFailed with the reason recorded, rather than being silently
/// left in Initiating. That matters operationally: a project stuck mid-initiation with no
/// explanation is the single most confusing state a distributed workflow can leave behind,
/// and InitiationFailed is explicitly resubmittable once the underlying problem is fixed.
/// </remarks>
public sealed class FailProjectInitiationConsumer(
    ProjectsDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    ILogger<FailProjectInitiationConsumer> logger)
    : IdempotentConsumer<ProjectsDbContext, FailProjectInitiation>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<FailProjectInitiation> context)
    {
        var project = await Db.Projects
            .FirstOrDefaultAsync(p => p.Id == context.Message.ProjectId, context.CancellationToken);

        if (project is null || project.Stage != ProjectStage.Initiating)
        {
            return;
        }

        project.FailInitiation(context.Message.Reason);

        outbox.Enqueue(new ProjectInitiationFailed
        {
            ProjectId = project.Id,
            Code = project.Code,
            Reason = context.Message.Reason,
            CorrelationId = context.Message.CorrelationId
        });

        Logger.LogWarning(
            "Project {ProjectCode} initiation failed and was compensated: {Reason}",
            project.Code,
            context.Message.Reason);
    }
}
