namespace ConcurrencyLimits.Limit.Functions;

/// <summary>
/// Specialized utility function used by limiters to calculate thresholds using the square root
/// of the current limit. The square root of numbers up to 1000 is pre-computed because the
/// square root operation can be slow.
/// </summary>
public static class SquareRootFunction
{
    private static readonly int[] Lookup = new int[1000];

    static SquareRootFunction()
    {
        for (int i = 0; i < 1000; i++)
        {
            Lookup[i] = Math.Max(1, (int)Math.Sqrt(i));
        }
    }

    public static int Apply(int t) => t < 1000 ? Lookup[t] : (int)Math.Sqrt(t);

    /// <summary>Create a function that returns <c>max(baseline, sqrt(limit))</c>.</summary>
    public static Func<int, int> Create(int baseline) => t => Math.Max(baseline, Apply(t));
}
