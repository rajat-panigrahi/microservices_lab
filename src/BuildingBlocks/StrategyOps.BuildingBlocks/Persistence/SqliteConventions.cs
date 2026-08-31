using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace StrategyOps.BuildingBlocks.Persistence;

/// <summary>
/// Provider differences that leak into the domain, and the one line that stops them.
/// </summary>
/// <remarks>
/// <para>
/// SQLite has no native date type, so EF stores a <see cref="DateTimeOffset"/> as text and
/// then refuses to translate ORDER BY or range comparisons on it:
/// <c>"SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses"</c>.
/// The same LINQ runs perfectly against SQL Server or PostgreSQL, which is exactly what makes
/// it dangerous - it is a runtime failure on a query that compiled fine and passed review.
/// </para>
/// <para>
/// <see cref="DateTimeOffsetToBinaryConverter"/> stores the value as an order-preserving
/// long, so sorting and comparison work in SQL. Applying it by convention to every
/// DateTimeOffset property means no individual entity configuration has to remember.
/// </para>
/// <para>
/// This is worth knowing beyond SQLite: "it works on my machine's database" is a real class
/// of microservices bug, and the fix belongs in shared infrastructure rather than in each
/// service's query code.
/// </para>
/// </remarks>
public static class SqliteConventions
{
    public static ModelBuilder ApplyDateTimeOffsetConversions(this ModelBuilder modelBuilder, string? providerName)
    {
        if (providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) != true)
        {
            return modelBuilder;
        }

        var converter = new DateTimeOffsetToBinaryConverter();

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entity => entity.GetProperties())
                     .Where(property => property.ClrType == typeof(DateTimeOffset)
                                        || property.ClrType == typeof(DateTimeOffset?)))
        {
            property.SetValueConverter(converter);
        }

        return modelBuilder;
    }
}
