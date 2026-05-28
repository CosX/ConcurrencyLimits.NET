namespace ConcurrencyLimits.Internal;

public sealed class EmptyMetricRegistry : IMetricRegistry
{
    public static readonly EmptyMetricRegistry Instance = new();

    private EmptyMetricRegistry() { }

    private sealed class EmptySampleListener : IMetricRegistry.ISampleListener
    {
        public static readonly EmptySampleListener Instance = new();
        public void AddSample(double value) { }
    }

    public IMetricRegistry.ISampleListener Distribution(string id, params string[] tagNameValuePairs)
        => EmptySampleListener.Instance;

    public void Gauge(string id, Func<double> supplier, params string[] tagNameValuePairs) { }

    public IMetricRegistry.ICounter Counter(string id, params string[] tagNameValuePairs)
        => NoopCounter.Instance;
}
