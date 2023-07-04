// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;
using ConcurrencyToolkit.Internal;

namespace ConcurrencyToolkit.Tasks.Sources;

/// <summary>Provides the core logic for implementing a manual-reset <see cref="IValueTaskSource"/> or <see cref="IValueTaskSource{TResult}"/>.</summary>
/// <typeparam name="TResult">Specifies the type of results of the operation represented by this instance.</typeparam>
/// <remarks>
/// Differences with <see cref="ManualResetValueTaskSourceCore{TResult}"/>:
///   1. Removed exception and cancellation logic
///   2. OnCompleted allowed to run continuation inline, when <see cref="RunContinuationsAsynchronously"/> is false.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal struct ManualResetCompletionSourceCore<TResult>
{
  /// <summary>
  /// The callback to invoke when the operation completes if <see cref="OnCompleted"/> was called before the operation completed,
  /// or <see cref="ManualResetCompletionSourceCoreShared.s_sentinel"/> if the operation completed before a callback was supplied,
  /// or null if a callback hasn't yet been provided and the operation hasn't yet completed.
  /// </summary>
  private Action<object?>? _continuation;

  /// <summary>State to pass to <see cref="_continuation"/>.</summary>
  private object? _continuationState;

  /// <summary>
  /// Null if no special context was found.
  /// ExecutionContext if one was captured due to needing to be flowed.
  /// A scheduler (TaskScheduler or SynchronizationContext) if one was captured and needs to be used for callback scheduling.
  /// Or a CapturedContext if there's both an ExecutionContext and a scheduler.
  /// The most common and the fast path case to optimize for is null.
  /// </summary>
  private object? _capturedContext;

  /// <summary>The result with which the operation succeeded, or the default value if it hasn't yet completed or failed.</summary>
  private TResult? _result;

  /// <summary>The current version of this value, used to help prevent misuse.</summary>
  private short _version;

  /// <summary>Whether the current operation has completed.</summary>
  private bool _completed;

  /// <summary>Whether to force continuations to run asynchronously.</summary>
  private bool _runContinuationsAsynchronously;

  /// <summary>Gets or sets whether to force continuations to run asynchronously.</summary>
  /// <remarks>Continuations may run asynchronously if this is false, but they'll never run synchronously if this is true.</remarks>
  public bool RunContinuationsAsynchronously
  {
    get => _runContinuationsAsynchronously;
    set => _runContinuationsAsynchronously = value;
  }

  /// <summary>Resets to prepare for the next operation.</summary>
  public void Reset()
  {
    // Reset/update state for the next use/await of this instance.
    _version++;
    _continuation = null;
    _continuationState = null;
    _capturedContext = null;
    _result = default;
    _completed = false;
  }

  /// <summary>Completes with a successful result.</summary>
  /// <param name="result">The result.</param>
  public void SetResult(TResult result)
  {
    _result = result;
    SignalCompletion();
  }

  /// <summary>Gets the operation version.</summary>
  public short Version => _version;

  /// <summary>Gets the status of the operation.</summary>
  /// <param name="token">Opaque value that was provided to the <see cref="ValueTask"/>'s constructor.</param>
  public ValueTaskSourceStatus GetStatus(short token)
  {
    ValidateToken(token);
    return
      Volatile.Read(ref _continuation) is null || !_completed
        ? ValueTaskSourceStatus.Pending
        : ValueTaskSourceStatus.Succeeded;
  }

  /// <summary>Gets the result of the operation.</summary>
  /// <param name="token">Opaque value that was provided to the <see cref="ValueTask"/>'s constructor.</param>
  [StackTraceHidden]
  public TResult GetResult(short token)
  {
    if (token != _version || !_completed)
    {
      ThrowHelper.InvalidOperation();
    }

    return _result!;
  }

  /// <summary>Schedules the continuation action for this operation.</summary>
  /// <param name="continuation">The continuation to invoke when the operation has completed.</param>
  /// <param name="state">The state object to pass to <paramref name="continuation"/> when it's invoked.</param>
  /// <param name="token">Opaque value that was provided to the <see cref="ValueTask"/>'s constructor.</param>
  /// <param name="flags">The flags describing the behavior of the continuation.</param>
  public void OnCompleted(Action<object?> continuation, object? state, short token,
    ValueTaskSourceOnCompletedFlags flags)
  {
    ArgumentNullException.ThrowIfNull(continuation);

    ValidateToken(token);

    if ((flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) != 0)
    {
      _capturedContext = ExecutionContext.Capture();
    }

    if ((flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext) != 0)
    {
      if (SynchronizationContext.Current is SynchronizationContext sc &&
          sc.GetType() != typeof(SynchronizationContext))
      {
        _capturedContext = _capturedContext is null
          ? sc
          : new CapturedSchedulerAndExecutionContext(sc, (ExecutionContext)_capturedContext);
      }
      else
      {
        TaskScheduler ts = TaskScheduler.Current;
        if (ts != TaskScheduler.Default)
        {
          _capturedContext = _capturedContext is null
            ? ts
            : new CapturedSchedulerAndExecutionContext(ts, (ExecutionContext)_capturedContext);
        }
      }
    }

    // We need to set the continuation state before we swap in the delegate, so that
    // if there's a race between this and SetResult/Exception and SetResult/Exception
    // sees the _continuation as non-null, it'll be able to invoke it with the state
    // stored here.  However, this also means that if this is used incorrectly (e.g.
    // awaited twice concurrently), _continuationState might get erroneously overwritten.
    // To minimize the chances of that, we check preemptively whether _continuation
    // is already set to something other than the completion sentinel.
    object? storedContinuation = _continuation;
    if (storedContinuation is null)
    {
      _continuationState = state;
      storedContinuation = Interlocked.CompareExchange(ref _continuation, continuation, null);
      if (storedContinuation is null)
      {
        // Operation hadn't already completed, so we're done. The continuation will be
        // invoked when SetResult/Exception is called at some later point.
        return;
      }
    }

    // Operation already completed, so we need to queue the supplied callback.
    // At this point the storedContinuation should be the sentinal; if it's not, the instance was misused.
    Debug.Assert(storedContinuation is not null, $"{nameof(storedContinuation)} is null");
    if (!ReferenceEquals(storedContinuation, ManualResetCompletionSourceCoreShared.s_sentinel))
    {
      ThrowHelper.InvalidOperation();
    }

    object? capturedContext = _capturedContext;
    switch (capturedContext)
    {
      case null:
        if (RunContinuationsAsynchronously)
          ThreadPool.UnsafeQueueUserWorkItem(continuation, state, preferLocal: true);
        else // difference with ManualResetValueTaskSourceCore: execute continuation inline
          continuation(state);
        break;

      case ExecutionContext:
        ThreadPool.QueueUserWorkItem(continuation, state, preferLocal: true);
        break;

      default:
        ManualResetCompletionSourceCoreShared.ScheduleCapturedContext(capturedContext, continuation, state);
        break;
    }
  }

  /// <summary>Ensures that the specified token matches the current version.</summary>
  /// <param name="token">The token supplied by <see cref="ValueTask"/>.</param>
  private void ValidateToken(short token)
  {
    if (token != _version)
    {
      ThrowHelper.InvalidOperation();
    }
  }

  /// <summary>Signals that the operation has completed.  Invoked after the result or error has been set.</summary>
  private void SignalCompletion()
  {
    if (_completed)
    {
      ThrowHelper.InvalidOperation();
    }

    _completed = true;

    Action<object?>? continuation =
      Volatile.Read(ref _continuation) ??
      Interlocked.CompareExchange(ref _continuation, ManualResetCompletionSourceCoreShared.s_sentinel, null);

    if (continuation is not null)
    {
      Debug.Assert(continuation is not null, $"{nameof(continuation)} is null");

      object? context = _capturedContext;
      if (context is null)
      {
        if (_runContinuationsAsynchronously)
        {
          ThreadPool.UnsafeQueueUserWorkItem(continuation, _continuationState, preferLocal: true);
        }
        else
        {
          continuation(_continuationState);
        }
      }
      else if (context is ExecutionContext or CapturedSchedulerAndExecutionContext)
      {
        ManualResetCompletionSourceCoreShared.InvokeContinuationWithContext(context, continuation, _continuationState,
          _runContinuationsAsynchronously);
      }
      else
      {
        Debug.Assert(context is TaskScheduler or SynchronizationContext, $"context is {context}");
        ManualResetCompletionSourceCoreShared.ScheduleCapturedContext(context, continuation, _continuationState);
      }
    }
  }
}