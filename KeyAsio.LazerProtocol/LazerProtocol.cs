using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace KeyAsio.LazerProtocol;

public enum LazerFieldKind : byte
{
    ProcessId = 1,
    Status = 2,
    PlayTime = 3,
    Mods = 4,
    Combo = 5,
    Score = 6,
    IsReplay = 7,
    Username = 8,
    BeatmapFolder = 9,
    BeatmapFilename = 10,
    BeatmapFiles = 11,
    Statistics = 12,
    HitErrors = 13,
    SkinInfos = 14,
    UserDataDirectory = 15,
    ExeDirectory = 16,
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
    Score = 1 << 5,
    IsReplay = 1 << 6,
    Username = 1 << 7,
    BeatmapFolder = 1 << 8,
    BeatmapFilename = 1 << 9,
    BeatmapFiles = 1 << 10,
    Statistics = 1 << 11,
    HitErrors = 1 << 12,
    SkinInfos = 1 << 13,
    UserDataDirectory = 1 << 14,
    ExeDirectory = 1 << 15,

    Timing = ProcessId | PlayTime,

    Events = ProcessId | Status | Mods | Combo | Score | IsReplay | Username | BeatmapFolder | BeatmapFilename |
             BeatmapFiles | Statistics | HitErrors | SkinInfos | UserDataDirectory | ExeDirectory,
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
    public string Creator { get; set; } = string.Empty;
    public string InstantiationInfo { get; set; } = string.Empty;
    public bool Protected { get; set; }
    public LazerFile[] Files { get; set; } = [];
}

public struct LazerStatistics
{
    public int Perfect { get; set; }
    public int Great { get; set; }
    public int Good { get; set; }
    public int Ok { get; set; }
    public int Meh { get; set; }
    public int Miss { get; set; }

    public static LazerStatistics Empty => default;
}

public struct LazerDeltaField
{
    public LazerFieldKind Kind { get; set; }
    public int IntValue { get; set; }
    public uint UIntValue { get; set; }
    public bool BoolValue { get; set; }
    public string? StringValue { get; set; }
    public LazerFile[]? FilesValue { get; set; }
    public LazerStatistics StatisticsValue { get; set; }
    public int[]? IntArrayValue { get; set; }
    public LazerSkinInfo[]? SkinInfosValue { get; set; }

    public static LazerDeltaField ForInt(LazerFieldKind kind, int value)
        => new() { Kind = kind, IntValue = value };

    public static LazerDeltaField ForUInt(LazerFieldKind kind, uint value)
        => new() { Kind = kind, UIntValue = value };

    public static LazerDeltaField ForBool(LazerFieldKind kind, bool value)
        => new() { Kind = kind, BoolValue = value };

    public static LazerDeltaField ForString(LazerFieldKind kind, string? value)
        => new() { Kind = kind, StringValue = value };

    public static LazerDeltaField ForFiles(LazerFile[] value)
        => new() { Kind = LazerFieldKind.BeatmapFiles, FilesValue = value };

    public static LazerDeltaField ForStatistics(LazerStatistics value)
        => new() { Kind = LazerFieldKind.Statistics, StatisticsValue = value };

    public static LazerDeltaField ForHitErrors(int index, int[] values)
        => new()
        {
            Kind = LazerFieldKind.HitErrors,
            IntValue = index,
            IntArrayValue = values,
        };

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
                case LazerFieldKind.Score:
                    field.IntValue = reader.ReadInt32();
                    break;

                case LazerFieldKind.Mods:
                    field.UIntValue = reader.ReadUInt32();
                    break;

                case LazerFieldKind.IsReplay:
                    field.BoolValue = reader.ReadByte() != 0;
                    break;

                case LazerFieldKind.Username:
                case LazerFieldKind.BeatmapFolder:
                case LazerFieldKind.BeatmapFilename:
                case LazerFieldKind.UserDataDirectory:
                case LazerFieldKind.ExeDirectory:
                    field.StringValue = reader.ReadString();
                    break;

                case LazerFieldKind.BeatmapFiles:
                    field.FilesValue = reader.ReadFiles();
                    break;

                case LazerFieldKind.Statistics:
                    field.StatisticsValue = reader.ReadStatistics();
                    break;

