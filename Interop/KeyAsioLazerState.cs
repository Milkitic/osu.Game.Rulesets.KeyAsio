namespace osu.Game.Rulesets.KeyAsio.Interop;

internal sealed class KeyAsioLazerState
{
    public int Version { get; init; } = KeyAsioLazerIpcClient.ProtocolVersion;
    public int ProcessId { get; init; }
    public int Status { get; init; }
    public int PlayTime { get; init; }
    public uint Mods { get; init; }
    public int Combo { get; init; }
    public int Score { get; init; }
    public bool IsReplay { get; init; }
    public string? Username { get; init; }
    public string? BeatmapFolder { get; init; }
    public string? BeatmapFilename { get; init; }
    public KeyAsioLazerFile[] BeatmapFiles { get; init; } = [];
    public KeyAsioLazerStatistics Statistics { get; init; } = new();
    public int HitErrorIndex { get; init; }
    public int[] HitErrors { get; init; } = [];
}

internal enum KeyAsioLazerFieldKind : byte
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
}

[Flags]
internal enum KeyAsioLazerFieldMask
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

    Timing = ProcessId | PlayTime,
    Events = ProcessId | Status | Mods | Combo | Score | IsReplay | Username | BeatmapFolder | BeatmapFilename |
             BeatmapFiles | Statistics | HitErrors,
    All = Timing | Events,
}

internal sealed partial class KeyAsioLazerDeltaFrame
{
    public int Version { get; set; } = KeyAsioLazerIpcClient.ProtocolVersion;
    public KeyAsioLazerDeltaField[] Fields { get; set; } = [];

    public static KeyAsioLazerDeltaFrame Create(KeyAsioLazerState? previous, KeyAsioLazerState current,
        KeyAsioLazerFieldMask fieldMask = KeyAsioLazerFieldMask.All)
    {
        var fields = new List<KeyAsioLazerDeltaField>(14);

        if (previous == null)
        {
            if (fieldMask.HasFlag(KeyAsioLazerFieldMask.ProcessId))
                fields.Add(KeyAsioLazerDeltaField.ForInt(KeyAsioLazerFieldKind.ProcessId, current.ProcessId));
            if (fieldMask.HasFlag(KeyAsioLazerFieldMask.Status))
                fields.Add(KeyAsioLazerDeltaField.ForInt(KeyAsioLazerFieldKind.Status, current.Status));
            if (fieldMask.HasFlag(KeyAsioLazerFieldMask.PlayTime))
                fields.Add(KeyAsioLazerDeltaField.ForInt(KeyAsioLazerFieldKind.PlayTime, current.PlayTime));
            if (fieldMask.HasFlag(KeyAsioLazerFieldMask.Mods))
                fields.Add(KeyAsioLazerDeltaField.ForUInt(KeyAsioLazerFieldKind.Mods, current.Mods));
            if (fieldMask.HasFlag(KeyAsioLazerFieldMask.Combo))
                fields.Add(KeyAsioLazerDeltaField.ForInt(KeyAsioLazerFieldKind.Combo, current.Combo));
            if (fieldMask.HasFlag(KeyAsioLazerFieldMask.Score))
                fields.Add(KeyAsioLazerDeltaField.ForInt(KeyAsioLazerFieldKind.Score, current.Score));
            if (fieldMask.HasFlag(KeyAsioLazerFieldMask.IsReplay))
                fields.Add(KeyAsioLazerDeltaField.ForBool(KeyAsioLazerFieldKind.IsReplay, current.IsReplay));
            if (fieldMask.HasFlag(KeyAsioLazerFieldMask.Username))
                fields.Add(KeyAsioLazerDeltaField.ForString(KeyAsioLazerFieldKind.Username, current.Username));
            if (fieldMask.HasFlag(KeyAsioLazerFieldMask.BeatmapFolder))
                fields.Add(KeyAsioLazerDeltaField.ForString(KeyAsioLazerFieldKind.BeatmapFolder, current.BeatmapFolder));
            if (fieldMask.HasFlag(KeyAsioLazerFieldMask.BeatmapFilename))
                fields.Add(KeyAsioLazerDeltaField.ForString(KeyAsioLazerFieldKind.BeatmapFilename, current.BeatmapFilename));
            if (fieldMask.HasFlag(KeyAsioLazerFieldMask.BeatmapFiles) && current.BeatmapFiles.Length > 0)
                fields.Add(KeyAsioLazerDeltaField.ForFiles(current.BeatmapFiles));
            if (fieldMask.HasFlag(KeyAsioLazerFieldMask.Statistics))
                fields.Add(KeyAsioLazerDeltaField.ForStatistics(current.Statistics));
            if (fieldMask.HasFlag(KeyAsioLazerFieldMask.HitErrors))
                fields.Add(KeyAsioLazerDeltaField.ForHitErrors(current.HitErrorIndex, current.HitErrors));

            return new KeyAsioLazerDeltaFrame { Fields = fields.ToArray() };
        }

        if (fieldMask.HasFlag(KeyAsioLazerFieldMask.ProcessId) && previous.ProcessId != current.ProcessId)
            fields.Add(KeyAsioLazerDeltaField.ForInt(KeyAsioLazerFieldKind.ProcessId, current.ProcessId));
        if (fieldMask.HasFlag(KeyAsioLazerFieldMask.Status) && previous.Status != current.Status)
            fields.Add(KeyAsioLazerDeltaField.ForInt(KeyAsioLazerFieldKind.Status, current.Status));
        if (fieldMask.HasFlag(KeyAsioLazerFieldMask.PlayTime) && previous.PlayTime != current.PlayTime)
            fields.Add(KeyAsioLazerDeltaField.ForInt(KeyAsioLazerFieldKind.PlayTime, current.PlayTime));
        if (fieldMask.HasFlag(KeyAsioLazerFieldMask.Mods) && previous.Mods != current.Mods)
            fields.Add(KeyAsioLazerDeltaField.ForUInt(KeyAsioLazerFieldKind.Mods, current.Mods));
        if (fieldMask.HasFlag(KeyAsioLazerFieldMask.Combo) && previous.Combo != current.Combo)
            fields.Add(KeyAsioLazerDeltaField.ForInt(KeyAsioLazerFieldKind.Combo, current.Combo));
        if (fieldMask.HasFlag(KeyAsioLazerFieldMask.Score) && previous.Score != current.Score)
            fields.Add(KeyAsioLazerDeltaField.ForInt(KeyAsioLazerFieldKind.Score, current.Score));
        if (fieldMask.HasFlag(KeyAsioLazerFieldMask.IsReplay) && previous.IsReplay != current.IsReplay)
            fields.Add(KeyAsioLazerDeltaField.ForBool(KeyAsioLazerFieldKind.IsReplay, current.IsReplay));
        if (fieldMask.HasFlag(KeyAsioLazerFieldMask.Username) && previous.Username != current.Username)
            fields.Add(KeyAsioLazerDeltaField.ForString(KeyAsioLazerFieldKind.Username, current.Username));
        if (fieldMask.HasFlag(KeyAsioLazerFieldMask.BeatmapFolder) && previous.BeatmapFolder != current.BeatmapFolder)
            fields.Add(KeyAsioLazerDeltaField.ForString(KeyAsioLazerFieldKind.BeatmapFolder, current.BeatmapFolder));
        if (fieldMask.HasFlag(KeyAsioLazerFieldMask.BeatmapFilename) && previous.BeatmapFilename != current.BeatmapFilename)
            fields.Add(KeyAsioLazerDeltaField.ForString(KeyAsioLazerFieldKind.BeatmapFilename, current.BeatmapFilename));
        if (fieldMask.HasFlag(KeyAsioLazerFieldMask.BeatmapFiles) && current.BeatmapFiles.Length > 0 &&
            !FilesEqual(previous.BeatmapFiles, current.BeatmapFiles))
            fields.Add(KeyAsioLazerDeltaField.ForFiles(current.BeatmapFiles));
        if (fieldMask.HasFlag(KeyAsioLazerFieldMask.Statistics) &&
            !StatisticsEqual(previous.Statistics, current.Statistics))
            fields.Add(KeyAsioLazerDeltaField.ForStatistics(current.Statistics));
        if (fieldMask.HasFlag(KeyAsioLazerFieldMask.HitErrors) &&
            (previous.HitErrorIndex != current.HitErrorIndex ||
             !previous.HitErrors.AsSpan().SequenceEqual(current.HitErrors)))
            fields.Add(KeyAsioLazerDeltaField.ForHitErrors(current.HitErrorIndex, current.HitErrors));

        return new KeyAsioLazerDeltaFrame { Fields = fields.ToArray() };
    }

