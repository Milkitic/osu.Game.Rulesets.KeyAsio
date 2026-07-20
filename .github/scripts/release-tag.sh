#!/usr/bin/env bash
# Build the plugin for an existing tag and publish its GitHub Release.
set -euo pipefail

TAG="${1:-}"
PROJECT="osu.Game.Rulesets.OverlayAPI/osu.Game.Rulesets.OverlayAPI.csproj"
ARTIFACT="osu.Game.Rulesets.OverlayAPI/bin/Release/net8.0/osu.Game.Rulesets.OverlayAPI.dll"

if [ -z "$TAG" ]; then
  echo "::error::Usage: release-tag.sh <tag>"
  exit 1
fi

case "$TAG" in
  v[0-9]*-lazer.[0-9]*.[0-9]*.[0-9]*)
    CHANNEL="lazer"
    PRERELEASE=false
    OSU_VER="${TAG#v*-lazer.}"
    TITLE="OverlayAPI ${TAG} — osu! lazer ${OSU_VER}"
    SUMMARY="Automatically synchronized for the osu! lazer channel."
    ;;
  v[0-9]*-tachyon.[0-9]*.[0-9]*.[0-9]*)
    CHANNEL="tachyon"
    PRERELEASE=false
    OSU_VER="${TAG#v*-tachyon.}"
    TITLE="OverlayAPI ${TAG} — osu! tachyon ${OSU_VER}"
    SUMMARY="Automatically synchronized for the osu! tachyon channel."
    ;;
  v[0-9]*-a | v[0-9]*-a.[0-9]*)
    CHANNEL="manual"
    PRERELEASE=true
    TITLE="OverlayAPI ${TAG} — manual enhancement"
    OSU_VER="N/A (manual code baseline)"
    SUMMARY="Manual enhancement pre-release baseline. Successful publication automatically starts lazer and tachyon channel synchronization."
    ;;
  *)
    echo "::error::Unsupported release tag '$TAG'. Use a lazer, tachyon, -a, or -a.N tag."
    exit 1
    ;;
esac

if gh release view "$TAG" >/dev/null 2>&1; then
  echo "GitHub Release $TAG already exists — nothing to publish."
  exit 0
fi

dotnet build "$PROJECT" -c Release

if [ ! -f "$ARTIFACT" ]; then
  echo "::error::Expected final ILRepack artifact was not found: $ARTIFACT"
  exit 1
fi

NUGET_VER="$(sed -n 's/.*<PpyOsuGameVersion>\([^<]*\)<\/PpyOsuGameVersion>.*/\1/p' "$PROJECT" | head -1)"
if [ -z "$NUGET_VER" ]; then
  NUGET_VER="$(sed -n 's/.*<PackageReference *Include="ppy\.osu\.Game" *Version="\([^"]*\)".*/\1/p' "$PROJECT" | head -1)"
fi
COMMIT="$(git rev-parse --short=12 HEAD)"
NOTES="$(printf '%s\n\n- Channel: `%s`\n- osu! target: `%s`\n- ppy.osu.Game: `%s`\n- Source commit: `%s`\n\nThe single DLL asset is the final ILRepack ruleset assembly; copy it into the osu! `rulesets` directory.' \
  "$SUMMARY" "$CHANNEL" "$OSU_VER" "${NUGET_VER:-unknown}" "$COMMIT")"

ARGS=(release create "$TAG" "${ARTIFACT}#osu.Game.Rulesets.OverlayAPI.dll" --verify-tag --title "$TITLE" --notes "$NOTES" --generate-notes)
[ "$PRERELEASE" = true ] && ARGS+=(--prerelease)
gh "${ARGS[@]}"
