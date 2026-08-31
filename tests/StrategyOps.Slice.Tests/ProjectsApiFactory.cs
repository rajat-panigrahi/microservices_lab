using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StrategyOps.BuildingBlocks.Outbox;
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
public sealed class ProjectsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
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

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ProjectsDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<ProjectsDbContext>(options => options.UseSqlite(_connection));

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
