# Semaphores

A semaphore is a synchronization primitive that limits concurrency between some workers (synchronous threads or asynchronous tasks).

It may be acquired simultaneously by `count` of workers, and other workers attempting to acquire the semaphore will be rejected or queued. When a worker releases the semaphore, it can wake up another worker from the queue.

## The problem with SemaphoreSlim

`SemaphoreSlim` has `lock` inside. And contention on this lock may kill performance under heavy load. 

## Semaphore interface

So, a semaphore may be represented by two methods: `acquire()` and `release()`. However, in C# reality, two methods are not enough.

 - Workers entering the semaphore may be both a synchronous threads, or asynchronous tasks
 - Acquire method should support cancellation. E.g. we may want to complete an incoming request after some timeout, and no longer wait on the semaphore anymore
 - Exception handling is expensive. If semaphore throws `OperationCanceledException` under heavy load, it can degrade performance and reduce available compute time for request processing

Assuming that, `ISemaphore` in the ConcurrencyToolkit defined as:

```csharp
public interface ISemaphore {
  // Synchronous methods, which may block
  void Acquire(CancellationToken token=default);
  bool TryAcquire(CancellationToken token=default);

  // Asynchronous methods, which mmay suspend
  ValueTask AcquireAsync(CancellationToken token=default)
  ValueTask<bool> TryAcquireAsync(CancellationToken token=default);

  // Other
  bool TryAcquireImmediately();
  int CurrentCount { get; }
  int CurrentQueue { get; }
}
```

`CurrentQueue` property provides a rough estimation of the number of workers currently waiting for permit. This property added to the `ISemaphore` interface for better observability.

### Why synchronous and asynchronous methods are not unified?

It is not as simple, as it may looks like.

Calling `.GetAwaiter().GetResult()` on `ValueTask` causes undefined behavior, depending on ValueTask's underlying object implementation.

For standard reusable `IValueTaskSource` implementation, named `ManualResetValueTaskSourceCore`, `.GetAwaiter().GetResult()` will throw for incomplete ValueTasks. For Task-backed ValueTasks, it will succeed.

`Task` as return type makes it impossible to reduce allocations. Blocking waiting on Tasks need to be done carefully, because it may cause deadlocks inside external `SynchronizationContext`.

Using a custom awaitable type makes an external implementation of `ISemaphore` nearly impossible.

### Why there is throwing and non-throwing methods?

As stated before, throwing an exceptions may worsen ability to recover under heavy load.

However, for some scenarios cancellation token is not used, and nothing can cause an `OperationCanceledException`. It is also easy to forget to check the return value of `TryAcquire`. Implementing `Acquire` as an extension method can degrade performance, and make the method less discoverable.

`Try...` methods returns false only for expected situations, like cancellation occurred, or queue is full. It may throw on internal errors, OOMs, and other unexpected cases.

### Why there are none methods without cancellation token?

Just to not x2 the number of methods. Semaphore implementations should check if the token can be canceled and optimize it away.

### Why there is no fancy IDisposable syntax for permit releasing?

Like this:

```csharp
using (await semaphore.Acquire())
{
}
```

Because it may be done as an extension method in the user code. Different use cases enforces different requirements, e.g. disposable type may be `class`, `struct`, or `ref struct`

### Why there is no max permits limit, like in SemaphoreSlim

`maxPermits` parameter is only about validation of the user code: when `count` of the semaphore exceeds `maxPermits`, the `.Release()` method should throw. It may be added as an improvement in the future.

Currently, this validation may be performed via external `ISemaphore` wrapper by checking `CurrentCount` property (although, it may catch wrong usage not immediately).

## Semaphore requirements

- Semaphore initializes with some `initialCount`. Then it allows to be acquired by `initialCount` workers, and block or suspend others until sufficient amount of releases
- `initalCount` may be 0. It means that semaphore don't pass any workers until the external signal (.Release() call)
- When cancellation occurs, cancelled worker receives a notification immediately.
- `Release` wakes worker in some implementation defined order
- `Release` must find suitable worker for release synchronously, or ensure that the queue is empty

## Implementations

ConcurrencyToolkit provides a number of `ISemaphore` implementations, which differs in:

- Internal data structure. E.g. `ConcurrentQueue` is very efficient with almost no allocations, and `ConcurrentStack` allocates a node for each element.
- Waiters resuming order. For incoming request throttling, the LIFO order is more efficient because it tends to release waiters with unexpired timeouts first.
- Cancellation support. `ConcurrentQueue` and `ConcurrentStack` are not support removal from the middle. Thus, if the waiter is cancelled, the completion handle will remain in the data structure until it is removed by a `Release` call (unsuccessful `Release` which will be retried). It may lead to inefficient memory usage, if there are *many* cancellations.

| Implementation           | Order  | Data structure allocations              | Completion allocations                         | Cancellation support |
|--------------------------|--------|-----------------------------------------|------------------------------------------------|----------------------|
| ConcurrentQueueSemaphore | FIFO   | no allocations                          | uses pool when cancellation token is unused    | partial              |
| ConcurrentStackSemaphore | LIFO   | allocates an object per waiter          | not documented (as subject to change)          | partial              |
| LiteSegmentSemaphore     | FIFO   | allocates an array per 256 waiters      | always uses pool                               | partial              |
| SegmentSemaphore         | FIFO   | allocates an array per 16 waiters       | always uses pool                               | full                 |
| SemaphoreSlim (BCL)      | FIFO   | allocates a linked list node per waiter | combined with linked list node (single object) | full                 |
| PrioritySemaphore        | Any    | similar to the Slim, but uses pool      | similar to the Slim                            | full                 |

`full` completion support means that waiter is removed from the internal data structure on cancellation earlier than `.Release()` call occurs. `SemaphoreSlim` removes a single waiter node from linked list for this purpose, `SegmentSemaphore` removes 16-waiters segments, which are full of cancelled waiters.
