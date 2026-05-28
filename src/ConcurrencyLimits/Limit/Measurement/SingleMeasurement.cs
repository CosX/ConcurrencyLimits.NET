namespace ConcurrencyLimits.Limit.Measurement;

public sealed class SingleMeasurement : IMeasurement
{
    private double _value;

    public double Add(double sample) => _value = sample;

    public double Get() => _value;

    public void Reset() => _value = 0.0;

    public void Update(Func<double, double> operation) => _value = operation(_value);
}
