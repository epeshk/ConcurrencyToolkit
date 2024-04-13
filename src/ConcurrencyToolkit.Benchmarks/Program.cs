// ReSharper disable RedundantUsingDirective

using System.Diagnostics;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using ConcurrencyToolkit.Benchmarks.Collections;
using ConcurrencyToolkit.Benchmarks.Metrics;
using ConcurrencyToolkit.Benchmarks.Pooling;
using ConcurrencyToolkit.Benchmarks.Synchronization;
using ConcurrencyToolkit.Benchmarks.Threading;
using ConcurrencyToolkit.Threading;


BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
// BenchmarkRunner.Run<SemaphoreBenchmarks>();
// BenchmarkRunner.Run<CancellationSemaphoreBenchmarks>();
// BenchmarkRunner.Run<SemaphoreSingleThreadBenchmarks>();
// BenchmarkRunner.Run<BoundedConcurrentQueueBenchmark>();
// BenchmarkRunner.Run<SingleWriterDictionaryBenchmarks>();
// BenchmarkRunner.Run<ArrayPoolBenchmark>();
// BenchmarkRunner.Run<InterlockedFloatingPointBenchmark>();
// BenchmarkRunner.Run<ThreadSafeCounterBenchmarks>();
// BenchmarkRunner.Run<ThreadSafeCounterDoubleBenchmarks>();
// BenchmarkRunner.Run<ShardingIdBenchmark>();
