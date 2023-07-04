// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

namespace ConcurrencyToolkit.Synchronization;

public interface IPrioritySemaphore
{
  /// <summary>
  /// The max priority supported by this <see cref="IPrioritySemaphore"/> instance.
  /// </summary>
  public int MaxPriority { get; }

  /// <returns>A wrapper over the semaphore instance, configured for specified <paramref name="priority"/>.</returns>
  public ISemaphore WithPriority(int priority);

  /// <summary>
  /// Releases a permit, returning it into this semaphore.
  /// </summary>
  public void Release();
}