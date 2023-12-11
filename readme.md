# ConcurrencyToolkit

Concurrent collections and synchronization primitives for writing fast multithreaded and asynchronous code. This library is experimental and provided without warranty, so use it carefully.

## Features
 - lockless semaphores
 - priority semaphore
 - lockless object and array pools
 - memory compact single writer multiple readers hash map
 - memory compact thread safe (but locked) hash map
 - write optimized lock-free counter

## Types

### ConcurrencyToolkit.Synchronization

- `QueueSemaphore`: uses `ConcurrentQueue` to order waiters
- `StackSemaphore`: uses `ConcurrentStack` to order waiters
- `SimpleSegmentSemaphore`: uses segment queue to order waiters (similar to the Semaphore from kotlinx.coroutines)
- `SegmentSemaphore`: uses segment queue with support of early removal of cancelled waiters
- `PrioritySemaphore`: lock-based semaphore (like SemaphoreSlim), but releases waiters in specified priority

### ConcurrencyToolkit.Pooling
- `LocklessArrayPool<T>.Shared`: similar to `ArrayPool<T>.Shared` but uses bounded concurrent queue instead of lock protected arrays to store pooled items.
- `IObjectPool<T>`, `ObjectPool<T>`

### ConcurrencyToolkit.Collections
- `SingleWriterDictionary<TKey, TValue, TComparer>`: memory-compact hash map that supports single writer and multiple readers simultaneously. Writes are lock-free (except for resizes, which may trigger GC), but reads may be retried when a parallel update occurs.
- `StripedDictionary<TKey, TValue, TComparer>`: memory-compact hash map, designed to avoid `ConcurrentDictionary` GC overhead. It is not general purpose `ConcurrentDictionary` replacement, and generally is slower, especially for reads.
- `DefaultComparer<TKey>`: default comparer for ConcurrencyToolkit hash maps
- `ComparerWrapper<TKey>`: struct wrapper over the reference type comparer

### ConcurrencyToolkit.Metrics
- `ThreadSafeCounter64`: scalable atomic counter that distributes writes across per-core variables
