using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ConcurrencyToolkit.Synchronization;

namespace ConcurrencyToolkit.Benchmarks.Synchronization;

[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
// [SimpleJob(RuntimeMoniker.NativeAot80)]
[MemoryDiagnoser]
[WarmupCount(32)]
[MaxIterationCount(32)]
public class CancellationSemaphoreBenchmarks : SemaphoreBenchmarkBase
{
  public Args[] AllTests { get; } =
  {
    new(2, 4, 4 * 64 * 1024, 4, 0.1),
    new(2, 4, 4 * 64 * 1024, 4, 0.8),
  };

  [ParamsSource(nameof(AllTests))]
  public Args Parameters { get; set; }

  public record Args(int Permits, int Parallelism, int BatchSize, int ThreadPoolSize, double CancellationProbability)
  {
    public override string ToString() => $"{Permits},{Parallelism},{BatchSize},{ThreadPoolSize},{CancellationProbability}";
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
    var Parallelism = Parameters.Parallelism;

    var tasks = new List<Task>(Parallelism);
    var cts = new PaddedReference<CancellationTokenSource>[Parallelism];

    var n = Parameters.BatchSize;

    var inside = 0;
    var MaxPermits = Parameters.Permits;

    for (int i = 0; i < Parallelism; i++)
    {
      var taskId = i;
      tasks.Add(Task.Run(async () =>
      {
        var toDo = 0;
        var p = Parameters.CancellationProbability;
        var source = cts[taskId].Object = new();
        while (toDo < n)
        {
          try
          {
            if (!await semaphore.TryAcquireAsync(source.Token))
            {
              cts[taskId].Object = source = new();
              continue;
            }
          }
          catch (OutOfMemoryException)
          {
            continue;
          }

          try
          {
            var curInside = Interlocked.Increment(ref inside);
            if (curInside > MaxPermits)
              throw new($"Limit violation {curInside} > {MaxPermits}");
            toDo++;

            if (Random.Shared.NextDouble() < p)
            {
              int rand = taskId;
              while (rand == taskId)
                rand = Random.Shared.Next(cts.Length);
              if (rand != taskId)
                cts[Random.Shared.Next(cts.Length)].Object?.Cancel();
            }
          }
          finally
          {
            Interlocked.Decrement(ref inside);
            semaphore.Release();
          }
        }
      }));
    }

    foreach (var task in tasks)
      task.GetAwaiter().GetResult();

    if (semaphore.CurrentCount > MaxPermits)
      throw new($"semaphore.CurrentCount > MaxPermits: {semaphore.CurrentCount} > {MaxPermits}");

    if (semaphore.CurrentCount < MaxPermits)
      DrainSemaphore(MaxPermits, semaphore);
  }
  [Benchmark]
  public void BenchmarkSemaphore_Throwing()
  {
    var semaphore = Impl;
    var Parallelism = Parameters.Parallelism;

    var tasks = new List<Task>(Parallelism);
    var cts = new PaddedReference<CancellationTokenSource>[Parallelism];

    var n = Parameters.BatchSize;

    var inside = 0;
    var MaxPermits = Parameters.Permits;

    for (int i = 0; i < Parallelism; i++)
    {
      var taskId = i;
      tasks.Add(Task.Run(async () =>
      {
        var toDo = 0;
        var p = Parameters.CancellationProbability;
        var source = cts[taskId].Object = new();
        while (toDo < n)
        {
          try
          {
            try
            {
              await semaphore.AcquireAsync(source.Token);
            }
            catch (OperationCanceledException)
            {
              cts[taskId].Object = source = new();
              continue;
            }
          }
          catch (OutOfMemoryException)
          {
            continue;
          }

          try
          {
            var curInside = Interlocked.Increment(ref inside);
            if (curInside > MaxPermits)
              throw new($"Limit violation {curInside} > {MaxPermits}");
            toDo++;

            if (Random.Shared.NextDouble() < p)
            {
              int rand = taskId;
              while (rand == taskId)
                rand = Random.Shared.Next(cts.Length);
              if (rand != taskId)
                cts[Random.Shared.Next(cts.Length)].Object?.Cancel();
            }
          }
          finally
          {
            Interlocked.Decrement(ref inside);
            semaphore.Release();
          }
        }
      }));
    }

    foreach (var task in tasks)
      task.GetAwaiter().GetResult();

    if (semaphore.CurrentCount > MaxPermits)
      throw new($"semaphore.CurrentCount > MaxPermits: {semaphore.CurrentCount} > {MaxPermits}");

    if (semaphore.CurrentCount < MaxPermits)
      DrainSemaphore(MaxPermits, semaphore);
  }

  // [Benchmark]
  public void BenchmarkSemaphore_Sync()
  {
    var semaphore = Impl;
    var Parallelism = Parameters.Parallelism;

    var tasks = new List<Task>(Parallelism);
    var cts = new PaddedReference<CancellationTokenSource>[Parallelism];

    var n = Parameters.BatchSize;

    var inside = 0;
    var MaxPermits = Parameters.Permits;

    for (int i = 0; i < Parallelism; i++)
    {
      var taskId = i;
      tasks.Add(Task.Run(() =>
      {
        var toDo = 0;
        var p = Parameters.CancellationProbability;
        var source = cts[taskId].Object = new();
        while (toDo < n)
        {
          try
          {
            if (!semaphore.TryAcquire(source.Token))
            {
              cts[taskId].Object = source = new();
              continue;
            }
          }
          catch (OutOfMemoryException)
          {
            continue;
          }

          try
          {
            var curInside = Interlocked.Increment(ref inside);
            if (curInside > MaxPermits)
              throw new($"Limit violation {curInside} > {MaxPermits}");
            toDo++;

            if (Random.Shared.NextDouble() < p)
            {
              int rand = taskId;
              while (rand == taskId)
                rand = Random.Shared.Next(cts.Length);
              if (rand != taskId)
                cts[Random.Shared.Next(cts.Length)].Object?.Cancel();
            }
          }
          finally
          {
            Interlocked.Decrement(ref inside);
            semaphore.Release();
          }
        }
      }));
    }

    foreach (var task in tasks)
      task.GetAwaiter().GetResult();

    if (semaphore.CurrentCount > MaxPermits)
      throw new($"semaphore.CurrentCount > MaxPermits: {semaphore.CurrentCount} > {MaxPermits}");

    if (semaphore.CurrentCount < MaxPermits)
      DrainSemaphore(MaxPermits, semaphore);
  }

  private static void DrainSemaphore(int MaxPermits, ISemaphore semaphore)
  {
    for (int i = 0; i < MaxPermits; ++i)
    {
      var task = semaphore.AcquireAsync();
      if (!task.IsCompletedSuccessfully) throw new("Couldn't drain semaphore at the end of the test");
      task.GetAwaiter().GetResult();
    }

    var finalTask = semaphore.AcquireAsync();
    if (finalTask.IsCompletedSuccessfully) throw new("Semaphore is not limiting concurrency");

    for (int i = 0; i < MaxPermits + 1; ++i)
      semaphore.Release();
    if (!finalTask.IsCompletedSuccessfully) throw new("finalTask is not signaled");
    finalTask.GetAwaiter().GetResult();
  }
}