# Installing Aperture

Aperture runs on the photographer's own computer and is used in the browser there, at `http://localhost:5090`,
reachable from that computer only. It runs while they're logged in, backs up daily and updates itself. You, whoever
looks after Aperture, publish the releases it installs: a feed that you fill by hand, so computers only get versions
you've tried.

| | macOS | Windows | Linux |
|---|---|---|---|
| Needs | Apple silicon, macOS 15 or newer | 64-bit Windows 10 (1809) or 11 | 64-bit (x64), systemd |
| Installer | `Aperture-Installer.pkg` | `Aperture-Setup.exe` | the package, `aperture-…-linux-x64.tar.gz`, with `install.sh` |
| Installs into | `~/Aperture` | `%LOCALAPPDATA%\Aperture` | `~/.local/share/aperture` |
| Runs as | launchd agents (`~/Library/LaunchAgents/ch.aperture.*`) | scheduled tasks (*Aperture*, *Aperture Backup*, *Aperture Updater*) | systemd user units (`~/.config/systemd/user/aperture*`) |

None of them needs administrator rights beyond what the system's installer asks for.

The install folder holds:

```
app                 the current version (a link into releases)
releases/           the last three versions
data/               database, uploaded files, login, backup status   ← this is the business data
backups/            the daily backup, and database copies taken before each update and restore
logs/               aperture.log, backup.log, update.log (and on Windows install.log, uninstall.log)
aperture.json       port, data folder and update feed
backup.conf         where backups go, one folder per line
```

The program installs, updates and backs itself up: `Aperture.Web install`, `update`, `backup`, `restore` and
`uninstall` (`src/Aperture.Web/Installation`). The installers only unpack a release and start `install`.

## Setting it up, once (you)

1. **Put the code on GitHub** as a public repository and push `main`:
   ```bash
   gh repo create aperture --public --source . --push
   ```
   Every push to `main` runs the tests and publishes a **pre-release** with a package per system and `latest.json`,
   which names the version and, per system, the download and its SHA-256 (`.github/workflows/release.yml`). Two more
   jobs build the installers around the packages, on a macOS and a Windows runner, and attach them. Installed copies
   never see pre-releases. No token or secret is needed: the workflows use the repository's own.
2. **Promote a release:** *Actions → Promote → Run workflow* (`.github/workflows/promote.yml`). Leave the version empty
   for the newest release, or enter one of the last five; it needs both installers, so wait for its Release run to
   finish. It becomes the repository's *latest* release, which is what installed copies read. These addresses always
   stay the same:
   - macOS: `https://github.com/<you>/aperture/releases/latest/download/Aperture-Installer.pkg`
   - Windows: `https://github.com/<you>/aperture/releases/latest/download/Aperture-Setup.exe`
   - Linux: the release page, `https://github.com/<you>/aperture/releases/latest`
   - the feed for updates: `https://github.com/<you>/aperture/releases/latest/download/latest.json`
3. **Send the download link** to whoever installs Aperture, or put it on a website. It always gets the latest promoted
   version, so there's nothing to rebuild.

## Installing

### macOS

1. **Download `Aperture-Installer.pkg`** and double-click it in Downloads.
2. **Unsigned, macOS refuses it the first time** ("Apple could not verify…"). Click *Done*, then go to *System
   Settings → Privacy & Security*, scroll down and click *Open Anyway* next to the message about the package. This is
   needed once, and not at all once the installer is signed (see *Signing the installers*).
3. **Click through the installer:** welcome, license, *Install*. The Mac password it asks for is the Installer's usual
   one. It refuses Macs without Apple silicon or older than macOS 15, which .NET 10 needs.
4. It installs the release it carries (`deploy/macos/install.sh`, then `Aperture.Web install`) and opens
   `http://localhost:5090`.

To give Aperture its own window and a place in the Dock: *File → Add to Dock* in Safari.

### Windows

1. **Download `Aperture-Setup.exe`** and double-click it.
2. **Unsigned, Windows SmartScreen warns the first time** ("Windows protected your PC"). Click *More info*, then *Run
   anyway*. This goes away once the setup is signed (see *Signing the installers*).
