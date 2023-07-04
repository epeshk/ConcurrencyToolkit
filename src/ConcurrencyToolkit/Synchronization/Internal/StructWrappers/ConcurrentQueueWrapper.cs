// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Collections;
using System.Collections.Concurrent;

namespace ConcurrencyToolkit.Synchronization;

internal struct ConcurrentQueueWrapper<T> : IProducerConsumerCollection<T>
{
  private readonly ConcurrentQueue<T> queue;

  public ConcurrentQueueWrapper(ConcurrentQueue<T> queue) => this.queue = queue;
  public IEnumerator<T> GetEnumerator() => queue.GetEnumerator();

  IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)queue).GetEnumerator();

  public void CopyTo(Array array, int index) => ((ICollection)queue).CopyTo(array, index);

  public int Count => queue.Count;

  public bool IsSynchronized => ((ICollection)queue).IsSynchronized;

  public object SyncRoot => ((ICollection)queue).SyncRoot;

  public void CopyTo(T[] array, int index) => queue.CopyTo(array, index);

  public T[] ToArray() => queue.ToArray();

  public bool TryAdd(T item) => ((IProducerConsumerCollection<T>)queue).TryAdd(item);

  public bool TryTake(out T item) => ((IProducerConsumerCollection<T>)queue).TryTake(out item);
}