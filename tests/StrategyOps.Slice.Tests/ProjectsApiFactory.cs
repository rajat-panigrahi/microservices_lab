using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.Projects.Api;
using StrategyOps.Projects.Api.Infrastructure;

namespace StrategyOps.Slice.Tests;

/// <summary>
/// Boots the real Projects service - real endpoints, real handlers, real EF mappings - against
/// a private in-memory SQLite database.
/// </summary>
/// <remarks>
/// Two deliberate substitutions, and only two:
/// <list type="bullet">
///   <item>the connection points at <c>:memory:</c>, held open for the fixture's lifetime;</item>
///   <item>the outbox background publisher is removed, so tests drain the outbox explicitly
///   and assert on the result instead of racing a 500ms timer.</item>
/// </list>
/// Everything else is production wiring. A test that passes here has exercised the same
/// serialization, validation, routing and SQL the deployed service uses.
/// </remarks>
public sealed class ProjectsApiFactory : WebApplicationFactory<ProjectsApiEntryPoint>, IAsyncLifetime
{
    // A file, not ":memory:". A shared in-memory SQLite connection is reused by every scope,
    // so anything that opens an explicit transaction - MassTransit's saga repository, for one
    // - ends up nesting transactions on a single connection, which SQLite forbids. A file
    // gives each scope its own pooled connection, exactly as the deployed service gets.
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"strategyops-projects-tests-{Guid.NewGuid():n}.db");

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    /// <summary>Runs one outbox pass, the way the background publisher would.</summary>
    public async Task<int> DrainOutboxAsync()
    {
        using var scope = Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor<ProjectsDbContext>>();
        return await processor.DrainOnceAsync(CancellationToken.None);
    }

    public async Task<T> QueryAsync<T>(Func<ProjectsDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ProjectsDbContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Real authentication runs; only the signing key is pinned so tests can mint valid
        // tokens. Discovery self-registration is off because there is no registry here.
        builder.UseSetting("Jwt:SigningKey", TestTokens.SigningKey);
        builder.UseSetting("Jwt:Issuer", TestTokens.Issuer);
        builder.UseSetting("Jwt:Audience", TestTokens.Audience);
        builder.UseSetting("Discovery:Enabled", "false");
        builder.UseSetting("RabbitMq:UseInMemoryTransport", "true");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ProjectsDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<ProjectsDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));

            var publisher = services.SingleOrDefault(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(OutboxPublisherService<ProjectsDbContext>));

            if (publisher is not null)
            {
                services.Remove(publisher);
            }
        });
    }
}
