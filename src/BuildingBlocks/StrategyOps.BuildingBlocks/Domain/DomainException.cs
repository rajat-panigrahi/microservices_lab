namespace StrategyOps.BuildingBlocks.Domain;

/// <summary>
/// Thrown when an aggregate is asked to do something its invariants forbid - closing a
/// project that was never activated, for example.
/// </summary>
/// <remarks>
/// This is deliberately distinct from input validation. Validation (is the name blank?)
/// happens at the edge and yields 400. A domain exception means the request was well formed
/// but the aggregate is in the wrong state for it, which is 409 Conflict. The
/// <see cref="Code"/> is a stable, machine-readable identifier the API surfaces to clients
/// and the tests assert on, so error handling never depends on message wording.
/// </remarks>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
