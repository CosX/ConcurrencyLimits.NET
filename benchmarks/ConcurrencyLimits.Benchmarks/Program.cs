using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

// Run all:      dotnet run -c Release --project benchmarks/ConcurrencyLimits.Benchmarks -- --filter '*'
// Quick pass:   append `--job short`
// One class:    --filter '*GateThroughput*'
IConfig config = DefaultConfig.Instance
    // Gate/algorithm names are longer than the default 20-char parameter column and would be truncated.
    .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(40));
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
