using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.KeyAsio.Interop;

internal sealed class KeyAsioLazerIpcClient
{
    public const int ProtocolVersion = 2;

    private const string timing_pipe_name = "KeyAsio.LazerBridge.v1";
    private const string event_pipe_name = "KeyAsio.LazerBridge.Events.v1";
    private const int large_frame_log_threshold = 1024 * 1024;

    public static KeyAsioLazerIpcClient Shared { get; } = new();

    private readonly PipePublisher timingPublisher =
        new(timing_pipe_name, KeyAsioLazerFieldMask.Timing, preserveEveryFrame: false);

    private readonly PipePublisher eventPublisher =
        new(event_pipe_name, KeyAsioLazerFieldMask.Events, preserveEveryFrame: true);

    private KeyAsioLazerIpcClient()
    {
    }

    public void Publish(KeyAsioLazerState state)
    {
        PublishTiming(state);
        PublishEvent(state);
    }

    public void PublishTiming(KeyAsioLazerState state) => timingPublisher.Publish(state);

    public void PublishEvent(KeyAsioLazerState state) => eventPublisher.Publish(state);

    private sealed class PipePublisher
    {
        private const int max_queued_states = 256;

        private readonly string pipeName;
        private readonly KeyAsioLazerFieldMask fieldMask;
        private readonly ConcurrentQueue<KeyAsioLazerState>? queuedStates;
        private readonly object startupLock = new();

        private CancellationTokenSource? cts;
        private Task? workerTask;
        private KeyAsioLazerState? latestState;
        private int queuedStateCount;
        private long sequence;

        public PipePublisher(string pipeName, KeyAsioLazerFieldMask fieldMask, bool preserveEveryFrame)
        {
            this.pipeName = pipeName;
            this.fieldMask = fieldMask;
            queuedStates = preserveEveryFrame ? new ConcurrentQueue<KeyAsioLazerState>() : null;
        }

        public void Publish(KeyAsioLazerState state)
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
            if (workerTask != null) return;

            lock (startupLock)
            {
                if (workerTask != null) return;

                cts = new CancellationTokenSource();
                workerTask = Task.Run(() => RunAsync(cts.Token));
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
                    KeyAsioLazerState? lastSentState = null;
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

        private async Task<KeyAsioLazerState?> WriteChangedFieldsAsync(Stream stream, KeyAsioLazerState state,
            KeyAsioLazerState? lastSentState, byte[] lengthPrefix, PooledBufferWriter payloadWriter,
            CancellationToken token)
        {
            var delta = KeyAsioLazerDeltaFrame.Create(lastSentState, state, fieldMask);
            if (delta.Fields.Length > 0)
            {
                await WriteFrameAsync(stream, delta, pipeName, lengthPrefix, payloadWriter, token);
            }

            return state;
        }
    }

    private static async Task WriteFrameAsync(Stream stream, KeyAsioLazerDeltaFrame frame, string pipeName,
        byte[] lengthPrefix, PooledBufferWriter payloadWriter, CancellationToken token)
    {
        payloadWriter.Clear();
        WriteFramePayload(payloadWriter, frame);
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, payloadWriter.WrittenCount);

        if (payloadWriter.WrittenCount >= large_frame_log_threshold)
        {
            var fields = string.Join(", ", frame.Fields.Select(static field => field.Kind.ToString()));
            Logger.Log($"KeyASIO lazer IPC large frame on {pipeName}: {payloadWriter.WrittenCount} bytes ({fields}).");
        }

        await stream.WriteAsync(lengthPrefix.AsMemory(0, sizeof(int)), token);
        await stream.WriteAsync(payloadWriter.WrittenMemory, token);
        await stream.FlushAsync(token);
    }

