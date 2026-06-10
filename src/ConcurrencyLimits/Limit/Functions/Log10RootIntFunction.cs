namespace ConcurrencyLimits.Limit.Functions;

/// <summary>
/// Function used by limiters to calculate thresholds using log10 of the current limit.
/// The log10 of numbers up to 1000 is pre-computed as an optimization.
/// </summary>
public static class Log10RootIntFunction
{
    private static readonly int[] Lookup = new int[1000];

    static Log10RootIntFunction()
    {
        for (int i = 0; i < Lookup.Length; i++)
        {
            Lookup[i] = Math.Max(1, (int)Math.Log10(i));
        }
    }

    // Clamp negatives to 0: custom limit functions can momentarily produce values below
    // zero and must not turn that into an IndexOutOfRangeException.
    public static int Apply(int t) => t < 1000 ? Lookup[Math.Max(0, t)] : (int)Math.Log10(t);

    /// <summary>Create a function that returns <c>log10(limit) + baseline</c>.</summary>
    public static Func<int, int> Create(int baseline)
        => baseline == 0 ? Apply : t => Apply(t) + baseline;
}
