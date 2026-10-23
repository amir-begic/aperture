# Working on Aperture

Aperture is a small ERP for one photographer: .NET 10, Blazor Web App (Interactive Server), Radzen.Blazor, EF Core
with SQLite. [README.md](README.md) has the setup, the project layout and the conventions (Money in Rappen, Swiss local
time through `TimeProvider`, enums as text, frozen issued invoices, the look). [docs/accounting.md](docs/accounting.md)
explains the numbers, [docs/deployment.md](docs/deployment.md) installing, updating and the program's own commands. Read
the part you touch; this file is only what they don't say.

## Checking your work

- `dotnet build` must stay at 0 warnings, and `dotnet test` must pass.
- To see a change, run the app (`dotnet run --project src/Aperture.Web`) with a data directory of its own
  (`Aperture__DataDirectory=/some/scratch/folder`), not the developer's `src/Aperture.Web/data`.
- Behaviour that only exists in an installed copy (stopping when idle, the launchers, static files) needs a published
  build: `dotnet publish src/Aperture.Web -c Release -o <folder>`, run with `ASPNETCORE_ENVIRONMENT=Production`. A
  Release build run from `bin/` doesn't serve Blazor's script, so no page becomes interactive.
- The developer's machine may have Aperture installed and running (`systemctl --user status aperture.service`,
  launchd's `ch.aperture.web`, the "Aperture" scheduled task). Never stop, restart or reinstall it while testing: the
  install, update, open and idle-stop code calls `systemctl`, `launchctl` and `schtasks`, so put a stand-in for them
  first on the `PATH`.

## Texts and languages

- Every text people see is written in English in the code: `L["…"]` in Razor (injected in `_Imports.razor`),
  `Texts.Get` / `Texts.Format` elsewhere, and keys in `ErrorMessage` of validation attributes. The German goes in
  `src/Aperture.Web/Localization/Texts.de.resx`, keyed by the exact English text. Add the German with every new or
  changed text; a missing entry silently shows English.
- Resource names ignore case: "Draft" and "DRAFT" would be one key. When one English text needs two German ones, use a
  context key ("ShootStatus.Cancelled") with its English in `Texts.resx`.
- `{0}`, `{1}`, … must match between the key and the German (a test checks this). Plurals are two keys
  ("1 invoice" / "{0} invoices"), not string building.
- German is Swiss: "ss" (never "ß"), MWST, Offerte, Shooting, Kunde. The app says "du"; what goes to clients (quotes,
  invoices) says "Sie".
- Quotes and invoices are written in their own language (`ClientDocument.Language`), not the page's: their PDFs format
  with that culture explicitly. The tax export and notifications follow the app's language.
- Lists of texts must be built when used, not in `static readonly` fields, or they keep the first language.
- Form fields use `FieldValidator`, not `RadzenDataAnnotationValidator`, so messages are translated.

## Database

- Migrations: `dotnet ef migrations add <Name> --project src/Aperture.Web --output-dir Data/Migrations` (after
  `dotnet tool restore`). Never edit a migration that's on `origin/main`: installed copies have run it. Add a new one.
- A new non-null column needs a real default for existing rows (e.g. `"English"` for an enum stored as text), not the
  `""` EF generates.
- After a migration, check `dotnet ef migrations has-pending-model-changes --project src/Aperture.Web`.

## Code

- Match the surrounding code: comments are short and say why, not what; `<summary>` on public types and members.
- Write user-facing English the way the existing texts are: short, plain, no jargon.
- Platform differences belong in `Installation/Platform.cs` (macOS, Windows, Linux classes); everything else is the
  same code on every system.

## Git

- Pushing `main` publishes a pre-release (`.github/workflows/release.yml`). Commit or push only when asked.
- Commit messages: a subject that says what changed for the photographer, then a prose body on why and how, wrapped
  around 75 characters, like the existing history.
- Keep unrelated changes in separate commits.
