using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using OverlayAPI.LazerProtocol;

namespace osu.Game.Rulesets.OverlayAPI.Interop;

internal static class LazerBeatmapFileMapper
{
    public static Task<MappedBeatmap?> CreateAsync(WorkingBeatmap workingBeatmap, Storage storage,
        BackgroundWorker worker, CancellationToken token)
        => worker.EnqueueAsync(_ => Create(workingBeatmap, storage, token));

    private static MappedBeatmap? Create(WorkingBeatmap workingBeatmap, Storage storage, CancellationToken token)
    {
        var beatmapPath = workingBeatmap.BeatmapInfo.Path;
        if (string.IsNullOrWhiteSpace(beatmapPath))
        {
            return null;
        }

        var fileStorage = storage.GetStorageForDirectory("files");
        var normalizedBeatmapPath = NormalizeName(beatmapPath);
        var files = new List<LazerFile>();

        foreach (var file in workingBeatmap.BeatmapSetInfo.Files)
        {
            token.ThrowIfCancellationRequested();

            if (!IsRequiredResource(file.Filename, normalizedBeatmapPath))
                continue;

            var absolutePath = fileStorage.GetFullPath(file.File.GetStoragePath());
            if (!File.Exists(absolutePath))
            {
                continue;
            }

            files.Add(new LazerFile
            {
                Name = NormalizeName(file.Filename),
                Path = Path.GetFullPath(absolutePath)
            });
        }

        return files.Any(file => file.Name.Equals(normalizedBeatmapPath, StringComparison.OrdinalIgnoreCase))
            ? new MappedBeatmap(normalizedBeatmapPath, files.ToArray())
            : null;
    }

    private static bool IsRequiredResource(string filename, string normalizedBeatmapPath)
    {
        var extension = Path.GetExtension(filename);
        return NormalizeName(filename).Equals(normalizedBeatmapPath, StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string name)
        => name.Replace('\\', '/').TrimStart('/');
}

internal sealed record MappedBeatmap(string Filename, LazerFile[] Files);
