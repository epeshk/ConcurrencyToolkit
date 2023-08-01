using System.Diagnostics;
using ConcurrencyToolkit.Synchronization;
using FluentAssertions;

namespace ConcurrencyToolkit.Tests.Synchronization;

public class QueueSemaphoreTests : SemaphoreTestsBase
{
  public override Func<int, ISemaphore> Create => limit => new QueueSemaphore(limit);
}

public class StackSemaphoreTests : SemaphoreTestsBase
{
  public override Func<int, ISemaphore> Create => limit => new StackSemaphore(limit);
}

public class SimpleSegmentSemaphoreTests : SemaphoreTestsBase
{
  public override Func<int, ISemaphore> Create => limit => new SimpleSegmentSemaphore(limit);
}

public class SegmentSemaphoreTests : SemaphoreTestsBase
{
  public override Func<int, ISemaphore> Create => limit => new SegmentSemaphore(limit);

  [Test]
  public void CancellationWithSegmentRemoval()
  {
    var s = new SegmentSemaphore(0);

    var segmentSize = 16;

    s.SegmentsCount.Should().Be(1);
    for (var i = 0; i < segmentSize; ++i)
      s.AcquireAsync();

    var cts = new CancellationTokenSource();
    for (var i = 0; i < segmentSize; ++i)
      s.AcquireAsync(cts.Token);

    s.SegmentsCount.Should().Be(2);
    for (var i = 0; i < segmentSize; ++i)
      s.AcquireAsync();

    s.SegmentsCount.Should().Be(3);
    s.CurrentQueue.Should().Be(3 * segmentSize);
    cts.Cancel();
    s.SegmentsCount.Should().Be(2);

    s.CurrentQueue.Should().Be(2 * segmentSize);
  }
}

public class SlimSemaphoreTests : SemaphoreTestsBase
{
  public override Func<int, ISemaphore> Create => limit => new SemaphoreSlimWrapper(new(limit));
}

public class PrioritySemaphoreTests : SemaphoreTestsBase
{
  public override Func<int, ISemaphore> Create => limit => new PrioritySemaphore(limit, 0).WithPriority(0);

  [Test]
  public void ShouldReleaseWaitersInPriority_Fifo()
  {
    var semaphore = new PrioritySemaphore(0, 2);

    var highP_1 = semaphore.WithPriority(1).AcquireAsync();
    var lowP = semaphore.WithPriority(0).AcquireAsync();
    var highP_2 = semaphore.WithPriority(1).AcquireAsync();

    highP_1.IsCompleted.Should().BeFalse();
    highP_2.IsCompleted.Should().BeFalse();
    lowP.IsCompleted.Should().BeFalse();

    semaphore.Release();
    highP_1.IsCompletedSuccessfully.Should().BeTrue();
    semaphore.Release();
    highP_2.IsCompletedSuccessfully.Should().BeTrue();
    semaphore.Release();
    lowP.IsCompletedSuccessfully.Should().BeTrue();
  }

  [Test]
  public void ShouldReleaseWaitersInPriority_Lifo()
  {
    var semaphore = new PrioritySemaphore(0, 2, true);

    var highP_1 = semaphore.WithPriority(1).AcquireAsync();
    var lowP = semaphore.WithPriority(0).AcquireAsync();
    var highP_2 = semaphore.WithPriority(1).AcquireAsync();

    highP_1.IsCompleted.Should().BeFalse();
    highP_2.IsCompleted.Should().BeFalse();
    lowP.IsCompleted.Should().BeFalse();

    semaphore.Release();
    highP_2.IsCompletedSuccessfully.Should().BeTrue();
    semaphore.Release();
    highP_1.IsCompletedSuccessfully.Should().BeTrue();
    semaphore.Release();
    lowP.IsCompletedSuccessfully.Should().BeTrue();
  }

