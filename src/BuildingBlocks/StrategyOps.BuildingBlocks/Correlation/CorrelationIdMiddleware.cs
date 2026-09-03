using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace StrategyOps.BuildingBlocks.Correlation;

/// <summary>
/// Assigns or adopts the correlation id for a request, and puts it on every log line.
/// </summary>
/// <remarks>
/// <para>
/// This is the single most useful thing in this repository for debugging a distributed
/// system. Without it, "the user says their project did not activate" means reading nine
/// services' logs and guessing which lines belong together. With it, one grep across
/// everything returns the whole story in order.
/// </para>
/// <para>
/// The id is <b>adopted, not always generated</b>: if the caller already sent one, that one
/// wins. Otherwise a request that crosses the gateway would get a fresh id at every hop and
/// the chain would break exactly where it matters.
/// </para>
/// <para>
/// It also goes back on the <b>response</b>, so a user reporting a problem can quote the id
/// from their browser's network tab and support can find the request instantly.
/// </para>
/// </remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HttpCorrelationContext.HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("n");
        }

        context.Items[HttpCorrelationContext.HeaderName] = correlationId;

        // Also write it back onto the REQUEST. A reverse proxy forwards the incoming headers,
        // so without this a correlation id generated here (because the caller sent none)
        // would be logged locally and then vanish at the first hop - the chain would start at
        // the gateway and stop there. Writing it onto the request makes the generated id
        // indistinguishable from one the caller supplied.
        context.Request.Headers[HttpCorrelationContext.HeaderName] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HttpCorrelationContext.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // LogContext puts the property on every log line written anywhere inside this
        // request - including deep in a handler that has never heard of correlation ids.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}

public static class CorrelationMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
