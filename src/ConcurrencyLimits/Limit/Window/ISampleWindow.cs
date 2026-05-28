namespace ConcurrencyLimits.Limit.Window;

/// <summary>
/// Implementations of this interface track immutable samples held in an atomic reference.
/// </summary>
/// <seealso cref="WindowedLimit"/>
public interface ISampleWindow
{
    ISampleWindow AddSample(long rtt, int inflight, bool dropped);

    long GetCandidateRttNanos();

    long GetTrackedRttNanos();

    int GetMaxInFlight();

    int GetSampleCount();

    bool DidDrop();
}
