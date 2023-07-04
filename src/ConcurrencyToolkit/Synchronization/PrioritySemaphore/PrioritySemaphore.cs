// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Internal;

namespace ConcurrencyToolkit.Synchronization;

public sealed class PrioritySemaphore : IPrioritySemaphore
{
  private class Configured : ISemaphore
  {
    private readonly PrioritySemaphore semaphore;
    private readonly int priority;

    public Configured(PrioritySemaphore semaphore, int priority)
    {
      this.semaphore = semaphore;
      this.priority = priority;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask AcquireAsync(CancellationToken cancellationToken = default) =>
      semaphore.TryEnter(priority, true, cancellationToken, out var node)
        ? ValueTask.CompletedTask
        : node?.ValueTask ?? ValueTasks.Canceled;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> TryAcquireAsync(CancellationToken cancellationToken = default) =>
      semaphore.TryEnter(priority, true, cancellationToken, out var node)
        ? ValueTask.FromResult(true)
        : node?.ValueTaskBool ?? ValueTask.FromResult(false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Acquire(CancellationToken cancellationToken = default)
    {
      if (semaphore.TryEnter(priority, false, cancellationToken, out var node))
        return;

      if (node is null)
      {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowHelper.Unreachable();
      }
      node.Wait();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquire(CancellationToken cancellationToken = default) =>
      semaphore.TryEnter(priority, false, cancellationToken, out var node) || (node is not null && node.TryWait());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquireImmediately() =>
      semaphore.TryAcquireImmediately();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release() => semaphore.Release();

    public int CurrentCount => semaphore.CurrentCount;
    public int CurrentQueue => semaphore.CurrentQueueByPriority(priority);
  }

  private volatile int currentCount;
  private int failedCount;
  private readonly bool lifo;

  private readonly WaiterList[] waiterLists;

  public PrioritySemaphore(int initialCount, int maxPriority = 3, bool lifo = false)
  {
    if (initialCount < 0)
      throw new ArgumentOutOfRangeException(nameof(initialCount), initialCount, "Should not be negative");
    if (maxPriority < 0)
      throw new ArgumentOutOfRangeException(nameof(maxPriority), maxPriority, "Should not be negative");
    if (maxPriority >= 100)
      throw new ArgumentOutOfRangeException(nameof(maxPriority), maxPriority, "Value is too large");

    this.currentCount = initialCount;
    this.lifo = lifo;

    waiterLists = new WaiterList[maxPriority + 1];
    for (int i = 0; i < waiterLists.Length; i++)
      waiterLists[i] = new(this);
  }

  private bool TryEnter(int priority, bool async, CancellationToken cancellationToken, out SemaphoreCompletionSourceNode? node)
  {
    node = null;
    lock (waiterLists)
    {
      if (cancellationToken.IsCancellationRequested)
        return false;

      if (currentCount > 0)
      {
        --currentCount;
        return true;
      }

      node = CreateCompletion(priority, async, cancellationToken);
    }

    return false;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void RegisterCancellation(CancellationToken cancellationToken, SemaphoreCompletionSourceNode completion)
  {
    cancellationToken.Register((o, token) =>
    {
      var node = (SemaphoreCompletionSourceNode)o;
      bool removed;
      lock (node.Semaphore.waiterLists)
        removed = node.Semaphore.waiterLists[node.Priority].Remove(node);
      if (removed)
        node.Cancel(token);
    }, completion);
  }

  private SemaphoreCompletionSourceNode CreateCompletion(int priority, bool async, CancellationToken cancellationToken)
  {
    Assertion.True(Monitor.IsEntered(waiterLists));
    var completion = SemaphoreCompletionSourceNode.Get(async);
    completion.Semaphore = this;
    completion.Priority = priority;

    if (cancellationToken.CanBeCanceled)
      RegisterCancellation(cancellationToken, completion);

    waiterLists[priority].AddToTail(completion);
    return completion;
  }

  public void Release()
  {
    lock (waiterLists)
    {
      for (int i = waiterLists.Length - 1; i >= 0; i--)
      {
        var list = waiterLists[i];
        while (true)
        {
          var node = list.Remove(lifo);
          if (node is null) break;
          node.Resume();
          return;
        }
      }

      currentCount++;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool TryAcquireImmediately()
  {
    if (currentCount <= 0)
      return false;
    lock (waiterLists)
    {
      if (currentCount <= 0) return false;
      currentCount--;
    }

    return true;
  }

  public int CurrentCount => Math.Max(0, currentCount);

  private int CurrentQueueByPriority(int priority) => waiterLists[priority].Count;

  public int MaxPriority => waiterLists.Length - 1;

  public ISemaphore WithPriority(int priority)
  {
    if (priority < 0 || priority > MaxPriority)
      ThrowHelper.OutOfRange_Priority(priority, MaxPriority);
    return new Configured(this, priority);
  }
}