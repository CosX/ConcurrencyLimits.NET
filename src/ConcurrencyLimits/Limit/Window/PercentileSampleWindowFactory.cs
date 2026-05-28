using ConcurrencyLimits.Internal;

namespace ConcurrencyLimits.Limit.Window;

public sealed class PercentileSampleWindowFactory : ISampleWindowFactory
{
    private readonly double _percentile;
    private readonly int _windowSize;

    private PercentileSampleWindowFactory(double percentile, int windowSize)
    {
        _percentile = percentile;
        _windowSize = windowSize;
    }

    public static PercentileSampleWindowFactory Of(double percentile, int windowSize)
    {
        Preconditions.CheckArgument(percentile > 0 && percentile < 1.0, "Percentile should belong to (0, 1.0)");
        return new PercentileSampleWindowFactory(percentile, windowSize);
    }

    public ISampleWindow NewInstance() => new ImmutablePercentileSampleWindow(_percentile, _windowSize);
}
