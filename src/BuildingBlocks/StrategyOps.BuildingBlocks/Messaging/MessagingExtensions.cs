using System.Reflection;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Outbox;

namespace StrategyOps.BuildingBlocks.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";

    public ushort Port { get; set; } = 5672;

    public string VirtualHost { get; set; } = "/";

    public string Username { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    /// <summary>
    /// Runs the bus on MassTransit's in-memory transport instead of RabbitMQ.
    /// </summary>
    /// <remarks>
    /// Used by the slice tests, which are about one service's HTTP surface and persistence
    /// and have no business needing a broker running to pass. Consumers, the outbox publisher
    /// and the inbox all still work - only the hop between processes disappears, and a slice
    /// test never makes that hop anyway.
    ///
    /// This is not a way to run the system: in-memory means in THIS process, so nothing
    /// actually reaches another service.
    /// </remarks>
    public bool UseInMemoryTransport { get; set; }
}

public static class MessagingExtensions
{
    /// <summary>
    /// Wires MassTransit over RabbitMQ, plus the inbox that makes consumers idempotent.
    /// </summary>
    /// <param name="consumerAssembly">
    /// The service's own assembly; consumers are found by scan, so adding a consumer needs no
    /// registration.
    /// </param>
    public static IServiceCollection AddStrategyOpsMessaging<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Assembly consumerAssembly,
        Action<IBusRegistrationConfigurator>? configureBus = null)
        where TDbContext : Microsoft.EntityFrameworkCore.DbContext, IInboxDbContext
    {
        var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();

        services.AddScoped<IInboxDbContext>(sp => sp.GetRequiredService<TDbContext>());
        services.AddScoped<IInboxStore, InboxStore>();
        services.AddScoped<IIntegrationEventPublisher, MassTransitIntegrationEventPublisher>();

        // Saga timeouts need a message scheduler. RabbitMQ can do this natively only with the
        // delayed-message-exchange plugin installed on the broker, which is not something a
        // reader can assume they have, so this uses Quartz with an in-memory store instead.
        // The trade-off is explicit: scheduled messages live in this process, so a restart
        // loses pending timeouts. Point Quartz at a shared database (or install the plugin
        // and use UseDelayedMessageScheduler) to make them durable.
        services.AddQuartz();
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        services.AddMassTransit(bus =>
        {
            bus.AddConsumers(consumerAssembly);
            bus.AddQuartzConsumers();
            bus.AddPublishMessageScheduler();
            configureBus?.Invoke(bus);

            // Queue names come from the consumer name, kebab-cased: RiskEscalatedConsumer
            // becomes "risk-escalated". Readable in the RabbitMQ management UI, which matters
            // the first time you have to debug why a message is sitting somewhere.
            bus.SetKebabCaseEndpointNameFormatter();

            if (options.UseInMemoryTransport)
            {
                bus.UsingInMemory((context, cfg) =>
                {
                    cfg.UseMessageRetry(retry => retry.Immediate(2));

                    // The saga schedules a timeout, and a state machine without a scheduler
                    // faults with PayloadNotFoundException the moment it tries. Easy to miss
                    // because it only shows up on the transport the tests use.
                    cfg.UsePublishMessageScheduler();
                    cfg.UseSendFilter(typeof(CorrelationSendFilter<>), context);
                    cfg.UseConsumeFilter(typeof(CorrelationConsumeFilter<>), context);

                    cfg.ConfigureEndpoints(context);
                });

                return;
            }

            bus.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(options.Host, options.Port, options.VirtualHost, host =>
                {
                    host.Username(options.Username);
                    host.Password(options.Password);
                });

                // Transient failures (a locked row, a brief network blip) are retried in
                // process. Anything that survives this goes to the _error queue rather than
                // being silently dropped - that queue is the first place to look when a
                // consumer "isn't firing".
                cfg.UseMessageRetry(retry => retry.Exponential(
                    retryLimit: 5,
                    minInterval: TimeSpan.FromMilliseconds(200),
                    maxInterval: TimeSpan.FromSeconds(10),
                    intervalDelta: TimeSpan.FromMilliseconds(500)));

                cfg.UsePublishMessageScheduler();

                // Carries the correlation id across the broker in both directions - the hop
                // where most correlation chains quietly end.
                cfg.UseSendFilter(typeof(CorrelationSendFilter<>), context);
                cfg.UseConsumeFilter(typeof(CorrelationConsumeFilter<>), context);

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
