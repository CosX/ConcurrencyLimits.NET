using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConcurrencyLimits.Limit;

public sealed class TracingLimitDecorator : ILimit
{
    private readonly ILimit _delegate;
    private readonly ILogger _log;

    public static TracingLimitDecorator Wrap(ILimit delegateLimit, ILogger? logger = null) => new(delegateLimit, logger);

    public TracingLimitDecorator(ILimit delegateLimit, ILogger? logger = null)
    {
        _delegate = delegateLimit;
        _log = logger ?? NullLogger.Instance;
    }

    public int GetLimit() => _delegate.GetLimit();

    public void OnSample(long startTime, long rtt, int inflight, bool didDrop)
    {
        _log.LogDebug("maxInFlight={Inflight} minRtt={MinRtt} ms", inflight, rtt / 1e6);
        _delegate.OnSample(startTime, rtt, inflight, didDrop);
    }

    public void NotifyOnChange(Action<int> consumer) => _delegate.NotifyOnChange(consumer);
}
