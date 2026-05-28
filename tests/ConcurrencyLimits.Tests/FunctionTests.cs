using ConcurrencyLimits.Limit.Functions;
using Xunit;

namespace ConcurrencyLimits.Tests;

public class SquareRootFunctionTest
{
    [Fact]
    public void Confirm0Index() => Assert.Equal(4, SquareRootFunction.Create(4)(0));

    [Fact]
    public void ConfirmMaxIndex() => Assert.Equal(31, SquareRootFunction.Create(4)(999));

    [Fact]
    public void ConfirmOutOfLookupRange() => Assert.Equal(50, SquareRootFunction.Create(4)(2500));
}

#pragma warning disable CS0618
public class Log10RootFunctionTest
{
    [Fact]
    public void Test0Index() => Assert.Equal(1, Log10RootFunction.Create(0)(0));

    [Fact]
    public void TestInRange() => Assert.Equal(2, Log10RootFunction.Create(0)(100));

    [Fact]
    public void TestOutOfLookupRange() => Assert.Equal(4, Log10RootFunction.Create(0)(10000));
}
#pragma warning restore CS0618

public class Log10RootIntFunctionTest
{
    [Fact]
    public void Test0Index() => Assert.Equal(1, Log10RootIntFunction.Create(0)(0));

    [Fact]
    public void TestInRange() => Assert.Equal(2, Log10RootIntFunction.Create(0)(100));

    [Fact]
    public void TestOutOfLookupRange() => Assert.Equal(4, Log10RootIntFunction.Create(0)(10000));
}