  [Test]
  public void MaxPriorityTest()
  {
    var sem = new PrioritySemaphore(2, maxPriority: 2);
    sem.MaxPriority.Should().Be(2);

    sem.WithPriority(0).Should().NotBeNull();
    sem.WithPriority(1).Should().NotBeNull();
    sem.WithPriority(2).Should().NotBeNull();

    Assert.Throws<ArgumentOutOfRangeException>(() => sem.WithPriority(-1));
    Assert.Throws<ArgumentOutOfRangeException>(() => sem.WithPriority(3));
  }
}

public abstract class SemaphoreTestsBase
{
  public const int TimeoutMs = 10000;
  public static TimeSpan TimeLimit = TimeSpan.FromSeconds(2);

  public abstract Func<int, ISemaphore> Create { get; }

  [TestCase(1, 4)]
  [TestCase(2, 4)]
  [TestCase(3, 4)]
  [TestCase(4, 4, true)]
  [Timeout(TimeoutMs)]
  public void SemaphoreShouldLimitAsyncConsumers(int limit, int threads, bool withWorkload = false)
  {
    var semaphore = Create(limit);

    var tasks = new Task[threads];
    var sw = Stopwatch.StartNew();
    var TL = TimeLimit;

    var parallelTasks = 0;
    var execs = new int[threads];

    for (int i = 0; i < threads; i++)
    {
      var num = i;
      tasks[i] = Task.Run(async () =>
      {
        while (sw.Elapsed < TL)
        {
          for (int j = 0; j < 1000; j++)
          {
            await semaphore.AcquireAsync();
            try
            {
              if (Interlocked.Increment(ref parallelTasks) > limit)
                Assert.Fail("Semaphore is broken");
              execs[num]++;

              if (withWorkload)
                Workload(0.01);

              Interlocked.Decrement(ref parallelTasks);
            }
            finally
            {
              semaphore.Release();
            }
          }
        }
      });
    }

    Task.WaitAll(tasks);
    execs.Should().AllSatisfy(x => x.Should().BePositive());
    parallelTasks.Should().Be(0);
    semaphore.CurrentCount.Should().Be(limit);

    Console.WriteLine($"exec: {string.Join(" ", execs)}");
  }

  [TestCase(1, 4)]
  [TestCase(2, 4)]
  [TestCase(3, 4)]
  [TestCase(4, 4, true)]
  [Timeout(TimeoutMs)]
  public void SemaphoreShouldLimitAsyncConsumers_WithCancellation(int limit, int threads, bool withWorkload = false)
  {
    var semaphore = Create(limit);

    var tasks = new Task[threads];
    var sw = Stopwatch.StartNew();
    var TL = TimeLimit;

    var parallelTasks = 0;
    var execs = new int[threads];
    var canc = new int[threads];
    var cts = new CancellationTokenSource?[threads];

    for (int i = 0; i < threads; i++)
    {
      var num = i;
      tasks[i] = Task.Run(async () =>
      {
        try
        {
          while (sw.Elapsed < TL)
          {
            for (int j = 0; j < 1000; j++)
            {
              var myCts = new CancellationTokenSource();
              Volatile.Write(ref cts[num], myCts);

              if (!await semaphore.TryAcquireAsync(myCts.Token))
              {
                canc[num]++;
                continue;
              }

              try
              {
                var cur = Interlocked.Increment(ref parallelTasks);
                if (cur > limit)
                  Assert.Fail($"Semaphore is broken. cur: {cur}, limit: {limit}");
                execs[num]++;
                if (Random.Shared.NextDouble() < 0.1)
                {
                  var otherCts = Volatile.Read(ref cts[Random.Shared.Next(threads)]);
                  otherCts?.Cancel();
                }

                if (withWorkload)
                  Workload(0.01);

                Interlocked.Decrement(ref parallelTasks);
              }
              finally
              {
                semaphore.Release();
              }
            }
          }
        }
        catch (Exception e)
        {
          Console.WriteLine(e);
          throw;
        }
      });
    }

    Task.WaitAll(tasks);
    execs.Should().AllSatisfy(x => x.Should().BePositive());
    if (limit < threads)
      canc.Should().AllSatisfy(x => x.Should().BePositive());
    parallelTasks.Should().Be(0);

    Console.WriteLine($"exec: {string.Join(" ", execs)}");
    Console.WriteLine($"canc: {string.Join(" ", canc)}");
  }