3. **Click through the setup:** welcome, license, *Install*. It needs no administrator rights.
4. It installs into `%LOCALAPPDATA%\Aperture`, runs `Aperture.Web install`, adds *Aperture* to the Start menu and opens
   `http://localhost:5090`.

To give Aperture its own window: in Edge or Chrome, *⋯ → Apps → Install this site as an app*. To remove Aperture:
*Settings → Apps → Installed apps → Aperture → Uninstall*; data and backups stay.

### Linux

```bash
tar -xzf aperture-…-linux-x64.tar.gz
./aperture-…/install.sh
```

It copies the release into `~/.local/share/aperture`, sets up systemd user units, adds *Aperture* to the
applications menu and opens `http://localhost:5090`. It needs systemd's user manager, which every desktop session
has. Aperture runs while you're logged in; for it to run without a login session too, enable lingering once:
`loginctl enable-linger`.

### Then, everywhere

**Create the login** on the page that opens: a username and password for Aperture, kept hashed in
`data/keys/login.json`. Then fill in *Settings*: name, address and IBAN appear on invoices; the email is needed for
notifications.

## Calendar and notifications

- **Calendar:** every shoot has *Add to calendar*: it downloads the shoot as a file that opens in the calendar app
  (Calendar on a Mac, Outlook on Windows, …). With a synced calendar (iCloud, Google, Outlook) it's on the phone too.
  If the date or time changes later, change it there as well.
- **Notifications:** *Settings → Notifications → Turn on notifications*, then *Allow*. They appear on that computer
  for overdue invoices, deliveries due within a week or overdue, inquiries waiting a week for an answer, prep still
  open three days before a shoot, backup problems and new versions: each once, between 08:00 and 21:00, and only while
  the computer is on, since Aperture sends them itself. Needs your email under *Settings → Contact*. *Send a test*
  checks that it works.

## Updates

After you promote a release, installed copies find it within six hours (or right away with *Settings → Updates →
Check for updates*). The Dashboard says so, and *Install update* installs it with no password. Aperture downloads the
package for its system and checks it against the feed's SHA-256; the updater (`Aperture.Web update`, started by
launchd, systemd or the *Aperture Updater* task) then stops Aperture, saves a copy of the database in
`backups/before-update`, switches `app` to the new release and checks it. If the new version doesn't come up within a
minute, the previous version and database come back by themselves; the details are in `logs/update.log`. Only
promote versions you've used for a while yourself.

Installing a newer installer over an existing installation does the same, and keeps all data.

Versions look like `2026.10.20-58-ed1231e`: date, build number (the number of commits, so it only grows) and commit.
Aperture compares the build number to tell whether a release is newer.

## Backups

- **Daily, automatically:** `Aperture.Web backup` runs after login and at 13:00 (or when the computer is next on). It
  writes to `backups/Aperture Backup` in the install folder: a copy of the database per day (kept for 90 days, the
  one from the 1st of each month forever; Swiss law requires business records to be kept for 10 years), all uploaded
  files and the login. Unlike the live database, these copies are always consistent. That copy is on the same disk:
  it helps after a mistake or a damaged database, not after losing the computer. **Time Machine** on a Mac, **File
  History** or any backup of the user folder on Windows, and the usual home backup on Linux take it along.
