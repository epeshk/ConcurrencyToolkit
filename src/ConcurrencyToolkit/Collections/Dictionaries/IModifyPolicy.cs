// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

namespace ConcurrencyToolkit.Collections;

/// <summary>
/// Statically configures a collection write operation.
/// </summary>
internal interface IModifyPolicy
{
  /// <summary>
  /// <c>true</c> when a write operation may override the value for an existing key.
  /// </summary>
  static abstract bool CanModify { get; }

  /// <summary>
  /// <c>true</c> when a write operation may add a new key-value pair to the collection.
  /// </summary>
  static abstract bool MayAdd { get; }
}

/// <summary>
/// May override the value for an existing key, but can't add a new key-value pair to the collection.
/// </summary>
internal struct OnlyModifyPolicy : IModifyPolicy
{
  public static bool CanModify => true;
  public static bool MayAdd => false;
}

/// <summary>
/// May add a new key-value pair, or override the value for an existing key
/// </summary>
internal struct CanModifyPolicy : IModifyPolicy
{
  public static bool CanModify => true;
  public static bool MayAdd => true;
}

/// <summary>
/// May add a new key-value pair to the collection, but can't override the value for an existing key
/// </summary>
internal struct RefuseModifyPolicy : IModifyPolicy
{
  public static bool CanModify => false;
  public static bool MayAdd => true;
}