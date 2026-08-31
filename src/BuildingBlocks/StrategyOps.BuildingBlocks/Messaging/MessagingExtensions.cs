using System.Reflection;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddMassTransit(bus =>
        {
            bus.AddConsumers(consumerAssembly);
            configureBus?.Invoke(bus);

            // Queue names come from the consumer name, kebab-cased: RiskEscalatedConsumer
            // becomes "risk-escalated". Readable in the RabbitMQ management UI, which matters
            // the first time you have to debug why a message is sitting somewhere.
            bus.SetKebabCaseEndpointNameFormatter();

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

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
