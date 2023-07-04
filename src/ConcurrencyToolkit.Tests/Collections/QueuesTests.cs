// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Collections.Concurrent;
using ConcurrencyToolkit.Collections.Queues;
using FluentAssertions;

namespace ConcurrencyToolkit.Tests.Collections;

public class FixedSizeQueueTests : QueuesTests
{
  public override Func<IProducerConsumerCollection<int>> Create { get; } = () => new FixedSizeConcurrentQueue<int>(1024);
}

public class UnpaddedFixedSizeQueueTests : QueuesTests
{
  public override Func<IProducerConsumerCollection<int>> Create { get; } = () => new UnpaddedFixedSizeConcurrentQueue<int>(1024);
}

public abstract class QueuesTests
{
  public abstract Func<IProducerConsumerCollection<int>> Create { get; }

  [Test, Timeout(10000)]
  public void Test()
  {
    var q = Create();
    int n = 1024 * 1024;
    var writers = Enumerable.Range(0, 2).Select(x => Task.Run(() =>
    {
      for (int i = 0; i < n; ++i)
        while (!q.TryAdd(i));
    })).ToArray();

    var vals = new List<int>();
    while (vals.Count < 2 * n)
    {
      int val;
      while (!q.TryTake(out val)) ;
      vals.Add(val);
    }

    Task.WaitAll(writers);

    var lens = vals.GroupBy(x => x).Select(x => x.Count()).Distinct().ToArray();
    lens.Length.Should().Be(1);
    lens[0].Should().Be(2);
  }
}