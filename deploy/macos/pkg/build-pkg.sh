#!/bin/bash
# Builds the installer, artifacts/Aperture-Installer.pkg, around a release. Run on a Mac (pkgbuild and productbuild come
# with macOS); .github/workflows/release.yml runs it for every release and attaches the installer to it:
#
#   deploy/macos/pkg/build-pkg.sh artifacts/aperture-…-osx-arm64.tar.gz \
#       https://github.com/<you>/aperture/releases/latest/download/latest.json
#
# The installer carries the release, so installing needs no download. Double-clicked, it shows a welcome page and the
# license, checks for Apple silicon and macOS 15, and installs for the person logged in (postinstall, which runs
# install.sh with the feed for updates).
#
# Unsigned, macOS blocks it on first open: System Settings → Privacy & Security → Open Anyway (once). To avoid that,
# set APERTURE_PKG_IDENTITY to a "Developer ID Installer: …" identity, then notarize it (docs/deployment.md).
set -euo pipefail
cd "$(dirname "$0")/../../.."

[[ $(uname -s) == Darwin ]] || { echo "Run this on a Mac: pkgbuild and productbuild come with macOS." >&2; exit 1; }
[[ $# -eq 2 ]] || { echo "Usage: deploy/macos/pkg/build-pkg.sh <aperture-…-osx-arm64.tar.gz> <feed URL (…/latest.json)>" >&2; exit 1; }
tarball="$1"
feed="$2"
name="$(basename "$tarball")"
[[ -f $tarball && $name =~ ^aperture-[0-9A-Za-z.-]+-osx-arm64\.tar\.gz$ ]] || { echo "Not a release package: $tarball" >&2; exit 1; }
feed_pattern='^https://[A-Za-z0-9._~:/?#@!$()*+,;=%-]+$'
[[ $feed =~ $feed_pattern ]] || { echo "The feed must be an https address: $feed" >&2; exit 1; }

# aperture-2026.10.20-58-ed1231e-osx-arm64.tar.gz → 2026.10.20.58: date and build number, which only grows.
version="$(sed -E 's/^aperture-([0-9]+\.[0-9]+\.[0-9]+)-([0-9]+)-.*/\1.\2/' <<< "$name")"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
pkg=deploy/macos/pkg
mkdir "$work/scripts" "$work/resources"
sed "s|__FEED_URL__|$feed|g" "$pkg/postinstall" > "$work/scripts/postinstall"
chmod 755 "$work/scripts/postinstall"
cp "$tarball" "$work/scripts/"
cp "$pkg"/resources/*.html "$work/resources/"
cp LICENSE "$work/resources/LICENSE.txt"
sed "s|__VERSION__|$version|g" "$pkg/distribution.xml" > "$work/distribution.xml"

pkgbuild --nopayload --scripts "$work/scripts" --identifier ch.aperture.installer --version "$version" \
    "$work/Aperture.pkg" >/dev/null

sign=()
[[ -z ${APERTURE_PKG_IDENTITY:-} ]] || sign=(--sign "$APERTURE_PKG_IDENTITY")
mkdir -p artifacts
productbuild --distribution "$work/distribution.xml" --resources "$work/resources" --package-path "$work" \
    ${sign[@]+"${sign[@]}"} artifacts/Aperture-Installer.pkg >/dev/null
echo "artifacts/Aperture-Installer.pkg"
