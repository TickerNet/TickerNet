using BenchmarkDotNet.Running;
using TickerNet.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
