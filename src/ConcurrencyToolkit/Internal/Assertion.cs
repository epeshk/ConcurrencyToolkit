// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ConcurrencyToolkit.Internal;

internal static class Assertion
{
  [Conditional("CT_ASSERT")]
  public static void True([DoesNotReturnIf(false)] bool condition, [CallerArgumentExpression(nameof(condition))] string expression = null!)
  {
    if (!condition)
      ThrowHelper.Assertion(expression);
  }

  [Conditional("CT_ASSERT")]
  public static void False([DoesNotReturnIf(true)] bool condition, [CallerArgumentExpression(nameof(condition))] string expression = null!)
  {
    if (condition)
      ThrowHelper.Assertion(expression);
  }

  [Conditional("CT_ASSERT")]
  public static void Equals<T>(
    T value,
    T expected,
    [CallerArgumentExpression(nameof(value))] string valueExpr = null!,
    [CallerArgumentExpression(nameof(expected))] string expectedExpr = null!)
  {
    if (!EqualityComparer<T>.Default.Equals(value, expected))
      ThrowHelper.Assertion($"Expression '{valueExpr}' expected to be: '{expected}({expectedExpr})', but actual: '{value}'");
  }
}