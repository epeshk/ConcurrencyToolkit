// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics;
using ConcurrencyToolkit.Internal;

namespace ConcurrencyToolkit.Synchronization;

internal class WaiterList
{
  private volatile SemaphoreCompletionSourceNode? Head;
  private volatile SemaphoreCompletionSourceNode? Tail;
  public int Count { get; private set; }
  public PrioritySemaphore Semaphore { get; }

  public WaiterList(PrioritySemaphore semaphore) => Semaphore = semaphore;

  public SemaphoreCompletionSourceNode? Remove(bool lifo)
  {
    if (Head is null) return null;
    var waiter = lifo ? Tail : Head;
    if (waiter is null)
      return null;
    Remove(waiter);
    return waiter;
  }

  public bool Remove(SemaphoreCompletionSourceNode node)
  {
    if (node.Next is null && node.Prev is null && Head != node)
      return false;
    // Remove it from the linked list
    if (node.Next is not null) node.Next.Prev = node.Prev;
    if (node.Prev is not null) node.Prev.Next = node.Next;
    if (Head == node) Head = node.Next;
    if (Tail == node) Tail = node.Prev;
    Debug.Assert((Head is null) == (Tail is null), "Head is null iff tail is null");

    // Make sure not to leak
    node.Next = node.Prev = null;

    // Return whether the task was in the list
    Count--;
    return true;
  }

  public void AddToTail(SemaphoreCompletionSourceNode node)
  {
    var tail = Tail;
    if (tail is null)
    {
      Head = Tail = node;
      Assertion.Equals(Count, 0);
      Count = 1;
      return;
    }

    node.Prev = tail;
    Tail = tail.Next = node;
    Count++;
  }
}