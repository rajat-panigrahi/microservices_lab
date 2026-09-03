using System.Reflection;
using StrategyOps.Contracts.V1;

namespace StrategyOps.BuildingBlocks.Outbox;

/// <summary>
/// Maps the <see cref="OutboxMessage.Type"/> string back to a CLR type on the way out.
/// </summary>
/// <remarks>
/// The stored name is the contract's full name, never an assembly-qualified name: an
/// assembly-qualified name bakes the version into rows that may sit in the table across a
/// deployment, and then fails to resolve after an upgrade.
/// </remarks>
public static class IntegrationEventTypeRegistry
{
    private static readonly Dictionary<string, Type> ByName = typeof(IntegrationEvent).Assembly
        .GetTypes()
        .Where(t => t is { IsAbstract: false } && typeof(IntegrationEvent).IsAssignableFrom(t))
        .ToDictionary(t => t.FullName!, t => t, StringComparer.Ordinal);

    public static string NameOf(IntegrationEvent @event) => @event.GetType().FullName!;

    public static bool TryResolve(string name, out Type type) => ByName.TryGetValue(name, out type!);

    public static IReadOnlyCollection<Type> All => ByName.Values;

    /// <summary>Every contract type, for the MassTransit endpoint wiring and the contract tests.</summary>
    public static IEnumerable<Type> InAssemblyOf<T>() => typeof(T).Assembly
        .GetTypes()
        .Where(t => t is { IsAbstract: false } && typeof(IntegrationEvent).IsAssignableFrom(t));

    internal static Assembly ContractsAssembly => typeof(IntegrationEvent).Assembly;
}
