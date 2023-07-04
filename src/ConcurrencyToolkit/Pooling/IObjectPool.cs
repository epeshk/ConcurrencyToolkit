// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics.CodeAnalysis;

namespace ConcurrencyToolkit.Pooling;

/// <summary>
/// Provides a resource pool that enables reusing instances of type <typeparamref name="T"/>.
/// </summary>
/// <remarks>Implementations of this interface are meant to be thread-safe. All members may be used by multiple threads concurrently.</remarks>
public interface IObjectPool<T> where T : class
{
  /// <summary>
  /// Rents an object from the pool.
  /// </summary>
  T Rent();

  /// <summary>
  /// Tries to rent an object from the pool.
  /// </summary>
  /// <param name="obj">The rented object, if return value is <c>true</c></param>
  /// <returns><c>true</c> when object is rented, <c>false</c> otherwise.</returns>
  bool TryRent([NotNullWhen(true)] out T obj);

  /// <summary>
  /// Tries to return an object to the pool.
  /// </summary>
  /// <param name="obj">An object to return.</param>
  void Return(T obj);
}