using KeyAsio.LazerProtocol;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using osu.Framework.Platform;

namespace osu.Game.Rulesets.KeyAsio.Interop;

internal static class LazerBeatmapFileMapper
{
    public static Task<MappedBeatmap?> CreateAsync(WorkingBeatmap workingBeatmap, Storage storage,
        CancellationToken token)
        => Task.Run(() => Create(workingBeatmap, storage, token), token);

    private static MappedBeatmap? Create(WorkingBeatmap workingBeatmap, Storage storage, CancellationToken token)
    {
        var beatmapPath = workingBeatmap.BeatmapInfo.Path;
        if (string.IsNullOrWhiteSpace(beatmapPath))
        {
            return null;
        }

        var fileStorage = storage.GetStorageForDirectory("files");
        var root = Path.GetFullPath(fileStorage.GetFullPath("."));
        var files = new List<LazerFile>();

        foreach (var file in workingBeatmap.BeatmapSetInfo.Files)
        {
            token.ThrowIfCancellationRequested();

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

        var normalizedBeatmapPath = NormalizeName(beatmapPath);
        return files.Any(file => file.Name.Equals(normalizedBeatmapPath, StringComparison.OrdinalIgnoreCase))
            ? new MappedBeatmap(root, normalizedBeatmapPath, files.ToArray())
            : null;
    }

    private static string NormalizeName(string name)
        => name.Replace('\\', '/').TrimStart('/');
}

internal sealed record MappedBeatmap(string RootPath, string Filename, LazerFile[] Files);
