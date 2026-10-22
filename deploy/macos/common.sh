#!/bin/bash
# Shared by install.sh and update.sh. Written for macOS's /bin/bash 3.2: no bash 4+ features.

APERTURE_HOME="${APERTURE_HOME:-$HOME/Aperture}"
WEB_LABEL="ch.aperture.web"
BACKUP_LABEL="ch.aperture.backup"
UPDATER_LABEL="ch.aperture.updater"
WEB_PLIST="/Library/LaunchDaemons/$WEB_LABEL.plist"
BACKUP_PLIST="/Library/LaunchDaemons/$BACKUP_LABEL.plist"
UPDATER_PLIST="/Library/LaunchDaemons/$UPDATER_LABEL.plist"
SUDOERS_FILE="/etc/sudoers.d/aperture"
FIREWALL="/usr/libexec/ApplicationFirewall/socketfilterfw"
KEEP_RELEASES=3

# Several passwords come up during setup; sudo's bare "Password:" is easy to mistake for one of the others.
export SUDO_PROMPT="Mac password for %u (for system settings): "

say() { printf '\n\033[1m%s\033[0m\n' "$*"; }
fail() { printf '\n\033[31mError: %s\033[0m\n' "$*" >&2; exit 1; }

ask_yes() {
    local answer
    read -r -p "$1 [Y/n] " answer
    [[ -z $answer || $answer == [Yy]* ]]
}

require_mac() {
    [[ $(uname -s) == Darwin ]] || fail "This script runs on the Mac mini (macOS)."
    [[ $(uname -m) == arm64 ]] || fail "This build is for Apple silicon (arm64)."
    [[ $EUID -ne 0 ]] || fail "Run it as your normal user, not with sudo. It asks for your password when needed."
}

# Unpacks a release tarball into $APERTURE_HOME/releases and prints the release directory.
unpack_release() {
    local tarball="$1" name
    [[ -f $tarball ]] || fail "Release not found: $tarball"
    name="$(tar -tzf "$tarball" | awk -F/ 'NR == 1 { print $1 }')"
    [[ $name == aperture-* ]] || fail "$tarball doesn't look like a Aperture release."
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

# The macOS firewall asks before an app may accept connections; a background service can't answer,
# so the executable is allowed explicitly (again after each update, as it's a new file).
allow_through_firewall() {
    local executable="$1"
    if sudo "$FIREWALL" --getglobalstate | grep -q "enabled"; then
        sudo "$FIREWALL" --add "$executable" >/dev/null
        sudo "$FIREWALL" --unblockapp "$executable" >/dev/null
    fi
}

start_web() {
    sudo launchctl bootstrap system "$WEB_PLIST" 2>/dev/null || sudo launchctl kickstart -k "system/$WEB_LABEL"
}

stop_web() {
    sudo launchctl bootout "system/$WEB_LABEL" 2>/dev/null || true
    # bootout returns before the process is gone; wait so the database is closed.
    local i
    for i in $(seq 1 20); do
        pgrep -f "$APERTURE_HOME/.*/Aperture.Web" >/dev/null || return 0
        sleep 0.5
    done
}

# install.sh writes the port to a file, so updates don't need sudo to read it from the service definition.
web_port() {
    if [[ -f $APERTURE_HOME/port ]]; then
        head -n 1 "$APERTURE_HOME/port"
    else
        sudo /usr/libexec/PlistBuddy -c "Print :EnvironmentVariables:ASPNETCORE_URLS" "$WEB_PLIST" 2>/dev/null | sed 's/.*://'
    fi
}

# Lets update.sh restart Aperture without a password, so updates from Settings can run unattended (and deploy.sh
# doesn't ask): exactly the commands it uses, nothing else. Checked with visudo before it's installed.
install_sudo_rules() {
    local file="$APERTURE_HOME/.sudoers"
    cat > "$file" <<RULES
# Aperture: lets $USER's update script restart the Aperture service without a password (installed by install.sh).
$USER ALL=(root) NOPASSWD: /bin/launchctl bootout system/$WEB_LABEL
$USER ALL=(root) NOPASSWD: /bin/launchctl bootstrap system $WEB_PLIST
$USER ALL=(root) NOPASSWD: /bin/launchctl kickstart -k system/$WEB_LABEL
$USER ALL=(root) NOPASSWD: $FIREWALL --getglobalstate
$USER ALL=(root) NOPASSWD: $FIREWALL --add $APERTURE_HOME/releases/*/Aperture.Web
$USER ALL=(root) NOPASSWD: $FIREWALL --unblockapp $APERTURE_HOME/releases/*/Aperture.Web
RULES
    if /usr/sbin/visudo -cf "$file" >/dev/null; then
        sudo install -m 440 -o root -g wheel "$file" "$SUDOERS_FILE"
        rm -f "$file"
    else
        rm -f "$file"
        echo "Couldn't set up the sudo rules (is there a space in $APERTURE_HOME?). Updates will ask for your password."
        return 1
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

# The log is reopened when the service restarts, so updates are a good moment to start a new one.
rotate_log() {
    local log="$APERTURE_HOME/logs/aperture.log"
    [[ -f $log ]] || return 0
    mv -f "$log.2" "$log.3" 2>/dev/null || true
    mv -f "$log.1" "$log.2" 2>/dev/null || true
    mv -f "$log" "$log.1"
}
