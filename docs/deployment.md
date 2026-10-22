# Installing Aperture

Aperture runs on the photographer's own Mac with Apple silicon, usually a MacBook, and is used in the browser there.
An installer package sets it up for the person logged in, with no admin rights or tokens. Aperture then runs while
they're logged in, at `http://localhost:5090`, reachable from that Mac only. You, whoever looks after Aperture, publish
the releases it installs: a public feed that you fill by hand, so the Mac only gets versions you've tried.

Everything is in `~/Aperture`:

```
~/Aperture/app          the current version (a link into releases/)
~/Aperture/releases/    the last three versions
~/Aperture/data/        database, uploaded files, login, backup status   ← this is the business data
~/Aperture/backups/     the daily backup, and database copies taken before each update
~/Aperture/logs/        aperture.log, backup.log, update.log
~/Aperture/backup.conf  where backups go, one folder per line
~/Library/LaunchAgents/ch.aperture.*.plist   Aperture, the updater and the backup, as agents of the user
```

## Setting it up, once (you)

1. **Put the code on GitHub** as a public repository and push `main`:
   ```bash
   gh repo create aperture --public --source . --push
   ```
   Every push to `main` runs the tests and publishes a **pre-release** with the package and its `latest.json`, which
   names the version, the download and its SHA-256 (`.github/workflows/release.yml`). Macs never see pre-releases.
   No token or secret is needed: the workflows use the repository's own.
2. **Promote a release:** *Actions → Promote → Run workflow* (`.github/workflows/promote.yml`). Leave the version empty
   for the newest release, or enter one of the last five. It becomes the repository's *latest* release, which is what
   the Macs read. The feed address is always the same:
   `https://github.com/<you>/aperture/releases/latest/download/latest.json`.
3. **Build the installer** on your Mac:
   ```bash
   deploy/macos/pkg/build-pkg.sh https://github.com/<you>/aperture/releases/latest/download/latest.json
   # → artifacts/Aperture-Installer.pkg
   ```
   The package contains no program, only a script that downloads the latest (promoted) release. So it never goes
   stale. Build it again only if the feed address changes. Send it by AirDrop or mail, or attach it to a release.

## Installing (on the laptop)

1. **Open `Aperture-Installer.pkg`.** It isn't signed, so macOS refuses the first time ("can't be opened"). Click
   *Done*, then go to *System Settings → Privacy & Security*, scroll down and click *Open Anyway* next to the
   message about the package. This is needed once. Then *Continue → Install*; the Mac password it asks for is the
   Installer's usual one.
2. The installer downloads the newest release, checks it against the feed, installs it into `~/Aperture`
   (`deploy/macos/install-laptop.sh`) and opens `http://localhost:5090`.
3. **Create the login** on the page that opens: a username and password for Aperture. They're kept in
   `~/Aperture/data/keys/login.json`, hashed. Then fill in *Settings*.

## Calendar and notifications

- **Calendar:** every shoot has *Add to calendar*: it downloads the shoot as a file that opens in Calendar. In an
  iCloud calendar it's on the iPhone too. If the date or time changes later, change it there as well.
- **Notifications:** *Settings → Notifications → Turn on notifications*, then *Allow*. They appear on the Mac for
  overdue invoices, deliveries due within a week or overdue, inquiries waiting a week for an answer, prep still open
  three days before a shoot, backup problems and new versions: each once, between 08:00 and 21:00, and only while the
  Mac is awake, since Aperture sends them itself. Needs your email under *Settings → Contact*. *Send a test* checks
  that it works.

## Updates

Every push to `main` on GitHub runs the tests and publishes a pre-release (`.github/workflows/release.yml`). The Mac
doesn't see it until you promote it (*Setting it up*, step 2). It then
finds it within six hours (or right away with *Settings → Updates → Check for updates*). The Dashboard says so, and
*Install update* installs it with no password: everything belongs to the user. Aperture stops, saves a copy of the
database in `~/Aperture/backups/before-update/`, starts the new version and checks it. If the new version doesn't come
up within a minute, the previous version and database come back by themselves; the details are in
`~/Aperture/logs/update.log`. Only promote versions you've used for a while yourself.

Versions look like `2026.10.20-58-ed1231e`: date, build number (the number of commits, so it only grows) and commit.
Aperture compares the build number to tell whether a release is newer.

## Backups

- **Daily, automatically:** `backup.sh` runs at login and at 13:00 (when the Mac wakes up, if it was asleep). It writes
  to `~/Aperture/backups/Aperture Backup`: a copy of the database per day (kept for 90 days, the one from the 1st of
  each month forever; Swiss law requires business records to be kept for 10 years), all uploaded files and the login.
  Unlike the live database, these copies are always consistent. That copy is on the same disk: it helps after a
  mistake or a damaged database, not after losing the laptop. **Time Machine** (or any backup of the home folder)
  takes it along.
