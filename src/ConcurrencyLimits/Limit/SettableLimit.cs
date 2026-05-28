namespace ConcurrencyLimits.Limit;

/// <summary>
/// <see cref="ILimit"/> to be used mostly for testing where the limit can be manually adjusted.
/// </summary>
public class SettableLimit : AbstractLimit
{
    public SettableLimit(int limit) : base(limit) { }

    public static SettableLimit StartingAt(int limit) => new(limit);

    protected override int Update(long startTime, long rtt, int inflight, bool didDrop) => GetLimit();

    public void SetLimitValue(int limit) => SetLimit(limit);

    public override string ToString() => $"SettableLimit [limit={GetLimit()}]";
}
