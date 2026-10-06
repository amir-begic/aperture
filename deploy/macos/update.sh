#!/bin/bash
# Installs a new version of Funk on the Mac mini.
#
#   ~/Funk/app/deploy/macos/update.sh ~/Downloads/funk-2026.10.06-b0d6009-osx-arm64.tar.gz
#
# Stops the app, copies the database, switches to the new version and checks that it starts.
# If it doesn't, the previous version and the database copy are restored.
set -euo pipefail
source "$(cd "$(dirname "$0")" && pwd)/common.sh"

require_mac
[[ $# -eq 1 ]] || fail "Usage: update.sh <funk-…-osx-arm64.tar.gz>"
[[ -f $WEB_PLIST ]] || fail "Funk isn't installed yet. Run install.sh first."

say "Unpacking"
release="$(unpack_release "$1")"
prepare_release "$release"
previous="$(current_release || true)"
[[ $release != "$previous" ]] || say "Reinstalling the same version"
echo "New: ${release##*/}"
echo "Old: ${previous##*/}"

port="$(web_port)"
port="${port:-5080}"

say "Stopping Funk (sudo password may be needed)"
stop_web
if ! snapshot="$(snapshot_database "${release##*/}")"; then
    start_web
    fail "Could not copy the database, so nothing was changed. The old version is running again."
fi
[[ -n $snapshot ]] && echo "Database copy: $snapshot"

say "Starting the new version"
switch_to "$release"
rotate_log
allow_through_firewall "$release/Funk.Web"
start_web

if wait_until_healthy "$port"; then
    remove_old_releases
    say "Updated to ${release##*/}"
    exit 0
fi

say "The new version didn't start. Last lines of the log:"
tail -n 30 "$FUNK_HOME/logs/funk.log" || true

[[ -n $previous ]] || fail "No previous version to go back to."
say "Going back to ${previous##*/}"
stop_web
[[ -n $snapshot ]] && restore_database "$snapshot"
switch_to "$previous"
allow_through_firewall "$previous/Funk.Web"
start_web
if wait_until_healthy "$port"; then
    fail "Update failed; ${previous##*/} is running again with the database as it was before."
fi
fail "Update failed, and the previous version didn't start either. See $FUNK_HOME/logs/funk.log."
