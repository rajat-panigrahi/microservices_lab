using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StrategyOps.BuildingBlocks.Chaos;

/// <summary>
/// A switch that makes a service fail on purpose.
/// </summary>
/// <remarks>
/// Resilience code is the least-tested code in most systems, because the conditions it exists
/// for are hard to reproduce. You cannot see a circuit breaker work by reading it - you have
/// to watch it open. This makes that a one-line curl instead of an outage.
///
/// It is registered only outside Production, and it is the honest small-scale version of what
/// chaos engineering does at scale.
/// </remarks>
public sealed class ChaosState
{
    private int _failuresRemaining;
    private volatile bool _failing;

    public bool IsFailing => _failing || Volatile.Read(ref _failuresRemaining) > 0;

    public int Latency { get; private set; }

    /// <summary>Fail every request until healed, or only the next <paramref name="count"/>.</summary>
    public void Fail(int? count = null, int latencyMs = 0)
    {
        Latency = latencyMs;

        if (count is null)
        {
            _failing = true;
            return;
        }

        Interlocked.Exchange(ref _failuresRemaining, count.Value);
    }

    public void Heal()
    {
        _failing = false;
        Latency = 0;
        Interlocked.Exchange(ref _failuresRemaining, 0);
    }

    /// <summary>Consumes one scheduled failure, if any remain.</summary>
    public bool ShouldFailNow()
    {
        if (_failing)
        {
            return true;
        }

        var remaining = Volatile.Read(ref _failuresRemaining);

        while (remaining > 0)
        {
            var updated = Interlocked.CompareExchange(ref _failuresRemaining, remaining - 1, remaining);

            if (updated == remaining)
            {
                return true;
            }

            remaining = updated;
        }

        return false;
    }
}

public static class ChaosExtensions
{
    public static IServiceCollection AddChaos(this IServiceCollection services)
    {
        services.AddSingleton<ChaosState>();
        return services;
    }

    /// <summary>
    /// Adds the chaos switch and the middleware that honours it. No-ops in Production, so
    /// there is no way to leave the switch deployed by accident.
    /// </summary>
    public static WebApplication UseChaos(this WebApplication app)
    {
        if (app.Environment.IsProduction())
        {
            return app;
        }

        var chaos = app.Services.GetRequiredService<ChaosState>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Chaos");

        app.Use(async (context, next) =>
        {
            // Health and the chaos switch itself stay honest, otherwise you cannot turn the
            // failure back off or see that the process is actually alive.
            if (context.Request.Path.StartsWithSegments("/chaos") || context.Request.Path.StartsWithSegments("/health"))
            {
                await next();
                return;
            }

            if (chaos.Latency > 0)
            {
                await Task.Delay(chaos.Latency, context.RequestAborted);
            }

            if (chaos.ShouldFailNow())
            {
                // 503 rather than 500: it is what an overloaded or unavailable dependency
                // actually returns, and it is what the retry and breaker policies are tuned for.
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new { error = "chaos", detail = "This service was told to fail." });
                return;
            }

            await next();
        });

        var group = app.MapGroup("/chaos").WithTags("Chaos").AllowAnonymous();

        group.MapPost("/fail", (ChaosState state, int? count, int? latencyMs) =>
            {
                state.Fail(count, latencyMs ?? 0);
                logger.LogWarning("Chaos enabled: count={Count} latencyMs={Latency}", count?.ToString() ?? "unlimited", latencyMs ?? 0);
                return TypedOk(new { failing = true, count, latencyMs });
            })
            .WithName("ChaosFail")
            .WithSummary("Make this service return 503 - all requests, or the next N");

        group.MapPost("/heal", (ChaosState state) =>
            {
                state.Heal();
                logger.LogInformation("Chaos disabled");
                return TypedOk(new { failing = false });
            })
            .WithName("ChaosHeal")
            .WithSummary("Stop failing");

        group.MapGet("/status", (ChaosState state) => TypedOk(new { failing = state.IsFailing, latencyMs = state.Latency }))
            .WithName("ChaosStatus")
            .WithSummary("Is this service currently failing on purpose?");

        return app;
    }

    // StrategyOps.BuildingBlocks.Results shadows Microsoft.AspNetCore.Http.Results inside
    // this assembly, so the framework helper is reached through a small local alias.
    private static IResult TypedOk<T>(T value) => Microsoft.AspNetCore.Http.Results.Ok(value);
}
