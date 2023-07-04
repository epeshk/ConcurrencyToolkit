// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Reflection;
using System.Runtime.InteropServices;

namespace ConcurrencyToolkit.Collections;

internal static class Atomic<TValue>
{
  public static readonly bool IsValueWriteAtomic = IsWriteAtomic();

  /// <summary>Determines whether type TValue can be written atomically.</summary>
  private static bool IsWriteAtomic()
  {
    // Section 12.6.6 of ECMA CLI explains which types can be read and written atomically without
    // the risk of tearing. See https://www.ecma-international.org/publications/files/ECMA-ST/ECMA-335.pdf

    if (!typeof(TValue).IsValueType ||
        typeof(TValue) == typeof(IntPtr) ||
        typeof(TValue) == typeof(UIntPtr))
    {
      return true;
    }

    switch (Type.GetTypeCode(typeof(TValue)))
    {
      case TypeCode.Boolean:
      case TypeCode.Byte:
      case TypeCode.Char:
      case TypeCode.Int16:
      case TypeCode.Int32:
      case TypeCode.SByte:
      case TypeCode.Single:
      case TypeCode.UInt16:
      case TypeCode.UInt32:
        return true;

      case TypeCode.Double:
      case TypeCode.Int64:
      case TypeCode.UInt64:
        return IntPtr.Size == 8;

      default:
        return false;
    }
  }
}

internal static class Atomic
{
  public static readonly bool IsStrongMemoryOrdering =
    RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.X86;
  public static readonly bool IsWeakMemoryOrdering =
    !IsStrongMemoryOrdering;
}