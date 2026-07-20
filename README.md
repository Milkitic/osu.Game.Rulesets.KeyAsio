# osu.Game.Rulesets.OverlayAPI

Place the `osu.Game.Rulesets.OverlayAPI.dll` downloaded from the Releases page into your `[Lazer Main Folder]/rulesets` directory. If an overlay icon appears on the top toolbar in-game, it indicates that the OverlayAPI ruleset has been loaded successfully.

## ⚠ Warning

OverlayAPI uses osu!'s public APIs and reads only the data required to communicate with streaming overlays and other external software.

It is designed to be minimally invasive: it does not modify osu!'s behaviour or use reflection.

However, it is not a complete, playable ruleset. **USE IT AT YOUR OWN RISK.**

Once osu!'s official external integrations are ready ([See PR Here](https://github.com/ppy/osu/pull/37335)), this ruleset will be discontinued immediately.
