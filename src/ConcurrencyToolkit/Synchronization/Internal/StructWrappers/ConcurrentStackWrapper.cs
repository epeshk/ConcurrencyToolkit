// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Collections;
using System.Collections.Concurrent;

namespace ConcurrencyToolkit.Synchronization;

internal struct ConcurrentStackWrapper<T> : IProducerConsumerCollection<T>
{
  private readonly ConcurrentStack<T> stack;

  public ConcurrentStackWrapper(ConcurrentStack<T> stack) => this.stack = stack;
  public IEnumerator<T> GetEnumerator() => stack.GetEnumerator();

  IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)stack).GetEnumerator();

  public void CopyTo(Array array, int index) => ((ICollection)stack).CopyTo(array, index);

  public int Count => stack.Count;

  public bool IsSynchronized => ((ICollection)stack).IsSynchronized;

  public object SyncRoot => ((ICollection)stack).SyncRoot;

  public void CopyTo(T[] array, int index) => stack.CopyTo(array, index);

  public T[] ToArray() => stack.ToArray();

  public bool TryAdd(T item) => ((IProducerConsumerCollection<T>)stack).TryAdd(item);

  public bool TryTake(out T item) => ((IProducerConsumerCollection<T>)stack).TryTake(out item);
}