  [TestCase(1, 4)]
  [TestCase(2, 4)]
  [TestCase(3, 4)]
  [TestCase(4, 4, true)]
  [Timeout(TimeoutMs)]
  public void SemaphoreShouldLimitAsyncConsumers_WithCancellation_Throwing(int limit, int threads, bool withWorkload = false)
  {
    var semaphore = Create(limit);

    var tasks = new Task[threads];
    var sw = Stopwatch.StartNew();
    var TL = TimeLimit;

    var parallelTasks = 0;
    var execs = new int[threads];
    var canc = new int[threads];
    var cts = new CancellationTokenSource?[threads];

    for (int i = 0; i < threads; i++)
    {
      var num = i;
      tasks[i] = Task.Run(async () =>
      {
        try
        {
          while (sw.Elapsed < TL)
          {
            for (int j = 0; j < 1000; j++)
            {
              var myCts = new CancellationTokenSource();
              Volatile.Write(ref cts[num], myCts);

              try
              {
                await semaphore.AcquireAsync(myCts.Token);
              }
              catch (OperationCanceledException)
              {
                canc[num]++;
                continue;
              }

              try
              {
                var cur = Interlocked.Increment(ref parallelTasks);
                if (cur > limit)
                  Assert.Fail($"Semaphore is broken. cur: {cur}, limit: {limit}");
                execs[num]++;
                if (Random.Shared.NextDouble() < 0.1)
                {
                  var otherCts = Volatile.Read(ref cts[Random.Shared.Next(threads)]);
                  otherCts?.Cancel();
                }

                if (withWorkload)
                  Workload(0.01);

                Interlocked.Decrement(ref parallelTasks);
              }
              finally
              {
                semaphore.Release();
              }
            }
          }
        }
        catch (Exception e)
        {
          Console.WriteLine(e);
          throw;
        }
      });
    }

    Task.WaitAll(tasks);
    execs.Should().AllSatisfy(x => x.Should().BePositive());
    if (limit < threads)
      canc.Should().AllSatisfy(x => x.Should().BePositive());
    parallelTasks.Should().Be(0);

    Console.WriteLine($"exec: {string.Join(" ", execs)}");
    Console.WriteLine($"canc: {string.Join(" ", canc)}");
  }

  [TestCase(1, 4)]
  [TestCase(2, 4)]
  [TestCase(3, 4)]
  [TestCase(4, 4, true)]
  [Timeout(TimeoutMs)]
  public void SemaphoreShouldLimitSyncConsumers_WithCancellation(int limit, int threads, bool withWorkload = false)
  {
    var semaphore = Create(limit);

    var tasks = new Task[threads];
    var sw = Stopwatch.StartNew();
    var TL = TimeLimit;

    var parallelTasks = 0;
    var execs = new int[threads];
    var canc = new int[threads];
    var cts = new CancellationTokenSource?[threads];

    for (int i = 0; i < threads; i++)
    {
      var num = i;
      tasks[i] = Task.Run(() =>
      {
        try
        {
          while (sw.Elapsed < TL)
          {
            for (int j = 0; j < 1000; j++)
            {
              var myCts = new CancellationTokenSource();
              Volatile.Write(ref cts[num], myCts);

              if (!semaphore.TryAcquire(myCts.Token))
              {
                canc[num]++;
                continue;
              }

              try
              {
                var cur = Interlocked.Increment(ref parallelTasks);
                if (cur > limit)
                  Assert.Fail($"Semaphore is broken. cur: {cur}, limit: {limit}");
                execs[num]++;
                if (Random.Shared.NextDouble() < 0.1)
                {
                  var otherCts = Volatile.Read(ref cts[Random.Shared.Next(threads)]);
                  otherCts?.Cancel();
                }

                if (withWorkload)
                  Workload(0.01);

                Interlocked.Decrement(ref parallelTasks);
              }
              finally
              {
                semaphore.Release();
              }
            }
          }
        }
        catch (Exception e)
        {
          Console.WriteLine(e);
          throw;
        }
      });
    }

    Task.WaitAll(tasks);
    execs.Should().AllSatisfy(x => x.Should().BePositive());
    if (limit < threads)
      canc.Should().AllSatisfy(x => x.Should().BePositive());
    parallelTasks.Should().Be(0);

    Console.WriteLine($"exec: {string.Join(" ", execs)}");
    Console.WriteLine($"canc: {string.Join(" ", canc)}");
  }

