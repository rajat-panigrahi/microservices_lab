using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StrategyOps.BuildingBlocks.Outbox;
using StrategyOps.Issues.Api;
using StrategyOps.Issues.Api.Infrastructure;

namespace StrategyOps.Slice.Tests;

/// <summary>
/// Boots the Issues service against in-memory SQLite, with MassTransit replaced by an
/// in-memory bus so no broker is needed.
/// </summary>
public sealed class IssuesApiFactory : WebApplicationFactory<IssuesApiEntryPoint>, IAsyncLifetime
{
    // A file, not ":memory:". A shared in-memory SQLite connection is reused by every scope,
    // so anything that opens an explicit transaction - MassTransit's saga repository, for one
    // - ends up nesting transactions on a single connection, which SQLite forbids. A file
    // gives each scope its own pooled connection, exactly as the deployed service gets.
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"strategyops-issues-tests-{Guid.NewGuid():n}.db");

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IssuesDbContext>().Database.MigrateAsync();
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

    public async Task<T> QueryAsync<T>(Func<IssuesDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IssuesDbContext>());
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
            services.RemoveAll<DbContextOptions<IssuesDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<IssuesDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));

            var publisher = services.SingleOrDefault(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(OutboxPublisherService<IssuesDbContext>));

            if (publisher is not null)
            {
                services.Remove(publisher);
            }
        });
    }
}

[CollectionDefinition(nameof(IssuesApiCollection))]
public sealed class IssuesApiCollection : ICollectionFixture<IssuesApiFactory>;
