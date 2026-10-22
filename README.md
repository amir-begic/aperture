# Aperture

A small ERP for running a photography business: shoots, clients, deliveries, costs, invoices (Swiss QR-bill) and tax exports.
Single user, CHF, English, self-hosted on a Mac mini and used from any browser at home or through Tailscale.

**Stack:** .NET 10 · Blazor Web App (Interactive Server) · Radzen.Blazor · EF Core + SQLite

## What it does

| Area | |
|---|---|
| **Shoots** | Client, location, date and time (several shoots per day, shoots past midnight), status *Inquiry → Booked → Shot → Delivered* or *Cancelled*, shoot type, quote. |
| **Shoot finances** | Costs such as gear rental, either covered by the quote or billed on top (at cost or with your margin). Payments received, with the payslip attached. Margin and what's still open. |
| **Delivery** | A delivery date per shoot, suggested from your usual delivery time (Settings), and a link to the client gallery. |
| **Prep** | Moodboard and reference links (Pinterest, Google Drive, …), notes, a checklist (wedding, portrait and commercial templates included), files. |
| **Calendar** | Month, week and year view. Hovering over a shoot shows a card with the details. A private link (Settings) puts your shoots in the iPhone's calendar. |
| **Invoices** | Created from a shoot: quote plus billed costs, optional VAT, rounded to 5 Rappen, Swiss QR-bill. Numbered `2026-001` once issued, then frozen and archived as PDF. |
| **Finance** | Income statement per year on a cash basis, business costs that don't belong to a shoot, equipment with depreciation. |
| **Tax export** | Per year, one ZIP: the statement as PDF, every line as Excel, and all receipts, payslips and invoices, numbered to match. |
| **Dashboard** | Year figures and a to-do list: overdue invoices, deliveries due or overdue, shoots without an invoice, open prep, backup problems. |
| **On the go** | Installable on the phone's home screen (PWA), with a full-screen menu on small screens. Notifications on the phone for overdue invoices, deliveries due within a week or overdue, open prep three days before a shoot, backup problems and new versions. |

How the numbers are calculated: [docs/accounting.md](docs/accounting.md).

## Getting started

Needs the .NET 10 SDK.

```bash
# 1. Create your login (prints the commands to store it as user-secrets)
dotnet run --project src/Aperture.Web -- hash-password

# 2. Run the app → http://localhost:5090
dotnet run --project src/Aperture.Web

# Tests
dotnet test
```

The database is created and migrated automatically on startup. Then fill in your business details under
**Settings**: address and IBAN appear on invoices.

## Running it on the Mac mini

See [docs/deployment.md](docs/deployment.md):
- `deploy/publish.sh` builds a self-contained release for Apple silicon.
- `deploy/macos/install.sh` sets it up as a service with nightly backups to your external drives.
- `deploy/deploy.sh` installs updates and goes back to the previous version if the new one doesn't start.
- Pushing to `main` on GitHub builds a release (`.github/workflows/release.yml`); Aperture offers it under
  **Settings → Updates** and installs it with the same rollback.

## Configuration

| Setting | Default | Notes |
|---|---|---|
| `Aperture:DataDirectory` | `data` (relative to `src/Aperture.Web`) | All business data. `~` is expanded; the Mac mini uses `~/Aperture/data`. |
| `Auth:Username` | – | Required. |
| `Auth:PasswordHash` | – | Required. Generate with `hash-password`; never commit it. |
| `Updates:Repository` | – | `owner/name` on GitHub; with the token, enables Settings → Updates. |
| `Updates:GitHubToken` | – | Fine-grained token, *Contents: Read-only* on that repository. |

In production, use environment variables (`Aperture__DataDirectory`, `Auth__Username`, `Auth__PasswordHash`,
`Updates__Repository`, `Updates__GitHubToken`).
The installer puts them in the service definition, which only root can read.

The data directory holds:

```
aperture.db               the database (SQLite, WAL mode)
attachments/YYYY/     receipts, payslips, invoice PDFs, under generated names
keys/                 keys that sign the login cookie, so a restart doesn't log you out, and the
                      key pair that signs notifications (push-vapid.json)
backup-status.json    written by the nightly backup, shown under Settings and on the dashboard
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
  Auth/            single-user cookie login, hash-password command
  Components/      app shell, layouts, routing
  Configuration/   data paths, app version
  Data/            DbContext, entities, migrations, value converters
  Domain/          the rules, without UI or database: Money (CHF in Rappen), invoice totals,
                   depreciation, shoot financials, categories, IBAN and contact validation
  Features/        one folder per area, pages and their logic together:
                   Shoots, Clients, Locations, Calendar, Attachments, Invoices, Finance, Dashboard, Backup,
                   Notifications, Settings
tests/Aperture.Tests/  xUnit; database tests run against in-memory SQLite
deploy/            release build, Mac mini install/update/backup scripts and service definitions
docs/              deployment guide, accounting rules
```

A few conventions:
- **Money** is a `Money` value holding whole Rappen, stored as an integer. Never `double`.
- **Times** are local Swiss time. Code that needs "now" takes a `TimeProvider`, so tests can fix the date.
- **Enums** are stored as text, so the database stays readable.
- **Issued invoices** never change; they keep a frozen copy of both addresses.
- **Look:** black frame, white working surface, gray panels, 1px rules, square
  corners, one weight, the ring as logo, light only. Labels, navigation, buttons and tags are uppercase; what people
  typed keeps its case. No accent color: red only for danger and overdue, amber for warnings. The tokens (`--ss-*`)
  and Radzen overrides live in `wwwroot/brand.css` on top of Radzen's *standard* theme; `app.css` holds the app's
  own components. The typeface is the brand typeface, self-hosted in `wwwroot/fonts`.

## Third-party components

| Component | Used for | License |
|---|---|---|
| Radzen.Blazor | UI components | MIT |
| QuestPDF | invoice and statement PDFs | Community license: free below USD 1M annual revenue |
| Codecrete.SwissQRBill.Generator | QR-bill | MIT |
| ClosedXML | Excel export | MIT |
| Liberation Sans | font embedded in PDFs | SIL Open Font License |
| Brand typeface | app font, self-hosted in `wwwroot/fonts` | Commercial license (paid); keep the repository private |
