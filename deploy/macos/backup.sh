#!/bin/bash
# Nightly backup of Funk's data to every connected backup drive (run by launchd, see ch.funk.backup.plist).
#
# For each drive listed in ~/Funk/backup.conf that is connected, writes to "<drive>/Funk Backup":
#   database/funk-YYYY-MM-DD.db   a consistent copy of the database (taken once, checked, then copied)
#   attachments/                  all uploaded files (receipts, payslips, invoices); added, never deleted
#   keys/                         login cookie keys
# Database copies older than 90 days are removed, except those from the 1st of a month (kept forever).
# The result goes to ~/Funk/data/backup-status.json, which the app shows in Settings and on the Dashboard.
#
# Run by hand:  bash ~/Funk/app/deploy/macos/backup.sh
set -uo pipefail

FUNK_HOME="${FUNK_HOME:-$HOME/Funk}"
DATA="$FUNK_HOME/data"
CONF="$FUNK_HOME/backup.conf"
STATUS="$DATA/backup-status.json"
KEEP_DAYS="${FUNK_BACKUP_KEEP_DAYS:-90}"
SQLITE3="${SQLITE3:-sqlite3}"   # /usr/bin/sqlite3 ships with macOS

run_started="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
today="$(date +%Y-%m-%d)"
destinations=()
problems=()

log() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*"; }

json_string() {
    local s="${1//\\/\\\\}"
    s="${s//\"/\\\"}"
    printf '"%s"' "$s"
}

json_array() {
    local first=1 item
    printf '['
    for item in "$@"; do
        [[ $first -eq 1 ]] || printf ','
        json_string "$item"
        first=0
    done
    printf ']'
}

# Keeps the last successful time when this run fails, so the app can say how old the newest backup is.
previous_success() {
    [[ -f $STATUS ]] && sed -n 's/.*"lastSuccess":"\([^"]*\)".*/\1/p' "$STATUS"
}

write_status() {
    local last_success="$1" tmp
    tmp="$(mktemp "$DATA/.backup-status.XXXXXX")" || return
    {
        printf '{"lastRun":%s,' "$(json_string "$run_started")"
        if [[ -n $last_success ]]; then printf '"lastSuccess":%s,' "$(json_string "$last_success")"; else printf '"lastSuccess":null,'; fi
        printf '"destinations":%s,' "$(json_array ${destinations[@]+"${destinations[@]}"})"
        printf '"problems":%s}\n' "$(json_array ${problems[@]+"${problems[@]}"})"
    } > "$tmp" && mv "$tmp" "$STATUS"
}

finish() {
    if [[ ${#destinations[@]} -gt 0 ]]; then
        write_status "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
        log "Done: ${#destinations[@]} drive(s), ${#problems[@]} problem(s)"
        exit 0
    fi
    write_status "$(previous_success)"
    log "FAILED: no backup written"
    exit 1
}

log "Backup started"

if [[ ! -f $DATA/funk.db ]]; then
    problems+=("No database found in $DATA")
    finish
fi

if [[ ! -f $CONF ]] || ! grep -qv '^[[:space:]]*\(#.*\)\{0,1\}$' "$CONF"; then
    problems+=("No backup drives configured in $CONF")
    finish
fi

# One consistent snapshot (safe while the app is running), checked before it's copied anywhere.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
snapshot="$work/funk.db"
if ! "$SQLITE3" "$DATA/funk.db" ".backup '$snapshot'"; then
    problems+=("Could not copy the database")
    finish
fi
integrity="$("$SQLITE3" "$snapshot" "PRAGMA integrity_check;")"
if [[ $integrity != "ok" ]]; then
    problems+=("Database integrity check failed: $integrity")
    finish
fi

while IFS= read -r line || [[ -n $line ]]; do
    drive="${line%%#*}"
    drive="${drive#"${drive%%[![:space:]]*}"}"
    drive="${drive%"${drive##*[![:space:]]}"}"
    [[ -z $drive ]] && continue

    if [[ ! -d $drive ]]; then
        problems+=("Not connected: $drive")
        log "Skipped (not connected): $drive"
        continue
    fi

    target="$drive/Funk Backup"
    if mkdir -p "$target/database" "$target/attachments" "$target/keys" \
        && cp "$snapshot" "$target/database/funk-$today.db" \
        && { [[ ! -d $DATA/attachments ]] || rsync -a "$DATA/attachments/" "$target/attachments/"; } \
        && { [[ ! -d $DATA/keys ]] || rsync -a "$DATA/keys/" "$target/keys/"; }; then
        find "$target/database" -name 'funk-*.db' ! -name 'funk-*-*-01.db' -mtime "+$KEEP_DAYS" -delete
        destinations+=("$target")
        log "Backed up to $target"
    else
        problems+=("Backup failed: $target")
        log "FAILED: $target"
    fi
done < "$CONF"

finish
