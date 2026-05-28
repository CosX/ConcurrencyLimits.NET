using ConcurrencyLimits.Limit.Measurement;
using ConcurrencyLimits.Limit.Window;
using Xunit;

namespace ConcurrencyLimits.Tests;

public class ExpAvgMeasurementTest
{
    [Fact]
    public void TestWarmup()
    {
        var avg = new ExpAvgMeasurement(100, 10);

        double[] expected = { 10.0, 10.5, 11, 11.5, 12, 12.5, 13, 13.5, 14, 14.5 };
        for (int i = 0; i < 10; i++)
        {
            avg.Add(i + 10);
            Assert.Equal(expected[i], avg.Get(), 0.01);
        }

        avg.Add(100);
        Assert.Equal(16.2, avg.Get(), 0.1);
    }
}

public class ImmutableAverageSampleWindowTest
{
    private const long BigRtt = 5000;
    private const long ModerateRtt = 500;
    private const long LowRtt = 10;

    [Fact]
    public void CalculateAverage()
    {
        ISampleWindow window = new ImmutableAverageSampleWindow();
        window = window.AddSample(BigRtt, 1, false);
        window = window.AddSample(ModerateRtt, 1, false);
        window = window.AddSample(LowRtt, 1, false);
        Assert.Equal((BigRtt + ModerateRtt + LowRtt) / 3, window.GetTrackedRttNanos());
    }

    [Fact]
    public void DroppedSampleShouldChangeTrackedAverage()
    {
        ISampleWindow window = new ImmutableAverageSampleWindow();
        window = window.AddSample(BigRtt, 1, false);
        window = window.AddSample(ModerateRtt, 1, false);
        window = window.AddSample(LowRtt, 1, false);
        window = window.AddSample(BigRtt, 1, true);
        Assert.Equal((BigRtt + ModerateRtt + LowRtt + BigRtt) / 4, window.GetTrackedRttNanos());
    }
}

public class ImmutablePercentileSampleWindowTest
{
    private const long BigRtt = 5000;
    private const long ModerateRtt = 500;
    private const long LowRtt = 10;

    [Fact]
    public void CalculateP50()
    {
        ISampleWindow window = new ImmutablePercentileSampleWindow(0.5, 10);
        window = window.AddSample(BigRtt, 0, false);
        window = window.AddSample(ModerateRtt, 0, false);
        window = window.AddSample(LowRtt, 0, false);
        Assert.Equal(ModerateRtt, window.GetTrackedRttNanos());
    }

    [Fact]
    public void DroppedSampleShouldChangeTrackedRtt()
    {
        ISampleWindow window = new ImmutablePercentileSampleWindow(0.5, 10);
        window = window.AddSample(LowRtt, 1, false);
        window = window.AddSample(BigRtt, 1, true);
        window = window.AddSample(BigRtt, 1, true);
        Assert.Equal(BigRtt, window.GetTrackedRttNanos());
    }

    [Fact]
    public void P999ReturnsSlowestObservedRtt()
    {
        ISampleWindow window = new ImmutablePercentileSampleWindow(0.999, 10);
        window = window.AddSample(BigRtt, 1, false);
        window = window.AddSample(ModerateRtt, 1, false);
        window = window.AddSample(LowRtt, 1, false);
        Assert.Equal(BigRtt, window.GetTrackedRttNanos());
    }

    [Fact]
    public void RttObservationOrderDoesntAffectResultValue()
    {
        ISampleWindow window = new ImmutablePercentileSampleWindow(0.999, 10);
        window = window.AddSample(ModerateRtt, 1, false);
        window = window.AddSample(LowRtt, 1, false);
        window = window.AddSample(BigRtt, 1, false);
        window = window.AddSample(LowRtt, 1, false);
        Assert.Equal(BigRtt, window.GetTrackedRttNanos());
    }
}
