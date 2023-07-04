// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using ConcurrencyToolkit.Internal;

namespace ConcurrencyToolkit.Tasks.Sources;

internal static class ManualResetCompletionSourceCoreShared // separated out of generic to avoid unnecessary duplication
{
  internal static readonly Action<object?> s_sentinel = CompletionSentinel;

  private static void CompletionSentinel(object? _) // named method to aid debugging
  {
    Debug.Fail("The sentinel delegate should never be invoked.");
    ThrowHelper.InvalidOperation();
  }

  internal static void ScheduleCapturedContext(object context, Action<object?> continuation, object? state)
  {
    Debug.Assert(
      context is SynchronizationContext or TaskScheduler or CapturedSchedulerAndExecutionContext,
      $"{nameof(context)} is {context}");

    switch (context)
    {
      case SynchronizationContext sc:
        ScheduleSynchronizationContext(sc, continuation, state);
        break;

      case TaskScheduler ts:
        ScheduleTaskScheduler(ts, continuation, state);
        break;

      default:
        CapturedSchedulerAndExecutionContext cc = (CapturedSchedulerAndExecutionContext)context;
        if (cc._scheduler is SynchronizationContext ccsc)
        {
          ScheduleSynchronizationContext(ccsc, continuation, state);
        }
        else
        {
          Debug.Assert(cc._scheduler is TaskScheduler, $"{nameof(cc._scheduler)} is {cc._scheduler}");
          ScheduleTaskScheduler((TaskScheduler)cc._scheduler, continuation, state);
        }

        break;
    }

    static void ScheduleSynchronizationContext(SynchronizationContext sc, Action<object?> continuation,
      object? state) =>
      sc.Post(continuation.Invoke, state);

    static void ScheduleTaskScheduler(TaskScheduler scheduler, Action<object?> continuation, object? state) =>
      Task.Factory.StartNew(continuation, state, CancellationToken.None, TaskCreationOptions.DenyChildAttach,
        scheduler);
  }

  internal static void InvokeContinuationWithContext(object capturedContext, Action<object?> continuation,
    object? continuationState, bool runContinuationsAsynchronously)
  {
    // This is in a helper as the error handling causes the generated asm
    // for the surrounding code to become less efficient (stack spills etc)
    // and it is an uncommon path.
    Debug.Assert(continuation is not null, $"{nameof(continuation)} is null");
    Debug.Assert(capturedContext is ExecutionContext or CapturedSchedulerAndExecutionContext,
      $"{nameof(capturedContext)} is {capturedContext}");

    // Capture the current EC.  We'll switch over to the target EC and then restore back to this one.
    ExecutionContext? currentContext = ExecutionContext.Capture();

    if (capturedContext is ExecutionContext ec)
    {
      ExecutionContext.Restore(ec); // Restore the captured ExecutionContext before executing anything.
      if (runContinuationsAsynchronously)
      {
        try
        {
          ThreadPool.QueueUserWorkItem(continuation, continuationState, preferLocal: true);
        }
        finally
        {
          ExecutionContext.Restore(currentContext); // Restore the current ExecutionContext.
        }
      }
      else
      {
        // Running inline may throw; capture the edi if it does as we changed the ExecutionContext,
        // so need to restore it back before propagating the throw.
        ExceptionDispatchInfo? edi = null;
        SynchronizationContext? syncContext = SynchronizationContext.Current;
        try
        {
          continuation(continuationState);
        }
        catch (Exception ex)
        {
          // Note: we have a "catch" rather than a "finally" because we want
          // to stop the first pass of EH here.  That way we can restore the previous
          // context before any of our callers' EH filters run.
          edi = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
          // Set sync context back to what it was prior to coming in.
          // Then restore the current ExecutionContext.
          SynchronizationContext.SetSynchronizationContext(syncContext);
          ExecutionContext.Restore(currentContext);
        }

        // Now rethrow the exception; if there is one.
        edi?.Throw();
      }
    }
    else
    {
      CapturedSchedulerAndExecutionContext cc = (CapturedSchedulerAndExecutionContext)capturedContext;
      ExecutionContext.Restore(cc
        ._executionContext); // Restore the captured ExecutionContext before executing anything.
      try
      {
        ScheduleCapturedContext(capturedContext, continuation, continuationState);
      }
      finally
      {
        ExecutionContext.Restore(currentContext); // Restore the current ExecutionContext.
      }
    }
  }
}