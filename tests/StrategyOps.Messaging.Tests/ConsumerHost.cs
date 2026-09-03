using MassTransit;
using MassTransit.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StrategyOps.BuildingBlocks.Correlation;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.BuildingBlocks.Time;

namespace StrategyOps.Messaging.Tests;

/// <summary>
/// Hosts one service's consumers against MassTransit's in-memory harness and a private
/// SQLite database.
/// </summary>
/// <remarks>
/// The harness is a real MassTransit bus with an in-memory transport, so consumers, the
/// inbox filter, message ids and publish behaviour all run exactly as they do over RabbitMQ.
/// What it removes is the broker itself - which means these tests need no infrastructure and
/// run in milliseconds, while still testing the thing that actually breaks in production:
/// what happens when the same message arrives twice.
/// </remarks>
public sealed class ConsumerHost<TDbContext> : IAsyncDisposable
    where TDbContext : DbContext, IOutboxDbContext, IInboxDbContext
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    private ConsumerHost(SqliteConnection connection, ServiceProvider provider)
    {
        _connection = connection;
        _provider = provider;
        Harness = provider.GetRequiredService<ITestHarness>();
    }

    public ITestHarness Harness { get; }

    public static async Task<ConsumerHost<TDbContext>> StartAsync(
        Action<IBusRegistrationConfigurator> addConsumers,
        DateTimeOffset? now = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddHttpContextAccessor();
        services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
        services.AddSingleton<IClock>(new FixedClock(now ?? new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero)));

        services.AddDbContext<TDbContext>(options => options.UseSqlite(connection));
        services.AddScoped<IOutboxDbContext>(sp => sp.GetRequiredService<TDbContext>());
        services.AddScoped<IInboxDbContext>(sp => sp.GetRequiredService<TDbContext>());
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IInboxStore, InboxStore>();

        services.AddMassTransitTestHarness(addConsumers);

        var provider = services.BuildServiceProvider(validateScopes: true);

        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TDbContext>().Database.MigrateAsync();
        }

        var host = new ConsumerHost<TDbContext>(connection, provider);
        await host.Harness.Start();
        return host;
    }

    /// <summary>Publishes with an explicit message id, so a test can replay the same message.</summary>
    public Task PublishAsync<TMessage>(TMessage message, Guid messageId)
        where TMessage : class =>
        Harness.Bus.Publish(message, context => context.MessageId = messageId);

    public async Task<TResult> QueryAsync<TResult>(Func<TDbContext, Task<TResult>> query)
    {
        using var scope = _provider.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TDbContext>());
    }

    public async Task SeedAsync(Func<TDbContext, Task> seed)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await seed(db);
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Harness.Stop();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
