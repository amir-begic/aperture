#!/bin/bash
# Builds the installer for a laptop, artifacts/Aperture-Installer.pkg. Run on a Mac (pkgbuild comes with macOS):
#
#   deploy/macos/pkg/build-pkg.sh https://github.com/<you>/aperture-releases/releases/latest/download/latest.json
#
# The package holds no program, only postinstall: it downloads the newest release from the feed and runs
# install-laptop.sh for the person logged in. So it never goes stale; build it again only when the feed address or
# postinstall changes.
#
# Unsigned, macOS blocks it on first open: System Settings → Privacy & Security → Open Anyway (once). To avoid that,
# set APERTURE_PKG_IDENTITY to a "Developer ID Installer: …" identity, then notarize it (docs/deployment.md).
set -euo pipefail
cd "$(dirname "$0")/../../.."

[[ $(uname -s) == Darwin ]] || { echo "Run this on a Mac: pkgbuild comes with macOS." >&2; exit 1; }
[[ $# -eq 1 ]] || { echo "Usage: deploy/macos/pkg/build-pkg.sh <feed URL (…/latest.json)>" >&2; exit 1; }
feed="$1"
feed_pattern='^https://[A-Za-z0-9._~:/?#@!$()*+,;=%-]+$'
[[ $feed =~ $feed_pattern ]] || { echo "The feed must be an https address: $feed" >&2; exit 1; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir "$work/scripts"
sed "s|__FEED_URL__|$feed|g" deploy/macos/pkg/postinstall > "$work/scripts/postinstall"
chmod 755 "$work/scripts/postinstall"

sign=()
[[ -z ${APERTURE_PKG_IDENTITY:-} ]] || sign=(--sign "$APERTURE_PKG_IDENTITY")
mkdir -p artifacts
pkgbuild --nopayload --scripts "$work/scripts" --identifier ch.aperture.installer --version "$(date +%Y.%m.%d)" \
    ${sign[@]+"${sign[@]}"} artifacts/Aperture-Installer.pkg
echo "artifacts/Aperture-Installer.pkg"
