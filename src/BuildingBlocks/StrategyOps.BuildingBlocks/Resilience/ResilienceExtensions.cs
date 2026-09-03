using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Timeout;

namespace StrategyOps.BuildingBlocks.Resilience;

public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    /// <summary>How long one attempt may take before it is abandoned.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 3;

    /// <summary>Ceiling on the whole operation, retries included.</summary>
    public int TotalTimeoutSeconds { get; set; } = 12;

    public int RetryCount { get; set; } = 3;

    /// <summary>Proportion of failures within the sampling window that opens the breaker.</summary>
    public double FailureRatio { get; set; } = 0.5;

    public int SamplingDurationSeconds { get; set; } = 20;

    /// <summary>Minimum calls in the window before the ratio is trusted at all.</summary>
    public int MinimumThroughput { get; set; } = 5;

    /// <summary>How long the breaker stays open before letting one probe through.</summary>
    public int BreakDurationSeconds { get; set; } = 15;
}

public static class ResilienceExtensions
{
    /// <summary>
    /// Adds the standard retry / circuit-breaker / timeout pipeline to an HTTP client.
    /// </summary>
    /// <remarks>
    /// <para><b>The order matters and it is the thing people get wrong.</b> The pipeline runs
    /// outermost-first:</para>
    /// <list type="number">
    ///   <item><b>Total timeout</b> - a ceiling on the whole operation. Without it, three
    ///   retries against a service taking three seconds each means the caller waits nine
    ///   seconds for a failure, and their own caller has probably given up already.</item>
    ///   <item><b>Retry</b> with exponential backoff <b>and jitter</b>. Jitter is not
    ///   decoration: without it, every caller that failed at the same moment retries at the
    ///   same moment, and the recovering service is knocked over by a synchronised
    ///   thundering herd.</item>
    ///   <item><b>Circuit breaker</b>, inside the retry so it sees every individual attempt.
    ///   Outside, it would only ever see one failure per operation and would take far too
    ///   long to trip.</item>
    ///   <item><b>Per-attempt timeout</b> - innermost, so a single hung request is abandoned
    ///   and retried rather than consuming the whole budget.</item>
    /// </list>
    ///
    /// <para><b>Why a breaker at all, when retries already handle failure?</b> Retries help
    /// with a <em>transient</em> fault and actively hurt with a <em>sustained</em> one: a
    /// struggling service gets three times the traffic exactly when it can least afford it,
    /// and callers pile up waiting on it until their own thread pools are exhausted. That
    /// cascade is how one slow service takes down a platform. The breaker's job is to fail
    /// fast instead - to stop asking, give the dependency room to recover, and keep the
    /// caller responsive. A retry is optimism; a breaker is knowing when to stop being
    /// optimistic.</para>
    ///
    /// <para>Retries also assume the operation is safe to repeat. Everything retried here is
    /// a GET; the write paths in this system go through the outbox and are made safe by the
    /// inbox instead.</para>
    /// </remarks>
    public static IHttpClientBuilder AddStrategyOpsResilience(this IHttpClientBuilder builder, ResilienceOptions options)
    {
        builder.AddResilienceHandler("strategyops", (pipeline, context) =>
        {
            pipeline.AddTimeout(TimeSpan.FromSeconds(options.TotalTimeoutSeconds));

            pipeline.AddRetry(new Polly.Retry.RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = options.RetryCount,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(200),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(response =>
                        response.StatusCode is System.Net.HttpStatusCode.RequestTimeout
                            or System.Net.HttpStatusCode.TooManyRequests
                            or System.Net.HttpStatusCode.BadGateway
                            or System.Net.HttpStatusCode.ServiceUnavailable
                            or System.Net.HttpStatusCode.GatewayTimeout
                            // 500 is retried; 4xx is NOT. A 400 or a 404 will fail
                            // identically every time, and retrying it just wastes the budget
                            // and multiplies load for nothing.
                            || response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
            });

            pipeline.AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = options.FailureRatio,
                SamplingDuration = TimeSpan.FromSeconds(options.SamplingDurationSeconds),
                MinimumThroughput = options.MinimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(options.BreakDurationSeconds),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(response => (int)response.StatusCode >= 500)
            });

            pipeline.AddTimeout(TimeSpan.FromSeconds(options.AttemptTimeoutSeconds));
        });

        return builder;
    }
}
