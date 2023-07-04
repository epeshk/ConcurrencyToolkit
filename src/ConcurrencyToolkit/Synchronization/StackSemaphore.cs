// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Synchronization.Internal.ProducerConsumerSemaphore;

namespace ConcurrencyToolkit.Synchronization;

using Impl = ProducerConsumerCollectionSemaphore<ConcurrentStackWrapper<ValueHolder>>;

/// <summary>
/// The <see cref="ISemaphore"/> implementation which uses <see cref="ConcurrentQueue{T}"/> to order queued waiters.
/// </summary>
/// <remarks>
/// This implementation allocates <see cref="ConcurrentStack{T}"/> node for each waiter.
/// Also is allocates additional object per waiter when cancellation token is used.
/// <br/>
/// Use this semaphore when LIFO order of resumes is required, e.g. for request throttling implementation.
/// </remarks>
[DebuggerDisplay("Count = {CurrentCount}, Queue = {CurrentQueue}")]
public sealed class StackSemaphore : ISemaphore
{
  private readonly Impl semaphore;

  public StackSemaphore(int initialCount) => semaphore = new(initialCount, new(new()));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public ValueTask AcquireAsync(CancellationToken cancellationToken = default) =>
    semaphore.AcquireAsync(cancellationToken);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public ValueTask<bool> TryAcquireAsync(CancellationToken cancellationToken = default) =>
    semaphore.TryAcquireAsync(cancellationToken);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Acquire(CancellationToken cancellationToken = default) =>
    semaphore.Acquire(cancellationToken);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TryAcquire(CancellationToken cancellationToken = default) =>
    semaphore.TryAcquire(cancellationToken);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TryAcquireImmediately() =>
    semaphore.TryAcquireImmediately();

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Release() => semaphore.Release();

  public int CurrentCount => semaphore.CurrentCount;

  public int CurrentQueue => semaphore.CurrentQueue;
}