    private static bool StatisticsEqual(KeyAsioLazerStatistics left, KeyAsioLazerStatistics right)
        => left.Perfect == right.Perfect &&
           left.Great == right.Great &&
           left.Good == right.Good &&
           left.Ok == right.Ok &&
           left.Meh == right.Meh &&
           left.Miss == right.Miss;

    private static bool FilesEqual(KeyAsioLazerFile[] left, KeyAsioLazerFile[] right)
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
}

internal partial struct KeyAsioLazerDeltaField
{
    public KeyAsioLazerFieldKind Kind { get; set; }
    public int IntValue { get; set; }
    public uint UIntValue { get; set; }
    public bool BoolValue { get; set; }
    public string? StringValue { get; set; }
    public KeyAsioLazerFile[]? FilesValue { get; set; }
    public KeyAsioLazerStatistics StatisticsValue { get; set; }
    public int[]? IntArrayValue { get; set; }

    public static KeyAsioLazerDeltaField ForInt(KeyAsioLazerFieldKind kind, int value)
        => new() { Kind = kind, IntValue = value };

    public static KeyAsioLazerDeltaField ForUInt(KeyAsioLazerFieldKind kind, uint value)
        => new() { Kind = kind, UIntValue = value };

    public static KeyAsioLazerDeltaField ForBool(KeyAsioLazerFieldKind kind, bool value)
        => new() { Kind = kind, BoolValue = value };

    public static KeyAsioLazerDeltaField ForString(KeyAsioLazerFieldKind kind, string? value)
        => new() { Kind = kind, StringValue = value };

    public static KeyAsioLazerDeltaField ForFiles(KeyAsioLazerFile[] value)
        => new() { Kind = KeyAsioLazerFieldKind.BeatmapFiles, FilesValue = value };

    public static KeyAsioLazerDeltaField ForStatistics(KeyAsioLazerStatistics value)
        => new() { Kind = KeyAsioLazerFieldKind.Statistics, StatisticsValue = value };

    public static KeyAsioLazerDeltaField ForHitErrors(int index, int[] values)
        => new()
        {
            Kind = KeyAsioLazerFieldKind.HitErrors,
            IntValue = index,
            IntArrayValue = values,
        };
}

internal sealed partial class KeyAsioLazerFile
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

internal partial struct KeyAsioLazerStatistics
{
    public int Perfect { get; set; }
    public int Great { get; set; }
    public int Good { get; set; }
    public int Ok { get; set; }
    public int Meh { get; set; }
    public int Miss { get; set; }
}
