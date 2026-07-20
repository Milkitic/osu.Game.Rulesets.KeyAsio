using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.OverlayAPI.UI;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.OverlayAPI;

public sealed partial class OverlayApiRuleset : Ruleset
{
    public const string OVERLAYAPI_SHORT_NAME = "overlay-api";

    public OverlayApiRuleset()
    {
        RulesetInfo.OnlineID = -1;
        RulesetInfo.ShortName = ShortName;
        RulesetInfo.Name = Description;
    }

    public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

    public override string ShortName => OVERLAYAPI_SHORT_NAME;

    public override string Description => "OverlayAPI";

    public override string PlayingVerb => "Using OverlayAPI";

    public override IEnumerable<Mod> GetModsFor(ModType type) => [];

    public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
        => throw new NotSupportedException("OverlayAPI Frontend does not provide gameplay.");

    public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap)
        => new FrontendBeatmapConverter(beatmap, this);

    public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap)
        => new FrontendDifficultyCalculator(RulesetInfo, beatmap);

    public override IRulesetFilterCriteria? CreateRulesetFilterCriteria() => new EmptySongSelectFilterCriteria();

    public override Drawable CreateIcon() => new OverlayApiRulesetIcon();

    private sealed partial class OverlayApiRulesetIcon : CompositeDrawable
    {
        public OverlayApiRulesetIcon()
        {
            AutoSizeAxes = Axes.Both;

            InternalChildren =
            [
                new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = FontAwesome.Solid.LayerGroup,
                },
                new OverlayApiLazerGlobalSyncBootstrapper()
            ];
        }
    }

    private sealed class EmptySongSelectFilterCriteria : IRulesetFilterCriteria
    {
        public bool Matches(BeatmapInfo beatmapInfo, FilterCriteria criteria) => false;

        public bool TryParseCustomKeywordCriteria(string key, Operator op, string value) => false;

        public bool FilterMayChangeFromMods(FilterCriteria criteria, ValueChangedEvent<IReadOnlyList<Mod>> mods) => false;
    }

    private sealed class FrontendBeatmapConverter : BeatmapConverter<HitObject>
    {
        public FrontendBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
            : base(beatmap, ruleset)
        {
        }

        public override bool CanConvert() => false;
    }

    private sealed class FrontendDifficultyCalculator : DifficultyCalculator
    {
        public FrontendDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

#if OSU_LEGACY_DIFFICULTY_API
        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
            => new DifficultyAttributes(mods, 0);

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, double clockRate) => [];

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods, double clockRate) => [];
#else
        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
            => new DifficultyAttributes(mods, 0);

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods) => [];

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods) => [];
#endif
    }
}
