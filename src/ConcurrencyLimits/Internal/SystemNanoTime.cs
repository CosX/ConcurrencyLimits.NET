using System.Diagnostics;

namespace ConcurrencyLimits.Internal;

/// <summary>
/// Monotonic nanosecond clock, the .NET equivalent of Java's System.nanoTime().
/// </summary>
public static class SystemNanoTime
{
    private static readonly double NanosPerTick = 1_000_000_000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Current value of a monotonic clock, in nanoseconds. Only differences between
    /// two readings are meaningful.
    /// </summary>
    public static long Now() => (long)(Stopwatch.GetTimestamp() * NanosPerTick);
}
