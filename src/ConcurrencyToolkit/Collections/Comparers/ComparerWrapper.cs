// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Runtime.CompilerServices;

namespace ConcurrencyToolkit.Collections;

public struct ComparerWrapper<TKey> : IEqualityComparer<TKey>
{
  /// <remarks>
  /// Always not <c>null</c> for reference types.
  /// <br/>
  /// <c>null</c> for value types when default comparer is used.
  /// </remarks>
  private readonly IEqualityComparer<TKey>? comparer;

  public ComparerWrapper(IEqualityComparer<TKey>? comparer)
  {
    if (typeof(TKey).IsValueType)
      this.comparer = ReferenceEquals(comparer, EqualityComparer<TKey>.Default) ? null : comparer;
    else
      this.comparer = comparer ?? EqualityComparer<TKey>.Default;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool Equals(TKey? x, TKey? y)
  {
    if (typeof(TKey).IsValueType && comparer is null)
      return EqualityComparer<TKey>.Default.Equals(x, y);

    return comparer!.Equals(x, y);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int GetHashCode(TKey obj)
  {
    if (typeof(TKey).IsValueType && comparer is null)
      return EqualityComparer<TKey>.Default.GetHashCode(obj);
    return comparer!.GetHashCode(obj);
  }
}
