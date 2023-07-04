// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ConcurrencyToolkit.Internal;
using ConcurrencyToolkit.Metrics;
using ConcurrencyToolkit.Synchronization.Internal;

namespace ConcurrencyToolkit.Synchronization;

/// <summary>
/// The <see cref="ISemaphore"/> implementation which uses FIFO segment queue to order queued waiters.
/// </summary>
/// <remarks>
/// Reuses underlying <see cref="ValueTask"/> sources even when cancellation token is used.
/// <br/>
/// Based on SegmentQueueSynchronizer Semaphore (SIMPLE_ASYNC) from kotlinx.coroutines
/// https://github.com/Kotlin/kotlinx.coroutines/tree/sqs-experiments
/// https://www.youtube.com/watch?v=2uxsNJ0TdIM
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("Count = {CurrentCount}, Queue = {CurrentQueue}")]
public sealed class SimpleSegmentSemaphore : ISemaphore
{
  private volatile int currentCount;

  // releases
  private ulong dequeueIdx;
  private volatile Segment dequeueSegment;

  // acquires
  private ulong enqueueIdx;
  private volatile Segment enqueueSegment;

  private volatile int canceledCount;

  public SimpleSegmentSemaphore(int initialCount)
  {
    if (initialCount < 0)
      throw new ArgumentOutOfRangeException(nameof(initialCount), initialCount, "Should be non-negative");

    currentCount = initialCount;
    dequeueIdx = enqueueIdx = 0;
    dequeueSegment = enqueueSegment = new(0, this);
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

  private static void ResetWaiter(SemaphoreCompletionSource waiter) => waiter.ResetAndReturn();

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
      while (true)
      {
        if (IncrementCounter() || ResumeFromQueue())
          return;
      }
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

    var id = myDequeueIdx / Segment.Size;
    var i = (byte)(myDequeueIdx % Segment.Size);
    var segment = mySegment.Id == id
      ? mySegment // fast path
      : SegmentList.FindSegmentAndMoveForward(ref dequeueSegment, mySegment, id);

    if (segment.Id > id)
      return false;

    Assertion.Equals(segment.Id, id);

    var cellState =
      Interlocked.Exchange(ref segment[i], Consts.PERMIT); // set PERMIT and retrieve the prev cell state
    if (cellState is null)
      return true;
    if (ReferenceEquals(cellState, Consts.CANCELED))
    {
      Interlocked.Decrement(ref canceledCount);
      return false;
    }

    if (cellState is SemaphoreCompletionSource scs)
    {
      scs.SetResult(true);
    }
    else
    {
      var scsd = (CancellableSemaphoreCompletionSource)cellState;
      scsd.SetResult(true);
    }

    return true;
  }

  public int CurrentCount => Math.Max(0, currentCount);
  public int CurrentQueue => Math.Max(0, Math.Max(0, -currentCount) - canceledCount);

  private bool AddAcquireToQueue(object cont, CancellationToken cancellationToken)
  {
    var mySegment = enqueueSegment;
    var myEnqueueIdx = Interlocked.Increment(ref enqueueIdx) - 1;

    var id = myEnqueueIdx / Segment.Size;
    var i = (byte)(myEnqueueIdx % Segment.Size);

    var segment = mySegment.Id == id
      ? mySegment
      : SegmentList.FindSegmentAndMoveForward(ref enqueueSegment, mySegment, id);

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

  private static void InstallCancellationHandler(CancellableSemaphoreCompletionSource source, CancellationToken token, Segment segment, byte i)
  {
    source.Segment = segment;
    source.Index = i;
    source.Registration = token.UnsafeRegister(CancellationCallback, source);
  }

  private static void CancellationCallback(object? o, CancellationToken token) => CancellationCallback((CancellableSemaphoreCompletionSource)o!, token);
  private static void CancellationCallback(CancellableSemaphoreCompletionSource cs, CancellationToken token)
  {
    var segment = (Segment)cs.Segment;
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
      Interlocked.Increment(ref segment.Semaphore.canceledCount);
      cc.SetCancelled(token);
      return;
    }

    ThrowHelper.Unreachable();
  }
}