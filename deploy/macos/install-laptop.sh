#!/bin/bash
# Sets up Aperture on a Mac laptop, for the person logged in: no admin rights, nothing reachable from other devices.
#
#   install-laptop.sh <aperture-…-osx-arm64.tar.gz> [feed URL]
#
# Usually run by the installer package (deploy/macos/pkg), or by hand from the unpacked release.
# Installs into ~/Aperture and runs Aperture as launchd agents of your user (~/Library/LaunchAgents): the app at
# http://localhost:5090 whenever you're logged in, the updater, and a daily backup into ~/Aperture/backups. The login
# is created in the browser on first start. With a feed URL (latest.json, see .github/workflows/promote.yml), Aperture
# offers new releases from it under Settings → Updates; without one, it keeps the current one. Safe to run again:
# existing data is kept.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
source "$here/common.sh"

require_mac
[[ $# -ge 1 && $# -le 2 ]] || fail "Usage: install-laptop.sh <aperture-…-osx-arm64.tar.gz> [feed URL]"
unsafe='[[:space:]|&]'   # the folder goes into plists through sed
[[ ! $APERTURE_HOME =~ $unsafe ]] || fail "The install folder can't contain spaces, | or &: $APERTURE_HOME"

feed="${2:-}"
if [[ -z $feed && -f $AGENTS_DIR/$WEB_LABEL.plist ]]; then
    feed="$(/usr/libexec/PlistBuddy -c "Print :EnvironmentVariables:Updates__FeedUrl" "$AGENTS_DIR/$WEB_LABEL.plist" 2>/dev/null || true)"
fi
# It goes into a plist through sed: no &, |, quotes or angle brackets.
feed_pattern='^https://[A-Za-z0-9._~:/?#@!$()*+,;=%-]+$'
[[ -z $feed || $feed =~ $feed_pattern ]] || fail "The feed must be an https address: $feed"

say "1/3  Installing into $APERTURE_HOME"
mkdir -p "$APERTURE_HOME/data/updates" "$APERTURE_HOME/logs" "$APERTURE_HOME/backups" "$AGENTS_DIR"
chmod 700 "$APERTURE_HOME/data" "$APERTURE_HOME/backups"   # receipts, invoices and login keys: yours only
[[ -f $APERTURE_HOME/data/aperture.db ]] && echo "Existing data found in $APERTURE_HOME/data, it will be kept."
release="$(unpack_release "$1")"
prepare_release "$release"
echo "Version: ${release##*/}"

port="$(web_port 2>/dev/null || true)"
port="${port:-5090}"

say "2/3  Starting Aperture"
stop_web
switch_to "$release"
for label in "$UPDATER_LABEL" "$BACKUP_LABEL"; do
    launchctl bootout "gui/$UID/$label" 2>/dev/null || true
done
for label in "$WEB_LABEL" "$UPDATER_LABEL" "$BACKUP_LABEL"; do
    sed -e "s|__APERTURE_HOME__|$APERTURE_HOME|g" -e "s|__PORT__|$port|g" -e "s|__FEED_URL__|$feed|g" \
        "$here/$label.plist" > "$AGENTS_DIR/$label.plist"
done
printf '%s\n' "$port" > "$APERTURE_HOME/port"
start_web
launchctl bootstrap "gui/$UID" "$AGENTS_DIR/$UPDATER_LABEL.plist"
if wait_until_healthy "$port"; then
    echo "Aperture is running."
else
    tail -n 30 "$APERTURE_HOME/logs/aperture.log" || true
    fail "Aperture didn't start. See the log above."
fi
[[ -n $feed ]] && echo "Updates from: $feed" || echo "No update feed: Aperture won't look for new versions."

say "3/3  Daily backup"
# backup.sh writes to "<folder>/Aperture Backup" for each line. ~/Aperture/backups is on this Mac, so Time Machine (or
# any backup of your home folder) takes the copies along; the database copies are consistent, the live file may not be.
if [[ ! -f $APERTURE_HOME/backup.conf ]]; then
    {
        echo "# Aperture backup folders, one per line. Folders that aren't there (e.g. a drive that isn't connected) are skipped."
        echo "# Add an external drive for a copy off this Mac, e.g. /Volumes/Backup (needs Full Disk Access for /bin/bash)."
        echo "$APERTURE_HOME/backups"
    } > "$APERTURE_HOME/backup.conf"
fi
launchctl bootstrap "gui/$UID" "$AGENTS_DIR/$BACKUP_LABEL.plist"   # runs one right away, then daily
echo "Backups go to:"
grep -v '^[[:space:]]*\(#.*\)\{0,1\}$' "$APERTURE_HOME/backup.conf" | sed 's|$|/Aperture Backup|; s|^|  |' || true

say "Done"
echo "Aperture:  http://localhost:$port"
echo "Data:      $APERTURE_HOME/data"
echo "Logs:      $APERTURE_HOME/logs"
echo "Uninstall: $APERTURE_HOME/app/deploy/macos/uninstall-laptop.sh"
[[ ${APERTURE_NO_OPEN:-} == 1 ]] || open "http://localhost:$port"
