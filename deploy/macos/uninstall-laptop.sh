#!/bin/bash
# Removes Aperture from a laptop (install-laptop.sh) but keeps your data:
#
#   ~/Aperture/app/deploy/macos/uninstall-laptop.sh
#
# Stops Aperture, the updater and the backup, removes their launchd agents and the program. ~/Aperture/data (database,
# receipts, invoices, login) and ~/Aperture/backups stay; delete ~/Aperture yourself to remove those too.
set -euo pipefail
export APERTURE_LAPTOP=1
source "$(cd "$(dirname "$0")" && pwd)/common.sh"

require_mac
[[ ! -f $WEB_PLIST ]] || fail "Aperture runs as a service on this Mac (install.sh); this script is for laptop installations."

say "Stopping Aperture"
stop_web
for label in "$UPDATER_LABEL" "$BACKUP_LABEL"; do
    launchctl bootout "gui/$UID/$label" 2>/dev/null || true
done
for label in "$WEB_LABEL" "$UPDATER_LABEL" "$BACKUP_LABEL"; do
    rm -f "$AGENTS_DIR/$label.plist"
done

say "Removing the program"
# This script lives in there; bash has already opened it, so removing it doesn't stop it.
rm -rf "$APERTURE_HOME/releases" "$APERTURE_HOME/app" "$APERTURE_HOME/port" "$APERTURE_HOME/data/updates"

say "Done"
echo "Your data is still in $APERTURE_HOME/data, the backups in $APERTURE_HOME/backups."
echo "Installing again keeps using them. To remove everything: rm -rf \"$APERTURE_HOME\""
