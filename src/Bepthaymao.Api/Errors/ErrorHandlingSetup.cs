namespace Bepthaymao.Api.Errors;

internal static class ErrorHandlingSetup
{
    public static IServiceCollection AddApiErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                string correlationId = context.HttpContext.Items[Observability.CorrelationIdMiddleware.ItemKey]
                    as string ?? context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Extensions["correlationId"] = correlationId;
                context.ProblemDetails.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString();
            };
        });
        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }

    public static WebApplication UseApiErrorHandling(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }
}
