// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ConcurrencyToolkit.Internal;

internal class AssertionException : Exception
{
  public AssertionException(string? message) : base(message)
  {
  }
}

internal static class ThrowHelper
{
  [DoesNotReturn] public static void Unreachable() => throw new UnreachableException();
  [DoesNotReturn] public static void Assertion(string expression) => throw new AssertionException(expression);
  [DoesNotReturn] public static T Unreachable<T>() => throw new UnreachableException();
  [DoesNotReturn] public static void InvalidOperation() => throw new InvalidOperationException();
  [DoesNotReturn] public static void OperationCanceledWithoutToken() => throw new OperationCanceledException();

  public static TValue KeyNotFound<TKey, TValue>(TKey key) =>
    throw new KeyNotFoundException($"Not found. Key: '{key}'");

  public static void KeyAlreadyExists<TKey>(TKey key) =>
    throw new ArgumentException($"Key '{key}' already present in the dictionary.", nameof(key));

  public static void InvalidOperation_ConcurrentModification() => throw new InvalidOperationException(
    "A concurrent update was performed on this collection and corrupted its state. The collection's state is no longer correct");

  [MethodImpl(MethodImplOptions.NoInlining)]
  public static void FailFast(OutOfMemoryException oom)
  {
    if (Settings.FailFastOnCorruptedState)
      Environment.FailFast(nameof(OutOfMemoryException), oom);
  }

  public static void OutOfRange_Priority(int priority, int maxPriority) =>
    throw new ArgumentOutOfRangeException(nameof(priority), priority, $"Priority should be >= 0 && <= {maxPriority}");
}

internal static class ValueTasks
{
  public static ValueTask Canceled;

  static ValueTasks()
  {
    var cts = new CancellationTokenSource();
    cts.Cancel();
    Canceled = ValueTask.FromCanceled(cts.Token);
  }
}