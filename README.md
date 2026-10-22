# Aperture

A small ERP for running a photography business: shoots, clients, deliveries, costs, invoices (Swiss QR-bill) and tax exports.
Single user, CHF, English. It runs on the photographer's own Mac, installed from a package and used in the browser
there.

**Stack:** .NET 10 · Blazor Web App (Interactive Server) · Radzen.Blazor · EF Core + SQLite

## What it does

| Area | |
|---|---|
| **Shoots** | Client, location, date and time (several shoots per day, shoots past midnight), status *Inquiry → Booked → Shot → Delivered* or *Cancelled*, shoot type, quote. |
| **Packages** | Your price list (e.g. *Wedding full day*, CHF 3'800, 10 hours, delivery in 6 weeks). Picking one on a shoot fills in type, quote, end time, delivery date and what's included; the invoice names it. |
| **Shoot finances** | Costs such as gear rental, either covered by the quote or billed on top (at cost or with your margin). *Add train trip* enters the SBB ticket there and back, with the fare you paid last time to the same location. Payments received, with the payslip attached. Margin and what's still open. |
| **Delivery** | A delivery date per shoot, suggested from your usual delivery time (Settings), and a link to the client gallery. |
| **Prep** | Moodboard and reference links (Pinterest, Google Drive, …), notes, a checklist (wedding, portrait, event and commercial templates, the one for the shoot type offered first), files. |
| **Calendar** | Month, week and year view. Hovering over a shoot shows a card with the details. *Add to calendar* on a shoot puts it in Calendar on the Mac (and through iCloud on the iPhone). |
| **Invoices** | Created from a shoot: quote plus billed costs, optional VAT, rounded to 5 Rappen, Swiss QR-bill. Numbered `2026-001` once issued, then frozen and archived as PDF. |
| **Finance** | Income statement per year on a cash basis, business costs that don't belong to a shoot, equipment with depreciation. |
| **Tax export** | Per year, one ZIP: the statement as PDF, every line as Excel, and all receipts, payslips and invoices, numbered to match. |
| **Dashboard** | Year figures and a to-do list: overdue invoices, deliveries due or overdue, inquiries waiting a week for an answer or whose date has passed, shoots without an invoice, open prep, backup problems. |
| **Notifications** | In the browser on the Mac: overdue invoices, deliveries due within a week or overdue, inquiries waiting a week, open prep three days before a shoot, backup problems and new versions. |
| **Backups** | Daily into `~/Aperture/backups` (and any external drive you add), picked up by Time Machine. *Download a backup* in Settings gets everything as one ZIP, e.g. to move to a new Mac. |

How the numbers are calculated: [docs/accounting.md](docs/accounting.md).

## Getting started

Needs the .NET 10 SDK.

```bash
# Run the app → http://localhost:5090
dotnet run --project src/Aperture.Web

# Tests
dotnet test
```

The database is created and migrated automatically on startup. The first visit asks for a username and password and
keeps them, hashed, in `data/keys/login.json`; delete that file to start over. Then fill in your business details
under **Settings**: address and IBAN appear on invoices.

## Installing it

See [docs/deployment.md](docs/deployment.md):
- Pushing to `main` on GitHub builds a release in this private repository (`.github/workflows/release.yml`, with
  `deploy/publish.sh`: self-contained, for Apple silicon).
- `.github/workflows/promote.yml` copies a release you've tried to a public releases repository, whose `latest.json`
  the Mac checks for updates. Aperture offers it under **Settings → Updates** and goes back to the previous version if
  the new one doesn't start.
- `deploy/macos/pkg/build-pkg.sh` builds an installer package that installs the newest promoted release for the person
  logged in (`deploy/macos/install-laptop.sh`), without admin rights or a token; the login is created in the browser.

## Configuration

| Setting | Default | Notes |
|---|---|---|
| `Aperture:DataDirectory` | `data` (relative to `src/Aperture.Web`) | All business data. `~` is expanded; the installer uses `~/Aperture/data`. |
| `Updates:FeedUrl` | – | The `latest.json` of the public releases repository (https). Enables Settings → Updates. |

The installer sets them as environment variables (`Aperture__DataDirectory`, `Updates__FeedUrl`) in the launchd
agent, `~/Library/LaunchAgents/ch.aperture.web.plist`.

The data directory holds:

```
aperture.db               the database (SQLite, WAL mode)
attachments/YYYY/     receipts, payslips, invoice PDFs, under generated names
keys/                 the login (login.json), keys that sign the login cookie, so a restart doesn't log you out,
                      and the key pair that signs notifications (push-vapid.json)
backup-status.json    written by the backup, shown under Settings and on the dashboard
```

## Database migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Aperture.Web --output-dir Data/Migrations
```

A test fails if the model changes without a migration.

## Project layout

```
src/Aperture.Web/
  Auth/            single-user cookie login, created in the browser on first start
  Components/      app shell, layouts, routing
  Configuration/   data paths, app version
  Data/            DbContext, entities, migrations, value converters
  Domain/          the rules, without UI or database: Money (CHF in Rappen), invoice totals,
                   depreciation, shoot financials, categories, IBAN and contact validation
  Features/        one folder per area, pages and their logic together:
                   Shoots, Packages, Clients, Locations, Calendar, Attachments, Invoices, Finance, Dashboard,
                   Backup, Notifications, Updates, Settings
tests/Aperture.Tests/  xUnit; database tests run against in-memory SQLite
deploy/            release build, install/update/backup scripts, launchd agent definitions and the installer
                   package (macos/pkg)
docs/              deployment guide, accounting rules
```

A few conventions:
- **Money** is a `Money` value holding whole Rappen, stored as an integer. Never `double`.
- **Times** are local Swiss time. Code that needs "now" takes a `TimeProvider`, so tests can fix the date.
- **Enums** are stored as text, so the database stays readable.
- **Issued invoices** never change; they keep a frozen copy of both addresses.
- **Look:** black frame, white working surface, gray panels, 1px rules, square corners, one weight, the ring as logo,
  light only. Labels, navigation, buttons and tags are uppercase; what people typed keeps its case. No accent color:
  red only for danger and overdue, amber for warnings. The tokens (`--ap-*`) and Radzen overrides live in
  `wwwroot/brand.css` on top of Radzen's *standard* theme; `app.css` holds the app's own components. The typeface is
  Space Grotesk Regular, self-hosted in `wwwroot/fonts`.

## Third-party components

| Component | Used for | License |
|---|---|---|
| Radzen.Blazor | UI components | MIT |
| QuestPDF | invoice and statement PDFs | Community license: free below USD 1M annual revenue |
| Codecrete.SwissQRBill.Generator | QR-bill | MIT |
| ClosedXML | Excel export | MIT |
| Liberation Sans | font embedded in PDFs | SIL Open Font License |
| Space Grotesk Regular | app font, self-hosted in `wwwroot/fonts` | SIL Open Font License |
