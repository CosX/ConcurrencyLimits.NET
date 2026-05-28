namespace ConcurrencyLimits;

/// <summary>Common metric ids.</summary>
public static class MetricIds
{
    public const string LimitName = "limit";
    public const string CallName = "call";
    public const string InflightName = "inflight";
    public const string PartitionLimitName = "limit.partition";
    public const string MinRttName = "min_rtt";
    public const string WindowMinRttName = "min_window_rtt";
    public const string WindowQueueSizeName = "queue_size";
}