  [TestCase(1, 4)]
  [TestCase(2, 4)]
  [TestCase(3, 4)]
  [TestCase(4, 4, true)]
  [Timeout(TimeoutMs)]
  public void SemaphoreShouldLimitSyncConsumers_WithCancellation_Throwing(int limit, int threads, bool withWorkload = false)
  {
    var semaphore = Create(limit);

    var tasks = new Task[threads];
    var sw = Stopwatch.StartNew();
    var TL = TimeLimit;

    var parallelTasks = 0;
    var execs = new int[threads];
    var canc = new int[threads];
    var cts = new CancellationTokenSource?[threads];

    for (int i = 0; i < threads; i++)
    {
      var num = i;
      tasks[i] = Task.Run(() =>
      {
        try
        {
          while (sw.Elapsed < TL)
          {
            for (int j = 0; j < 1000; j++)
            {
              var myCts = new CancellationTokenSource();
              Volatile.Write(ref cts[num], myCts);

              try
              {
                semaphore.Acquire(myCts.Token);
              }
              catch (OperationCanceledException)
              {
                canc[num]++;
                continue;
              }

              try
              {
                var cur = Interlocked.Increment(ref parallelTasks);
                if (cur > limit)
                  Assert.Fail($"Semaphore is broken. cur: {cur}, limit: {limit}");
                execs[num]++;
                if (Random.Shared.NextDouble() < 0.1)
                {
                  var otherCts = Volatile.Read(ref cts[Random.Shared.Next(threads)]);
                  otherCts?.Cancel();
                }

                if (withWorkload)
                  Workload(0.01);

                Interlocked.Decrement(ref parallelTasks);
              }
              finally
              {
                semaphore.Release();
              }
            }
          }
        }
        catch (Exception e)
        {
          Console.WriteLine(e);
          throw;
        }
      });
    }

    Task.WaitAll(tasks);
    execs.Should().AllSatisfy(x => x.Should().BePositive());
    if (limit < threads)
      canc.Should().AllSatisfy(x => x.Should().BePositive());
    parallelTasks.Should().Be(0);

    Console.WriteLine($"exec: {string.Join(" ", execs)}");
    Console.WriteLine($"canc: {string.Join(" ", canc)}");
  }

