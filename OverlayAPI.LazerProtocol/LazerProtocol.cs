using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace OverlayAPI.LazerProtocol;

public enum LazerFieldKind : byte
{
    // Numeric gaps are fields removed in protocol v4. Keep the remaining IDs stable so
    // malformed or stale frames cannot be mistaken for a different kind of data.
    ProcessId = 1,
    Status = 2,
    PlayTime = 3,
    Mods = 4,
    Combo = 5,
    IsReplay = 7,
    BeatmapFilename = 10,
    BeatmapFiles = 11,
    SkinInfos = 14,
    BeatmapOffset = 17,
}

[Flags]
public enum LazerFieldMask
{
    None = 0,
    ProcessId = 1 << 0,
    Status = 1 << 1,
    PlayTime = 1 << 2,
    Mods = 1 << 3,
    Combo = 1 << 4,
    IsReplay = 1 << 6,
    BeatmapFilename = 1 << 9,
    BeatmapFiles = 1 << 10,
    SkinInfos = 1 << 13,
    BeatmapOffset = 1 << 16,

    Timing = ProcessId | PlayTime | BeatmapOffset,

    Events = ProcessId | Status | Mods | Combo | IsReplay | BeatmapFilename | BeatmapFiles | SkinInfos,
    All = Timing | Events,
}

public sealed class LazerFile
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public sealed class LazerSkinInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LazerFile[] Files { get; set; } = [];
}

public struct LazerDeltaField
{
    public LazerFieldKind Kind { get; set; }
    public int IntValue { get; set; }
    public uint UIntValue { get; set; }
    public double DoubleValue { get; set; }
    public bool BoolValue { get; set; }
    public string? StringValue { get; set; }
    public LazerFile[]? FilesValue { get; set; }
    public LazerSkinInfo[]? SkinInfosValue { get; set; }

    public static LazerDeltaField ForInt(LazerFieldKind kind, int value)
        => new() { Kind = kind, IntValue = value };

    public static LazerDeltaField ForUInt(LazerFieldKind kind, uint value)
        => new() { Kind = kind, UIntValue = value };

    public static LazerDeltaField ForDouble(LazerFieldKind kind, double value)
        => new() { Kind = kind, DoubleValue = value };

    public static LazerDeltaField ForBool(LazerFieldKind kind, bool value)
        => new() { Kind = kind, BoolValue = value };

    public static LazerDeltaField ForString(LazerFieldKind kind, string? value)
        => new() { Kind = kind, StringValue = value };

    public static LazerDeltaField ForFiles(LazerFile[] value)
        => new() { Kind = LazerFieldKind.BeatmapFiles, FilesValue = value };

    public static LazerDeltaField ForSkinInfos(LazerSkinInfo[] value)
        => new() { Kind = LazerFieldKind.SkinInfos, SkinInfosValue = value };
}

public sealed class LazerDeltaFrame
{
    public int Version { get; set; } = LazerProtocolConstants.ProtocolVersion;
    public LazerDeltaField[] Fields { get; set; } = [];

    public static LazerDeltaFrame Parse(ReadOnlySpan<byte> payload)
    {
        var reader = new FrameReader(payload);
        var version = reader.ReadInt32();
        var fieldCount = reader.ReadInt32();
        if (fieldCount is < 0 or > 64)
        {
            throw new InvalidDataException($"Invalid lazer IPC field count: {fieldCount}.");
        }

        var fields = new LazerDeltaField[fieldCount];
        for (var i = 0; i < fields.Length; i++)
        {
            var field = new LazerDeltaField { Kind = (LazerFieldKind)reader.ReadByte() };
            switch (field.Kind)
            {
                case LazerFieldKind.ProcessId:
                case LazerFieldKind.Status:
                case LazerFieldKind.PlayTime:
                case LazerFieldKind.Combo:
                    field.IntValue = reader.ReadInt32();
                    break;

                case LazerFieldKind.Mods:
                    field.UIntValue = reader.ReadUInt32();
                    break;

                case LazerFieldKind.BeatmapOffset:
                    field.DoubleValue = reader.ReadDouble();
                    break;

                case LazerFieldKind.IsReplay:
                    field.BoolValue = reader.ReadByte() != 0;
                    break;

                case LazerFieldKind.BeatmapFilename:
                    field.StringValue = reader.ReadString();
                    break;

                case LazerFieldKind.BeatmapFiles:
                    field.FilesValue = reader.ReadFiles();
                    break;

                case LazerFieldKind.SkinInfos:
                    field.SkinInfosValue = reader.ReadSkinInfos();
                    break;

                default:
                    throw new InvalidDataException($"Unsupported lazer IPC field kind: {field.Kind}.");
            }

            fields[i] = field;
        }

        reader.EnsureEnd();
        return new LazerDeltaFrame
        {
            Version = version,
            Fields = fields,
        };
    }

    public void Write(IBufferWriter<byte> writer)
    {
        FrameWriter.WriteInt32(writer, Version);
        FrameWriter.WriteInt32(writer, Fields.Length);

        foreach (var field in Fields)
        {
            FrameWriter.WriteByte(writer, (byte)field.Kind);
            switch (field.Kind)
            {
                case LazerFieldKind.ProcessId:
                case LazerFieldKind.Status:
                case LazerFieldKind.PlayTime:
                case LazerFieldKind.Combo:
                    FrameWriter.WriteInt32(writer, field.IntValue);
                    break;

                case LazerFieldKind.Mods:
                    FrameWriter.WriteUInt32(writer, field.UIntValue);
                    break;

                case LazerFieldKind.BeatmapOffset:
                    FrameWriter.WriteDouble(writer, field.DoubleValue);
                    break;

                case LazerFieldKind.IsReplay:
                    FrameWriter.WriteByte(writer, field.BoolValue ? (byte)1 : (byte)0);
                    break;

                case LazerFieldKind.BeatmapFilename:
                    FrameWriter.WriteString(writer, field.StringValue);
                    break;

                case LazerFieldKind.BeatmapFiles:
                    FrameWriter.WriteFiles(writer, field.FilesValue);
                    break;

                case LazerFieldKind.SkinInfos:
                    FrameWriter.WriteSkinInfos(writer, field.SkinInfosValue);
                    break;
            }
        }
    }

