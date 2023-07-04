// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ConcurrencyToolkit.Metrics;

namespace ConcurrencyToolkit.Synchronization;

[StructLayout(LayoutKind.Sequential)]
internal class RemovableSegment
{
  public const int Size =
#if SEGMENT_SIZE_1
    1
#else
    16
#endif
    ;

  public ulong Id;
  public SegmentSemaphore Semaphore;
  public volatile RemovableSegment? Prev;
  public volatile RemovableSegment? Next;
  private volatile int Count;

  #region embeddedarray

  public object? _00;
#if !SEGMENT_SIZE_1
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
  public object? _01, _02, _03, _04, _05, _06, _07, _08, _09, _10, _11, _12, _13, _14, _15;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value
#endif
  #endregion

  public ref object? this[uint index] => ref Unsafe.Add(ref _00, index);

  public RemovableSegment() => Counter<SegmentCreated>.Increment();

  public void IncrementCanceled()
  {
    if (Interlocked.Increment(ref Count) == Size)
      Remove();
  }

  public bool IncrementPointers()
  {
    while (true)
    {
      var count = Count;
      if ((count & ushort.MaxValue) == Size && !IsTail)
        return false;
      if (Interlocked.CompareExchange(ref Count, count + (1 << 16), count) == count)
        return true;
    }
  }

  private bool IsTail => Volatile.Read(ref Next) is null;

  public bool DecrementPointers() => Interlocked.Add(ref Count, -(1 << 16)) == Size && !IsTail;

  public bool Removed
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => Count == Size && !IsTail;
  }

  public void Remove()
  {
    while (true)
    {
      if (IsTail) return;
      var prev = FindLeftAliveSegment();
      var next = FindRightAliveSegment();

      next.Prev = prev;
      if (prev != null)
        prev.Next = next;
      if (next is { Removed: true })
        continue;
      if (prev?.Removed ?? false)
        continue;
      return;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private RemovableSegment? FindLeftAliveSegment()
  {
    var cur = Prev;
    while (cur is { Removed: true })
      cur = cur.Prev;
    return cur;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private RemovableSegment FindRightAliveSegment()
  {
    var cur = Next!;
    while (cur.Removed) // last segment is never removed, and FindRightAliveSegment called only during removal
      cur = cur.Next;
    return cur;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Init(ulong id, int pointers, SegmentSemaphore semaphore)
  {
    Id = id;
    Count = pointers << 16;
    Semaphore = semaphore;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Reset() => (Semaphore, Prev) = (null, null);
}