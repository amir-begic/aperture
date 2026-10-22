#!/usr/bin/env bash
# Builds Aperture for Apple silicon and packs it, scripts included:
#
#   deploy/publish.sh            -> artifacts/aperture-<date>-<build>-<commit>-osx-arm64.tar.gz
#
# The build is self-contained, so the Mac doesn't need .NET installed.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet="$(command -v dotnet || echo "$HOME/.dotnet/dotnet")"
# Date, build number (commits on this branch, so it only grows) and commit, e.g. 2026.10.20-58-ed1231e.
# Aperture compares the build number to tell whether a release is newer (Settings → Updates).
version="$(date -u +%Y.%m.%d)-$(git rev-list --count HEAD)-$(git rev-parse --short HEAD)"
git diff --quiet HEAD -- || version="$version-dirty"
name="aperture-$version"
out="artifacts/$name"

rm -rf "$out"
"$dotnet" publish src/Aperture.Web -c Release -r osx-arm64 --self-contained \
    -p:InformationalVersion="$version" -o "$out" --nologo -v quiet >&2
# Releases end up public (promote.yml): never with the development data, login or keys.
[[ ! -e $out/data ]] || { echo "The build contains a data folder; check the Content items in Aperture.Web.csproj." >&2; exit 1; }
mkdir -p "$out/deploy/macos"
cp deploy/macos/*.sh deploy/macos/*.plist "$out/deploy/macos/"
chmod +x "$out"/deploy/macos/*.sh

tar -C artifacts -czf "artifacts/$name-osx-arm64.tar.gz" "$name"
rm -rf "$out"
echo "artifacts/$name-osx-arm64.tar.gz"
