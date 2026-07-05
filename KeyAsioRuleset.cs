using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.KeyAsio.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.KeyAsio;

public sealed partial class KeyAsioRuleset : Ruleset
{
    public const string KEYASIO_SHORT_NAME = "keyasio-osu";

    public KeyAsioRuleset()
    {
        RulesetInfo.OnlineID = -1;
        RulesetInfo.ShortName = ShortName;
        RulesetInfo.Name = Description;
    }

    public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

    public override string ShortName => KEYASIO_SHORT_NAME;

    public override string Description => "KeyASIO Frontend";

    public override string PlayingVerb => "Using KeyASIO";

    public override IEnumerable<Mod> GetModsFor(ModType type) => [];

    public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
        => throw new NotSupportedException("KeyASIO Frontend does not provide gameplay.");

    public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap)
        => new FrontendBeatmapConverter(beatmap, this);

    public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap)
        => new FrontendDifficultyCalculator(RulesetInfo, beatmap);

    public override IRulesetFilterCriteria? CreateRulesetFilterCriteria() => new EmptySongSelectFilterCriteria();

    public override Drawable CreateIcon() => new KeyAsioRulesetIcon();

    private sealed partial class KeyAsioRulesetIcon : CompositeDrawable
    {
        public KeyAsioRulesetIcon()
        {
            AutoSizeAxes = Axes.Both;

            InternalChildren =
            [
                new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = FontAwesome.Solid.Bolt,
                },
                new KeyAsioLazerGlobalSyncBootstrapper()
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

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
            => new DifficultyAttributes(mods, 0);

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, double clockRate) => [];

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods, double clockRate) => [];
    }
}
