using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.KeyAsio.UI;

internal sealed partial class KeyAsioLazerGlobalSyncBootstrapper : Component
{
    private static int installStarted;

    [Resolved(CanBeNull = true)]
    private OsuGameBase? game { get; set; }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (game == null)
            return;

        if (Interlocked.CompareExchange(ref installStarted, 1, 0) != 0)
            return;

        try
        {
            game.Add(new KeyAsioLazerGlobalSyncComponent());
            Logger.Log("Installed KeyASIO lazer global sync component.");
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref installStarted, 0);
            Logger.Error(ex, "Failed to install KeyASIO lazer global sync component.");
        }
    }
}
