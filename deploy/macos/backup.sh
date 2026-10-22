#!/bin/bash
# The daily backup on a Mac, run by launchd (ch.aperture.backup): "Aperture.Web backup" (src/Aperture.Web/Installation/BackupJob.cs).
#
# It goes through /bin/bash on purpose, and runs the program as a child rather than replacing this shell: macOS grants
# Full Disk Access (needed for external drives) to /bin/bash once, while the program's path changes with every release.
APERTURE_HOME="${APERTURE_HOME:-$HOME/Aperture}"
"$APERTURE_HOME/app/Aperture.Web" backup --home "$APERTURE_HOME"
