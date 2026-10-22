#!/bin/bash
# Shared by install-laptop.sh, update.sh and uninstall-laptop.sh. Written for macOS's /bin/bash 3.2: no bash 4+ features.
#
# Aperture runs as launchd agents of the person logged in (~/Library/LaunchAgents): no sudo, and nothing listens beyond
# this Mac.

APERTURE_HOME="${APERTURE_HOME:-$HOME/Aperture}"
WEB_LABEL="ch.aperture.web"
BACKUP_LABEL="ch.aperture.backup"
UPDATER_LABEL="ch.aperture.updater"
AGENTS_DIR="$HOME/Library/LaunchAgents"
KEEP_RELEASES=3

say() { printf '\n\033[1m%s\033[0m\n' "$*"; }
fail() { printf '\n\033[31mError: %s\033[0m\n' "$*" >&2; exit 1; }

is_installed() {
    [[ -f $AGENTS_DIR/$WEB_LABEL.plist ]]
}

require_mac() {
    [[ $(uname -s) == Darwin ]] || fail "This script runs on a Mac (macOS)."
    [[ $(uname -m) == arm64 ]] || fail "This build is for Apple silicon (arm64)."
    [[ $EUID -ne 0 ]] || fail "Run it as your normal user, not with sudo."
}

# Unpacks a release tarball into $APERTURE_HOME/releases and prints the release directory.
unpack_release() {
    local tarball="$1" name
    [[ -f $tarball ]] || fail "Release not found: $tarball"
    name="$(tar -tzf "$tarball" | awk -F/ 'NR == 1 { print $1 }')"
    [[ $name == aperture-* ]] || fail "$tarball doesn't look like an Aperture release."
    mkdir -p "$APERTURE_HOME/releases"
    rm -rf "$APERTURE_HOME/releases/$name"
    tar -xzf "$tarball" -C "$APERTURE_HOME/releases"
    printf '%s' "$APERTURE_HOME/releases/$name"
}

# Removes the download quarantine flag and makes sure the executable is signed (Apple silicon refuses
# unsigned code; the .NET SDK signs it ad hoc, this re-signs only if that signature is missing or broken).
prepare_release() {
    local release="$1"
    xattr -dr com.apple.quarantine "$release" 2>/dev/null || true
    if ! codesign --verify "$release/Aperture.Web" 2>/dev/null; then
        codesign --force --sign - "$release/Aperture.Web" || fail "Could not sign $release/Aperture.Web"
    fi
    chmod +x "$release/Aperture.Web" "$release"/deploy/macos/*.sh
}

current_release() {
    [[ -L $APERTURE_HOME/app ]] && readlink "$APERTURE_HOME/app"
}

switch_to() {
    ln -sfn "$1" "$APERTURE_HOME/app"
}

start_web() {
    launchctl bootstrap "gui/$UID" "$AGENTS_DIR/$WEB_LABEL.plist" 2>/dev/null || launchctl kickstart -k "gui/$UID/$WEB_LABEL"
}

stop_web() {
    launchctl bootout "gui/$UID/$WEB_LABEL" 2>/dev/null || true
    # bootout returns before the process is gone; wait so the database is closed.
    local i
    for i in $(seq 1 20); do
        pgrep -f "$APERTURE_HOME/.*/Aperture.Web" >/dev/null || return 0
        sleep 0.5
    done
}

# install-laptop.sh writes the port to a file, so updates know where to check that Aperture is up.
web_port() {
    if [[ -f $APERTURE_HOME/port ]]; then
        head -n 1 "$APERTURE_HOME/port"
    fi
}

wait_until_healthy() {
    local port="$1" i
    for i in $(seq 1 60); do
        if [[ $(curl -fsS --max-time 2 "http://127.0.0.1:$port/health" 2>/dev/null) == ok ]]; then
            return 0
        fi
        sleep 1
    done
    return 1
}

# A copy of the database before anything changes it (updates run database migrations).
snapshot_database() {
    local label="$1" dir="$APERTURE_HOME/backups/before-update" file
    [[ -f $APERTURE_HOME/data/aperture.db ]] || return 0
    mkdir -p "$dir"
    file="$dir/aperture-$(date +%Y%m%d-%H%M%S)-$label.db"
    sqlite3 "$APERTURE_HOME/data/aperture.db" ".backup '$file'" || return 1
    ls -1t "$dir"/aperture-*.db 2>/dev/null | tail -n +6 | while IFS= read -r old; do rm -f "$old"; done
    printf '%s' "$file"
}

restore_database() {
    local file="$1"
    cp "$file" "$APERTURE_HOME/data/aperture.db"
    rm -f "$APERTURE_HOME/data/aperture.db-wal" "$APERTURE_HOME/data/aperture.db-shm"
}

remove_old_releases() {
    local current keep=0 dir
    current="$(current_release)"
    ls -1dt "$APERTURE_HOME"/releases/aperture-* 2>/dev/null | while IFS= read -r dir; do
        if [[ $dir == "$current" ]]; then continue; fi
        keep=$((keep + 1))
        if [[ $keep -ge $KEEP_RELEASES ]]; then rm -rf "$dir"; fi
    done
}

# The log is reopened when Aperture restarts, so updates are a good moment to start a new one.
rotate_log() {
    local log="$APERTURE_HOME/logs/aperture.log"
    [[ -f $log ]] || return 0
    mv -f "$log.2" "$log.3" 2>/dev/null || true
    mv -f "$log.1" "$log.2" 2>/dev/null || true
    mv -f "$log" "$log.1"
}
