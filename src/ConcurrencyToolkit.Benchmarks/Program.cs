// ReSharper disable RedundantUsingDirective
using System.Reflection;
using BenchmarkDotNet.Running;
using ConcurrencyToolkit.Benchmarks.Collections;
using ConcurrencyToolkit.Benchmarks.Pooling;
using ConcurrencyToolkit.Benchmarks.Synchronization;

BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run();
// BenchmarkRunner.Run<SemaphoreBenchmarks>();
// BenchmarkRunner.Run<CancellationSemaphoreBenchmarks>();
// BenchmarkRunner.Run<SemaphoreSingleThreadBenchmarks>();
// BenchmarkRunner.Run<BoundedConcurrentQueueBenchmark>();
// BenchmarkRunner.Run<SingleWriterDictionaryBenchmarks>();
// BenchmarkRunner.Run<ArrayPoolBenchmark>();