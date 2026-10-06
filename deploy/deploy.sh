#!/usr/bin/env bash
# Builds a new version and installs it on the Mac mini over SSH (Tailscale SSH or Remote Login):
#
#   deploy/deploy.sh amir@mac-mini
#
# Needs Funk installed there already (deploy/macos/install.sh). Asks for the Mac's password for sudo.
set -euo pipefail
cd "$(dirname "$0")/.."
[[ $# -eq 1 ]] || { echo "Usage: deploy/deploy.sh <user@mac>" >&2; exit 1; }
target="$1"

tarball="$(deploy/publish.sh)"
file="${tarball##*/}"
echo "Copying $file to $target"
scp "$tarball" "$target:/tmp/$file"
ssh -t "$target" "\$HOME/Funk/app/deploy/macos/update.sh /tmp/$file && rm -f /tmp/$file"
