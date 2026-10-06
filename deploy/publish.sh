#!/usr/bin/env bash
# Builds Funk for the Mac mini (Apple silicon) and packs it, scripts included:
#
#   deploy/publish.sh            -> artifacts/funk-<date>-<commit>-osx-arm64.tar.gz
#
# The build is self-contained, so the Mac doesn't need .NET installed.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet="$(command -v dotnet || echo "$HOME/.dotnet/dotnet")"
version="$(date +%Y.%m.%d)-$(git rev-parse --short HEAD)"
git diff --quiet HEAD -- || version="$version-dirty"
name="funk-$version"
out="artifacts/$name"

rm -rf "$out"
"$dotnet" publish src/Funk.Web -c Release -r osx-arm64 --self-contained \
    -p:InformationalVersion="$version" -o "$out" --nologo -v quiet >&2
mkdir -p "$out/deploy/macos"
cp deploy/macos/*.sh deploy/macos/*.plist "$out/deploy/macos/"
chmod +x "$out"/deploy/macos/*.sh

tar -C artifacts -czf "artifacts/$name-osx-arm64.tar.gz" "$name"
rm -rf "$out"
echo "artifacts/$name-osx-arm64.tar.gz"
