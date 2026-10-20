#!/bin/bash
# First-time setup of Funk on the Mac mini. Run from the unpacked release:
#
#   tar -xzf funk-<version>-osx-arm64.tar.gz
#   ./funk-<version>/deploy/macos/install.sh funk-<version>-osx-arm64.tar.gz
#
# Sets up ~/Funk, your login, the Funk service (starts at boot), updates from GitHub, the nightly backup,
# the firewall, sleep settings and optionally HTTPS through Tailscale. Safe to run again: existing data is kept.
set -euo pipefail
source "$(cd "$(dirname "$0")" && pwd)/common.sh"
templates="$(cd "$(dirname "$0")" && pwd)"

require_mac
[[ $# -eq 1 ]] || fail "Usage: install.sh <funk-…-osx-arm64.tar.gz>"

echo "Setting up Funk needs three different things; each prompt says which one it wants:"
echo "  - your Mac password, for the system parts (asked now, not saved),"
echo "  - a username and password for Funk itself (your choice, separate from the Mac),"
echo "  - optionally a GitHub token for updates."
sudo -v || fail "The Mac password is needed to set up the service."

say "1/7  Installing into $FUNK_HOME"
mkdir -p "$FUNK_HOME/data/updates" "$FUNK_HOME/logs" "$FUNK_HOME/backups"
chmod 700 "$FUNK_HOME/data" "$FUNK_HOME/backups"   # receipts, invoices and login keys: yours only
[[ -f $FUNK_HOME/data/funk.db ]] && echo "Existing data found in $FUNK_HOME/data, it will be kept."
release="$(unpack_release "$1")"
prepare_release "$release"
switch_to "$release"
echo "Version: ${release##*/}"

say "2/7  Your login"
login=""
while [[ ! $login =~ ^[A-Za-z0-9._@-]+$ ]]; do
    read -r -p "Funk username [$USER]: " login
    login="${login:-$USER}"
done
while :; do
    read -r -s -p "Funk password (at least 8 characters, hidden): " password; echo
    read -r -s -p "Repeat the Funk password: " repeated; echo
    if [[ ${#password} -lt 8 ]]; then echo "Too short."; continue; fi
    if [[ $password != "$repeated" ]]; then echo "Passwords don't match."; continue; fi
    break
done
hash="$(printf '%s\n%s\n%s\n' "$login" "$password" "$password" \
    | "$FUNK_HOME/app/Funk.Web" hash-password | sed -n 's/^ *Auth__PasswordHash=//p')"
unset password repeated
[[ -n $hash ]] || fail "Could not create the password hash."

read -r -p "Port for Funk on your network [5080]: " port
port="${port:-5080}"
[[ $port =~ ^[0-9]+$ ]] || fail "Not a port number: $port"

echo
echo "Updates from GitHub (optional): Funk shows new releases under Settings → Updates and installs them."
echo "Needs the repository and a fine-grained token with read-only access to its contents. Empty to skip."
current_repo="" current_token=""
if [[ -f $WEB_PLIST ]]; then
    current_repo="$(sudo /usr/libexec/PlistBuddy -c "Print :EnvironmentVariables:Updates__Repository" "$WEB_PLIST" 2>/dev/null || true)"
    current_token="$(sudo /usr/libexec/PlistBuddy -c "Print :EnvironmentVariables:Updates__GitHubToken" "$WEB_PLIST" 2>/dev/null || true)"
fi
read -r -p "GitHub repository (owner/name)${current_repo:+ [$current_repo]}: " repo
repo="${repo:-$current_repo}"
token=""
if [[ -n $repo ]]; then
    [[ $repo =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail "Not a repository name (owner/name): $repo"
    read -r -s -p "GitHub token (github_pat_…, hidden${current_token:+; Enter keeps the current one}): " token; echo
    token="${token:-$current_token}"
    [[ $token =~ ^[A-Za-z0-9_]+$ ]] || fail "That doesn't look like a GitHub token."
fi

say "3/7  Funk service (your Mac password is needed for the system parts)"
stop_web
sed -e "s|__USER__|$USER|g" -e "s|__FUNK_HOME__|$FUNK_HOME|g" -e "s|__PORT__|$port|g" \
    -e "s|__LOGIN__|$login|g" -e "s|__HASH__|$hash|g" -e "s|__REPO__|$repo|g" -e "s|__TOKEN__|$token|g" \
    "$templates/ch.funk.web.plist" > "$FUNK_HOME/.web.plist"
sudo install -m 600 -o root -g wheel "$FUNK_HOME/.web.plist" "$WEB_PLIST"
rm -f "$FUNK_HOME/.web.plist"
printf '%s\n' "$port" > "$FUNK_HOME/port"
install_sudo_rules || true
allow_through_firewall "$release/Funk.Web"
start_web
sed -e "s|__USER__|$USER|g" -e "s|__FUNK_HOME__|$FUNK_HOME|g" "$templates/ch.funk.updater.plist" > "$FUNK_HOME/.updater.plist"
sudo launchctl bootout "system/$UPDATER_LABEL" 2>/dev/null || true
sudo install -m 644 -o root -g wheel "$FUNK_HOME/.updater.plist" "$UPDATER_PLIST"
rm -f "$FUNK_HOME/.updater.plist"
sudo launchctl bootstrap system "$UPDATER_PLIST"
if wait_until_healthy "$port"; then
    echo "Funk is running."
else
    tail -n 30 "$FUNK_HOME/logs/funk.log" || true
    fail "Funk didn't start. See the log above."
fi

say "4/7  Backup drives"
echo "Connected drives:"
ls -1 /Volumes | grep -v '^Macintosh HD' | sed 's|^|  /Volumes/|' || true
if [[ -f $FUNK_HOME/backup.conf ]]; then
    echo "Current backup drives:"
    grep -v '^[[:space:]]*\(#.*\)\{0,1\}$' "$FUNK_HOME/backup.conf" | sed 's/^/  /' || true
fi
echo "Enter the drives to back up to, one per line (e.g. /Volumes/Backup A). Empty line to finish,"
echo "or just press Enter to keep the current list."
drives=()
while IFS= read -r -p "Drive: " drive && [[ -n $drive ]]; do
    [[ -d $drive ]] || echo "  (not connected right now; it's backed up whenever it is)"
    drives+=("$drive")
done
if [[ ${#drives[@]} -gt 0 ]]; then
    { echo "# Funk backup drives, one per line. Drives that aren't connected are skipped."; printf '%s\n' "${drives[@]}"; } \
        > "$FUNK_HOME/backup.conf"
fi
[[ -f $FUNK_HOME/backup.conf ]] || echo "# Funk backup drives, one per line." > "$FUNK_HOME/backup.conf"
sed -e "s|__USER__|$USER|g" -e "s|__FUNK_HOME__|$FUNK_HOME|g" "$templates/ch.funk.backup.plist" > "$FUNK_HOME/.backup.plist"
sudo launchctl bootout "system/$BACKUP_LABEL" 2>/dev/null || true
sudo install -m 644 -o root -g wheel "$FUNK_HOME/.backup.plist" "$BACKUP_PLIST"
rm -f "$FUNK_HOME/.backup.plist"
sudo launchctl bootstrap system "$BACKUP_PLIST"
echo "Nightly backup scheduled for 03:30."

say "5/7  Keep the Mac mini available"
if ask_yes "Never sleep, and start again after a power failure?"; then
    sudo pmset -a sleep 0
    sudo pmset -a autorestart 1
fi

say "6/7  HTTPS through Tailscale"
# Homebrew's /opt/homebrew/bin isn't on the PATH over SSH, so look in the usual places too.
tailscale_cli="$(command -v tailscale || true)"
for candidate in /opt/homebrew/bin/tailscale /usr/local/bin/tailscale /Applications/Tailscale.app/Contents/MacOS/Tailscale; do
    [[ -n $tailscale_cli ]] && break
    [[ -x $candidate ]] && tailscale_cli="$candidate"
done
if [[ -z $tailscale_cli ]]; then
    echo "Tailscale CLI not found; skipped. See docs/deployment.md to set it up later."
elif ask_yes "Make Funk available at https://<this Mac>.<your tailnet>.ts.net?"; then
    # sudo: with Homebrew's tailscaled (running as root), only root or the "operator" may change the serve config.
    if sudo "$tailscale_cli" serve --bg "http://127.0.0.1:$port"; then
        sudo "$tailscale_cli" serve status || true
    else
        echo "tailscale serve failed. Are HTTPS certificates enabled for your tailnet (admin console → DNS)?"
    fi
fi

say "7/7  First backup"
if ask_yes "Run a backup now to check the drives?"; then
    sudo launchctl kickstart "system/$BACKUP_LABEL"
    sleep 5
    tail -n 10 "$FUNK_HOME/logs/backup.log" || true
fi

say "Done"
echo "On your network:  http://$(scutil --get LocalHostName).local:$port"
echo "Data:             $FUNK_HOME/data"
echo "Logs:             $FUNK_HOME/logs"
echo "Updates:          $FUNK_HOME/app/deploy/macos/update.sh <new release .tar.gz>"
