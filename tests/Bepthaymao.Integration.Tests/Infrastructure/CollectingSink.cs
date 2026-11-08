using System.Collections.Concurrent;
using System.Globalization;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Bepthaymao.Integration.Tests.Infrastructure;

internal sealed class CollectingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    public string Render()
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        CompactJsonFormatter formatter = new();
        foreach (LogEvent logEvent in _events)
        {
            formatter.Format(logEvent, writer);
        }

        return writer.ToString();
    }
}
