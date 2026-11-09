using System.Text.RegularExpressions;
using Serilog.Context;

namespace Bepthaymao.Api.Observability;

internal sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();

    public async Task InvokeAsync(HttpContext context)
    {
        string candidate = context.Request.Headers[HeaderName].FirstOrDefault() ?? string.Empty;
        string correlationId = CorrelationIdPattern().IsMatch(candidate)
            ? candidate
            : Guid.CreateVersion7().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(static state =>
        {
            (HttpContext httpContext, string id) = ((HttpContext, string))state;
            httpContext.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        }, (context, correlationId));
        System.Diagnostics.Activity.Current?.SetTag("correlation.id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
