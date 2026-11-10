using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace Bepthaymao.Api.Errors;

internal sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ProblemDetails problemDetails;
        if (exception is TooManyRequestsException throttled)
        {
            httpContext.Response.Headers.RetryAfter = throttled.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests.",
            };
        }
        else if (exception is AuthenticationFailedException)
        {
            problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Authentication failed.",
            };
        }
        else if (exception is ValidationException validationException)
        {
            problemDetails = new HttpValidationProblemDetails(validationException.Errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            };
        }
        else if (exception is DomainRuleException domainRuleException)
        {
            problemDetails = new HttpValidationProblemDetails(new Dictionary<string, string[]>
            {
                [domainRuleException.Rule] = [domainRuleException.Message],
            })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The request violates a domain rule.",
            };
        }
        else
        {
            ApiErrorLogging.Unhandled(logger, exception);
            problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
            };
        }

        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
        });
        return true;
    }
}

internal static partial class ApiErrorLogging
{
    [LoggerMessage(LogLevel.Error, "Unhandled API exception")]
    public static partial void Unhandled(ILogger logger, Exception exception);
}
