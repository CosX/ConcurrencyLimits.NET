namespace ConcurrencyLimits.Limit.Measurement;

public sealed class MinimumMeasurement : IMeasurement
{
    private double _value;

    public double Add(double sample)
    {
        if (_value == 0.0 || sample < _value)
        {
            _value = sample;
        }
        return _value;
    }

    public double Get() => _value;

    public void Reset() => _value = 0.0;

    public void Update(Func<double, double> operation) { }
}
