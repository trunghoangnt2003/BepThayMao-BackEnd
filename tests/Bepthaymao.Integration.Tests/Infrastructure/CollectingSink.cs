using Serilog.Core;
using Serilog.Events;

namespace Bepthaymao.Integration.Tests.Infrastructure;

internal sealed class CollectingSink : ILogEventSink
{
    private readonly List<LogEvent> _events = [];
    private readonly object _gate = new();

    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    public void Emit(LogEvent logEvent)
    {
        lock (_gate)
        {
            _events.Add(logEvent);
        }
    }
}
