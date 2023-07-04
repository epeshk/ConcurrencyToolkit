// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Metrics;
using ConcurrencyToolkit.Pooling.LiteObjectPool;

namespace ConcurrencyToolkit.Synchronization;

internal static class SegmentList
{
  [MethodImpl(MethodImplOptions.NoInlining)]
  public static Segment FindSegmentAndMoveForward(ref Segment from, Segment start, ulong id)
  {
    Counter<FindSegmentCalled>.Increment();
    var to = FindSegment(start, id);
    MoveForward(ref from, to);
    return to;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Segment FindSegment(Segment start, ulong id)
  {
    var cur = start;

    while (cur.Id < id)
    {
      var next = cur.Next;
      if (next != null)
      {
        cur = next;
        continue;
      }

      var newTail = ThreadStaticPool<Segment>.TryRent() ?? new();//Allocator.New<Segment, SegmentConstructor>();
      newTail.Init(cur.Id + 1, cur.Semaphore);

      cur = Interlocked.CompareExchange(ref cur.Next, newTail, null) ?? newTail;
      if (cur == newTail)
        continue;

      Counter<SegmentPooled>.Increment();
      newTail.Reset();
      ThreadStaticPool<Segment>.Return(newTail);
    }

    return cur;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void MoveForward(ref Segment from, Segment to)
  {
    while (true)
    {
      var cur = Volatile.Read(ref from);
      if (cur.Id >= to.Id) return;
      if (Interlocked.CompareExchange(ref from, to, cur) == cur) return;
    }
  }
}
