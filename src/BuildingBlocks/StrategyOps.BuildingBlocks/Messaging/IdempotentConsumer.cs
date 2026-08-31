using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StrategyOps.BuildingBlocks.Inbox;

namespace StrategyOps.BuildingBlocks.Messaging;

/// <summary>
/// Base class for every consumer in the system. Handles the message exactly once per
/// consumer, no matter how many times the broker delivers it.
/// </summary>
/// <remarks>
/// <para>
/// Redelivery is not an edge case - it is the normal cost of at-least-once delivery. The
/// outbox publisher can crash after the broker accepted a message but before the row was
/// marked processed; RabbitMQ redelivers anything not acked; a retry policy replays on
/// transient failure. Any of those means a consumer sees the same message twice.
/// </para>
/// <para>
/// The important detail is the ordering below: the inbox claim is <em>staged</em>, the
/// business work runs, and then ONE SaveChanges commits both. Saving the claim separately
/// would open a window where the service has recorded the message as handled but crashed
/// before doing the work - which loses it permanently, the failure mode idempotency was
/// supposed to prevent.
/// </para>
/// </remarks>
public abstract class IdempotentConsumer<TDbContext, TMessage>(
    TDbContext db,
    IInboxStore inbox,
    ILogger logger) : IConsumer<TMessage>
    where TDbContext : DbContext, IInboxDbContext
    where TMessage : class
{
    protected TDbContext Db { get; } = db;

    protected ILogger Logger { get; } = logger;

    public async Task Consume(ConsumeContext<TMessage> context)
    {
        var messageId = context.MessageId
            ?? throw new InvalidOperationException(
                $"{typeof(TMessage).Name} arrived without a MessageId; it cannot be deduplicated.");

        var consumerName = GetType().Name;

        if (!await inbox.TryClaimAsync(messageId, consumerName, context.CancellationToken))
        {
            Logger.LogInformation(
                "Skipping {MessageType} {MessageId}: {Consumer} has already handled it",
                typeof(TMessage).Name,
                messageId,
                consumerName);
            return;
        }

        await ConsumeOnceAsync(context);

        // The inbox claim and everything ConsumeOnceAsync changed commit together.
        await Db.SaveChangesAsync(context.CancellationToken);
    }

    /// <summary>
    /// Do the work and stage any changes. Do NOT call SaveChanges - the base class commits,
    /// so that the work and the "I handled this" record are one transaction.
    /// </summary>
    protected abstract Task ConsumeOnceAsync(ConsumeContext<TMessage> context);
}
