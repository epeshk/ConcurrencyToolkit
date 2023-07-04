// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

namespace ConcurrencyToolkit.Internal;

internal static class Settings
{
  private const string Prefix = "ConcurrencyToolkit_";

  public static int MaxBuffersPerArraySizePerCore = ReadIntFromEnv(nameof(MaxBuffersPerArraySizePerCore), 8);
  public static bool FailFastOnCorruptedState = ReadIntFromEnv(nameof(FailFastOnCorruptedState), 1) != 0;

  private static int ReadIntFromEnv(string key, int defaultValue)
  {
    try
    {
      var str = Environment.GetEnvironmentVariable(Prefix + key)?.Trim();
      if (string.IsNullOrWhiteSpace(str)) return defaultValue;
      return int.TryParse(str, out var val) ? val : defaultValue;
    }
    catch
    {
      return defaultValue;
    }
  }
}