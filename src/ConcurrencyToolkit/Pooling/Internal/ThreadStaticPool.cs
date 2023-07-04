// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Runtime.CompilerServices;

namespace ConcurrencyToolkit.Pooling.LiteObjectPool;

/// <summary>
/// A simple object pool, which only has one per thread slot.
/// </summary>
internal static class ThreadStaticPool<T> where T : class
{
  [ThreadStatic] private static T? item;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static T? TryRent()
  {
    T? obj;

    obj = item;
    if (obj is not null)
    {
      item = null;
      return obj;
    }
    return null;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void Return(T obj)
  {
    if (item is null)
      item = obj;
  }
}