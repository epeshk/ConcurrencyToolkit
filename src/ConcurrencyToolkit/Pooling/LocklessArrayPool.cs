// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Buffers;

namespace ConcurrencyToolkit.Pooling;

/// <summary>
/// Provides global resource pool that enables reusing instances of arrays.
/// </summary>
/// <remarks>
/// Provided pool is similar to <see cref="ArrayPool{T}.Shared"/> but uses fixed size concurrent queues instead of locked stacks.
/// </remarks>
public static class LocklessArrayPool<T>
{
  /// <inheritdoc cref="LocklessArrayPool{T}"/>
  public static readonly ArrayPool<T> Shared = new TlsOverPerCoreBoundedQueueArrayPool<T>();
}