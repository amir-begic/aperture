#!/bin/bash
# Removes Aperture from this Mac but keeps your data:
#
#   ~/Aperture/app/deploy/macos/uninstall.sh
#
# Stops Aperture, the updater and the backup and removes them and the program ("Aperture.Web uninstall").
# ~/Aperture/data (database, receipts, invoices, login) and ~/Aperture/backups stay; delete ~/Aperture to remove those too.
set -euo pipefail
APERTURE_HOME="${APERTURE_HOME:-$HOME/Aperture}"
exec "$APERTURE_HOME/app/Aperture.Web" uninstall --home "$APERTURE_HOME"