  [TestCase(1, 4)]
  [TestCase(2, 4)]
  [TestCase(3, 4)]
  [TestCase(4, 4, true)]
  [Timeout(TimeoutMs)]
  public void SemaphoreShouldLimitSyncConsumers(int limit, int threads, bool withWorkload = false)
  {
    var semaphore = Create(limit);

    var tasks = new Task[threads];
    var sw = Stopwatch.StartNew();
    var TL = TimeLimit;

    var parallelTasks = 0;
    var execs = new int[threads];
    var fullThroughputReached = false;

    for (int i = 0; i < threads; i++)
    {
      var num = i;
      tasks[i] = Task.Run(() =>
      {
        while (sw.Elapsed < TL)
        {
          for (int j = 0; j < 1000; j++)
          {
            semaphore.Acquire();
            try
            {
              var currentWorkers = Interlocked.Increment(ref parallelTasks);
              if (currentWorkers > limit)
                Assert.Fail("Semaphore is broken");
              if (!fullThroughputReached && currentWorkers == limit)
                fullThroughputReached = true;
              execs[num]++;

              if (withWorkload)
                Workload(0.01);

              Interlocked.Decrement(ref parallelTasks);
            }
            finally
            {
              semaphore.Release();
            }
          }
        }
      });
    }

    Task.WaitAll(tasks);
    execs.Should().AllSatisfy(x => x.Should().BePositive());
    parallelTasks.Should().Be(0);
    fullThroughputReached.Should().BeTrue();
    semaphore.CurrentCount.Should().Be(limit);
    // semaphore.CurrentQueue.Should().Be(0); // some semaphores settle CurrentQueue asynchronously

    Console.WriteLine($"exec: {string.Join(" ", execs)}");
  }

  [Test]
  public void QueueCount()
  {
    var semaphore = Create(1);
    var cts1 = new CancellationTokenSource();
    var cts2 = new CancellationTokenSource();

    semaphore.AcquireAsync().IsCompleted.Should().BeTrue();
    semaphore.AcquireAsync(cts1.Token).IsCompleted.Should().BeFalse();
    semaphore.AcquireAsync(cts2.Token).IsCompleted.Should().BeFalse();
    semaphore.AcquireAsync(cts1.Token).IsCompleted.Should().BeFalse();
    semaphore.AcquireAsync(cts1.Token).IsCompleted.Should().BeFalse();
    semaphore.AcquireAsync(cts2.Token).IsCompleted.Should().BeFalse();

    semaphore.CurrentQueue.Should().Be(5);
    cts1.Cancel();
    if (semaphore is SemaphoreSlimWrapper) Thread.Sleep(100);
    semaphore.CurrentQueue.Should().Be(2);
    semaphore.Release();
    if (semaphore is SemaphoreSlimWrapper) Thread.Sleep(100);
    semaphore.CurrentQueue.Should().Be(1);
    cts2.Cancel();
    if (semaphore is SemaphoreSlimWrapper) Thread.Sleep(100);
    semaphore.CurrentQueue.Should().Be(0);
  }

  [Test]
  public void QueueCount_TryAcquireAsync()
  {
    var semaphore = Create(1);
    var cts1 = new CancellationTokenSource();
    var cts2 = new CancellationTokenSource();

    semaphore.TryAcquireAsync().IsCompleted.Should().BeTrue();
    semaphore.TryAcquireAsync(cts1.Token).IsCompleted.Should().BeFalse();
    semaphore.TryAcquireAsync(cts2.Token).IsCompleted.Should().BeFalse();
    semaphore.TryAcquireAsync(cts1.Token).IsCompleted.Should().BeFalse();
    semaphore.TryAcquireAsync(cts1.Token).IsCompleted.Should().BeFalse();
    semaphore.TryAcquireAsync(cts2.Token).IsCompleted.Should().BeFalse();

    semaphore.CurrentQueue.Should().Be(5);
    cts1.Cancel();
    if (semaphore is SemaphoreSlimWrapper) Thread.Sleep(100);
    semaphore.CurrentQueue.Should().Be(2);
    semaphore.Release();
    if (semaphore is SemaphoreSlimWrapper) Thread.Sleep(100);
    semaphore.CurrentQueue.Should().Be(1);
    cts2.Cancel();
    if (semaphore is SemaphoreSlimWrapper) Thread.Sleep(100);
    semaphore.CurrentQueue.Should().Be(0);
  }

  private void Workload(double p)
  {
    while (Random.Shared.NextDouble() > p) ;
  }
}