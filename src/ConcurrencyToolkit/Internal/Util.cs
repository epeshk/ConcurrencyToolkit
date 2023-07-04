// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;

namespace ConcurrencyToolkit.Internal;

internal static class Utilities
{
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal static int SelectBucketIndex(int bufferSize)
  {
    // Buffers are bucketed so that a request between 2^(n-1) + 1 and 2^n is given a buffer of 2^n
    // Bucket index is log2(bufferSize - 1) with the exception that buffers between 1 and 16 bytes
    // are combined, and the index is slid down by 3 to compensate.
    // Zero is a valid bufferSize, and it is assigned the highest bucket index so that zero-length
    // buffers are not retained by the pool. The pool will return the Array.Empty singleton for these.
    return BitOperations.Log2((uint)bufferSize - 1 | 15) - 3;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal static int GetMaxSizeForBucket(int binIndex)
  {
    int maxSize = 16 << binIndex;
    Debug.Assert(maxSize >= 0);
    return maxSize;
  }

  internal enum MemoryPressure
  {
    Low,
    Medium,
    High
  }

  internal static MemoryPressure GetMemoryPressure()
  {
    const double HighPressureThreshold = .90; // Percent of GC memory pressure threshold we consider "high"
    const double MediumPressureThreshold = .70; // Percent of GC memory pressure threshold we consider "medium"

    GCMemoryInfo memoryInfo = GC.GetGCMemoryInfo();

    if (memoryInfo.MemoryLoadBytes >= memoryInfo.HighMemoryLoadThresholdBytes * HighPressureThreshold)
    {
      return MemoryPressure.High;
    }

    if (memoryInfo.MemoryLoadBytes >= memoryInfo.HighMemoryLoadThresholdBytes * MediumPressureThreshold)
    {
      return MemoryPressure.Medium;
    }

    return MemoryPressure.Low;
  }
}

/// <summary>
/// Schedules a callback roughly every gen 2 GC (you may see a Gen 0 an Gen 1 but only once)
/// (We can fix this by capturing the Gen 2 count at startup and testing, but I mostly don't care)
/// </summary>
internal sealed class Gen2GcCallback : CriticalFinalizerObject
{
  private readonly Func<bool>? _callback0;
  private readonly Func<object, bool>? _callback1;
  private GCHandle _weakTargetObj;

  private Gen2GcCallback(Func<bool> callback)
  {
    _callback0 = callback;
  }

  private Gen2GcCallback(Func<object, bool> callback, object targetObj)
  {
    _callback1 = callback;
    _weakTargetObj = GCHandle.Alloc(targetObj, GCHandleType.Weak);
  }

  /// <summary>
  /// Schedule 'callback' to be called in the next GC.  If the callback returns true it is
  /// rescheduled for the next Gen 2 GC.  Otherwise the callbacks stop.
  /// </summary>
  public static void Register(Func<bool> callback)
  {
    // Create a unreachable object that remembers the callback function and target object.
    new Gen2GcCallback(callback);
  }

  /// <summary>
  /// Schedule 'callback' to be called in the next GC.  If the callback returns true it is
  /// rescheduled for the next Gen 2 GC.  Otherwise the callbacks stop.
  ///
  /// NOTE: This callback will be kept alive until either the callback function returns false,
  /// or the target object dies.
  /// </summary>
  public static void Register(Func<object, bool> callback, object targetObj)
  {
    // Create a unreachable object that remembers the callback function and target object.
    new Gen2GcCallback(callback, targetObj);
  }

  ~Gen2GcCallback()
  {
    if (_weakTargetObj.IsAllocated)
    {
      // Check to see if the target object is still alive.
      object? targetObj = _weakTargetObj.Target;
      if (targetObj == null)
      {
        // The target object is dead, so this callback object is no longer needed.
        _weakTargetObj.Free();
        return;
      }

      // Execute the callback method.
      try
      {
        Debug.Assert(_callback1 != null);
        if (!_callback1(targetObj))
        {
          // If the callback returns false, this callback object is no longer needed.
          _weakTargetObj.Free();
          return;
        }
      }
      catch
      {
        // Ensure that we still get a chance to resurrect this object, even if the callback throws an exception.
#if DEBUG
        // Except in DEBUG, as we really shouldn't be hitting any exceptions here.
        throw;
#endif
      }
    }
    else
    {
      // Execute the callback method.
      try
      {
        Debug.Assert(_callback0 != null);
        if (!_callback0())
        {
          // If the callback returns false, this callback object is no longer needed.
          return;
        }
      }
      catch
      {
        // Ensure that we still get a chance to resurrect this object, even if the callback throws an exception.
#if DEBUG
        // Except in DEBUG, as we really shouldn't be hitting any exceptions here.
        throw;
#endif
      }
    }

    // Resurrect ourselves by re-registering for finalization.
    GC.ReRegisterForFinalize(this);
  }
}