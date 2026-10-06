# Running Funk on the Mac mini

Funk runs on the Mac mini as a background service. It starts when the Mac boots, restarts if it stops, and doesn't need anyone logged in. You use it from the browser on any device in your home network, or anywhere through Tailscale. A backup goes to your backup drives every night.

```
~/Funk/app          the current version (a link into releases/)
~/Funk/releases/    the last three versions
~/Funk/data/        database, uploaded files, login keys, backup status   ← this is your business data
~/Funk/backups/     database copies taken before each update
~/Funk/logs/        funk.log, backup.log
~/Funk/backup.conf  the backup drives, one per line
```

## Before you start

- **Tailscale:** for HTTPS, enable *HTTPS Certificates* in the Tailscale admin console under DNS. MagicDNS has to be on.
- **Backup drives:** connect them, so the installer can list them.
- **Copying files to the Mac:** use SSH (*System Settings → General → Sharing → Remote Login*, or Tailscale SSH), AirDrop or a USB stick. SSH also lets you update with one command later.

## 1. Build the release (on the development machine)

```bash
deploy/publish.sh
# → artifacts/funk-2026.10.06-b0d6009-osx-arm64.tar.gz
```

The build is self-contained: the Mac doesn't need .NET.

## 2. Install (on the Mac mini)

Copy the `.tar.gz` to the Mac (here: `~/Downloads`), then in Terminal:

```bash
cd ~/Downloads
tar -xzf funk-*-osx-arm64.tar.gz
./funk-*/deploy/macos/install.sh funk-*-osx-arm64.tar.gz
```

The installer walks through seven steps and asks before each change to the system:

1. **Files:** installs into `~/Funk`. Existing data is kept, so it's safe to run again.
2. **Login:** your username and password for Funk. They're separate from your Mac account.
3. **Service:** installs and starts `ch.funk.web`. Your Mac password is needed here.
4. **Backup drives:** e.g. `/Volumes/Backup A` and `/Volumes/Backup B`.
5. **Availability:** the Mac never sleeps, and it starts again after a power failure.
6. **Tailscale:** `tailscale serve` makes Funk available at `https://<mac>.<tailnet>.ts.net`.
7. **First backup:** runs one to check that the drives work.

Then open `http://<mac-name>.local:5080` from your home PC. The installer prints the exact address.

### Moving the data you already entered

Your current data lives in `src/Funk.Web/data/` on the development machine. To bring it along:

1. **Stop the app on the development machine.** The database must not change while you copy it.
2. **Stop Funk on the Mac:**
   ```bash
   sudo launchctl bootout system/ch.funk.web
   ```
3. **Copy everything from `src/Funk.Web/data/` into `~/Funk/data/` on the Mac:** `funk.db` and `attachments/`. Don't copy `keys/`; you'll simply log in once more.
4. **Start Funk again:**
   ```bash
   sudo launchctl bootstrap system /Library/LaunchDaemons/ch.funk.web.plist
   ```

Funk updates the database to the newest version by itself when it starts.

## 3. Updates

With SSH set up, one command builds the new version, copies it and installs it:

```bash
deploy/deploy.sh amir@mac-mini
```

Without SSH, copy the new `.tar.gz` to the Mac and run:

```bash
~/Funk/app/deploy/macos/update.sh ~/Downloads/funk-<version>-osx-arm64.tar.gz
```

The update stops Funk, copies the database into `~/Funk/backups/before-update/` and starts the new version. If the new version doesn't come up within a minute, it goes back to the old version and the old database by itself. **Settings** shows the running version at the bottom.

## 4. Backups

- **When:** every night at 03:30. If the Mac was asleep, it runs when it wakes up.
- **Where:** every drive in `~/Funk/backup.conf` that is connected, into the folder `Funk Backup`:
  - `database/funk-YYYY-MM-DD.db`: one copy per day. Daily copies are kept for 90 days, and the one from the 1st of each month is kept forever. Swiss law requires business records to be kept for 10 years.
  - `attachments/`: all receipts, payslips and invoices. Files are only ever added here, never deleted.
  - `keys/`: the login keys.
