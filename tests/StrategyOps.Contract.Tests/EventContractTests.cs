using System.Reflection;
using System.Text;
using System.Text.Json;
using Shouldly;
using StrategyOps.Contracts.V1;

namespace StrategyOps.Contract.Tests;

/// <summary>
/// Guards the shape of every message that crosses a service boundary.
/// </summary>
/// <remarks>
/// <para>
/// A contract is the one thing in this system that cannot be refactored freely. Renaming a
/// property on an internal class is a rename; renaming it on an integration event silently
/// breaks every consumer that is still deployed with the old version - and it breaks at
/// runtime, in another team's service, usually at the worst moment.
/// </para>
/// <para>
/// These tests are a <b>snapshot</b>: they write the shape of every contract to a file, and
/// fail if it ever differs. The failure is the point. It is not "you broke something", it is
/// <b>"you are about to change a contract - was that deliberate, and is it backwards
/// compatible?"</b> Updating the snapshot is a one-line change; the value is that it cannot
/// happen by accident, in a diff nobody read closely.
/// </para>
/// <para>
/// This is the cheap end of consumer-driven contract testing. Pact and friends go further by
/// having each consumer declare what it actually uses, so a producer learns which of its
/// fields are safe to remove. That needs a broker and cross-team ceremony; a snapshot needs a
/// file, and catches the majority of real breakages.
/// </para>
/// </remarks>
public class EventContractTests
{
    private static readonly string SnapshotPath = Path.Combine(AppContext.BaseDirectory, "Snapshots", "event-contracts.txt");

    private static IEnumerable<Type> ContractTypes =>
        typeof(IntegrationEvent).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsPublic: true } && typeof(IntegrationEvent).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    [Fact]
    public void EveryContractShapeMatchesTheSnapshot()
    {
        var actual = DescribeAllContracts();

        if (!File.Exists(SnapshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            File.WriteAllText(SnapshotPath, actual);

            Assert.Fail(
                $"No contract snapshot existed, so one was written to {SnapshotPath}. " +
                "Copy it into the repository's Snapshots folder and commit it.");
        }

        var expected = File.ReadAllText(SnapshotPath).ReplaceLineEndings("\n");

        actual.ReplaceLineEndings("\n").ShouldBe(
            expected,
            "An integration event's shape changed. Every consumer still running the old " +
            "version will be affected. If the change is additive and optional it is safe - " +
            "update the snapshot. If it renames or removes a field, it needs a new contract " +
            "version instead.");
    }

    [Fact]
    public void EveryContractCarriesTheFieldsTheInboxAndTracingDependOn()
    {
        foreach (var contract in ContractTypes)
        {
            var properties = contract.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();

            properties.ShouldContain(nameof(IntegrationEvent.MessageId), $"{contract.Name} needs a MessageId - it is the inbox's deduplication key.");
            properties.ShouldContain(nameof(IntegrationEvent.CorrelationId), $"{contract.Name} needs a CorrelationId or the trace ends at this hop.");
            properties.ShouldContain(nameof(IntegrationEvent.OccurredAtUtc), $"{contract.Name} needs OccurredAtUtc.");
        }
    }

    [Fact]
    public void NoContractExposesAnEnum()
    {
        // Adding a value to a shared enum silently breaks consumers compiled against the old
        // one: they deserialise an unknown number and fall into whatever branch happens to be
        // first. A string lets an old consumer default sensibly instead - which is exactly
        // what Issue.SeverityFromRiskTier does with a tier it has never heard of.
        var offenders = ContractTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => Nullable.GetUnderlyingType(p.PropertyType)?.IsEnum == true || p.PropertyType.IsEnum)
                .Select(p => $"{t.Name}.{p.Name} ({p.PropertyType.Name})"))
            .ToList();

        offenders.ShouldBeEmpty(
            "Enum-like values must cross service boundaries as strings. Offenders: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryContractRoundTripsThroughTheOutboxSerializer()
    {
        // The outbox stores JSON and deserialises it later, possibly after a deployment. A
        // contract that cannot round-trip is a message that will sit in the outbox forever.
        foreach (var contract in ContractTypes)
        {
            var instance = CreateInstance(contract);

            var json = JsonSerializer.Serialize(instance, contract, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var back = JsonSerializer.Deserialize(json, contract, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            back.ShouldNotBeNull($"{contract.Name} did not survive a JSON round trip.");
            ((IntegrationEvent)back!).MessageId.ShouldBe(((IntegrationEvent)instance).MessageId);
        }
    }

    [Fact]
    public void ContractsLiveUnderAVersionedNamespace()
    {
        foreach (var contract in ContractTypes)
        {
            contract.Namespace!.StartsWith("StrategyOps.Contracts.V1", StringComparison.Ordinal).ShouldBeTrue(
                $"{contract.Name} must sit under a version folder, so V2 can exist alongside it during a rollout.");
        }
    }

    private static string DescribeAllContracts()
    {
        var builder = new StringBuilder();

        foreach (var contract in ContractTypes)
        {
            builder.AppendLine(contract.FullName);

            foreach (var property in contract.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                builder.AppendLine($"    {property.Name}: {FriendlyName(property.PropertyType)}");
            }
        }

        return builder.ToString().ReplaceLineEndings("\n");
    }

    private static string FriendlyName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        return underlying is not null ? $"{underlying.Name}?" : type.Name;
    }

    private static object CreateInstance(Type contract)
    {
        // Contracts use `required` init properties, so there is no parameterless path.
        // Deserialising a JSON object with every property defaulted is the simplest way to
        // get a valid instance without hand-writing a factory per contract.
        var json = new StringBuilder("{");

        foreach (var property in contract.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            json.Append($"\"{JsonNamingPolicy.CamelCase.ConvertName(property.Name)}\":{SampleFor(property.PropertyType)},");
        }

        if (json[^1] == ',')
        {
            json.Length--;
        }

        json.Append('}');

        return JsonSerializer.Deserialize(json.ToString(), contract, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static string SampleFor(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;

        return actual switch
        {
            _ when actual == typeof(Guid) => $"\"{Guid.NewGuid()}\"",
            _ when actual == typeof(string) => "\"sample\"",
            _ when actual == typeof(DateTimeOffset) => "\"2026-01-15T09:00:00+00:00\"",
            _ when actual == typeof(decimal) || actual == typeof(int) || actual == typeof(long) => "1",
            _ when actual == typeof(bool) => "true",
            _ => "null"
        };
    }
}
