using OverlayAPI.LazerProtocol;

namespace osu.Game.Rulesets.OverlayAPI.Interop;

internal sealed class OverlayApiLazerState
{
    public int ProcessId { get; init; }
    public int Status { get; init; }
    public int PlayTime { get; init; }
    public double BeatmapOffset { get; init; }
    public uint Mods { get; init; }
    public int Combo { get; init; }
    public bool IsReplay { get; init; }
    public string? BeatmapFilename { get; init; }
    public LazerFile[] BeatmapFiles { get; init; } = [];
    public LazerSkinInfo[]? SkinInfos { get; init; }
}

internal static class OverlayApiLazerDeltaBuilder
{
    public static LazerDeltaFrame Create(OverlayApiLazerState? previous, OverlayApiLazerState current,
        LazerFieldMask fieldMask = LazerFieldMask.All)
    {
        var p = previous;
        var fields = new List<LazerDeltaField>(10);

        if (fieldMask.HasFlag(LazerFieldMask.ProcessId) && (p is null || p.ProcessId != current.ProcessId))
            fields.Add(LazerDeltaField.ForInt(LazerFieldKind.ProcessId, current.ProcessId));
        if (fieldMask.HasFlag(LazerFieldMask.Status) && (p is null || p.Status != current.Status))
            fields.Add(LazerDeltaField.ForInt(LazerFieldKind.Status, current.Status));
        if (fieldMask.HasFlag(LazerFieldMask.PlayTime) && (p is null || p.PlayTime != current.PlayTime))
            fields.Add(LazerDeltaField.ForInt(LazerFieldKind.PlayTime, current.PlayTime));
        if (fieldMask.HasFlag(LazerFieldMask.BeatmapOffset) &&
            (p is null || p.BeatmapOffset != current.BeatmapOffset))
            fields.Add(LazerDeltaField.ForDouble(LazerFieldKind.BeatmapOffset, current.BeatmapOffset));
        if (fieldMask.HasFlag(LazerFieldMask.Mods) && (p is null || p.Mods != current.Mods))
            fields.Add(LazerDeltaField.ForUInt(LazerFieldKind.Mods, current.Mods));
        if (fieldMask.HasFlag(LazerFieldMask.Combo) && (p is null || p.Combo != current.Combo))
            fields.Add(LazerDeltaField.ForInt(LazerFieldKind.Combo, current.Combo));
        if (fieldMask.HasFlag(LazerFieldMask.IsReplay) && (p is null || p.IsReplay != current.IsReplay))
            fields.Add(LazerDeltaField.ForBool(LazerFieldKind.IsReplay, current.IsReplay));
        if (fieldMask.HasFlag(LazerFieldMask.BeatmapFilename) && (p is null || p.BeatmapFilename != current.BeatmapFilename))
            fields.Add(LazerDeltaField.ForString(LazerFieldKind.BeatmapFilename, current.BeatmapFilename));
        if (fieldMask.HasFlag(LazerFieldMask.BeatmapFiles) && current.BeatmapFiles.Length > 0 &&
            (p is null || !LazerDeltaFrame.FilesEqual(p.BeatmapFiles, current.BeatmapFiles)))
            fields.Add(LazerDeltaField.ForFiles(current.BeatmapFiles));
        if (fieldMask.HasFlag(LazerFieldMask.SkinInfos) && current.SkinInfos != null &&
            (p is null || !LazerDeltaFrame.SkinInfosEqual(p.SkinInfos, current.SkinInfos)))
            fields.Add(LazerDeltaField.ForSkinInfos(current.SkinInfos));

        return new LazerDeltaFrame { Fields = fields.ToArray() };
    }
}
