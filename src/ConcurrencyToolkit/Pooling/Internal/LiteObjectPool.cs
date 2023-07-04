// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Internal;
using ConcurrencyToolkit.Metrics;

namespace ConcurrencyToolkit.Pooling.LiteObjectPool;

/// <summary>
/// An object pool which has one per-thread slot, and one per-core slot.
/// It is similar to the pool from <see cref="PoolingAsyncValueTaskMethodBuilder"/>.
/// </summary>
internal static class LiteObjectPool<T> where T : class
{
  [ThreadStatic] private static T? item;
  private static readonly PaddedReference[] Items = new PaddedReference[Environment.ProcessorCount];

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static T? TryRent()
  {
    T? obj;

    // First, try to get an object from TLS if possible.
    obj = item;
    if (obj is not null)
    {
      Counter<LocalCacheHit>.Increment();
      item = null;
      return obj;
    }

    return TryRentRare();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void Return(T obj)
  {
    if (item is null)
    {
      item = obj;
      return;
    }

    ReturnRare(obj);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void ReturnRare(T obj)
  {
    ref var preCoreSlot = ref Items[Thread.GetCurrentProcessorId() % (uint)Environment.ProcessorCount];

    if (preCoreSlot.Object == null)
      Volatile.Write(ref preCoreSlot.Object, obj);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static T? TryRentRare()
  {
    var obj = Interlocked.Exchange(ref Items[Thread.GetCurrentProcessorId() % (uint) Environment.ProcessorCount].Object, null);
    if (obj is not null)
    {
      Counter<SharedCacheHit>.Increment();
      return Unsafe.As<T>(obj);
    }

    Counter<CacheMiss>.Increment();
    return null;
  }
}