namespace ConcurrencyLimits;

/// <summary>
/// Contract for an algorithm that calculates a concurrency limit based on rtt measurements.
/// </summary>
public interface ILimit
{
    /// <summary>Current estimated limit.</summary>
    int GetLimit();

    /// <summary>
    /// Register a callback to receive notification whenever the limit is updated to a new value.
    /// </summary>
    void NotifyOnChange(Action<int> consumer);

    /// <summary>
    /// Update the limiter with a sample.
    /// </summary>
    /// <param name="startTime">start time in nanoseconds</param>
    /// <param name="rtt">round trip time in nanoseconds</param>
    /// <param name="inflight">number of in-flight requests when the sample was taken</param>
    /// <param name="didDrop">true if the request was dropped</param>
    void OnSample(long startTime, long rtt, int inflight, bool didDrop);
}
