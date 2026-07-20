using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace OverlayAPI.LazerProtocol;

/// <summary>
/// Runs work items sequentially on a dedicated background thread, keeping them off both
/// the .NET thread pool and the caller's thread. Suitable for long-lived loops (IPC pumps)
/// and short blocking I/O (file mapping, skin collection) that should not contend with the
/// game update thread or erode thread-pool capacity.
/// </summary>
public sealed class BackgroundWorker : IDisposable
{
    private readonly Channel<Func<CancellationToken, Task>> _channel;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Creates a new worker backed by a dedicated, long-running thread.
    /// </summary>
    /// <param name="name">Thread name for diagnostics.</param>
    public BackgroundWorker(string name = "OverlayAPI.BackgroundWorker")
    {
        _channel = Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _thread = new Thread(() => RunLoop(_cts.Token))
        {
            IsBackground = true,
            Name = name
        };
        _thread.Start();
    }

    /// <summary>
    /// Enqueue a cancellable async work item. Fire-and-forget; exceptions are swallowed.
    /// </summary>
    public void Enqueue(Func<CancellationToken, Task> workItem) => _channel.Writer.TryWrite(workItem);

    /// <summary>
    /// Enqueue a cancellable async work item and await its completion.
    /// </summary>
    public Task EnqueueAsync(Func<CancellationToken, Task> workItem)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Writer.TryWrite(async ct =>
        {
            try
            {
                await workItem(ct).ConfigureAwait(false);
                tcs.TrySetResult();
            }
            catch (OperationCanceledException oce)
            {
                tcs.TrySetCanceled(oce.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>
    /// Enqueue a cancellable async work item that returns a value, and await its result.
    /// </summary>
    public Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> workItem)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Writer.TryWrite(async ct =>
        {
            try
            {
                var result = await workItem(ct).ConfigureAwait(false);
                tcs.TrySetResult(result);
            }
            catch (OperationCanceledException oce)
            {
                tcs.TrySetCanceled(oce.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>
    /// Enqueue a synchronous work item that returns a value, and await its result.
    /// </summary>
    public Task<T> EnqueueAsync<T>(Func<CancellationToken, T> workItem)
        => EnqueueAsync(ct => Task.FromResult(workItem(ct)));

    /// <summary>
    /// Enqueue a synchronous work item. Fire-and-forget; exceptions are swallowed.
    /// </summary>
    public void Enqueue(Action<CancellationToken> workItem)
        => Enqueue(ct =>
        {
            workItem(ct);
            return Task.CompletedTask;
        });

    /// <summary>
    /// Enqueue a synchronous work item and await its completion.
    /// </summary>
    public Task EnqueueAsync(Action<CancellationToken> workItem)
        => EnqueueAsync(ct =>
        {
            workItem(ct);
            return Task.CompletedTask;
        });

    private async void RunLoop(CancellationToken token)
    {
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var workItem))
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        await workItem(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Ignore.
                    }
                    catch
                    {
                        // Swallow; callers that need error propagation use EnqueueAsync.
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore.
        }
        catch
        {
            // Swallow fatal loop errors to keep the thread from crashing silently into a bad state.
        }
    }

    public void Dispose()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
        _channel.Writer.TryComplete();
        _cts.Dispose();
    }
}
