using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ConcurrencyToolkit.Synchronization;

namespace ConcurrencyToolkit.Benchmarks.Synchronization;

[SimpleJob(RuntimeMoniker.Net80)]
// [SimpleJob(RuntimeMoniker.NativeAot80)]
[MemoryDiagnoser]
[WarmupCount(32)]
[MaxIterationCount(32)]
public class SemaphoreBenchmarks : SemaphoreBenchmarkBase
{
  public Args[] AllTests { get; } =
  {
    new(2, 4, 4 * 64 * 1024, 4),
  };

  [ParamsSource(nameof(AllTests))]
  public Args Parameters { get; set; }

  public record Args(int Permits, int Parallelism, int BatchSize, int ThreadPoolSize)
  {
    public override string ToString() => $"{Permits},{Parallelism},{BatchSize},{ThreadPoolSize}";
  }

  public ISemaphore Impl;

  [GlobalSetup]
  public void Setup()
  {
    Impl = SemaphoreFactory.Create(Parameters.Permits);
    ThreadPool.SetMinThreads(Parameters.ThreadPoolSize, 1);
    ThreadPool.SetMaxThreads(Parameters.ThreadPoolSize, 1);
    CTS = Enumerable.Range(0, Parameters.Parallelism).Select(x => new CancellationTokenSource()).ToArray();
  }

  public CancellationTokenSource[] CTS { get; set; }

  [Benchmark]
  public void BenchmarkSemaphore()
  {
    var semaphore = Impl;
    var parallelism = Parameters.Parallelism;

    var tasks = new List<Task>(parallelism);

    var n = Parameters.BatchSize;

    for (int i = 0; i < parallelism; i++)
    {
      tasks.Add(Task.Run(async () =>
      {
        var toDo = 0;
        while (toDo < n)
        {
          await semaphore.AcquireAsync();
          try
          {
            toDo++;
          }
          finally
          {
            semaphore.Release();
          }
        }
      }));
    }

    foreach (var task in tasks)
      task.GetAwaiter().GetResult();
  }

  [Benchmark]
  public void BenchmarkSemaphore_WithToken()
  {
    var semaphore = Impl;
    var parallelism = Parameters.Parallelism;

    var tasks = new List<Task>(parallelism);

    var n = Parameters.BatchSize;

    for (int i = 0; i < parallelism; i++)
    {
      var token = CTS[i].Token;
      tasks.Add(Task.Run(async () =>
      {
        var toDo = 0;
        while (toDo < n)
        {
          await semaphore.AcquireAsync(token);
          try
          {
            toDo++;
          }
          finally
          {
            semaphore.Release();
          }
        }
      }));
    }

    foreach (var task in tasks)
      task.GetAwaiter().GetResult();
  }

  [Benchmark]
  public void BenchmarkSemaphoreSync()
  {
    var semaphore = Impl;
    var parallelism = Parameters.Parallelism;

    var tasks = new List<Task>(parallelism);

    var n = Parameters.BatchSize;

    for (int i = 0; i < parallelism; i++)
    {
      tasks.Add(Task.Run(() =>
      {
        var toDo = 0;
        while (toDo < n)
        {
          semaphore.Acquire();
          try
          {
            toDo++;
          }
          finally
          {
            semaphore.Release();
          }
        }
      }));
    }

    foreach (var task in tasks)
      task.GetAwaiter().GetResult();
  }

  [Benchmark]
  public void BenchmarkSemaphoreSync_WithToken()
  {
    var semaphore = Impl;
    var parallelism = Parameters.Parallelism;

    var tasks = new List<Task>(parallelism);

    var n = Parameters.BatchSize;

    for (int i = 0; i < parallelism; i++)
    {
      var token = CTS[i].Token;
      tasks.Add(Task.Run(() =>
      {
        var toDo = 0;
        while (toDo < n)
        {
          semaphore.Acquire(token);
          try
          {
            toDo++;
          }
          finally
          {
            semaphore.Release();
          }
        }
      }));
    }

    foreach (var task in tasks)
      task.GetAwaiter().GetResult();
  }
}