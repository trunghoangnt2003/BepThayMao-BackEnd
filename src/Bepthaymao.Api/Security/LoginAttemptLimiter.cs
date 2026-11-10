using System.Threading.RateLimiting;
using Bepthaymao.Application.Abstractions.Errors;

namespace Bepthaymao.Api.Security;

internal sealed class LoginAttemptLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;
    private readonly int _windowSeconds;

    public LoginAttemptLimiter(IConfiguration configuration)
    {
        int permitLimit = configuration.GetValue("RateLimiting:LoginIdentifier:PermitLimit", 5);
        _windowSeconds = configuration.GetValue("RateLimiting:LoginIdentifier:WindowSeconds", 60);
        _limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(_windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    public void EnsureAllowed(string? ip, string identifier)
    {
        using RateLimitLease lease = _limiter.AttemptAcquire($"{ip ?? "unknown"}|{identifier.Trim().ToUpperInvariant()}");
        if (!lease.IsAcquired)
        {
            int retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retry)
                ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : _windowSeconds;
            throw new TooManyRequestsException(retryAfter);
        }
    }

    public void Dispose() => _limiter.Dispose();
}