                case LazerFieldKind.HitErrors:
                    field.IntValue = reader.ReadInt32();
                    field.IntArrayValue = reader.ReadInt32Array();
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
                case LazerFieldKind.Score:
                    FrameWriter.WriteInt32(writer, field.IntValue);
                    break;

                case LazerFieldKind.Mods:
                    FrameWriter.WriteUInt32(writer, field.UIntValue);
                    break;

                case LazerFieldKind.IsReplay:
                    FrameWriter.WriteByte(writer, field.BoolValue ? (byte)1 : (byte)0);
                    break;

                case LazerFieldKind.Username:
                case LazerFieldKind.BeatmapFolder:
                case LazerFieldKind.BeatmapFilename:
                case LazerFieldKind.UserDataDirectory:
                case LazerFieldKind.ExeDirectory:
                    FrameWriter.WriteString(writer, field.StringValue);
                    break;

                case LazerFieldKind.BeatmapFiles:
                    FrameWriter.WriteFiles(writer, field.FilesValue);
                    break;

                case LazerFieldKind.Statistics:
                    FrameWriter.WriteStatistics(writer, field.StatisticsValue);
                    break;

                case LazerFieldKind.HitErrors:
                    FrameWriter.WriteInt32(writer, field.IntValue);
                    FrameWriter.WriteInt32Array(writer, field.IntArrayValue);
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
        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i].Name != right[i].Name || left[i].Path != right[i].Path)
                return false;
        }

        return true;
    }

    public static bool StatisticsEqual(LazerStatistics left, LazerStatistics right)
        => left.Perfect == right.Perfect &&
           left.Great == right.Great &&
           left.Good == right.Good &&
           left.Ok == right.Ok &&
           left.Meh == right.Meh &&
           left.Miss == right.Miss;

    public static bool SkinInfosEqual(LazerSkinInfo[]? left, LazerSkinInfo[]? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        if (left.Length != right.Length) return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i].Id != right[i].Id ||
                left[i].Name != right[i].Name ||
                left[i].Creator != right[i].Creator ||
                left[i].Protected != right[i].Protected ||
                left[i].InstantiationInfo != right[i].InstantiationInfo)
                return false;

            if (!FilesEqual(left[i].Files, right[i].Files))
                return false;
        }

        return true;
    }
}

public static class LazerProtocolConstants
{
    public const int ProtocolVersion = 2;
    public const string TimingPipeName = "KeyAsio.LazerBridge.v1";
    public const string EventPipeName = "KeyAsio.LazerBridge.Events.v1";
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

    public static void WriteStatistics(IBufferWriter<byte> writer, LazerStatistics statistics)
    {
        WriteInt32(writer, statistics.Perfect);
        WriteInt32(writer, statistics.Great);
        WriteInt32(writer, statistics.Good);
        WriteInt32(writer, statistics.Ok);
        WriteInt32(writer, statistics.Meh);
        WriteInt32(writer, statistics.Miss);
    }

    public static void WriteInt32Array(IBufferWriter<byte> writer, int[]? values)
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
            WriteString(writer, skinInfo.Creator);
            WriteString(writer, skinInfo.InstantiationInfo);
            WriteByte(writer, skinInfo.Protected ? (byte)1 : (byte)0);
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

    public LazerStatistics ReadStatistics()
        => new()
        {
            Perfect = ReadInt32(),
            Great = ReadInt32(),
            Good = ReadInt32(),
            Ok = ReadInt32(),
            Meh = ReadInt32(),
            Miss = ReadInt32(),
        };

    public int[]? ReadInt32Array()
    {
        var count = ReadInt32();
        if (count < 0)
        {
            if (count == -1)
            {
                return null;
            }

            throw new InvalidDataException($"Invalid lazer IPC int array length: {count}.");
        }

        if (count > 100_000)
        {
            throw new InvalidDataException($"Invalid lazer IPC int array length: {count}.");
        }

        var values = new int[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ReadInt32();
        }

        return values;
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
                Creator = ReadString() ?? string.Empty,
                InstantiationInfo = ReadString() ?? string.Empty,
                Protected = ReadByte() != 0,
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
