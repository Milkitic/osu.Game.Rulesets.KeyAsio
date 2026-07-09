using System.Diagnostics;
using System.Reflection;
using KeyAsio.LazerProtocol;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.IO;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.KeyAsio.Interop;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Select;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.KeyAsio.UI;

internal sealed partial class KeyAsioLazerGlobalSyncComponent : Component
{
    private const int status_main_view = 0;
    private const int status_editing = 1;
    private const int status_playing = 2;
    private const int status_song_selection = 5;
    private const int status_results = 7;
    private const int status_beatmap_processing = 19;
    private const double timing_publish_interval = 2;
    private const int skin_publish_cooldown_frames = 150; // ~5s at 30fps Update rate

    private static readonly int s_processId = Process.GetCurrentProcess().Id;
    private static readonly PropertyInfo? s_playerScoreProcessorProperty =
        typeof(Player).GetProperty("ScoreProcessor", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo? s_playerDrawableRulesetProperty =
        typeof(Player).GetProperty("DrawableRuleset", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo? s_playerGameplayClockContainerProperty =
        typeof(Player).GetProperty("GameplayClockContainer", BindingFlags.Instance | BindingFlags.NonPublic);

    private static int activeComponent;

    private readonly CancellationTokenSource disposalCts = new();

    private CancellationTokenSource? beatmapMapCts;
    private Task<MappedBeatmap?>? beatmapMapTask;
    private MappedBeatmap? mappedBeatmap;
    private bool isActive;
    private double lastTimingPublishTime;
    private int lastHitEventIndex;
    private int lastPublishedStatus = int.MinValue;
    private string? lastPublishedUsername;
    private IScreen? currentScreen;
    private Player? boundPlayer;
    private GameplayClockContainer? boundGameplayClock;
    private ScoreProcessor? boundScoreProcessor;
    private DrawableRuleset? boundDrawableRuleset;
    private bool frontendLoadedNotificationShown;
    private Bindable<bool>? showConvertedBeatmaps;
    private bool? showConvertedBeatmapsValueBeforeKeyAsioSongSelect;

    [Resolved(CanBeNull = true)]
    private IBindable<WorkingBeatmap>? workingBeatmap { get; set; }

    [Resolved(CanBeNull = true)]
    private IBindable<RulesetInfo>? ruleset { get; set; }

    [Resolved(CanBeNull = true)]
    private IBindable<IReadOnlyList<Mod>>? selectedMods { get; set; }

    [Resolved(CanBeNull = true)]
    private Storage? storage { get; set; }

    [Resolved(CanBeNull = true)]
    private OsuConfigManager? config { get; set; }

    [Resolved(CanBeNull = true)]
    private OsuGame? osuGame { get; set; }

    [Resolved(CanBeNull = true)]
    private IAPIProvider? api { get; set; }

    [Resolved(CanBeNull = true)]
    private INotificationOverlay? notifications { get; set; }

    [Resolved(CanBeNull = true)]
    private SkinManager? skinManager { get; set; }

    private LazerSkinInfo[]? lastPublishedSkinInfos;
    private string? lastPublishedUserDataDirectory;
    private string? lastPublishedExeDirectory;
    private int skinPublishCooldown;

    // Cached across the component lifetime; the underlying values never change
    // while the process is running and the storage root is fixed.
    private string? cachedExeDirectory;
    private bool exeDirectoryCached;
    private string? cachedUserDataDirectory;
    private bool userDataDirectoryCached;

    // Ruleset instance cached to avoid CreateInstance() on every CreateState;
    // invalidated when the bound RulesetInfo changes.
    private Ruleset? cachedRuleset;
    private RulesetInfo? cachedRulesetInfo;

    public KeyAsioLazerGlobalSyncComponent()
    {
        AlwaysPresent = true;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        TryActivate();
    }

    protected override void Update()
    {
        base.Update();

        if (!isActive)
        {
            TryActivate();
            return;
        }

        ObserveBeatmapMapTask();
        currentScreen = GetCurrentScreen();
        ApplyKeyAsioSongSelectRestrictions();
        ShowFrontendLoadedNotificationIfNeeded();
        BindGameplayEvents(currentScreen as Player);

        if (Time.Current - lastTimingPublishTime >= timing_publish_interval)
            PublishTimingState();

        var currentStatus = CurrentStatus();
        var currentUsername = GetUsername();

        if (currentStatus != lastPublishedStatus || currentUsername != lastPublishedUsername)
            PublishEventState();
    }

    private void TryActivate()
    {
        if (isActive || workingBeatmap == null || ruleset == null || selectedMods == null || storage == null)
            return;

        if (Interlocked.CompareExchange(ref activeComponent, 1, 0) != 0)
            return;

        isActive = true;
        Logger.Log("Activated KeyASIO lazer global sync component.");

        workingBeatmap.ValueChanged += OnWorkingBeatmapChanged;
        ruleset.ValueChanged += OnRulesetChanged;
        selectedMods.ValueChanged += OnSelectedModsChanged;

        StartBeatmapMap(workingBeatmap.Value);
        PublishEventState(PublishOptions.ForceSkins);
        PublishTimingState();
    }

    private void OnWorkingBeatmapChanged(ValueChangedEvent<WorkingBeatmap> beatmap)
    {
        lastHitEventIndex = 0;
        StartBeatmapMap(beatmap.NewValue);
        PublishEventState();
        PublishTimingState();
    }

    private void OnRulesetChanged(ValueChangedEvent<RulesetInfo> _)
    {
        cachedRuleset = null;
        cachedRulesetInfo = null;
        PublishEventState();
    }

    private void OnSelectedModsChanged(ValueChangedEvent<IReadOnlyList<Mod>> _) => PublishEventState();

    private void StartBeatmapMap(WorkingBeatmap beatmap)
    {
        beatmapMapCts?.Cancel();
        beatmapMapCts?.Dispose();

        mappedBeatmap = null;
        beatmapMapTask = null;

        if (storage == null)
            return;

        beatmapMapCts = CancellationTokenSource.CreateLinkedTokenSource(disposalCts.Token);
        beatmapMapTask = LazerBeatmapFileMapper.CreateAsync(beatmap, storage, beatmapMapCts.Token);
    }

    private void ObserveBeatmapMapTask()
    {
        if (beatmapMapTask == null || !beatmapMapTask.IsCompleted)
            return;

        var completedTask = beatmapMapTask;
        beatmapMapTask = null;

        if (completedTask.IsCompletedSuccessfully)
        {
            mappedBeatmap = completedTask.Result;
            PublishEventState(mappedBeatmap != null ? PublishOptions.BeatmapFiles : PublishOptions.None);
            PublishTimingState();
        }
        else if (!completedTask.IsCanceled && completedTask.Exception != null)
        {
            Logger.Error(completedTask.Exception, "Failed to map lazer beatmap files for KeyASIO.");
        }
    }

    private void BindGameplayEvents(Player? player)
    {
        var changed = false;

        if (!ReferenceEquals(boundPlayer, player))
        {
            UnbindScoreProcessor();
            UnbindDrawableRuleset();

            boundPlayer = player;
            boundGameplayClock = null;
            lastHitEventIndex = 0;
            changed = true;
        }

        if (player != null)
        {
            if (boundScoreProcessor == null)
            {
                var scoreProcessor = GetPlayerProperty<ScoreProcessor>(player, s_playerScoreProcessorProperty);
                if (scoreProcessor != null)
                {
                    boundScoreProcessor = scoreProcessor;
                    lastHitEventIndex = scoreProcessor.HitEvents.Count;
                    boundScoreProcessor.NewJudgement += OnNewJudgement;
                    boundScoreProcessor.JudgementReverted += OnJudgementReverted;
                    boundScoreProcessor.OnResetFromReplayFrame += OnScoreResetFromReplayFrame;
                    changed = true;
                }
            }

            if (boundDrawableRuleset == null)
            {
                var drawableRuleset = GetPlayerProperty<DrawableRuleset>(player, s_playerDrawableRulesetProperty);
                if (drawableRuleset != null)
                {
                    boundDrawableRuleset = drawableRuleset;
                    boundDrawableRuleset.HasReplayLoaded.ValueChanged += OnReplayLoadedChanged;
                    changed = true;
                }
            }

            boundGameplayClock ??= GetPlayerProperty<GameplayClockContainer>(player, s_playerGameplayClockContainerProperty);
        }

        if (changed)
            PublishEventState();
    }

    private void UnbindScoreProcessor()
    {
        if (boundScoreProcessor == null)
            return;

        boundScoreProcessor.NewJudgement -= OnNewJudgement;
        boundScoreProcessor.JudgementReverted -= OnJudgementReverted;
        boundScoreProcessor.OnResetFromReplayFrame -= OnScoreResetFromReplayFrame;
        boundScoreProcessor = null;
    }

    private void UnbindDrawableRuleset()
    {
        if (boundDrawableRuleset == null)
            return;

        boundDrawableRuleset.HasReplayLoaded.ValueChanged -= OnReplayLoadedChanged;
        boundDrawableRuleset = null;
    }

    private void OnNewJudgement(JudgementResult _) => PublishEventState(PublishOptions.HitErrors);

    private void OnJudgementReverted(JudgementResult _) => PublishEventState(PublishOptions.HitErrors);

    private void OnScoreResetFromReplayFrame()
    {
        lastHitEventIndex = 0;
        PublishEventState(PublishOptions.HitErrors);
        PublishTimingState();
    }

    private void OnReplayLoadedChanged(ValueChangedEvent<bool> _) => PublishEventState();

    private void ApplyKeyAsioSongSelectRestrictions()
    {
        showConvertedBeatmaps ??= config?.GetBindable<bool>(OsuSetting.ShowConvertedBeatmaps);

        if (showConvertedBeatmaps == null)
            return;

        bool shouldBlockConvertedBeatmaps = currentScreen is SongSelect
                                            && ruleset?.Value.ShortName == KeyAsioRuleset.KEYASIO_SHORT_NAME;

        if (shouldBlockConvertedBeatmaps)
        {
            showConvertedBeatmapsValueBeforeKeyAsioSongSelect ??= showConvertedBeatmaps.Value;

            if (showConvertedBeatmaps.Value)
                showConvertedBeatmaps.Value = false;

            return;
        }

        RestoreShowConvertedBeatmapsSetting();
    }

    private void RestoreShowConvertedBeatmapsSetting()
    {
        if (showConvertedBeatmapsValueBeforeKeyAsioSongSelect == null || showConvertedBeatmaps == null)
            return;

        showConvertedBeatmaps.Value = showConvertedBeatmapsValueBeforeKeyAsioSongSelect.Value;
        showConvertedBeatmapsValueBeforeKeyAsioSongSelect = null;
    }

    private void ShowFrontendLoadedNotificationIfNeeded()
    {
        if (frontendLoadedNotificationShown || currentScreen is not SongSelect || notifications == null)
            return;

        frontendLoadedNotificationShown = true;
        notifications.Post(new SimpleNotification
        {
            Text = "KeyASIO frontend loaded. Please select another game mode.",
            Icon = FontAwesome.Solid.Bolt
        });
    }

    private void PublishTimingState()
    {
        if (!isActive)
            return;

        lastTimingPublishTime = Time.Current;

        KeyAsioLazerIpcClient.Shared.PublishTiming(new KeyAsioLazerState
        {
            ProcessId = s_processId,
            PlayTime = GetPlayTime()
        });
    }

    private void PublishEventState(PublishOptions options = PublishOptions.None)
    {
        if (!isActive)
            return;

        var state = CreateState(options);
        lastPublishedStatus = state.Status;
        lastPublishedUsername = state.Username;
        KeyAsioLazerIpcClient.Shared.PublishEvent(state);

        // Re-publish skins periodically to capture user changes (new imports, edits, deletions).
        // Cooldown advances after state creation so that CreateState sees the
        // expired (<=0) value on the same frame it re-collects skins.
        if (skinPublishCooldown > 0)
            skinPublishCooldown--;
        else
            skinPublishCooldown = skin_publish_cooldown_frames;
    }

    private KeyAsioLazerState CreateState(PublishOptions options)
    {
        int hitErrorIndex;
        int[] hitErrors;

        if (options.HasFlag(PublishOptions.HitErrors))
        {
            hitErrors = CollectHitErrors(out hitErrorIndex);
        }
        else
        {
            hitErrorIndex = lastHitEventIndex;
            hitErrors = [];
        }

        var exeDirectory = GetCurrentExeDirectory();
        var userDataDirectory = GetUserDataDirectory();

        var forceSkins = options.HasFlag(PublishOptions.ForceSkins);
        var skinInfos = (forceSkins || skinPublishCooldown <= 0)
            ? CollectSkinInfos()
            : lastPublishedSkinInfos;

        if (skinInfos != lastPublishedSkinInfos)
            lastPublishedSkinInfos = skinInfos;

        if (userDataDirectory != lastPublishedUserDataDirectory)
            lastPublishedUserDataDirectory = userDataDirectory;

        if (exeDirectory != lastPublishedExeDirectory)
            lastPublishedExeDirectory = exeDirectory;

        var includeBeatmapFiles = options.HasFlag(PublishOptions.BeatmapFiles);

        return new KeyAsioLazerState
        {
            ProcessId = s_processId,
            Status = CurrentStatus(),
            PlayTime = GetPlayTime(),
            Mods = GetLegacyMods(),
            Combo = boundScoreProcessor?.Combo.Value ?? 0,
            Score = ClampScore(boundScoreProcessor?.TotalScore.Value ?? 0),
            IsReplay = boundDrawableRuleset?.HasReplayLoaded.Value ?? false,
            Username = GetUsername(),
            BeatmapFolder = mappedBeatmap?.RootPath,
            BeatmapFilename = mappedBeatmap?.Filename,
            BeatmapFiles = includeBeatmapFiles && mappedBeatmap != null ? mappedBeatmap.Files : [],
            Statistics = new LazerStatistics
            {
                Perfect = GetStatistic(HitResult.Perfect),
                Great = GetStatistic(HitResult.Great),
                Good = GetStatistic(HitResult.Good),
                Ok = GetStatistic(HitResult.Ok),
                Meh = GetStatistic(HitResult.Meh),
                Miss = GetStatistic(HitResult.Miss)
            },
            HitErrorIndex = hitErrorIndex,
            HitErrors = hitErrors,
            SkinInfos = skinInfos,
            UserDataDirectory = userDataDirectory,
            ExeDirectory = exeDirectory
        };
    }

    private int CurrentStatus()
    {
        return currentScreen switch
        {
            Player => status_playing,
            PlayerLoader => status_beatmap_processing,
            SongSelect => status_song_selection,
            ResultsScreen => status_results,
            Editor => status_editing,
            MainMenu => status_main_view,
            _ => mappedBeatmap == null ? status_main_view : status_song_selection
        };
    }

    private IScreen? GetCurrentScreen()
    {
        var current = osuGame?.ScreenStack?.CurrentScreen;

        while (current is IHasSubScreenStack subScreenStack)
        {
            var nested = subScreenStack.SubScreenStack.CurrentScreen;
            if (nested == null || ReferenceEquals(nested, current))
                break;

            current = nested;
        }

        return current;
    }

    private int GetPlayTime()
    {
        if (boundGameplayClock != null && double.IsFinite(boundGameplayClock.CurrentTime))
            return (int)Math.Round(boundGameplayClock.CurrentTime);

        var beatmap = workingBeatmap?.Value;
        if (beatmap?.TrackLoaded == true && double.IsFinite(beatmap.Track.CurrentTime))
            return (int)Math.Round(beatmap.Track.CurrentTime);

        return 0;
    }

    private uint GetLegacyMods()
    {
        try
        {
            if (boundDrawableRuleset != null)
                return unchecked((uint)boundDrawableRuleset.Ruleset.ConvertToLegacyMods(boundDrawableRuleset.Mods.ToArray()));

            var currentRulesetInfo = ruleset?.Value;
            if (currentRulesetInfo == null)
                return 0;

            if (cachedRulesetInfo != currentRulesetInfo)
            {
                cachedRulesetInfo = currentRulesetInfo;
                cachedRuleset = currentRulesetInfo.CreateInstance();
            }

            return cachedRuleset == null
                ? 0
                : unchecked((uint)cachedRuleset.ConvertToLegacyMods(selectedMods?.Value?.ToArray() ?? []));
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to convert mods to legacy mods: {ex.Message}", level: LogLevel.Debug);
            return 0;
        }
    }

    private string? GetUsername()
    {
        var username = api?.LocalUser.Value.Username;
        if (string.IsNullOrWhiteSpace(username))
            username = api?.ProvidedUsername;

        return string.IsNullOrWhiteSpace(username) ? null : username;
    }

    private string? GetCurrentExeDirectory()
    {
        if (exeDirectoryCached)
            return cachedExeDirectory;

        exeDirectoryCached = true;
        try
        {
            using var proc = Process.GetCurrentProcess();
            var fileName = proc.MainModule?.FileName;
            if (string.IsNullOrEmpty(fileName))
                return cachedExeDirectory = null;

            var dir = Path.GetDirectoryName(fileName);
            return cachedExeDirectory = (string.IsNullOrEmpty(dir) ? null : dir);
        }
        catch
        {
            return cachedExeDirectory = null;
        }
    }

    private string? GetUserDataDirectory()
    {
        if (userDataDirectoryCached)
            return cachedUserDataDirectory;

        userDataDirectoryCached = true;
        if (storage == null)
            return cachedUserDataDirectory = null;

        try
        {
            var fullPath = Path.GetFullPath(storage.GetFullPath(string.Empty));
            return cachedUserDataDirectory = (Directory.Exists(fullPath) ? fullPath : null);
        }
        catch
        {
            return cachedUserDataDirectory = null;
        }
    }

    // lazer's built-in "random skin" virtual entry (SkinInfo.RANDOM_SKIN in osu.Game).
    // Excluded from the published skin list since it is not a real selectable skin.
    private static readonly Guid LAZER_RANDOM_SKIN_ID = new Guid("D39DFEFB-477C-4372-B1EA-2BCEA5FB8908");

    private LazerSkinInfo[]? CollectSkinInfos()
    {
        if (skinManager == null)
            return null;

        try
        {
            var skins = skinManager.GetAllUsableSkins();
            var result = new List<LazerSkinInfo>(skins.Count);

            foreach (var live in skins)
            {
                if (live.ID == LAZER_RANDOM_SKIN_ID)
                    continue;

                LazerSkinInfo mapped = live.PerformRead(info =>
                {
                    var files = MapSkinFiles(info);
                    return new LazerSkinInfo
                    {
                        Id = info.ID.ToString(),
                        Name = info.Name ?? string.Empty,
                        Creator = info.Creator ?? string.Empty,
                        InstantiationInfo = info.InstantiationInfo ?? string.Empty,
                        Protected = info.Protected,
                        Files = files
                    };
                });

                result.Add(mapped);
            }

            result.Sort(static (left, right) =>
            {
                var idComparison = string.Compare(left.Id, right.Id, StringComparison.Ordinal);
                return idComparison != 0
                    ? idComparison
                    : string.Compare(left.Name, right.Name, StringComparison.Ordinal);
            });

            return result.ToArray();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to collect lazer skin infos for KeyASIO.");
            return lastPublishedSkinInfos;
        }
    }

    private LazerFile[] MapSkinFiles(SkinInfo skinInfo)
    {
        if (skinInfo.Files == null || skinInfo.Files.Count == 0 || storage == null)
            return [];

        var filesRoot = Path.GetFullPath(storage.GetStorageForDirectory("files").GetFullPath("."));
        var list = new List<LazerFile>(skinInfo.Files.Count);

        foreach (var usage in skinInfo.Files)
        {
            try
            {
                var storagePath = usage.File.GetStoragePath();
                var absolutePath = Path.GetFullPath(Path.Combine(filesRoot, storagePath));

                if (File.Exists(absolutePath))
                {
                    list.Add(new LazerFile
                    {
                        Name = usage.Filename,
                        Path = absolutePath
                    });
                }
            }
            catch
            {
                // Ignore missing / inaccessible files.
            }
        }

        list.Sort(static (left, right) =>
        {
            var nameComparison = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
            return nameComparison != 0
                ? nameComparison
                : string.Compare(left.Path, right.Path, StringComparison.Ordinal);
        });

        return list.ToArray();
    }

    private int GetStatistic(HitResult result)
    {
        return boundScoreProcessor?.Statistics.TryGetValue(result, out var value) == true
            ? value
            : 0;
    }

    private int[] CollectHitErrors(out int hitErrorIndex)
    {
        hitErrorIndex = lastHitEventIndex;
        if (boundScoreProcessor == null)
            return [];

        var hitEvents = boundScoreProcessor.HitEvents;
        if (lastHitEventIndex > hitEvents.Count)
            lastHitEventIndex = 0;

        if (lastHitEventIndex == hitEvents.Count)
        {
            hitErrorIndex = lastHitEventIndex;
            return [];
        }

        var hitErrors = new List<int>(hitEvents.Count - lastHitEventIndex);
        for (var i = lastHitEventIndex; i < hitEvents.Count; i++)
        {
            var timeOffset = hitEvents[i].TimeOffset;
            if (double.IsFinite(timeOffset))
                hitErrors.Add((int)Math.Round(timeOffset));
        }

        lastHitEventIndex = hitEvents.Count;
        hitErrorIndex = lastHitEventIndex;
        return hitErrors.ToArray();
    }

    private static T? GetPlayerProperty<T>(Player player, PropertyInfo? property)
        where T : class
    {
        if (property == null)
            return null;

        try
        {
            return property.GetValue(player) as T;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to read Player property '{property.Name}' via reflection.");
            return null;
        }
    }

    private static int ClampScore(long score)
    {
        if (score > int.MaxValue) return int.MaxValue;
        if (score < int.MinValue) return int.MinValue;
        return (int)score;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
        {
            if (isActive)
            {
                workingBeatmap!.ValueChanged -= OnWorkingBeatmapChanged;
                ruleset!.ValueChanged -= OnRulesetChanged;
                selectedMods!.ValueChanged -= OnSelectedModsChanged;

                isActive = false;
                Interlocked.Exchange(ref activeComponent, 0);
            }

            UnbindScoreProcessor();
            UnbindDrawableRuleset();
            RestoreShowConvertedBeatmapsSetting();
            beatmapMapCts?.Cancel();
            beatmapMapCts?.Dispose();
            disposalCts.Cancel();
            disposalCts.Dispose();
        }

        base.Dispose(isDisposing);
    }
}

[Flags]
internal enum PublishOptions
{
    None = 0,
    BeatmapFiles = 1 << 0,
    HitErrors = 1 << 1,
    ForceSkins = 1 << 2,
}
