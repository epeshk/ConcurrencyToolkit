// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ConcurrencyToolkit.Internal;
using ConcurrencyToolkit.Metrics;
using ConcurrencyToolkit.Synchronization.Internal;

namespace ConcurrencyToolkit.Synchronization;

/// <summary>
/// The <see cref="ISemaphore"/> implementation which uses FIFO segment queue to order queued waiters. Removes cancelled waiters from queue early.
/// </summary>
/// <remarks>
/// Reuses underlying <see cref="ValueTask"/> sources even when cancellation token is used.
/// <br/>
/// Based on SegmentQueueSynchronizer Semaphore (SMART_ASYNC) from kotlinx.coroutines
/// https://github.com/Kotlin/kotlinx.coroutines/tree/sqs-experiments
/// https://www.youtube.com/watch?v=2uxsNJ0TdIM
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("Count = {CurrentCount}, Queue = {CurrentQueue}")]
public sealed class SegmentSemaphore : ISemaphore
{
  private int currentCount;

  // releases
  private ulong dequeueIdx;
  private volatile RemovableSegment dequeueSegment;

  // acquires
  private ulong enqueueIdx;
  private volatile RemovableSegment enqueueSegment;

  public SegmentSemaphore(int initialCount)
  {
    if (initialCount < 0)
      throw new ArgumentOutOfRangeException(nameof(initialCount), initialCount, "Should be non-negative");

    currentCount = initialCount;
    dequeueIdx = enqueueIdx = 0;
    dequeueSegment = enqueueSegment = new();
    dequeueSegment.Init(0, 2, this);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public ValueTask AcquireAsync(CancellationToken cancellationToken=default)
  {
    return TryEnter()
      ? ValueTask.CompletedTask
      : AcquireSuspending(cancellationToken);

    ValueTask AcquireSuspending(CancellationToken token) =>
      CreateWaiter(true, token)?.ValueTask ?? ValueTask.CompletedTask;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public ValueTask<bool> TryAcquireAsync(CancellationToken cancellationToken=default)
  {
    return TryEnter()
      ? ValueTask.FromResult(true)
      : TryAcquireSuspending(cancellationToken);

    ValueTask<bool> TryAcquireSuspending(CancellationToken token) =>
      CreateWaiter(true, token)?.ValueTaskBool ?? ValueTask.FromResult(true);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Acquire(CancellationToken cancellationToken=default)
  {
    if (TryEnter())
      return;
    AcquireBlocking(cancellationToken);
    return;

    void AcquireBlocking(CancellationToken token) => CreateWaiter(false, token)?.Wait();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TryAcquire(CancellationToken cancellationToken=default)
  {
    return TryEnter() || TryAcquireBlocking(cancellationToken);

    bool TryAcquireBlocking(CancellationToken token) => CreateWaiter(false, token)?.TryWait() ?? true;
  }

  private SemaphoreCompletionSource? CreateWaiter(bool async, CancellationToken cancellationToken)
  {
    try
    {
      var waiter = cancellationToken.CanBeCanceled
        ? CancellableSemaphoreCompletionSource.Get(async)
        : SemaphoreCompletionSource.Get(async);

      if (AddAcquireToQueue(waiter, cancellationToken))
        return waiter;

      ResetWaiter(waiter);
      return null;
    }
    catch (OutOfMemoryException oom)
    {
      ThrowHelper.FailFast(oom);
      throw;
    }
  }

  private static void ResetWaiter(SemaphoreCompletionSource waiter)
  {
    waiter.ResetAndReturn();
  }

  public bool TryAcquireImmediately()
  {
    while (true)
    {
      var p = currentCount;
      if (p <= 0) return false;
      if (Interlocked.CompareExchange(ref currentCount, p - 1, p) == p)
        return true;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Release()
  {
    if (IncrementCounter())
      return;

    ReleaseLoop();
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void ReleaseLoop()
  {
    try
    {
      if (ResumeFromQueue())
        return;

      Counter<ReleaseLoopEntered>.Increment();
      while (!ResumeFromQueue()) ;
    }
    catch (OutOfMemoryException oom)
    {
      ThrowHelper.FailFast(oom);
      throw;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool IncrementCounter()
  {
    var p = Interlocked.Increment(ref currentCount) - 1;
    return p >= 0;
  }

  private bool ResumeFromQueue()
  {
    var mySegment = dequeueSegment;
    var myDequeueIdx = Interlocked.Increment(ref dequeueIdx) - 1;

    var id = myDequeueIdx / RemovableSegment.Size;
    var i = (byte)(myDequeueIdx % RemovableSegment.Size);

    var segment = mySegment.Id == id
      ? mySegment // fast path
      : RemovableSegmentList.FindSegmentAndMoveForward(ref this.dequeueSegment, mySegment, id);

    if (segment.Prev is not null)
      segment.Prev = null;

    if (segment.Id > id)
    {
      AdjustDeqIdx(segment.Id * RemovableSegment.Size);
      return false;
    }

    Assertion.Equals(segment.Id, id);

    var cellState =
      Interlocked.Exchange(ref segment[i], Consts.PERMIT); // set PERMIT and retrieve the prev cell state
    if (cellState is null)
      return true;
    if (ReferenceEquals(cellState, Consts.CANCELED))
      return false;

    if (cellState is SemaphoreCompletionSource scs)
    {
      scs.SetResult(true);
    }
    else
    {
      var scsd = (CancellableSemaphoreCompletionSource)cellState;
      scsd.AssertWaiterId(myDequeueIdx);
      scsd.SetResult(true);
    }

    return true;
  }

  public int CurrentCount => Math.Max(0, currentCount);
  public int CurrentQueue => Math.Max(0, -currentCount);

  private bool AddAcquireToQueue(object cont, CancellationToken cancellationToken)
  {
    var mySegm = enqueueSegment;
    var myEnqIdx = Interlocked.Increment(ref enqueueIdx) - 1;

    var id = myEnqIdx / RemovableSegment.Size;
    var i = (byte)(myEnqIdx % RemovableSegment.Size);

    var segment = mySegm.Id == id
      ? mySegm
      : RemovableSegmentList.FindSegmentAndMoveForward(ref enqueueSegment, mySegm, id);

    Assertion.Equals(segment.Id, id);

    // the regular (fast) path -- if the cell is empty, try to install continuation
    var prev = Interlocked.CompareExchange(ref segment[i], cont, null);
    if (prev is null)
    {
      if (cont is CancellableSemaphoreCompletionSource cscs)
        InstallCancellationHandler(cscs, cancellationToken, segment, i);
      return true;
    }

    if (prev == Consts.PERMIT)
    {
      Counter<InstantPermit>.Increment();
      return false;
    }

    return ThrowHelper.Unreachable<bool>();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool TryEnter() => Interlocked.Decrement(ref currentCount) >= 0;

  private static void InstallCancellationHandler(CancellableSemaphoreCompletionSource source, CancellationToken token, RemovableSegment segment, byte i)
  {
    source.Segment = segment;
    source.Index = i;
    source.Registration = token.UnsafeRegister(CancellationCallback, source);
  }

  private static void CancellationCallback(object? o, CancellationToken token) => CancellationCallback((CancellableSemaphoreCompletionSource)o!, token);
  private static void CancellationCallback(CancellableSemaphoreCompletionSource cs, CancellationToken token)
  {
    var segment = (RemovableSegment)cs.Segment;
    var index = cs.Index;

    ref var cell = ref segment[index];

    var prev = Interlocked.Exchange(ref cell, Consts.CANCELED);
    if (prev == Consts.PERMIT)
    {
      Counter<CancellationAfterPermit>.Increment();
      return;
    }
    if (prev is CancellableSemaphoreCompletionSource cc)
    {
      Counter<Cancellation>.Increment();
      cc.SetCancelled(token);
      segment.IncrementCanceled();
      // if the semaphore counter is not negative - there is a concurrent release,
      // that lost the race to release the canceled waiter.
      // This release will put PERMIT into the queue, which will be taken on the next acquire
      IncrementWhenNegative(ref segment.Semaphore.currentCount);
      return;
    }

    ThrowHelper.Unreachable();
  }

  private void AdjustDeqIdx(ulong newValue)
  {
    while (true)
    {
      var cur = Interlocked.Read(ref dequeueIdx);
      if (cur >= newValue) return;
      if (Interlocked.CompareExchange(ref dequeueIdx, newValue, cur) == cur) return;
    }
  }

  private static void IncrementWhenNegative(ref int value)
  {
    while (true)
    {
      var cur = Volatile.Read(ref value);
      if (cur >= 0)
        return;
      if (Interlocked.CompareExchange(ref value, cur + 1, cur) == cur)
        return;
    }
  }

  internal int SegmentsCount
  {
    get
    {
      var cur = dequeueSegment;
      var count = 1;
      while (cur.Next is not null)
      {
        count++;
        cur = cur.Next;
      }

      return count;
    }
  }
}