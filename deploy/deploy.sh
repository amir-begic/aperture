#!/usr/bin/env bash
# Builds a new version and installs it on the Mac mini over SSH (Tailscale SSH or Remote Login):
#
#   deploy/deploy.sh amir@mac-mini      (or a Host alias from ~/.ssh/config, e.g. deploy/deploy.sh mac)
#
# The first time, when Funk isn't on the Mac yet, it runs the installer (deploy/macos/install.sh), which asks
# its questions right here in this terminal. After that it runs the update. Asks for the Mac's password for sudo.
set -euo pipefail
cd "$(dirname "$0")/.."
[[ $# -eq 1 ]] || { echo "Usage: deploy/deploy.sh <user@mac>" >&2; exit 1; }
target="$1"

tarball="$(deploy/publish.sh)"
file="${tarball##*/}"
dir="${file%-osx-arm64.tar.gz}"
echo "Copying $file to $target"
scp "$tarball" "$target:/tmp/$file"
# Runs in the Mac's login shell (zsh), so plain POSIX sh only. ("status" is reserved in zsh, hence "rc".)
ssh -t "$target" "
    if [ -x \"\$HOME/Funk/app/deploy/macos/update.sh\" ]; then
        \"\$HOME/Funk/app/deploy/macos/update.sh\" /tmp/$file
        rc=\$?
    else
        echo 'Funk is not installed on this Mac yet: running the installer.'
        cd /tmp && rm -rf $dir && tar -xzf $file && ./$dir/deploy/macos/install.sh /tmp/$file
        rc=\$?
        rm -rf /tmp/$dir
    fi
    rm -f /tmp/$file
    exit \$rc"
