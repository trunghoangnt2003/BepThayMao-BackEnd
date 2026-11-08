using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog.Context;

namespace Bepthaymao.Api.Observability;

internal sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        string supplied = context.Request.Headers[HeaderName].ToString();
        string id = supplied.Length <= 64 && CorrelationPattern().IsMatch(supplied)
            ? supplied : Guid.CreateVersion7().ToString("N");
        context.Items[ItemKey] = id;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        });
        Activity.Current?.SetTag("correlation.id", id);
        using (LogContext.PushProperty(ItemKey, id))
        {
            await next(context);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationPattern();
}
