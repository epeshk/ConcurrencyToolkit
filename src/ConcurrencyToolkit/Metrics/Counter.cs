// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics;

namespace ConcurrencyToolkit.Metrics;

internal static class Counter<TKey> where TKey : struct
{
#if CONCURRENCY_METRICS
  private static readonly ThreadSafeCounter64 counter = new();

  static Counter() => Counters.Register(typeof(TKey), counter);
#endif

  [Conditional("CONCURRENCY_METRICS")]
  public static void Add(long value)
  {
#if CONCURRENCY_METRICS
    counter.Add(value);
#endif
  }

  [Conditional("CONCURRENCY_METRICS")]
  public static void Increment()
  {
#if CONCURRENCY_METRICS
    counter.Increment();
#endif
  }
}