// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Runtime.InteropServices;

namespace ConcurrencyToolkit.Metrics;

/// <summary>
/// Thread safe counter that distributes writes across per-core variables.
/// </summary>
public sealed class ThreadSafeCounter64
{
  private readonly PaddedLong[] values = new PaddedLong[Environment.ProcessorCount];

  /// <summary>
  /// Adds <paramref name="value"/> to the counter value.
  /// </summary>
  public void Add(long value) =>
    Interlocked.Add(ref values[GetIndex()].Value, value);

  /// <summary>
  /// Increments the counter value.
  /// </summary>
  public void Increment() =>
    Interlocked.Increment(ref values[GetIndex()].Value);

  /// <summary>
  /// Retrieves counter value.
  /// </summary>
  public long Get()
  {
    var values = this.values;
    long sum = 0;
    for (int i = 0; i < values.Length; i++)
      sum += Interlocked.Read(ref values[i].Value);
    return sum;
  }

  /// <summary>
  /// Retrieves counter value and resets it to zero, as an atomic operation.
  /// </summary>
  /// <returns></returns>
  public long GetAndReset()
  {
    var values = this.values;
    long sum = 0;
    for (int i = 0; i < values.Length; i++)
      sum += Interlocked.Exchange(ref values[i].Value, 0);
    return sum;
  }

  private static int GetIndex() => Thread.GetCurrentProcessorId() % Environment.ProcessorCount;
}

[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct PaddedLong
{
  [FieldOffset(0)]
  public long Value;
}