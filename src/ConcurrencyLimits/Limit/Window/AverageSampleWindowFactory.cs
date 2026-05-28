namespace ConcurrencyLimits.Limit.Window;

public sealed class AverageSampleWindowFactory : ISampleWindowFactory
{
    private static readonly AverageSampleWindowFactory Instance = new();

    private AverageSampleWindowFactory() { }

    public static AverageSampleWindowFactory Create() => Instance;

    public ISampleWindow NewInstance() => new ImmutableAverageSampleWindow();
}
