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
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IssuesDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }

    public async Task<T> QueryAsync<T>(Func<IssuesDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IssuesDbContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<IssuesDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<IssuesDbContext>(options => options.UseSqlite(_connection));

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
