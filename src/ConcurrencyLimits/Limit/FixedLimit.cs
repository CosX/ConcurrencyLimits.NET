namespace ConcurrencyLimits.Limit;

/// <summary>Non dynamic limit with fixed value.</summary>
public sealed class FixedLimit : AbstractLimit
{
    private FixedLimit(int limit) : base(limit) { }

    public static FixedLimit Of(int limit) => new(limit);

    protected override int Update(long startTime, long rtt, int inflight, bool didDrop) => GetLimit();

    public override string ToString() => $"FixedLimit [limit={GetLimit()}]";
}