- **On an external drive too:** add its path to `~/Aperture/backup.conf`, e.g. `/Volumes/Backup`. A drive that isn't
  connected is skipped and reported. macOS needs Full Disk Access for `/bin/bash` for this (*System Settings →
  Privacy & Security → Full Disk Access*). Format the drive as *APFS (Encrypted)*: it holds all the business data.
- **By hand:** *Settings → Backups → Download a backup* downloads everything as one ZIP: the database and all uploaded
  files, with a note on how to restore it. For a copy somewhere else, or to move to a new Mac.
- **Check:** *Settings → Backups* shows the last backup and any problems. The Dashboard warns when it's more than 36
  hours old.
- **Run one now:** `launchctl kickstart gui/$UID/ch.aperture.backup`; the output goes to `~/Aperture/logs/backup.log`.

### Restoring

From the daily backup (pick the day in `database/`), or from a downloaded ZIP: double-click it in Downloads, then use
its folder, `~/Downloads/Aperture backup <date>`, which has `aperture.db` and `attachments/`. On a new Mac, install
Aperture and create a login first.

```bash
launchctl bootout gui/$UID/ch.aperture.web
cp ~/Aperture/backups/"Aperture Backup"/database/aperture-2026-10-05.db ~/Aperture/data/aperture.db
rm -f ~/Aperture/data/aperture.db-wal ~/Aperture/data/aperture.db-shm
rsync -a ~/Aperture/backups/"Aperture Backup"/attachments/ ~/Aperture/data/attachments/
launchctl bootstrap gui/$UID ~/Library/LaunchAgents/ch.aperture.web.plist
```

## Security

**What's protected:**
- **Only that Mac can reach Aperture.** It listens on `localhost`, so nothing on the network, not even the home Wi-Fi,
  can connect to it. That's also why there's no phone app.
- **Every page and file needs the login,** except `/health`, which only answers `ok`. The login is kept, hashed, in
  `~/Aperture/data/keys/login.json`.
- **The setup page** (`/setup`) only exists while there's no login yet, and only answers requests from that Mac. The
  installer opens it right away. Until it's filled in, another user account on the same Mac could claim it first.
- **Files are served by number** (`/attachments/12`), never by path. Uploaded files get generated names. Only the web
  root (`wwwroot`) is served, so the scripts, settings and database can't be downloaded.
- **Scripts can't be run through Aperture.** The app has no page or endpoint that runs commands.
- **Aperture runs as the user, not as root,** with no sudo rule and no firewall exception. `~/Aperture/data` and
  `~/Aperture/backups` are readable only by that user.
- **Updates only install release packages** that Aperture downloaded from the feed into `~/Aperture/data/updates` and
  checked against the feed's SHA-256.
- **Notifications are end-to-end encrypted.** Aperture sends them through the browser's push service (Apple's for
  Safari), which only passes them on: it can't read the text. Aperture signs them with a key pair in
  `~/Aperture/data/keys/push-vapid.json`. If that file is lost, turn notifications on again.

**Things to know:**
- **The release packages are public.** A self-contained .NET build is easy to look into, so treat that as publishing
  the code. Settings, data and the login never leave the Mac.
- **Wrong passwords aren't locked out.** Each failed login waits one second, which slows guessing down but doesn't
  stop it. Use a long password.
- **Turn on FileVault** (*System Settings → Privacy & Security*): the data folder holds receipts, invoices and client
  details.

## Troubleshooting

| Problem | What to do |
|---|---|
| The installer failed | Its log: *Console → Log Reports → install.log*, search for `Aperture`. Usually the Mac was offline. |
| Aperture isn't reachable | `launchctl print gui/$UID/ch.aperture.web` (look for `state = running`) and `tail -50 ~/Aperture/logs/aperture.log`. |
| Forgot the password | `rm ~/Aperture/data/keys/login.json && launchctl kickstart -k gui/$UID/ch.aperture.web`, then open `http://localhost:5090` and create a new login. All data stays. |
| Installing again | Open the installer again: it installs the newest release and keeps all data. |
| Uninstalling | `~/Aperture/app/deploy/macos/uninstall-laptop.sh`. Removes the program and agents and keeps `~/Aperture/data` and the backups. |
| Restoring a backup | See [Restoring](#restoring). |

## Signing the installer (optional)

With an Apple Developer account (USD 99 a year), the *Open Anyway* step goes away. Sign it with your *Developer ID
Installer* certificate, then notarize it:

```bash
APERTURE_PKG_IDENTITY="Developer ID Installer: <Your Name> (<TEAMID>)" deploy/macos/pkg/build-pkg.sh <feed URL>
xcrun notarytool submit artifacts/Aperture-Installer.pkg --keychain-profile <profile> --wait
xcrun stapler staple artifacts/Aperture-Installer.pkg
```

Updates don't need this either way: Aperture downloads them itself, and macOS only checks files downloaded by
browsers and similar apps.
