// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Metrics;
using ConcurrencyToolkit.Pooling.LiteObjectPool;

namespace ConcurrencyToolkit.Synchronization;

internal static class RemovableSegmentList
{
  [MethodImpl(MethodImplOptions.NoInlining)]
  public static RemovableSegment FindSegmentAndMoveForward(ref RemovableSegment from, RemovableSegment start, ulong id)
  {
    while (true)
    {
      var to = FindSegment(start, id);
      if (MoveForward(ref from, to))
        return to;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static RemovableSegment FindSegment(RemovableSegment start, ulong id)
  {
    Counter<FindSegmentCalled>.Increment();
    var cur = start;

    while (cur.Id < id || cur.Removed)
    {
      var next = cur.Next;
      if (next != null)
      {
        cur = next;
        continue;
      }

      var newTail = ThreadStaticPool<RemovableSegment>.TryRent() ?? new RemovableSegment();
      newTail.Init(cur.Id + 1, 1, cur.Semaphore);
      newTail.Prev = cur;

      var installedTail = Interlocked.CompareExchange(ref cur.Next, newTail, null) ?? newTail;
      if (installedTail == newTail)
      {
        if (cur.DecrementPointers())
          cur.Remove();
      }
      else
      {
        Counter<SegmentPooled>.Increment();
        newTail.Reset();
        ThreadStaticPool<RemovableSegment>.Return(newTail);
      }

      cur = installedTail;
    }

    return cur;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static bool MoveForward(ref RemovableSegment from, RemovableSegment to)
  {
    while (true)
    {
      var cur = Volatile.Read(ref from);
      if (cur.Id >= to.Id) return true;
      if (!to.IncrementPointers()) return false;
      if (Interlocked.CompareExchange(ref from, to, cur) == cur)
      {
        if (cur.DecrementPointers())
          cur.Remove();
        return true;
      }
      if (to.DecrementPointers())
        to.Remove();
    }
  }
}
