using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StrategyOps.Projects.Api.Features.Sagas;
using StrategyOps.Projects.Api.Infrastructure;

namespace StrategyOps.Messaging.Tests.Saga;

/// <summary>
/// Hosts the ProjectInitiationSaga alone, with the participant services replaced by the test
/// publishing their confirmations by hand.
/// </summary>
/// <remarks>
/// That isolation is deliberate. These tests are about the coordinator's decisions - when it
/// activates, when it compensates, what it does with a late answer - and mixing in the real
/// KPI, Risk and Benefits consumers would make a failure ambiguous between "the saga decided
/// wrongly" and "a participant misbehaved". The participants have their own tests.
/// </remarks>
public sealed class SagaHost : IAsyncDisposable
{
    private readonly string _databasePath;
    private readonly ServiceProvider _provider;

    private SagaHost(string databasePath, ServiceProvider provider)
    {
        _databasePath = databasePath;
        _provider = provider;
        Harness = provider.GetRequiredService<ITestHarness>();
    }

    public ITestHarness Harness { get; }

    public static async Task<SagaHost> StartAsync()
    {
        // A file, not ":memory:". A shared in-memory SQLite connection is reused by every
        // scope, so MassTransit's saga repository - which wraps each saga operation in an
        // explicit transaction - ends up trying to nest transactions on one connection, which
        // SQLite forbids. A file-backed database gives each scope its own pooled connection,
        // exactly as the deployed services get.
        var databasePath = Path.Combine(Path.GetTempPath(), $"strategyops-saga-{Guid.NewGuid():n}.db");

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<ProjectsDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));

        services.AddMassTransitTestHarness(bus =>
            bus.AddSagaStateMachine<ProjectInitiationSaga, ProjectInitiationState>()
                .EntityFrameworkRepository(repository =>
                {
                    repository.ExistingDbContext<ProjectsDbContext>();
                    repository.ConcurrencyMode = ConcurrencyMode.Optimistic;
                }));

        var provider = services.BuildServiceProvider(validateScopes: true);

        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().Database.MigrateAsync();
        }

        var host = new SagaHost(databasePath, provider);
        await host.Harness.Start();
        return host;
    }

    public ISagaStateMachineTestHarness<ProjectInitiationSaga, ProjectInitiationState> Saga =>
        Harness.GetSagaStateMachineHarness<ProjectInitiationSaga, ProjectInitiationState>();

    public Task PublishAsync<T>(T message) where T : class => Harness.Bus.Publish(message);

    /// <summary>
    /// Reads saga state straight out of the database, which is the only honest way to assert
    /// that it was persisted rather than merely held in memory.
    /// </summary>
    public async Task<ProjectInitiationState?> ReadStateAsync(Guid projectId)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        return await db.ProjectInitiations
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CorrelationId == projectId);
    }

    public async ValueTask DisposeAsync()
    {
        await Harness.Stop();
        await _provider.DisposeAsync();

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