- **Check:** **Settings → Backups** shows the last backup and any problems. The **Dashboard** warns you when the last backup is more than 36 hours old or a drive was missing.
- **Run one now:**
  ```bash
  sudo launchctl kickstart system/ch.funk.backup
  ```
  The output goes to `~/Funk/logs/backup.log`.
- **Change drives:** edit `~/Funk/backup.conf`, one path per line.

### Restoring

1. **Stop Funk:**
   ```bash
   sudo launchctl bootout system/ch.funk.web
   ```
2. **Copy the files back:**
   ```bash
   cp "/Volumes/Backup A/Funk Backup/database/funk-2026-10-05.db" ~/Funk/data/funk.db
   rm -f ~/Funk/data/funk.db-wal ~/Funk/data/funk.db-shm
   rsync -a "/Volumes/Backup A/Funk Backup/attachments/" ~/Funk/data/attachments/
   ```
3. **Start Funk:**
   ```bash
   sudo launchctl bootstrap system /Library/LaunchDaemons/ch.funk.web.plist
   ```

## Security

**What's protected:**
- **Every page and file needs the login,** except `/health`, which only answers `ok`.
- **Files are served by number** (`/attachments/12`), never by path. Uploaded files get generated names.
  Only the web root (`wwwroot`) is served, so the scripts, settings and database can't be downloaded.
- **Scripts can't be run through Funk.** The app has no page or endpoint that runs commands. Running
  `install.sh` or `update.sh` needs a shell on the Mac (at the Mac, SSH or Tailscale SSH) and your Mac password for `sudo`.
- **Funk runs as your user, not as root.** The service definition with the password hash is readable only by root,
  and `~/Funk/data` and `~/Funk/backups` only by you.
- **HTTPS headers are trusted only from the Mac itself,** i.e. from `tailscale serve`.

**Things to know:**
- **`http://<mac>.local:5080` is unencrypted.** Anyone on your home Wi-Fi could read the password and the login
  cookie. The Tailscale address (`https://….ts.net`) is encrypted. Prefer it, also at home.
- **Wrong passwords aren't locked out.** Each failed login waits one second, which slows guessing down but doesn't
  stop it. Use a long password.
- **Never use `tailscale funnel` or port forwarding on your router.** Both put Funk on the public internet.
  `tailscale serve` is visible only to devices in your tailnet.
- **The backup drives hold all your business data.** Format them as *APFS (Encrypted)* in Disk Utility. The
  backup works the same. Let the Mac remember the password: it then unlocks the drives when you log in. After a
  restart, log in once (with FileVault on, you have to anyway) or the night's backup skips the locked drives.

## Troubleshooting

| Problem | What to do |
|---|---|
| Funk isn't reachable | Check `sudo launchctl print system/ch.funk.web` (look for `state = running`) and `tail -50 ~/Funk/logs/funk.log`. |
| Reachable on the Mac, not from the PC | Check the macOS firewall: the installer allows Funk, but check *System Settings → Network → Firewall*. Try the IP instead of `.local`. |
| `backup.log` says *Operation not permitted* | macOS is blocking access to the drives. Add `/bin/bash` under *System Settings → Privacy & Security → Full Disk Access*. |
| Nothing works after a restart | With FileVault on, macOS only starts services after someone logs in once. For planned restarts, `sudo fdesetup authrestart` avoids this. |
| Tailscale URL doesn't work | Check `tailscale serve status`, and that HTTPS certificates are enabled for the tailnet. |
| Forgot the password | Run `install.sh` again with the current release. It keeps all data and only replaces the login. |

### Uninstalling (data stays in `~/Funk/data`)

```bash
sudo launchctl bootout system/ch.funk.web; sudo launchctl bootout system/ch.funk.backup
sudo rm /Library/LaunchDaemons/ch.funk.web.plist /Library/LaunchDaemons/ch.funk.backup.plist
tailscale serve --https=443 off
```
