# Running Aperture on the Mac mini

Aperture runs on the Mac mini as a background service. It starts when the Mac boots, restarts if it stops, and doesn't need anyone logged in. You use it from the browser on any device in your home network, or anywhere through Tailscale. A backup goes to your backup drives every night.

To give Aperture to someone who runs it on their own laptop, without Tailscale or a GitHub token, see [On a laptop](#on-a-laptop-for-someone-else).

```
~/Aperture/app          the current version (a link into releases/)
~/Aperture/releases/    the last three versions
~/Aperture/data/        database, uploaded files, login keys, backup status   ← this is your business data
~/Aperture/backups/     database copies taken before each update
~/Aperture/logs/        aperture.log, backup.log
~/Aperture/backup.conf  the backup drives, one per line
```

## Before you start

- **Tailscale:** for HTTPS, enable *HTTPS Certificates* in the Tailscale admin console under DNS. MagicDNS has to be on.
- **Backup drives:** connect them, so the installer can list them.
- **Copying files to the Mac:** use SSH (*System Settings → General → Sharing → Remote Login*, or Tailscale SSH), AirDrop or a USB stick. SSH also lets you update with one command later.

## 1. Build the release (on the development machine)

```bash
deploy/publish.sh
# → artifacts/aperture-2026.10.06-b0d6009-osx-arm64.tar.gz
```

The build is self-contained: the Mac doesn't need .NET.

## 2. Install

**With SSH** (see *Before you start*), from the development machine:

```bash
deploy/deploy.sh amir@mac-mini
```

It builds the release, copies it to the Mac and runs the installer there; its questions appear in your terminal.
Later runs of the same command install updates (see 3).

**Without SSH**, copy the `.tar.gz` to the Mac (here: `~/Downloads`), then in Terminal on the Mac:

```bash
cd ~/Downloads
tar -xzf aperture-*-osx-arm64.tar.gz
./aperture-*/deploy/macos/install.sh aperture-*-osx-arm64.tar.gz
```

The installer walks through seven steps and asks before each change to the system:

1. **Files:** installs into `~/Aperture`. Existing data is kept, so it's safe to run again.
2. **Login:** your username and password for Aperture. They're separate from your Mac account.
3. **Service:** installs and starts `ch.aperture.web`. Your Mac password is needed here.
4. **Backup drives:** e.g. `/Volumes/Backup A` and `/Volumes/Backup B`.
5. **Availability:** the Mac never sleeps, and it starts again after a power failure.
6. **Tailscale:** `tailscale serve` makes Aperture available at `https://<mac>.<tailnet>.ts.net`.
7. **First backup:** runs one to check that the drives work.

Then open `http://<mac-name>.local:5090` from your home PC. The installer prints the exact address.

### Moving the data you already entered

Your current data lives in `src/Aperture.Web/data/` on the development machine. To bring it along:

1. **Stop the app on the development machine.** The database must not change while you copy it.
2. **Stop Aperture on the Mac:**
   ```bash
   sudo launchctl bootout system/ch.aperture.web
   ```
3. **Copy everything from `src/Aperture.Web/data/` into `~/Aperture/data/` on the Mac:** `aperture.db` and `attachments/`. Don't copy `keys/`; you'll simply log in once more.
4. **Start Aperture again:**
   ```bash
   sudo launchctl bootstrap system /Library/LaunchDaemons/ch.aperture.web.plist
   ```

Aperture updates the database to the newest version by itself when it starts.

## 3. Updates

### From the app (with GitHub)

Every push to `main` on GitHub runs the tests and publishes a release (`.github/workflows/release.yml`). Aperture checks
for new releases every six hours; the **Dashboard** then says *Aperture build … is available*, and
**Settings → Updates** shows what's new with an **Install update** button (and **Check for updates** to look now).

Installing downloads the release; the updater on the Mac (`ch.aperture.updater`) then runs the same update as below,
with the same rollback. Aperture restarts and the page reconnects; Settings shows the result. If it failed, the
reason is there too, and the details are in `~/Aperture/logs/update.log`.

Setting it up, once:

1. **Put the code on GitHub** as a private repository and push:
   ```bash
   git remote add origin git@github.com:<you>/aperture.git
   git push -u origin main
   ```
   The first push already builds a release (*Actions* tab, then *Releases*).
2. **Make a token** for the Mac: *GitHub → Settings → Developer settings → Fine-grained tokens → Generate new
   token*. Repository access: *Only select repositories* → your Aperture repository. Permissions: *Contents: Read-only*.
   Pick an expiry; when it runs out, Settings → Updates says so, and you run step 3 again with a new one.
3. **Give it to Aperture:** run `install.sh` (or `deploy/deploy.sh mac` for the first install). It asks for the
   repository (`<you>/aperture`) and the token, keeps all data and only updates the settings. The token is stored in
   the root-only service definition, like the password hash.

### From the development machine

With SSH set up, one command builds the new version, copies it and installs it:

```bash
deploy/deploy.sh amir@mac-mini
```

Without SSH, copy the new `.tar.gz` to the Mac and run:

```bash
~/Aperture/app/deploy/macos/update.sh ~/Downloads/aperture-<version>-osx-arm64.tar.gz
```

The update stops Aperture, copies the database into `~/Aperture/backups/before-update/` and starts the new version. If the new version doesn't come up within a minute, it goes back to the old version and the old database by itself. **Settings → Updates** shows the running version.

Versions look like `2026.10.20-58-ed1231e`: date, build number (the number of commits, so it only grows) and commit.
Aperture compares the build number to tell whether a release is newer.

## 4. Backups

- **When:** every night at 03:30. If the Mac was asleep, it runs when it wakes up.
- **Where:** every drive in `~/Aperture/backup.conf` that is connected, into the folder `Aperture Backup`:
  - `database/aperture-YYYY-MM-DD.db`: one copy per day. Daily copies are kept for 90 days, and the one from the 1st of each month is kept forever. Swiss law requires business records to be kept for 10 years.
  - `attachments/`: all receipts, payslips and invoices. Files are only ever added here, never deleted.
  - `keys/`: the login keys.
- **Check:** **Settings → Backups** shows the last backup and any problems. The **Dashboard** warns you when the last backup is more than 36 hours old or a drive was missing.
- **Run one now:**
  ```bash
  sudo launchctl kickstart system/ch.aperture.backup
  ```
  The output goes to `~/Aperture/logs/backup.log`.
- **Change drives:** edit `~/Aperture/backup.conf`, one path per line.

### Restoring

1. **Stop Aperture:**
   ```bash
   sudo launchctl bootout system/ch.aperture.web
   ```
2. **Copy the files back:**
   ```bash
   cp "/Volumes/Backup A/Aperture Backup/database/aperture-2026-10-05.db" ~/Aperture/data/aperture.db
   rm -f ~/Aperture/data/aperture.db-wal ~/Aperture/data/aperture.db-shm
   rsync -a "/Volumes/Backup A/Aperture Backup/attachments/" ~/Aperture/data/attachments/
   ```
3. **Start Aperture:**
   ```bash
   sudo launchctl bootstrap system /Library/LaunchDaemons/ch.aperture.web.plist
   ```

## Security

**What's protected:**
- **Every page and file needs the login,** except `/health`, which only answers `ok`, and the calendar feed,
  whose URL contains a 256-bit secret (Settings → Calendar on your phone; *New link* revokes the old one).
- **Files are served by number** (`/attachments/12`), never by path. Uploaded files get generated names.
  Only the web root (`wwwroot`) is served, so the scripts, settings and database can't be downloaded.
- **Scripts can't be run through Aperture.** The app has no page or endpoint that runs commands. Running
  `install.sh` or `update.sh` needs a shell on the Mac (at the Mac, SSH or Tailscale SSH) and your Mac password for `sudo`.
- **Aperture runs as your user, not as root.** The service definition with the password hash is readable only by root,
  and `~/Aperture/data` and `~/Aperture/backups` only by you.
- **HTTPS headers are trusted only from the Mac itself,** i.e. from `tailscale serve`.
- **Updates run without a password through a narrow sudo rule** (`/etc/sudoers.d/aperture`): your user may stop, start
  and restart the Aperture service and let Aperture releases through the firewall, nothing else.
- **Updates only install release packages** that Aperture downloaded from your repository into `~/Aperture/data/updates`.
  The GitHub token can only read that one repository.
- **Notifications are end-to-end encrypted.** Aperture sends them through the phone's push service (Apple's for an
  iPhone), which only passes them on: it can't read the text. Aperture signs them with a key pair in
  `~/Aperture/data/keys/push-vapid.json`; the push service only accepts notifications signed with it. Nothing has to
  reach the Mac from outside for this. If that file is lost, turn notifications on again on each device.

