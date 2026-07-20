using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using osu.Framework.Logging;
using OverlayAPI.LazerProtocol;

namespace osu.Game.Rulesets.OverlayAPI.Interop;

internal sealed class OverlayApiLazerIpcClient
{
    private const int large_frame_log_threshold = 1024 * 1024;

    public static OverlayApiLazerIpcClient Shared { get; } = new();

    // Dedicated background worker for short, one-off off-thread work (beatmap mapping, skin
    // collection). NOT used for the long-lived pipe pump loops, which each own a dedicated thread
    // (see PipePublisher).
    private readonly BackgroundWorker worker = new("OverlayAPI.LazerWorker");

    /// <summary>
    /// The shared background worker for short off-thread I/O (beatmap mapping, skin collection).
    /// </summary>
    public BackgroundWorker Worker => worker;

    private readonly PipePublisher timingPublisher =
        new(LazerProtocolConstants.TimingPipeName, LazerFieldMask.Timing, preserveEveryFrame: false);

    private readonly PipePublisher eventPublisher =
        new(LazerProtocolConstants.EventPipeName, LazerFieldMask.Events, preserveEveryFrame: true);

    public void Publish(OverlayApiLazerState state)
    {
        PublishTiming(state);
        PublishEvent(state);
    }

    public void PublishTiming(OverlayApiLazerState state) => timingPublisher.Publish(state);

    public void PublishEvent(OverlayApiLazerState state) => eventPublisher.Publish(state);

    private sealed class PipePublisher
    {
        private const int max_queued_states = 256;

        private readonly string pipeName;
        private readonly LazerFieldMask fieldMask;
        private readonly ConcurrentQueue<OverlayApiLazerState>? queuedStates;
        private readonly object startupLock = new();

        private CancellationTokenSource? cts;
        private Thread? pumpThread;
        private OverlayApiLazerState? latestState;
        private int queuedStateCount;
        private long sequence;

        public PipePublisher(string pipeName, LazerFieldMask fieldMask, bool preserveEveryFrame)
        {
            this.pipeName = pipeName;
            this.fieldMask = fieldMask;
            queuedStates = preserveEveryFrame ? new ConcurrentQueue<OverlayApiLazerState>() : null;
        }

        public void Publish(OverlayApiLazerState state)
        {
            Volatile.Write(ref latestState, state);
            Interlocked.Increment(ref sequence);

            if (queuedStates != null)
            {
                queuedStates.Enqueue(state);
                Interlocked.Increment(ref queuedStateCount);
                TrimQueuedStates();
            }

            EnsureStarted();
        }

        private void TrimQueuedStates()
        {
            if (queuedStates == null)
                return;

            while (Volatile.Read(ref queuedStateCount) > max_queued_states && queuedStates.TryDequeue(out _))
                Interlocked.Decrement(ref queuedStateCount);
        }

        private void EnsureStarted()
        {
            if (pumpThread != null) return;

            lock (startupLock)
            {
                if (pumpThread != null) return;

                cts = new CancellationTokenSource();
                var token = cts.Token;

                pumpThread = new Thread(() => RunAsync(token).GetAwaiter().GetResult())
                {
                    IsBackground = true,
                    Name = $"OverlayAPI.LazerIpc.{pipeName}"
                };
                pumpThread.Start();
            }
        }

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
                        PipeOptions.Asynchronous);

                    await pipe.ConnectAsync(1000, token);
                    OverlayApiLazerState? lastSentState = null;
                    var lastSentSequence = -1L;
                    var lengthPrefix = ArrayPool<byte>.Shared.Rent(sizeof(int));
                    using var payloadWriter = new PooledBufferWriter(512);

