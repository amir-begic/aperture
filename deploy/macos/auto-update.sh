#!/bin/bash
# Installs an update that Aperture downloaded (Settings → Updates). Started by launchd (ch.aperture.updater.plist) as soon as
# Aperture writes ~/Aperture/data/updates/request, which holds the file name of the downloaded release next to it.
# Runs update.sh (stop, copy the database, switch, health check, roll back on failure) and reports the outcome in
# ~/Aperture/data/updates/status.json, which Aperture shows under Settings → Updates.
#
# On the Mac mini, needs the sudo rules from install.sh (/etc/sudoers.d/aperture): there's nobody to type a password
# here. A laptop installation (install-laptop.sh) runs as launchd agents of the user and needs no sudo.
set -uo pipefail

APERTURE_HOME="${APERTURE_HOME:-$HOME/Aperture}"
UPDATES="$APERTURE_HOME/data/updates"
REQUEST="$UPDATES/request"
STATUS="$UPDATES/status.json"

log() { printf '%s %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*"; }

json_string() {
    local s="$1"
    s="${s//\\/\\\\}"
    s="${s//\"/\\\"}"
    s="${s//$'\n'/ }"
    s="${s//$'\r'/}"
    s="${s//$'\t'/ }"
    printf '"%s"' "$s"
}

write_status() {
    local state="$1" version="$2" message="$3"
    printf '{"state":%s,"version":%s,"message":%s,"time":"%s"}\n' \
        "$(json_string "$state")" "$(json_string "$version")" "$(json_string "$message")" \
        "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$STATUS.tmp" && mv -f "$STATUS.tmp" "$STATUS"
}

# launchd also starts the job when the request file disappears; then there's nothing to do.
[[ -f $REQUEST ]] || exit 0

name="$(head -n 1 "$REQUEST" | tr -d '[:space:]')"
rm -f "$REQUEST"

# Only a release package lying in the updates folder, never a path from elsewhere.
if [[ ! $name =~ ^aperture-[0-9A-Za-z.-]+-osx-arm64\.tar\.gz$ || ! -f $UPDATES/$name ]]; then
    log "Ignored request: '$name'"
    write_status failed "" "The downloaded release wasn't found. Try again."
    exit 1
fi

# Without the sudo rules from install.sh, update.sh would stop at the first password prompt.
if [[ ! -f $HOME/Library/LaunchAgents/ch.aperture.web.plist ]] && ! sudo -n -l /bin/launchctl bootout system/ch.aperture.web >/dev/null 2>&1; then
    log "Not allowed to restart Aperture without a password"
    write_status failed "" "The updater isn't allowed to restart Aperture. Run install.sh again on the Mac; it sets this up."
    rm -f "$UPDATES/$name"
    exit 1
fi

version="${name#aperture-}"
version="${version%-osx-arm64.tar.gz}"
log "Updating to $version"
write_status running "$version" "Installing"

# update.sh needs the current scripts of the running version; it stops Aperture itself.
output="$("$APERTURE_HOME/app/deploy/macos/update.sh" "$UPDATES/$name" 2>&1)"
rc=$?
printf '%s\n' "$output" | sed 's/\x1b\[[0-9;]*m//g'
rm -f "$UPDATES/$name"

if [[ $rc -eq 0 ]]; then
    log "Updated to $version"
    write_status succeeded "$version" "Updated"
else
    reason="$(printf '%s\n' "$output" | sed 's/\x1b\[[0-9;]*m//g' | grep -E '^Error: ' | tail -n 1 | sed 's/^Error: //')"
    log "Update to $version failed: ${reason:-exit code $rc}"
    write_status failed "$version" "${reason:-update.sh stopped with exit code $rc.}"
fi
exit $rc