    private static void WriteFramePayload(PooledBufferWriter writer, KeyAsioLazerDeltaFrame frame)
    {
        WriteInt32(writer, frame.Version);
        WriteInt32(writer, frame.Fields.Length);

        foreach (var field in frame.Fields)
        {
            WriteByte(writer, (byte)field.Kind);
            switch (field.Kind)
            {
                case KeyAsioLazerFieldKind.ProcessId:
                case KeyAsioLazerFieldKind.Status:
                case KeyAsioLazerFieldKind.PlayTime:
                case KeyAsioLazerFieldKind.Combo:
                case KeyAsioLazerFieldKind.Score:
                    WriteInt32(writer, field.IntValue);
                    break;

                case KeyAsioLazerFieldKind.Mods:
                    WriteUInt32(writer, field.UIntValue);
                    break;

                case KeyAsioLazerFieldKind.IsReplay:
                    WriteByte(writer, field.BoolValue ? (byte)1 : (byte)0);
                    break;

                case KeyAsioLazerFieldKind.Username:
                case KeyAsioLazerFieldKind.BeatmapFolder:
                case KeyAsioLazerFieldKind.BeatmapFilename:
                case KeyAsioLazerFieldKind.UserDataDirectory:
                case KeyAsioLazerFieldKind.ExeDirectory:
                    WriteString(writer, field.StringValue);
                    break;

                case KeyAsioLazerFieldKind.BeatmapFiles:
                    WriteFiles(writer, field.FilesValue);
                    break;

                case KeyAsioLazerFieldKind.Statistics:
                    WriteStatistics(writer, field.StatisticsValue);
                    break;

                case KeyAsioLazerFieldKind.HitErrors:
                    WriteInt32(writer, field.IntValue);
                    WriteInt32Array(writer, field.IntArrayValue);
                    break;

                case KeyAsioLazerFieldKind.SkinInfos:
                    WriteSkinInfos(writer, field.SkinInfosValue);
                    break;
            }
        }
    }

    private static void WriteFiles(PooledBufferWriter writer, KeyAsioLazerFile[]? files)
    {
        if (files == null)
        {
            WriteInt32(writer, -1);
            return;
        }

        WriteInt32(writer, files.Length);
        foreach (var file in files)
        {
            WriteString(writer, file.Name);
            WriteString(writer, file.Path);
        }
    }

    private static void WriteStatistics(PooledBufferWriter writer, KeyAsioLazerStatistics statistics)
    {
        WriteInt32(writer, statistics.Perfect);
        WriteInt32(writer, statistics.Great);
        WriteInt32(writer, statistics.Good);
        WriteInt32(writer, statistics.Ok);
        WriteInt32(writer, statistics.Meh);
        WriteInt32(writer, statistics.Miss);
    }

    private static void WriteSkinInfos(PooledBufferWriter writer, KeyAsioLazerSkinInfo[]? skinInfos)
    {
        if (skinInfos == null)
        {
            WriteInt32(writer, -1);
            return;
        }

        WriteInt32(writer, skinInfos.Length);
        foreach (var skinInfo in skinInfos)
        {
            WriteString(writer, skinInfo.Id);
            WriteString(writer, skinInfo.Name);
            WriteString(writer, skinInfo.Creator);
            WriteString(writer, skinInfo.InstantiationInfo);
            WriteByte(writer, skinInfo.Protected ? (byte)1 : (byte)0);
            WriteFiles(writer, skinInfo.Files);
        }
    }

    private static void WriteInt32Array(PooledBufferWriter writer, int[]? values)
    {
        if (values == null)
        {
            WriteInt32(writer, -1);
            return;
        }

        WriteInt32(writer, values.Length);
        foreach (var value in values)
        {
            WriteInt32(writer, value);
        }
    }

    private static void WriteString(PooledBufferWriter writer, string? value)
    {
        if (value == null)
        {
            WriteInt32(writer, -1);
            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteInt32(writer, byteCount);
        var span = writer.GetSpan(byteCount);
        var written = Encoding.UTF8.GetBytes(value, span);
        writer.Advance(written);
    }

    private static void WriteByte(PooledBufferWriter writer, byte value)
    {
        var span = writer.GetSpan(sizeof(byte));
        span[0] = value;
        writer.Advance(sizeof(byte));
    }

    private static void WriteInt32(PooledBufferWriter writer, int value)
    {
        var span = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(span, value);
        writer.Advance(sizeof(int));
    }

    private static void WriteUInt32(PooledBufferWriter writer, uint value)
    {
        var span = writer.GetSpan(sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        writer.Advance(sizeof(uint));
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
