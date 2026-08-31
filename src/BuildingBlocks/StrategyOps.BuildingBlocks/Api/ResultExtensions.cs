using Microsoft.AspNetCore.Http;
using StrategyOps.BuildingBlocks.Results;

namespace StrategyOps.BuildingBlocks.Api;

/// <summary>
/// The single place where a domain outcome becomes an HTTP status code. Keeping this in one
/// file means every service in the system answers the same way for the same kind of failure,
/// which matters a lot more once a gateway is aggregating five of them.
/// </summary>
public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result) =>
        result.Status switch
        {
            ResultStatus.Ok => Microsoft.AspNetCore.Http.Results.NoContent(),
            ResultStatus.Created => Microsoft.AspNetCore.Http.Results.NoContent(),
            _ => Problem(result)
        };

    public static IResult ToHttpResult<T>(this Result<T> result, string? createdAtLocation = null) =>
        result.Status switch
        {
            ResultStatus.Ok => Microsoft.AspNetCore.Http.Results.Ok(result.Value),
            ResultStatus.Created => createdAtLocation is null
                ? Microsoft.AspNetCore.Http.Results.Ok(result.Value)
                : Microsoft.AspNetCore.Http.Results.Created(createdAtLocation, result.Value),
            _ => Problem(result)
        };

    private static IResult Problem(Result result)
    {
        var (statusCode, title) = result.Status switch
        {
            ResultStatus.Invalid => (StatusCodes.Status400BadRequest, "Request is not valid"),
            ResultStatus.NotFound => (StatusCodes.Status404NotFound, "Resource was not found"),
            ResultStatus.Conflict => (StatusCodes.Status409Conflict, "Resource is not in a state that allows this"),
            ResultStatus.Unavailable => (StatusCodes.Status503ServiceUnavailable, "A dependency is unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error")
        };

        return Microsoft.AspNetCore.Http.Results.Problem(
            title: title,
            detail: result.Error.Message,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
    }
}
