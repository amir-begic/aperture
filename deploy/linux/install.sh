#!/bin/sh
# Installs Aperture for the person logged in, from the unpacked release:
#
#   tar -xzf aperture-…-linux-x64.tar.gz && ./aperture-…/install.sh
#
# Copies the release into ~/.local/share/aperture and hands over to "Aperture.Web install", which sets up systemd user
# units (Aperture while you're logged in, the updater, the daily backup), adds Aperture to the applications menu and
# opens http://localhost:5090. Needs no root. Safe to run again: existing data is kept.
set -eu
here="$(cd "$(dirname "$0")" && pwd)"
command -v systemctl >/dev/null 2>&1 || { echo "Aperture needs systemd (systemctl --user)." >&2; exit 1; }
systemctl --user show-environment >/dev/null 2>&1 \
    || { echo "systemd's user manager isn't running for you. Log in to a desktop session and try again." >&2; exit 1; }
exec "$here/Aperture.Web" install "$@"
