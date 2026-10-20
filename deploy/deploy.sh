#!/usr/bin/env bash
# Builds a new version and installs it on the Mac mini over SSH (Tailscale SSH or Remote Login):
#
#   deploy/deploy.sh amir@mac-mini      (or a Host alias from ~/.ssh/config, e.g. deploy/deploy.sh mac)
#   deploy/deploy.sh mac --install      run the installer again, e.g. to set up something new it brings
#
# The first time, when Funk isn't on the Mac yet, it runs the installer (deploy/macos/install.sh), which asks
# its questions right here in this terminal; with --install it does so on an existing installation too (all data
# is kept). Otherwise it runs the update.
set -euo pipefail
cd "$(dirname "$0")/.."
usage() { echo "Usage: deploy/deploy.sh <user@mac> [--install]" >&2; exit 1; }
[[ $# -ge 1 && $# -le 2 ]] || usage
target="$1"
install=0
if [[ $# -eq 2 ]]; then
    [[ $2 == --install ]] || usage
    install=1
fi

tarball="$(deploy/publish.sh)"
file="${tarball##*/}"
dir="${file%-osx-arm64.tar.gz}"
echo "Copying $file to $target"
scp "$tarball" "$target:/tmp/$file"
# Runs in the Mac's login shell (zsh), so plain POSIX sh only. ("status" is reserved in zsh, hence "rc".)
ssh -t "$target" "
    if [ $install -eq 0 ] && [ -x \"\$HOME/Funk/app/deploy/macos/update.sh\" ]; then
        \"\$HOME/Funk/app/deploy/macos/update.sh\" /tmp/$file
        rc=\$?
    else
        echo 'Running the installer (existing data is kept).'
        cd /tmp && rm -rf $dir && tar -xzf $file && ./$dir/deploy/macos/install.sh /tmp/$file
        rc=\$?
        rm -rf /tmp/$dir
    fi
    rm -f /tmp/$file
    exit \$rc"
