using Microsoft.Extensions.Logging;

namespace Bepthaymao.Application.Abstractions.Behaviors;

internal static partial class CqrsLogging
{
    [LoggerMessage(LogLevel.Information, "Handling {RequestType}")]
    public static partial void Handling(ILogger logger, string requestType);

    [LoggerMessage(LogLevel.Information, "Handled {RequestType} in {ElapsedMs} ms")]
    public static partial void Handled(ILogger logger, string requestType, double elapsedMs);
}
