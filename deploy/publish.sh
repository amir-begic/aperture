#!/usr/bin/env bash
# Builds Aperture for one system and packs it, its install scripts included:
#
#   deploy/publish.sh [runtime]       runtime: osx-arm64 (default), win-x64, linux-x64, linux-arm64
#   -> artifacts/aperture-<date>-<build>-<commit>-<runtime>.tar.gz
#
# The build is self-contained, so the computer doesn't need .NET installed. With APERTURE_FEED_URL set (the release
# workflow sets it), the release remembers its update feed (feed.url), so its installers needn't know it.
set -euo pipefail
cd "$(dirname "$0")/.."

runtime="${1:-osx-arm64}"
[[ $runtime =~ ^(osx-arm64|win-x64|linux-x64|linux-arm64)$ ]] || { echo "Unknown runtime: $runtime" >&2; exit 1; }
dotnet="$(command -v dotnet || echo "$HOME/.dotnet/dotnet")"
# Date, build number (commits on this branch, so it only grows) and commit, e.g. 2026.10.20-58-ed1231e.
# Aperture compares the build number to tell whether a release is newer (Settings → Updates).
version="$(date -u +%Y.%m.%d)-$(git rev-list --count HEAD)-$(git rev-parse --short HEAD)"
git diff --quiet HEAD -- || version="$version-dirty"
name="aperture-$version"
out="artifacts/$name"

rm -rf "$out"
"$dotnet" publish src/Aperture.Web -c Release -r "$runtime" --self-contained \
    -p:InformationalVersion="$version" -o "$out" --nologo -v quiet >&2
# Releases are public (release.yml): never with the development data, login or keys.
[[ ! -e $out/data ]] || { echo "The build contains a data folder; check the Content items in Aperture.Web.csproj." >&2; exit 1; }
# Aperture's license, and the notices and license texts its third-party components require with the binaries.
cp -R LICENSE THIRD-PARTY-NOTICES.md licenses "$out/"
[[ -z ${APERTURE_FEED_URL:-} ]] || printf '%s\n' "$APERTURE_FEED_URL" > "$out/feed.url"

# What each system's installer needs from the release (the installing itself is "Aperture.Web install").
case "$runtime" in
    osx-*)
        mkdir -p "$out/deploy/macos"
        cp deploy/macos/*.sh deploy/icons/Aperture.icns "$out/deploy/macos/"   # the icon for the Aperture app
        chmod +x "$out"/deploy/macos/*.sh
        ;;
    linux-*)
        cp deploy/linux/install.sh "$out/install.sh"
        chmod +x "$out/install.sh"
        ;;
esac

tar -C artifacts -czf "artifacts/$name-$runtime.tar.gz" "$name"
rm -rf "$out"
echo "artifacts/$name-$runtime.tar.gz"
