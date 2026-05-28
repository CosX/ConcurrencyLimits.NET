namespace ConcurrencyLimits;

/// <summary>
/// Simple abstraction for tracking metrics in the limiters.
/// </summary>
public interface IMetricRegistry
{
    /// <summary>Listener to receive samples for a distribution.</summary>
    public interface ISampleListener
    {
        void AddSample(double value);

        void AddLongSample(long value) => AddSample(value);

        void AddDoubleSample(double value) => AddSample(value);
    }

    public interface ICounter
    {
        void Increment();
    }

    /// <summary>
    /// Register a sample distribution. Samples are added to the distribution via the returned
    /// <see cref="ISampleListener"/>. Will reuse an existing listener if the distribution already exists.
    /// </summary>
    /// <param name="id">metric id</param>
    /// <param name="tagNameValuePairs">pairs of tag name and tag value (count must be a multiple of 2)</param>
    ISampleListener Distribution(string id, params string[] tagNameValuePairs);

    /// <summary>
    /// Register a gauge using the provided supplier. The supplier will be polled whenever the gauge
    /// value is flushed by the registry.
    /// </summary>
    void Gauge(string id, Func<double> supplier, params string[] tagNameValuePairs);

    /// <summary>
    /// Create a counter that will be incremented when an event occurs.
    /// </summary>
    ICounter Counter(string id, params string[] tagNameValuePairs) => NoopCounter.Instance;
}

internal sealed class NoopCounter : IMetricRegistry.ICounter
{
    public static readonly NoopCounter Instance = new();
    public void Increment() { }
}
