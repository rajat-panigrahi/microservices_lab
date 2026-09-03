using MassTransit;
using MassTransit.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StrategyOps.BuildingBlocks.Inbox;
using StrategyOps.BuildingBlocks.Time;
using StrategyOps.Reporting.Api.Domain;
using StrategyOps.Reporting.Api.Hubs;
using StrategyOps.Reporting.Api.Infrastructure;

namespace StrategyOps.Messaging.Tests.Reporting;

/// <summary>
/// Hosts the read-model projections with a recording notifier in place of SignalR, so a test
/// can assert what the dashboard would have been told without opening a websocket.
/// </summary>
public sealed class ReportingHost : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    private ReportingHost(SqliteConnection connection, ServiceProvider provider, RecordingNotifier notifier)
    {
        _connection = connection;
        _provider = provider;
        Notifier = notifier;
        Harness = provider.GetRequiredService<ITestHarness>();
    }

    public ITestHarness Harness { get; }

    public RecordingNotifier Notifier { get; }

    public static async Task<ReportingHost> StartAsync(Action<IBusRegistrationConfigurator> addProjections)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var notifier = new RecordingNotifier();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<ReportingDbContext>(options => options.UseSqlite(connection));
        services.AddScoped<IInboxDbContext>(sp => sp.GetRequiredService<ReportingDbContext>());
        services.AddScoped<IInboxStore, InboxStore>();
        services.AddSingleton<IClock>(new FixedClock(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero)));
        services.AddSingleton<IPortfolioNotifier>(notifier);
        services.AddMassTransitTestHarness(addProjections);

        var provider = services.BuildServiceProvider(validateScopes: true);

        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
        }

        var host = new ReportingHost(connection, provider, notifier);
        await host.Harness.Start();
        return host;
    }

    public Task PublishAsync<T>(T message, Guid? messageId = null) where T : class =>
        Harness.Bus.Publish(message, context => context.MessageId = messageId ?? Guid.NewGuid());

    public async Task<PortfolioScorecard?> ReadAsync(Guid projectId)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ReportingDbContext>()
            .Scorecards.AsNoTracking().FirstOrDefaultAsync(s => s.ProjectId == projectId);
    }

    public async ValueTask DisposeAsync()
    {
        await Harness.Stop();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public sealed class RecordingNotifier : IPortfolioNotifier
    {
        private readonly List<PortfolioScorecard> _pushes = [];

        public IReadOnlyList<PortfolioScorecard> Pushes
        {
            get
            {
                lock (_pushes)
                {
                    return _pushes.ToList();
                }
            }
        }

        public Task ScorecardChangedAsync(PortfolioScorecard scorecard, CancellationToken cancellationToken)
        {
            lock (_pushes)
            {
                _pushes.Add(scorecard);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