**Things to know:**
- **`http://<mac>.local:5090` is unencrypted.** Anyone on your home Wi-Fi could read the password and the login
  cookie. The Tailscale address (`https://….ts.net`) is encrypted. Prefer it, also at home.
- **Wrong passwords aren't locked out.** Each failed login waits one second, which slows guessing down but doesn't
  stop it. Use a long password.
- **Never use `tailscale funnel` or port forwarding on your router.** Both put Aperture on the public internet.
  `tailscale serve` is visible only to devices in your tailnet.
- **The backup drives hold all your business data.** Format them as *APFS (Encrypted)* in Disk Utility. The
  backup works the same. Let the Mac remember the password: it then unlocks the drives when you log in. After a
  restart, log in once (with FileVault on, you have to anyway) or the night's backup skips the locked drives.

## On your phone

The phone needs Tailscale switched on. Use the Tailscale address, `https://<mac>.<tailnet>.ts.net`:
the `.local` address only works at home, and the app features below need HTTPS.

- **Aperture as an app:** open the address in Safari, tap *Share → Add to Home Screen*. Aperture then opens full screen
  from its own icon. It needs a connection to the Mac; without one it shows a "No connection" page.
- **Shoots in the iPhone calendar:** in Aperture on the phone, *Settings → Calendar on your phone → Create calendar link*,
  copy it, then *iPhone Settings → Apps → Calendar → Calendar Accounts → Add Account → Other → Add Subscribed
  Calendar* and paste it. The link is the only key to the calendar (calendar apps can't sign in), so keep it to
  yourself; *New link* there replaces it if it got out. The feed is `/calendar/<secret>.ics`, the only page besides
  `/health` and the login that works without signing in.
- **Notifications:** in the Aperture app from the home screen (not in Safari itself), *Settings → Notifications → Turn on
  notifications*, then *Allow*. Needs iOS 16.4 or newer and your email under *Settings → Contact* (push services
  only take notifications from senders with a contact address). Aperture then tells you about overdue invoices, prep
  still open three days before a shoot, backup problems and new versions: each once, between 08:00 and 21:00.
  They arrive even while Tailscale is off on the phone; tapping one opens Aperture, which needs it. *Send a test*
  checks that it works. Turn them off there too, or under *iPhone Settings → Notifications → Aperture*.
  Other devices with notifications are listed below the buttons and can be removed.

## Troubleshooting

| Problem | What to do |
|---|---|
| Aperture isn't reachable | Check `sudo launchctl print system/ch.aperture.web` (look for `state = running`) and `tail -50 ~/Aperture/logs/aperture.log`. |
| Reachable on the Mac, not from the PC | Check the macOS firewall: the installer allows Aperture, but check *System Settings → Network → Firewall*. Try the IP instead of `.local`. |
| `backup.log` says *Operation not permitted* | macOS is blocking access to the drives. Add `/bin/bash` under *System Settings → Privacy & Security → Full Disk Access*. |
| Nothing works after a restart | With FileVault on, macOS only starts services after someone logs in once. For planned restarts, `sudo fdesetup authrestart` avoids this. |
| Tailscale URL doesn't work | Check `tailscale serve status`, and that HTTPS certificates are enabled for the tailnet. |
| Forgot the password | Run `install.sh` again with the current release. It keeps all data and only replaces the login. |

### Uninstalling (data stays in `~/Aperture/data`)

```bash
sudo launchctl bootout system/ch.aperture.web; sudo launchctl bootout system/ch.aperture.backup
sudo rm /Library/LaunchDaemons/ch.aperture.web.plist /Library/LaunchDaemons/ch.aperture.backup.plist
tailscale serve --https=443 off
```

## On a laptop (for someone else)

For someone who runs Aperture on their own Mac with Apple silicon, e.g. a MacBook. They get an installer package and
need no Tailscale, no GitHub token and no admin rights beyond clicking *Install*. Aperture then runs while they're
logged in, at `http://localhost:5090`, reachable from that Mac only. Updates come from a public feed that you fill by
hand, so they only get versions you've tried.

```
~/Aperture/app, releases/, data/, logs/      as on the Mac mini
~/Aperture/backups/Aperture Backup/          the daily backup (see below)
~/Library/LaunchAgents/ch.aperture.*.plist   Aperture, the updater and the backup, as agents of their user
```

### Setting it up, once (you)

1. **Make a public repository for releases**, e.g. `<you>/aperture-releases`, with a README (a release needs a
   commit to point at). It holds only release packages, never code. Their release notes are the commit subjects, so
   those become public too.
2. **Give the workflow access:** *GitHub → Settings → Developer settings → Fine-grained tokens → Generate new token*.
   Repository access: only `aperture-releases`. Permissions: *Contents: Read and write*. Then in the private repository
   under *Settings → Secrets and variables → Actions*: secret `RELEASES_TOKEN` (the token) and variable
   `RELEASES_REPOSITORY` (`<you>/aperture-releases`). The token stays in GitHub Actions. The laptop gets none.
3. **Promote a release:** *Actions → Promote → Run workflow*. Leave the version empty for the newest release, or enter
   one of the last five (`.github/workflows/promote.yml`). It copies the package to the public repository with
   `latest.json`, which names the version, the download and its SHA-256. The feed address is then
   `https://github.com/<you>/aperture-releases/releases/latest/download/latest.json`.
4. **Build the installer** on your Mac:
   ```bash
   deploy/macos/pkg/build-pkg.sh https://github.com/<you>/aperture-releases/releases/latest/download/latest.json
   # → artifacts/Aperture-Installer.pkg
   ```
   The package contains no program, only a script that downloads the newest promoted release. So it never goes stale.
   Build it again only if the feed address changes. Send it by AirDrop or mail, or attach it to a release in the
   public repository.

### Installing (on the laptop)

1. **Open `Aperture-Installer.pkg`.** It isn't signed, so macOS refuses the first time ("can't be opened"). Click
   *Done*, then go to *System Settings → Privacy & Security*, scroll down and click *Open Anyway* next to the
   message about the package. This is needed once. Then *Continue → Install*; the Mac password it asks for is the
   Installer's usual one.
2. The installer downloads the newest release, checks it against the feed, installs it into `~/Aperture`
   (`deploy/macos/install-laptop.sh`) and opens `http://localhost:5090`.
3. **Create the login** on the page that opens: a username and password for Aperture. They're kept in
   `~/Aperture/data/keys/login.json`, hashed. Then fill in *Settings*.

### Updates

After you promote a release, the laptop finds it within six hours (or right away with *Settings → Updates → Check for
updates*). The Dashboard says so, and *Install update* runs the same update and rollback as on the Mac mini, with no
password: everything belongs to the user. Only promote versions that ran well on the Mac mini for a while.

### Backups

`backup.sh` runs at login and daily at 13:00 (when the Mac wakes up, if it was asleep). It writes to
`~/Aperture/backups/Aperture Backup`, in the same layout as on the backup drives. That copy is on the same disk: it
helps after a mistake or a damaged database, not after losing the laptop. **Time Machine** (or any backup of the home
folder) takes it along. Unlike the live database, these copies are always consistent. For a copy on an external drive,
add its path to `~/Aperture/backup.conf`; it needs Full Disk Access for `/bin/bash`, as on the Mac mini.

### What's different from the Mac mini

- **Only on that Mac.** No phone app, iPhone calendar or notifications: no phone can reach it. Settings hides them,
  and the calendar feed isn't served.
- **Runs while the user is logged in,** not from boot. Closing the lid pauses it, as with any app.
- **No sudo, no firewall rule, no service definition with secrets.** The login is in the data folder instead.
- **The release packages are public.** A self-contained .NET build is easy to look into, so treat that as publishing
  the code. Settings, data and the login never leave the laptop.
- **The setup page** (`/setup`) only exists while there's no login yet, and only answers requests from that Mac. The
  installer opens it right away. Until it's filled in, another user account on the same Mac could claim it first.

### Troubleshooting

| Problem | What to do |
|---|---|
| The installer failed | Its log: *Console → Log Reports → install.log*, search for `Aperture`. Usually the Mac was offline. |
| Aperture isn't reachable | `launchctl print gui/$UID/ch.aperture.web` (look for `state = running`) and `tail -50 ~/Aperture/logs/aperture.log`. |
| Forgot the password | `rm ~/Aperture/data/keys/login.json && launchctl kickstart -k gui/$UID/ch.aperture.web`, then open `http://localhost:5090` and create a new login. All data stays. |
| Installing again | Open the installer again: it installs the newest release and keeps all data. |
| Uninstalling | `~/Aperture/app/deploy/macos/uninstall-laptop.sh`. Removes the program and agents and keeps `~/Aperture/data` and the backups. |

### Signing the installer (optional)

With an Apple Developer account (USD 99 a year), the *Open Anyway* step goes away. Sign it with your *Developer ID
Installer* certificate, then notarize it:

```bash
APERTURE_PKG_IDENTITY="Developer ID Installer: <Your Name> (<TEAMID>)" deploy/macos/pkg/build-pkg.sh <feed URL>
xcrun notarytool submit artifacts/Aperture-Installer.pkg --keychain-profile <profile> --wait
xcrun stapler staple artifacts/Aperture-Installer.pkg
```

Updates don't need this either way: Aperture downloads them itself, and macOS only checks files downloaded by
browsers and similar apps.
