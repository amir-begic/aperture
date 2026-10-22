#!/bin/bash
# Removes Aperture from this Mac:
#
#   ~/Aperture/app/deploy/macos/uninstall.sh                  keeps your data and backups in ~/Aperture
#   ~/Aperture/app/deploy/macos/uninstall.sh --remove-data    removes them too (database, files, login, backups, logs)
#
# Stops Aperture, the updater and the backup and removes them and the program ("Aperture.Web uninstall").
set -euo pipefail
APERTURE_HOME="${APERTURE_HOME:-$HOME/Aperture}"
exec "$APERTURE_HOME/app/Aperture.Web" uninstall --home "$APERTURE_HOME" "$@"