                    try
                    {
                        if (queuedStates != null)
                        {
                            while (queuedStates.TryDequeue(out _))
                            {
                                Interlocked.Decrement(ref queuedStateCount);
                            }
                        }

                        var initialState = Volatile.Read(ref latestState);
                        if (initialState != null)
                        {
                            lastSentState = await WriteChangedFieldsAsync(pipe, initialState, lastSentState,
                                lengthPrefix, payloadWriter, token);
                            lastSentSequence = Volatile.Read(ref sequence);
                        }

                        while (!token.IsCancellationRequested && pipe.IsConnected)
                        {
                            if (queuedStates != null)
                            {
                                if (queuedStates.TryDequeue(out var queuedState))
                                {
                                    Interlocked.Decrement(ref queuedStateCount);
                                    lastSentState = await WriteChangedFieldsAsync(pipe, queuedState, lastSentState,
                                        lengthPrefix, payloadWriter, token);
                                }
                                else
                                {
                                    await Task.Delay(2, token);
                                }
                            }
                            else
                            {
                                var currentSequence = Volatile.Read(ref sequence);
                                if (currentSequence != lastSentSequence)
                                {
                                    var state = Volatile.Read(ref latestState);
                                    if (state != null)
                                    {
                                        lastSentState = await WriteChangedFieldsAsync(pipe, state, lastSentState,
                                            lengthPrefix, payloadWriter, token);
                                        lastSentSequence = currentSequence;
                                    }
                                }
                                else
                                {
                                    await Task.Delay(2, token);
                                }
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(lengthPrefix);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (TimeoutException)
                {
                    await DelayBeforeReconnect(token);
                }
                catch (IOException)
                {
                    await DelayBeforeReconnect(token);
                }
                catch
                {
                    await DelayBeforeReconnect(token);
                }
            }
        }

        private async Task<OverlayApiLazerState?> WriteChangedFieldsAsync(Stream stream, OverlayApiLazerState state,
            OverlayApiLazerState? lastSentState, byte[] lengthPrefix, PooledBufferWriter payloadWriter,
            CancellationToken token)
        {
            var delta = OverlayApiLazerDeltaBuilder.Create(lastSentState, state, fieldMask);
            if (delta.Fields.Length > 0)
            {
                await WriteFrameAsync(stream, delta, pipeName, lengthPrefix, payloadWriter, token);
            }

            return state;
        }
    }

    private static async Task WriteFrameAsync(Stream stream, LazerDeltaFrame frame, string pipeName,
        byte[] lengthPrefix, PooledBufferWriter payloadWriter, CancellationToken token)
    {
        payloadWriter.Clear();
        frame.Write(payloadWriter);
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, payloadWriter.WrittenCount);

        if (payloadWriter.WrittenCount >= large_frame_log_threshold)
        {
            var fields = string.Join(", ", frame.Fields.Select(static field => field.Kind.ToString()));
            Logger.Log($"OverlayAPI lazer IPC large frame on {pipeName}: {payloadWriter.WrittenCount} bytes ({fields}).");
        }

        await stream.WriteAsync(lengthPrefix.AsMemory(0, sizeof(int)), token);
        await stream.WriteAsync(payloadWriter.WrittenMemory, token);
        await stream.FlushAsync(token);
    }

    private static async Task DelayBeforeReconnect(CancellationToken token)
    {
        try
        {
            await Task.Delay(1000, token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[] buffer;
        private int index;

        public PooledBufferWriter(int initialCapacity)
        {
            buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        }

        public int WrittenCount => index;
        public ReadOnlyMemory<byte> WrittenMemory => buffer.AsMemory(0, index);

        public void Clear()
        {
            index = 0;
        }

        public void Advance(int count)
        {
            if ((uint)count > (uint)(buffer.Length - index))
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            index += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return buffer.AsMemory(index);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return buffer.AsSpan(index);
        }

        public void Dispose()
        {
            var toReturn = buffer;
            buffer = [];
            index = 0;
            ArrayPool<byte>.Shared.Return(toReturn);
        }

        private void Ensure(int sizeHint)
        {
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint));
            }

            if (sizeHint == 0)
            {
                sizeHint = 1;
            }

            if (sizeHint <= buffer.Length - index)
            {
                return;
            }

            var newSize = Math.Max(buffer.Length * 2, index + sizeHint);
            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            buffer.AsSpan(0, index).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = newBuffer;
        }
    }
}
