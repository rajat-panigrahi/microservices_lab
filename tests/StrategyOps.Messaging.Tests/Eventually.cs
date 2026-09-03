using Shouldly;

namespace StrategyOps.Messaging.Tests;

/// <summary>
/// Polls until a condition holds, instead of sleeping for a guessed interval.
/// </summary>
/// <remarks>
/// Asynchronous systems are asynchronous in tests too. A fixed delay is either too short
/// (flaky under load, which is exactly when CI runs) or too long (slow suite). Polling with a
/// deadline is both faster in the normal case and stable in the slow one.
/// </remarks>
public static class Eventually
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    public static async Task<T> SatisfiesAsync<T>(
        Func<Task<T>> probe,
        Func<T, bool> condition,
        string because,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        T last = default!;

        while (DateTime.UtcNow < deadline)
        {
            last = await probe();

            if (condition(last))
            {
                return last;
            }

            await Task.Delay(PollInterval);
        }

        throw new ShouldAssertException($"Timed out after {(timeout ?? DefaultTimeout).TotalSeconds:N0}s waiting until {because}. Last value: {last}");
    }

    public static Task IsTrueAsync(Func<Task<bool>> probe, string because, TimeSpan? timeout = null) =>
        SatisfiesAsync(probe, value => value, because, timeout);
}
