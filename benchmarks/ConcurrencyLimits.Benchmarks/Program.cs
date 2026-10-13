using BenchmarkDotNet.Running;

// Run all:      dotnet run -c Release --project benchmarks/ConcurrencyLimits.Benchmarks -- --filter '*'
// Quick pass:   append `--job short`
// One class:    --filter '*GateThroughput*'
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
