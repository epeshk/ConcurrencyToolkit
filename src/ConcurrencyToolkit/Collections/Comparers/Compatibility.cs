namespace ConcurrencyToolkit.Collections;

#if !NET9_0_OR_GREATER
internal interface IAlternateEqualityComparer<in TAlternate, T>
{
  /// <summary>Determines whether the specified <paramref name="alternate" /> equals the specified <paramref name="other" />.</summary>
  /// <param name="alternate">The instance of type <typeparamref name="TAlternate" /> to compare.</param>
  /// <param name="other">The instance of type <typeparamref name="T" /> to compare.</param>
  /// <returns>
  /// <see langword="true" /> if the specified instances are equal; otherwise, <see langword="false" />.</returns>
  bool Equals(TAlternate alternate, T other);

  /// <summary>Returns a hash code for the specified alternate instance.</summary>
  /// <param name="alternate">The instance of type <typeparamref name="TAlternate" /> for which to get a hash code.</param>
  /// <returns>A hash code for the specified instance.</returns>
  int GetHashCode(TAlternate alternate);

  /// <summary>Creates a <typeparamref name="T" /> that is considered by <see cref="M:System.Collections.Generic.IAlternateEqualityComparer`2.Equals(`0,`1)" /> to be equal to the specified <paramref name="alternate" />.</summary>
  /// <param name="alternate">The instance of type <typeparamref name="TAlternate" /> for which an equal <typeparamref name="T" /> is required.</param>
  /// <returns>A <typeparamref name="T" /> considered equal to the specified <paramref name="alternate" />.</returns>
  T Create(TAlternate alternate);
}
#endif