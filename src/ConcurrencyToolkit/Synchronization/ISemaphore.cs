// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using JetBrains.Annotations;

namespace ConcurrencyToolkit.Synchronization;

/// <summary>
/// <para>
/// A synchronization primitive for tasks and threads that limits concurrency between them by some number of permits.
/// </para>
///
/// <para>
/// Each successful acquire takes a single permit, and each <see cref="Release"/> adds a permit. Acquirer may suspend or block until the permit is available, and <see cref="Release"/> may wake up an acquirer.
/// </para>
/// </summary>
public interface ISemaphore
{
  /// <summary>
  /// Acquires a permit from this semaphore, suspending until one is available.
  /// </summary>
  /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled during asynchronous waiting.</exception>
  /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the asynchronous waiting. An implementation MAY not check this token if semaphore can be acquired without suspending.</param>
  /// <returns>A <see cref="ValueTask"/> which completes when the permit is obtained.</returns>
  [MustUseReturnValue]
  ValueTask AcquireAsync(CancellationToken cancellationToken=default);

  /// <summary>
  /// Acquires a permit from this semaphore, suspending until one is available.
  /// </summary>
  /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the asynchronous waiting. An implementation MAY not check this token if semaphore can be acquired without suspending.</param>
  /// <returns>A <see cref="ValueTask{Boolean}"/> which completes with <c>true</c> when the permit is obtained, and <c>false</c> when the permit couldn't be acquired due to cancellation or other reason (e.g. queue is full).</returns>
  [MustUseReturnValue]
  ValueTask<bool> TryAcquireAsync(CancellationToken cancellationToken=default);

  /// <summary>
  /// Acquires a permit from this semaphore, blocking until one is available.
  /// </summary>
  /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled during blocking waiting.</exception>
  /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the blocking waiting. An implementation MAY not check this token if semaphore can be acquired without blocking.</param>
  void Acquire(CancellationToken cancellationToken=default);

  /// <summary>
  /// Acquires a permit from this semaphore, blocking until one is available.
  /// </summary>
  /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the blocking waiting. An implementation MAY not check this token if semaphore can be acquired without blocking.</param>
  /// <returns><c>true</c> when the permit is obtained, and <c>false</c> when the permit couldn't be acquired due to cancellation or other reason (e.g. queue is full).</returns>
  [MustUseReturnValue]
  bool TryAcquire(CancellationToken cancellationToken=default);

  /// <summary>
  /// Tries to acquire a permit from this semaphore without suspension.
  /// </summary>
  /// <returns><c>true</c> if the permit is acquired, <c>false</c> otherwise</returns>
  [MustUseReturnValue]
  bool TryAcquireImmediately();

  /// <summary>
  /// <para>Releases a permit, returning it into this semaphore.</para>
  /// <para>
  /// Resumes the suspending acquirer if there is one at the point of invocation
  /// in order depending on the implementation.
  /// </para>
  /// </summary>
  /// <exception cref="SemaphoreFullException">Semaphore capacity after release exceeds implementation defined limit.</exception>
  void Release();

  /// <summary>
  /// <para>
  /// The number of immediately available permits via <see cref="TryAcquireImmediately"/> method.
  /// </para>
  ///
  /// <para>
  /// In other words: the number of consecutive calls to the <see cref="TryAcquireImmediately"/> method which will succeed, if there are no other concurrent operations running.
  /// </para>
  /// </summary>
  int CurrentCount { get; }

  /// <summary>
  /// The estimated number of workers currently waiting for permit.
  /// </summary>
  int CurrentQueue { get; }
}
