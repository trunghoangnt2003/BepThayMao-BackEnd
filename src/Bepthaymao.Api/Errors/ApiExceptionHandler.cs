using Bepthaymao.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ValidationException = Bepthaymao.Application.Abstractions.Errors.ValidationException;

namespace Bepthaymao.Api.Errors;

internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    private static readonly Action<ILogger, Exception?> _unhandled = LoggerMessage.Define(
        LogLevel.Error, new EventId(5001, "UnhandledRequest"), "Unhandled request error");

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        ProblemDetails problem;
        if (exception is ValidationException validation)
        {
            problem = new HttpValidationProblemDetails(validation.Errors.ToDictionary(
                entry => entry.Key, entry => entry.Value, StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Dữ liệu không hợp lệ.",
            };
        }
        else if (exception is DomainRuleException domain)
        {
            problem = new HttpValidationProblemDetails(new Dictionary<string, string[]>
            {
                [domain.Rule] = [domain.Message],
            })
            { Status = StatusCodes.Status400BadRequest, Title = "Dữ liệu không hợp lệ." };
        }
        else
        {
            _unhandled(logger, exception);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Đã có lỗi hệ thống.",
            };
        }

        await ErrorHandlingSetup.WriteProblemAsync(httpContext, problemDetails, problem, cancellationToken);
        return true;
    }
}
