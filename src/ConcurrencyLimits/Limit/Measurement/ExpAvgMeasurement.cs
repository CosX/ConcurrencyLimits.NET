namespace ConcurrencyLimits.Limit.Measurement;

public sealed class ExpAvgMeasurement : IMeasurement
{
    private double _value;
    private double _sum;
    private readonly int _window;
    private readonly int _warmupWindow;
    private int _count;

    public ExpAvgMeasurement(int window, int warmupWindow)
    {
        _window = window;
        _warmupWindow = warmupWindow;
        _sum = 0.0;
    }

    public double Add(double sample)
    {
        if (_count < _warmupWindow)
        {
            _count++;
            _sum += sample;
            _value = _sum / _count;
        }
        else
        {
            double factor = Factor(_window);
            _value = _value * (1 - factor) + sample * factor;
        }
        return _value;
    }

    private static double Factor(int n) => 2.0 / (n + 1);

    public double Get() => _value;

    public void Reset()
    {
        _value = 0.0;
        _count = 0;
        _sum = 0.0;
    }

    public void Update(Func<double, double> operation) => _value = operation(_value);
}
