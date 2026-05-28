namespace ConcurrencyLimits.Limit.Measurement;

/// <summary>
/// Contract for tracking a measurement such as a minimum or average of a sample set.
/// </summary>
public interface IMeasurement
{
    /// <summary>Add a single sample and update the internal state. Returns the current value.</summary>
    double Add(double sample);

    /// <summary>Return the current value.</summary>
    double Get();

    /// <summary>Reset the internal state as if no samples were ever added.</summary>
    void Reset();

    void Update(Func<double, double> operation);
}
