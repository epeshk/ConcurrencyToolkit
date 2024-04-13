namespace ConcurrencyToolkit.Threading;

public static class InterlockedFloatingPoint
{
  /// <summary>
  /// Adds two 64-bit <see cref="double"/> numbers and replaces the first number with the sum, with compare-and-set loop.
  /// </summary>
  /// <param name="location1">A variable containing the first value to be added. The sum of the two values is stored in <paramref name="location1"/>.</param>
  /// <param name="value">The value to be added to the double at <paramref name="location1"/>.</param>
  /// <returns>The previous value stored at <paramref name="location1"/>.</returns>
  /// <exception cref="NullReferenceException">The address of location is a null pointer.</exception>
  public static double GetAndAdd(ref double location1, double value)
    => GetAndAdd(ref location1, value, out _);

  /// <inheritdoc cref="GetAndAdd(ref double,double)"/>
  /// <param name="collision"><c>true</c> if CAS attempt is retried.</param>
  public static double GetAndAdd(ref double location1, double value, out bool collision)
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

  /// <summary>
  /// Adds two 32-bit <see cref="float"/> numbers and replaces the first number with the sum, with compare-and-set loop.
  /// </summary>
  /// <param name="location1">A variable containing the first value to be added. The sum of the two values is stored in <paramref name="location1"/>.</param>
  /// <param name="value">The value to be added to the float at <paramref name="location1"/>.</param>
  /// <returns>The previous value stored at <paramref name="location1"/>.</returns>
  /// <exception cref="NullReferenceException">The address of location is a null pointer.</exception>
  public static float GetAndAdd(ref float location1, float value) =>
    GetAndAdd(ref location1, value, out _);

  /// <inheritdoc cref="GetAndAdd(ref float,float)"/>
  /// <param name="collision"><c>true</c> if CAS attempt is retried.</param>
  public static float GetAndAdd(ref float location1, float value, out bool collision)
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
}