using System.Diagnostics;
using Bepthaymao.Api.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Bepthaymao.Api.Errors;

internal static class ErrorHandlingSetup
{
    public static IServiceCollection AddApiErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            AddIdentifiers(context.HttpContext, context.ProblemDetails));
        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }

    public static IApplicationBuilder UseApiErrorHandling(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages(async statusContext =>
        {
            HttpContext context = statusContext.HttpContext;
            await WriteProblemAsync(context, context.RequestServices.GetRequiredService<IProblemDetailsService>(),
                new ProblemDetails
                {
                    Status = context.Response.StatusCode,
                    Title = ReasonPhrases.GetReasonPhrase(context.Response.StatusCode),
                }, context.RequestAborted);
        });
        return app;
    }

    internal static async Task WriteProblemAsync(HttpContext context, IProblemDetailsService service,
        ProblemDetails problem, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        AddIdentifiers(context, problem);
        if (!await service.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem }))
        {
            await context.Response.WriteAsJsonAsync(problem, options: null,
                contentType: "application/problem+json", cancellationToken: cancellationToken);
        }
    }

    private static void AddIdentifiers(HttpContext context, ProblemDetails problem)
    {
        problem.Extensions["correlationId"] = context.Items[CorrelationIdMiddleware.ItemKey] as string ?? context.TraceIdentifier;
        if (Activity.Current is { } activity)
        {
            problem.Extensions["traceId"] = activity.TraceId.ToString();
        }
    }
}
