#!/bin/bash
# Sets up Aperture on this Mac for the person logged in: no admin rights, nothing reachable from other devices.
#
#   install.sh <aperture-…-osx-arm64.tar.gz> [feed URL]
#
# Usually run by the installer package (deploy/macos/pkg). Unpacks the release into ~/Aperture, gets it ready to run
# on a Mac, and hands over to "Aperture.Web install", which sets up the launchd agents (Aperture at login, the updater,
# the daily backup) and the Aperture app, and opens it. Safe to run again: existing data is kept.
# Written for macOS's /bin/bash 3.2: no bash 4+ features.
set -euo pipefail
APERTURE_HOME="${APERTURE_HOME:-$HOME/Aperture}"

fail() { printf '\n\033[31mError: %s\033[0m\n' "$*" >&2; exit 1; }

[[ $(uname -s) == Darwin ]] || fail "This script runs on a Mac (macOS)."
[[ $(uname -m) == arm64 ]] || fail "This build is for Apple silicon (arm64)."
[[ $EUID -ne 0 ]] || fail "Run it as your normal user, not with sudo."
[[ $# -ge 1 && $# -le 2 ]] || fail "Usage: install.sh <aperture-…-osx-arm64.tar.gz> [feed URL]"
tarball="$1"
unsafe='[[:space:]"$%\\]'   # the folder goes into the agents' definitions
[[ ! $APERTURE_HOME =~ $unsafe ]] || fail "The install folder can't contain spaces, quotes, \$, % or \\: $APERTURE_HOME"

# Unpack into ~/Aperture/releases.
[[ -f $tarball ]] || fail "Release not found: $tarball"
name="$(tar -tzf "$tarball" | awk -F/ 'NR == 1 { print $1 }')"
[[ $name == aperture-* ]] || fail "$tarball doesn't look like an Aperture release."
release="$APERTURE_HOME/releases/$name"
mkdir -p "$APERTURE_HOME/releases"
rm -rf "$release"
tar -xzf "$tarball" -C "$APERTURE_HOME/releases"

# Remove the download quarantine flag and make sure the program is signed: Apple silicon refuses unsigned code, and a
# release built on Linux has no signature, so it's signed here, ad hoc. After that the program can run and does the rest.
xattr -dr com.apple.quarantine "$release" 2>/dev/null || true
if ! codesign --verify "$release/Aperture.Web" 2>/dev/null; then
    codesign --force --sign - "$release/Aperture.Web" || fail "Could not sign $release/Aperture.Web"
fi
chmod +x "$release/Aperture.Web"

args=(install --home "$APERTURE_HOME")
[[ -z ${2:-} ]] || args+=(--feed "$2")
exec "$release/Aperture.Web" "${args[@]}"
