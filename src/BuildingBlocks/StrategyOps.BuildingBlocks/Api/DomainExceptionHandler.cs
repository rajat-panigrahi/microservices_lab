using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using StrategyOps.BuildingBlocks.Domain;

namespace StrategyOps.BuildingBlocks.Api;

/// <summary>
/// Turns an aggregate's invariant violation into a 409 with a machine-readable code, instead
/// of a 500 that tells the caller nothing and pages someone at 3am.
/// </summary>
public sealed class DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }

        logger.LogInformation(
            "Rejected {Method} {Path}: {Code} - {Message}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            domainException.Code,
            domainException.Message);

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Resource is not in a state that allows this",
                Detail = domainException.Message,
                Extensions = { ["code"] = domainException.Code }
            },
            cancellationToken);

        return true;
    }
}
