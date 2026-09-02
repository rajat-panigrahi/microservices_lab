using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace StrategyOps.BuildingBlocks.Observability;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>OTLP collector endpoint. Empty means traces go to the console only.</summary>
    public string OtlpEndpoint { get; set; } = string.Empty;

    /// <summary>Seq endpoint for structured logs. Empty means console only.</summary>
    public string SeqUrl { get; set; } = string.Empty;

    /// <summary>Write traces to the console. Noisy, but the only zero-infrastructure option.</summary>
    public bool ConsoleTraces { get; set; }
}

public static class ObservabilityExtensions
{
    /// <summary>
    /// Structured logging, distributed tracing and metrics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three are not interchangeable, and knowing which answers which question is the
    /// substance of "how do you monitor microservices?":
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Logs</b> answer "what happened in this one request?" - and only if they are
    ///   structured and correlated. Nine services' worth of unstructured text is unsearchable.</item>
    ///   <item><b>Traces</b> answer "where did the time go, and which hop failed?" A trace is
    ///   the shape of one request across every service; no amount of log reading reconstructs
    ///   it reliably.</item>
    ///   <item><b>Metrics</b> answer "is this normal?" - rates, latency percentiles, error
    ///   ratios. They are what you alert on, because you cannot alert on a log line without
    ///   drowning.</item>
    /// </list>
    /// <para>
    /// OpenTelemetry rather than a vendor SDK, because the instrumentation should not have to
    /// change when the backend does. The same traces go to Jaeger, Tempo, Honeycomb or
    /// Application Insights by changing an endpoint.
    /// </para>
    /// </remarks>
    public static WebApplicationBuilder AddStrategyOpsObservability(this WebApplicationBuilder builder, string serviceName)
    {
        var options = builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
                      ?? new ObservabilityOptions();

        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)

            // The template puts CorrelationId on every line, so one grep across all nine
            // services returns a single request's whole story, in order.
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {CorrelationId} {Message:lj}{NewLine}{Exception}");

        if (!string.IsNullOrWhiteSpace(options.SeqUrl))
        {
            loggerConfiguration.WriteTo.Seq(options.SeqUrl);
        }

        Log.Logger = loggerConfiguration.CreateLogger();
        builder.Host.UseSerilog();

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(instrumentation =>
                        // Health probes fire constantly and tell you nothing; tracing them
                        // buries the requests that matter in noise you pay to store.
                        instrumentation.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation()

                    // MassTransit emits its own activities, so a trace follows a request
                    // through the broker and into a consumer in another process.
                    .AddSource("MassTransit");

                if (options.ConsoleTraces)
                {
                    tracing.AddConsoleExporter();
                }

                if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
                {
                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.OtlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()

                    // The .NET runtime counters: GC, thread pool, exceptions. Thread-pool
                    // starvation is a classic microservices failure and is invisible without them.
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.OtlpEndpoint));
                }
            });

        return builder;
    }
}
