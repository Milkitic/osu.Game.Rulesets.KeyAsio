using KeyAsio.LazerProtocol;

namespace osu.Game.Rulesets.KeyAsio.Interop;

internal sealed class KeyAsioLazerState
{
    public int Version { get; init; } = LazerProtocolConstants.ProtocolVersion;
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
    public LazerFile[] BeatmapFiles { get; init; } = [];
    public LazerStatistics Statistics { get; init; } = new();
    public int HitErrorIndex { get; init; }
    public int[] HitErrors { get; init; } = [];
    public LazerSkinInfo[]? SkinInfos { get; init; }
    public string? UserDataDirectory { get; init; }
    public string? ExeDirectory { get; init; }
}

internal static class KeyAsioLazerDeltaBuilder
{
    public static LazerDeltaFrame Create(KeyAsioLazerState? previous, KeyAsioLazerState current,
        LazerFieldMask fieldMask = LazerFieldMask.All)
    {
        var p = previous;
        var fields = new List<LazerDeltaField>(14);

        if (fieldMask.HasFlag(LazerFieldMask.ProcessId) && (p is null || p.ProcessId != current.ProcessId))
            fields.Add(LazerDeltaField.ForInt(LazerFieldKind.ProcessId, current.ProcessId));
        if (fieldMask.HasFlag(LazerFieldMask.Status) && (p is null || p.Status != current.Status))
            fields.Add(LazerDeltaField.ForInt(LazerFieldKind.Status, current.Status));
        if (fieldMask.HasFlag(LazerFieldMask.PlayTime) && (p is null || p.PlayTime != current.PlayTime))
            fields.Add(LazerDeltaField.ForInt(LazerFieldKind.PlayTime, current.PlayTime));
        if (fieldMask.HasFlag(LazerFieldMask.Mods) && (p is null || p.Mods != current.Mods))
            fields.Add(LazerDeltaField.ForUInt(LazerFieldKind.Mods, current.Mods));
        if (fieldMask.HasFlag(LazerFieldMask.Combo) && (p is null || p.Combo != current.Combo))
            fields.Add(LazerDeltaField.ForInt(LazerFieldKind.Combo, current.Combo));
        if (fieldMask.HasFlag(LazerFieldMask.Score) && (p is null || p.Score != current.Score))
            fields.Add(LazerDeltaField.ForInt(LazerFieldKind.Score, current.Score));
        if (fieldMask.HasFlag(LazerFieldMask.IsReplay) && (p is null || p.IsReplay != current.IsReplay))
            fields.Add(LazerDeltaField.ForBool(LazerFieldKind.IsReplay, current.IsReplay));
        if (fieldMask.HasFlag(LazerFieldMask.Username) && (p is null || p.Username != current.Username))
            fields.Add(LazerDeltaField.ForString(LazerFieldKind.Username, current.Username));
        if (fieldMask.HasFlag(LazerFieldMask.BeatmapFolder) && (p is null || p.BeatmapFolder != current.BeatmapFolder))
            fields.Add(LazerDeltaField.ForString(LazerFieldKind.BeatmapFolder, current.BeatmapFolder));
        if (fieldMask.HasFlag(LazerFieldMask.BeatmapFilename) && (p is null || p.BeatmapFilename != current.BeatmapFilename))
            fields.Add(LazerDeltaField.ForString(LazerFieldKind.BeatmapFilename, current.BeatmapFilename));
        if (fieldMask.HasFlag(LazerFieldMask.BeatmapFiles) && current.BeatmapFiles.Length > 0 &&
            (p is null || !LazerDeltaFrame.FilesEqual(p.BeatmapFiles, current.BeatmapFiles)))
            fields.Add(LazerDeltaField.ForFiles(current.BeatmapFiles));
        if (fieldMask.HasFlag(LazerFieldMask.Statistics) &&
            (p is null || !LazerDeltaFrame.StatisticsEqual(p.Statistics, current.Statistics)))
            fields.Add(LazerDeltaField.ForStatistics(current.Statistics));
        if (fieldMask.HasFlag(LazerFieldMask.HitErrors) &&
            (p is null || p.HitErrorIndex != current.HitErrorIndex ||
             !p.HitErrors.AsSpan().SequenceEqual(current.HitErrors)))
            fields.Add(LazerDeltaField.ForHitErrors(current.HitErrorIndex, current.HitErrors));
        if (fieldMask.HasFlag(LazerFieldMask.SkinInfos) && current.SkinInfos != null &&
            (p is null || !LazerDeltaFrame.SkinInfosEqual(p.SkinInfos, current.SkinInfos)))
            fields.Add(LazerDeltaField.ForSkinInfos(current.SkinInfos));
        if (fieldMask.HasFlag(LazerFieldMask.UserDataDirectory) && current.UserDataDirectory != null &&
            (p is null || p.UserDataDirectory != current.UserDataDirectory))
            fields.Add(LazerDeltaField.ForString(LazerFieldKind.UserDataDirectory, current.UserDataDirectory));
        if (fieldMask.HasFlag(LazerFieldMask.ExeDirectory) && current.ExeDirectory != null &&
            (p is null || p.ExeDirectory != current.ExeDirectory))
            fields.Add(LazerDeltaField.ForString(LazerFieldKind.ExeDirectory, current.ExeDirectory));

        return new LazerDeltaFrame { Fields = fields.ToArray() };
    }
}