    public bool HasField(LazerFieldKind kind)
    {
        foreach (var field in Fields)
        {
            if (field.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    public static bool FilesEqual(LazerFile[] left, LazerFile[] right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i].Name != right[i].Name || left[i].Path != right[i].Path)
                return false;
        }

        return true;
    }

    public static bool SkinInfosEqual(LazerSkinInfo[]? left, LazerSkinInfo[]? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        if (left.Length != right.Length) return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i].Id != right[i].Id ||
                left[i].Name != right[i].Name)
                return false;

            if (!FilesEqual(left[i].Files, right[i].Files))
                return false;
        }

        return true;
    }
}

public static class LazerProtocolConstants
{
    public const int ProtocolVersion = 4;
    public const string TimingPipeName = "OverlayAPI.LazerBridge.v1";
    public const string EventPipeName = "OverlayAPI.LazerBridge.Events.v1";
}

internal static class FrameWriter
{
    public static void WriteByte(IBufferWriter<byte> writer, byte value)
    {
        var span = writer.GetSpan(sizeof(byte));
        span[0] = value;
        writer.Advance(sizeof(byte));
    }

    public static void WriteInt32(IBufferWriter<byte> writer, int value)
    {
        var span = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(span, value);
        writer.Advance(sizeof(int));
    }

    public static void WriteUInt32(IBufferWriter<byte> writer, uint value)
    {
        var span = writer.GetSpan(sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        writer.Advance(sizeof(uint));
    }

    public static void WriteDouble(IBufferWriter<byte> writer, double value)
    {
        var span = writer.GetSpan(sizeof(double));
        BinaryPrimitives.WriteInt64LittleEndian(span, BitConverter.DoubleToInt64Bits(value));
        writer.Advance(sizeof(double));
    }

    public static void WriteString(IBufferWriter<byte> writer, string? value)
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

    public static void WriteFiles(IBufferWriter<byte> writer, LazerFile[]? files)
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

    public static void WriteSkinInfos(IBufferWriter<byte> writer, LazerSkinInfo[]? skinInfos)
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
            WriteFiles(writer, skinInfo.Files);
        }
    }
}

internal ref struct FrameReader
{
    private readonly ReadOnlySpan<byte> payload;
    private int offset;

    public FrameReader(ReadOnlySpan<byte> payload)
    {
        this.payload = payload;
    }

    public byte ReadByte()
    {
        EnsureAvailable(sizeof(byte));
        return payload[offset++];
    }

    public int ReadInt32()
    {
        EnsureAvailable(sizeof(int));
        var value = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, sizeof(int)));
        offset += sizeof(int);
        return value;
    }

    public uint ReadUInt32()
    {
        EnsureAvailable(sizeof(uint));
        var value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, sizeof(uint)));
        offset += sizeof(uint);
        return value;
    }

    public double ReadDouble()
    {
        EnsureAvailable(sizeof(double));
        var bits = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(offset, sizeof(double)));
        offset += sizeof(double);
        return BitConverter.Int64BitsToDouble(bits);
    }

    public string? ReadString()
    {
        var byteCount = ReadInt32();
        if (byteCount < 0)
        {
            if (byteCount == -1)
            {
                return null;
            }

            throw new InvalidDataException($"Invalid lazer IPC string length: {byteCount}.");
        }

        EnsureAvailable(byteCount);
        var value = Encoding.UTF8.GetString(payload.Slice(offset, byteCount));
        offset += byteCount;
        return value;
    }

    public LazerFile[]? ReadFiles()
    {
        var count = ReadInt32();
        if (count < 0)
        {
            if (count == -1)
            {
                return null;
            }

            throw new InvalidDataException($"Invalid lazer IPC file count: {count}.");
        }

        if (count > 100_000)
        {
            throw new InvalidDataException($"Invalid lazer IPC file count: {count}.");
        }

        var files = new LazerFile[count];
        for (var i = 0; i < files.Length; i++)
        {
            files[i] = new LazerFile
            {
                Name = ReadString() ?? string.Empty,
                Path = ReadString() ?? string.Empty,
            };
        }

        return files;
    }

    public LazerSkinInfo[]? ReadSkinInfos()
    {
        var count = ReadInt32();
        if (count < 0)
        {
            if (count == -1)
            {
                return null;
            }

            throw new InvalidDataException($"Invalid lazer IPC skin info count: {count}.");
        }

        if (count > 10_000)
        {
            throw new InvalidDataException($"Invalid lazer IPC skin info count: {count}.");
        }

        var infos = new LazerSkinInfo[count];
        for (var i = 0; i < infos.Length; i++)
        {
            infos[i] = new LazerSkinInfo
            {
                Id = ReadString() ?? string.Empty,
                Name = ReadString() ?? string.Empty,
                Files = ReadFiles() ?? []
            };
        }

        return infos;
    }

    public void EnsureEnd()
    {
        if (offset != payload.Length)
        {
            throw new InvalidDataException("Lazer IPC frame has trailing bytes.");
        }
    }

    private void EnsureAvailable(int count)
    {
        if (count < 0 || count > payload.Length - offset)
        {
            throw new InvalidDataException("Lazer IPC frame ended unexpectedly.");
        }
    }
}
