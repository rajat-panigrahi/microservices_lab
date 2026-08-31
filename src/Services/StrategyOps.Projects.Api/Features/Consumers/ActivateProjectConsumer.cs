using MassTransit;
using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Messaging;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Contracts.V1.Projects;
using StrategyOps.Contracts.V1.Sagas;
using StrategyOps.Projects.Api.Domain;
using StrategyOps.Projects.Api.Infrastructure;

namespace StrategyOps.Projects.Api.Features.Consumers;

/// <summary>
/// The saga's happy-path outcome: every leg confirmed, so the project goes Active.
/// </summary>
/// <remarks>
/// The saga does not touch the Project aggregate itself. It sends a command and this
/// consumer applies it, so the aggregate stays the only thing that decides whether the
/// transition is legal - the state machine coordinates, the aggregate still enforces.
/// </remarks>
public sealed class ActivateProjectConsumer(
    ProjectsDbContext db,
    IInboxStore inbox,
    IOutboxWriter outbox,
    IClock clock,
    ILogger<ActivateProjectConsumer> logger)
    : IdempotentConsumer<ProjectsDbContext, ActivateProject>(db, inbox, logger)
{
    protected override async Task ConsumeOnceAsync(ConsumeContext<ActivateProject> context)
    {
        var project = await Db.Projects
            .FirstOrDefaultAsync(p => p.Id == context.Message.ProjectId, context.CancellationToken);

        if (project is null)
        {
            Logger.LogWarning("Cannot activate unknown project {ProjectId}", context.Message.ProjectId);
            return;
        }

        if (project.Stage != ProjectStage.Initiating)
        {
            Logger.LogInformation(
                "Project {ProjectCode} is already {Stage}; ignoring activation",
                project.Code,
                project.Stage);
            return;
        }

        project.CompleteInitiation(clock.UtcNow);

        outbox.Enqueue(new ProjectActivated
        {
            ProjectId = project.Id,
            Code = project.Code,
            CorrelationId = context.Message.CorrelationId
        });

        Logger.LogInformation("Project {ProjectCode} activated: all three legs provisioned", project.Code);
    }
}