- **On an external drive or a synced folder too:** add its path to `backup.conf`, e.g. `/Volumes/Backup`, `E:\` or a
  OneDrive folder. A folder that isn't there is skipped and reported. On a Mac, external drives need Full Disk Access
  for `/bin/bash` (*System Settings → Privacy & Security → Full Disk Access*); format the drive as *APFS (Encrypted)*.
- **By hand:** *Settings → Backups → Download a backup* downloads everything as one ZIP: the database and all uploaded
  files, with a note on how to restore it. For a copy somewhere else, or to move to a new computer, also from one
  system to another.
- **Check:** *Settings → Backups* shows the last backup and any problems. The Dashboard warns when it's more than 36
  hours old.

### Restoring

`Aperture.Web restore` takes a downloaded ZIP, or a daily copy from `Aperture Backup/database`. It keeps the current
database in `backups/before-restore`, puts the backup in place (with the files next to it) and restarts Aperture. On a
new computer, install Aperture and create a login first.

```bash
# macOS
~/Aperture/app/Aperture.Web restore ~/Downloads/"Aperture backup 2026-10-22.zip"
# Linux
~/.local/share/aperture/app/Aperture.Web restore ~/Downloads/"Aperture backup 2026-10-22.zip"
```

```powershell
# Windows (PowerShell)
& "$env:LOCALAPPDATA\Aperture\app\Aperture.Web.exe" restore "$env:USERPROFILE\Downloads\Aperture backup 2026-10-22.zip"
```

## Security

**What's protected:**
- **Only that computer can reach Aperture.** It listens on `localhost`, so nothing on the network, not even the home
  Wi-Fi, can connect to it. That's also why there's no phone app.
- **Every page and file needs the login,** except `/health`, which only answers `ok`. The login is kept, hashed, in
  `data/keys/login.json`.
- **The setup page** (`/setup`) only exists while there's no login yet, and only answers requests from that computer.
  The installer opens it right away. Until it's filled in, another user account on the same computer could claim it
  first.
- **Files are served by number** (`/attachments/12`), never by path. Uploaded files get generated names. Only the web
  root (`wwwroot`) is served, so the scripts, settings and database can't be downloaded.
- **Scripts can't be run through Aperture.** The app has no page or endpoint that runs commands.
- **Aperture runs as the user, not as an administrator,** with no system services. On macOS and Linux, `data` and
  `backups` are readable only by that user; on Windows, the user folder already is.
- **Updates only install release packages** for that system that Aperture downloaded from the feed into
  `data/updates` and checked against the feed's SHA-256.
- **Notifications are end-to-end encrypted.** Aperture sends them through the browser's push service (Apple's,
  Google's, Microsoft's, …), which only passes them on: it can't read the text. Aperture signs them with a key pair in
  `data/keys/push-vapid.json`. If that file is lost, turn notifications on again.

**Things to know:**
- **The release packages are public**, like the code.
- **Wrong passwords aren't locked out.** Each failed login waits one second, which slows guessing down but doesn't
  stop it. Use a long password.
- **Encrypt the disk** (FileVault on a Mac, BitLocker or Device Encryption on Windows, LUKS on Linux): the data folder
  holds receipts, invoices and client details.

## Troubleshooting

| Problem | What to do |
|---|---|
| The installer failed | macOS: *Console → Log Reports → install.log*, search for `Aperture`. Windows: `%LOCALAPPDATA%\Aperture\logs\install.log`. Linux: the terminal shows it. |
| Aperture isn't reachable | `logs/aperture.log` in the install folder. Is it running? macOS: `launchctl print gui/$UID/ch.aperture.web`; Windows: *Task Scheduler → Aperture*; Linux: `systemctl --user status aperture`. Installing again repairs the setup and keeps all data. |
| Forgot the password | Delete `data/keys/login.json`, restart Aperture (or log out and in), open `http://localhost:5090` and create a new login. All data stays. |
| Uninstalling | macOS: `~/Aperture/app/deploy/macos/uninstall.sh`. Windows: *Settings → Apps*. Linux: `~/.local/share/aperture/app/Aperture.Web uninstall`. Data and backups stay. |
| Restoring a backup | See [Restoring](#restoring). |

## Signing the installers (optional)

Unsigned, both installers need the one-time *Open Anyway* (macOS) or *Run anyway* (Windows). Signing removes that:

- **macOS:** an Apple Developer account (USD 99 a year) for a *Developer ID Installer* certificate, and Apple's
  notarization. By hand, on a Mac, with a release package from GitHub:
  ```bash
  APERTURE_PKG_IDENTITY="Developer ID Installer: <Your Name> (<TEAMID>)" \
    deploy/macos/pkg/build-pkg.sh aperture-…-osx-arm64.tar.gz https://github.com/<you>/aperture/releases/latest/download/latest.json
  xcrun notarytool submit artifacts/Aperture-Installer.pkg --keychain-profile <profile> --wait
  xcrun stapler staple artifacts/Aperture-Installer.pkg
  ```
- **Windows:** a code-signing certificate, or Azure Trusted Signing (about USD 10 a month), to sign
  `Aperture-Setup.exe` with `signtool`. SmartScreen trusts a new signature after some downloads.

The release workflow builds both unsigned. Signing there needs the certificates as repository secrets; until then,
sign by hand and replace the release's installer with the signed one.
