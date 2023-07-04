using System.Runtime.CompilerServices;
// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

namespace ConcurrencyToolkit.Collections;

public struct DefaultComparer<TKey> : IEqualityComparer<TKey>
{
  /// <remarks>
  /// unused for value types, for devirtualization of <see cref="EqualityComparer{TKey}.Default"/>
  /// </remarks>
  private static readonly IEqualityComparer<TKey>? comparer =
    typeof(TKey).IsValueType ? null : EqualityComparer<TKey>.Default;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool Equals(TKey? x, TKey? y) => typeof(TKey).IsValueType ? EqualityComparer<TKey>.Default.Equals(x, y) : comparer!.Equals(x, y);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int GetHashCode(TKey obj) =>
    typeof(TKey).IsValueType ? EqualityComparer<TKey>.Default.GetHashCode(obj) : obj.GetHashCode();
}