// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Runtime.InteropServices;

namespace ConcurrencyToolkit.Pooling.LiteObjectPool;

[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct PaddedReference
{
  [FieldOffset(0)]
  public object? Object;
}
