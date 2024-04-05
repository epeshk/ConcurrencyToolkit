// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ConcurrencyToolkit.Metrics;

/// <summary>
/// Thread safe counter that distributes writes across per-core variables.
/// </summary>
public sealed class ThreadSafeCounter64
{
  private readonly PaddedCounterData[] values = new PaddedCounterData[BitOperations.RoundUpToPowerOf2((uint)Environment.ProcessorCount) + 1];

  /// <summary>
  /// Adds <paramref name="value"/> to the counter value.
  /// </summary>
  public void Add(long value)
  {
    ref var location = ref GetCell().Long;
    var valueBefore = Volatile.Read(ref location);
    var valueAfter = Interlocked.Add(ref location, value);
    if (valueBefore + value != valueAfter)
      ShardingId.Advance();
  }

  /// <summary>
  /// Increments the counter value.
  /// </summary>
  public void Increment()
  {
    ref var location = ref GetCell().Long;
    var valueBefore = Volatile.Read(ref location);
    var valueAfter = Interlocked.Increment(ref location);
    if (valueBefore + 1 != valueAfter)
      ShardingId.Advance();
  }

  /// <summary>
  /// Adds <paramref name="value"/> to the counter value.
  /// </summary>
  public void Add(double value)
  {
    var longPart = (long)value;

    // ReSharper disable once CompareOfFloatsByEqualityOperator
    if (longPart == value)
      Add(longPart);
    else
      AddFloat(value);
  }

  private void AddFloat(double value)
  {
    ref var cell = ref GetCell();
    CompensatedGetAndAdd(ref cell.Double, ref cell.Error, value, out bool collision);
    if (collision)
      ShardingId.Advance();
  }

  /// <summary>
  /// Retrieves counter value.
  /// </summary>
  public long Get()
  {
    var (longPart, doublePart) = GetParts();
    return longPart + (long)doublePart;
  }

  /// <summary>
  /// Retrieves counter value.
  /// </summary>
  public double GetDouble()
  {
    var parts = GetParts();
    return parts.Long + parts.Double;
  }

  /// <summary>
  /// Retrieves counter value and resets it to zero.
  /// </summary>
  /// <returns></returns>
  public long GetAndReset()
  {
    var parts = GetPartsAndReset();
    return parts.Long + (long)parts.Double;
  }

  private (long Long, double Double) GetParts()
  {
    var values = this.values;
    long sumLong = 0;
    double sumDouble = 0;
    double err = 0;
    for (int i = 0; i < values.Length; i++)
    {
      sumLong += Volatile.Read(ref values[i].Long);

      var d = Volatile.Read(ref values[i].Double);
      var e = Volatile.Read(ref values[i].Error);
      Add(ref sumDouble, ref err, d);
      Add(ref sumDouble, ref err, e);
    }
    return (sumLong, sumDouble + err);
  }

  /// <summary>
  /// Retrieves counter value and resets it to zero.
  /// </summary>
  /// <returns></returns>
  public double GetDoubleAndReset()
  {
    var parts = GetPartsAndReset();
    return parts.Long + parts.Double;
  }

  private (long Long, double Double) GetPartsAndReset()
  {
    var values = this.values;
    long sumLong = 0;
    double sumDouble = 0;
    double err = 0;
    for (int i = 0; i < values.Length; i++)
    {
      sumLong += Interlocked.Exchange(ref values[i].Long, 0);
      var d = Interlocked.Exchange(ref values[i].Double, 0);
      var e = Interlocked.Exchange(ref values[i].Error, 0);
      Add(ref sumDouble, ref err, d);
      Add(ref sumDouble, ref err, e);
    }
    return (sumLong, sumDouble + err);
  }

  private static void Add(ref double location, ref double error, double value)
  {
    var sum = location;
    var t = sum + value;

    var errorDelta = Math.Abs(sum) >= Math.Abs(value)
      ? sum - t + value
      : value - t + sum;

    error += errorDelta;
    location = t;
  }

  static double GetAndAdd(ref double location1, double value, out bool collision)
  {
    collision = false;
    while (true)
    {
      var currentValue = Volatile.Read(ref location1);
      var newValue = currentValue + value;
      // ReSharper disable once CompareOfFloatsByEqualityOperator
      if (Interlocked.CompareExchange(ref location1, newValue, currentValue) == currentValue)
        return currentValue;
      collision = true;
    }
  }

  static void CompensatedGetAndAdd(ref double location1, ref double location2, double value, out bool collision)
  {
    var sum = GetAndAdd(ref location1, value, out collision);
    var t = sum + value;

    var errorDelta = Math.Abs(sum) >= Math.Abs(value)
      ? sum - t + value
      : value - t + sum;

    if (errorDelta != 0)
      GetAndAdd(ref location2, errorDelta, out _);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private ref PaddedCounterData GetCell()
  {
    // optimized for NativeAOT
    return ref values[1 + (ShardingId.Current & (values.Length - 2))];
  }
}

[StructLayout(LayoutKind.Explicit, Size = 64 + 16)]
internal struct PaddedCounterData
{
  [FieldOffset(0)]
  public long Long;
  [FieldOffset(8)]
  public double Double;
  [FieldOffset(16)]
  public double Error;
}

static class ShardingId
{
  [ThreadStatic] private static uint current;

  public static uint Current => current;

  public static void Advance()
  {
    var c = current;

    if (c == 0)
    {
      current = (uint)Environment.CurrentManagedThreadId;
      return;
    }
    c ^= c << 13;
    c ^= c >> 17;
    c ^= c << 5;
    current = c;
  }

  public static void Set(uint x) => current = x;
}