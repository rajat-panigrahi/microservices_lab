using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace StrategyOps.BuildingBlocks.Api;

/// <summary>
/// Runs a slice's FluentValidation validator before the handler sees the request.
/// </summary>
/// <remarks>
/// Applied per endpoint with <c>.WithValidation&lt;TCommand&gt;()</c> rather than globally,
/// because a slice owns its own rules. A missing validator is not an error: query endpoints
/// have nothing to validate.
/// </remarks>
public sealed class ValidationFilter<TRequest> : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService(typeof(IValidator<TRequest>)) as IValidator<TRequest>;

        if (validator is null)
        {
            return await next(context);
        }

        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();

        if (request is null)
        {
            return await next(context);
        }

        var validation = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        if (validation.IsValid)
        {
            return await next(context);
        }

        var errors = validation.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        return Microsoft.AspNetCore.Http.Results.ValidationProblem(
            errors,
            title: "Request is not valid",
            extensions: new Dictionary<string, object?> { ["code"] = "request.validation_failed" });
    }
}

public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class =>
        builder.AddEndpointFilter<ValidationFilter<TRequest>>()
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);
}
