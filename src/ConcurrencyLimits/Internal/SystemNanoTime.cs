using System.Diagnostics;

namespace ConcurrencyLimits.Internal;

/// <summary>
/// Monotonic nanosecond clock, the .NET equivalent of Java's System.nanoTime().
/// </summary>
public static class SystemNanoTime
{
    private const long NanosPerSecond = 1_000_000_000L;
    private static readonly long Frequency = Stopwatch.Frequency;

    /// <summary>
    /// Current value of a monotonic clock, in nanoseconds. Only differences between
    /// two readings are meaningful.
    /// </summary>
    public static long Now()
    {
        // Integer math avoids the double-mantissa precision loss that a (ticks * nanosPerTick)
        // multiply suffers once the timestamp grows large.
        long ticks = Stopwatch.GetTimestamp();
        long whole = ticks / Frequency * NanosPerSecond;
        long frac = ticks % Frequency * NanosPerSecond / Frequency;
        return whole + frac;
    }
}
