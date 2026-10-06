# Funk

A small ERP for running a DJ business: gigs, clients, costs, invoices (Swiss QR-bill) and tax exports.
Single user, CHF, self-hosted on a Mac mini.

**Stack:** .NET 10 · Blazor Web App (Interactive Server) · Radzen.Blazor · EF Core + SQLite

## Getting started

```bash
# 1. Create your login (prints the commands to store it as user-secrets)
dotnet run --project src/Funk.Web -- hash-password

# 2. Run the app → http://localhost:5080
dotnet run --project src/Funk.Web

# Tests
dotnet test
```

The database is created and migrated automatically on startup.

## Running it on the Mac mini

See [docs/deployment.md](docs/deployment.md): `deploy/publish.sh` builds a self-contained release for Apple silicon,
`deploy/macos/install.sh` sets it up as a service with nightly backups, `deploy/deploy.sh` installs updates.

## Configuration

| Setting | Default | Notes |
|---|---|---|
| `Funk:DataDirectory` | `data` (relative to `src/Funk.Web`) | Holds `funk.db`. `~` is expanded; the Mac mini uses `~/Funk/data`. |
| `Auth:Username` | – | Required. |
| `Auth:PasswordHash` | – | Required. Generate with `hash-password`; never commit it. |

In production, use environment variables (`Funk__DataDirectory`, `Auth__Username`, `Auth__PasswordHash`).

## Database migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Funk.Web --output-dir Data/Migrations
```

A test fails if the model changes without a migration.

## Project layout

```
src/Funk.Web/
  Auth/            single-user cookie login
  Components/      app shell, layouts, routing
  Configuration/   data paths
  Data/            DbContext, entities, migrations, value converters
  Domain/          Money (CHF in Rappen), IBAN validation
  Features/        one folder per area: Gigs, Calendar, Invoices, Finance, Dashboard, …
tests/Funk.Tests/
deploy/            release build, Mac mini install/update/backup scripts
docs/              deployment guide
```
