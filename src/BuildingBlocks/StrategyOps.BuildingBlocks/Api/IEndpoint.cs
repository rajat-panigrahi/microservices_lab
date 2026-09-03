using Microsoft.AspNetCore.Routing;

namespace StrategyOps.BuildingBlocks.Api;

/// <summary>
/// One vertical slice's HTTP surface. Implementations live next to the handler they call,
/// in <c>Features/&lt;UseCase&gt;/</c>.
/// </summary>
/// <remarks>
/// Endpoints are discovered by assembly scan, so adding a feature never means editing
/// Program.cs. That is the open/closed principle doing something useful rather than
/// decorative: the composition root is closed for modification, and the feature folder is
/// the only thing that opens.
/// </remarks>
public interface IEndpoint
{
    void Map(IEndpointRouteBuilder app);
}
