# Laboratory Management System (demo build)

WPF / .NET 10 / EF Core 10 (Code First) / SQL Server LocalDB, MVVM. Built from *Laboratory_Management_System_Claude_Specification.docx*.

> **Status: working demo / starting point – NOT production-ready.**
> The Domain project was compiled in the authoring sandbox; the Application, Infrastructure, Desktop and test projects
> could not be restored there (NuGet blocked), so **expect a few compile errors on first build** – fix them locally (e.g. with Copilot).
> Seeded test prices are 0 and reference ranges are *examples* (flagged `IsExample`) – the laboratory/pathologist must review them before any clinical use.

## Prerequisites
- Windows 10/11, Visual Studio 2022+ or `dotnet` CLI with **.NET 10 SDK**
- SQL Server LocalDB (`sqllocaldb info` should list `MSSQLLocalDB`)
- `dotnet tool install --global dotnet-ef`

## First run
```powershell
dotnet restore
dotnet build

# 1) Generate the initial migration (none is shipped – it could not be generated offline)
dotnet ef migrations add InitialCreate `
  --project src/LaboratoryManagement.Infrastructure `
  --startup-project src/LaboratoryManagement.Desktop `
  --output-dir Persistence/Migrations

# 2) Run
dotnet run --project src/LaboratoryManagement.Desktop
```
If no migration exists the app falls back to `EnsureCreated()`. If you ran it that way first, drop the database
(`sqllocaldb start MSSQLLocalDB` → `DROP DATABASE LaboratoryManagement`) before applying migrations.

Connection string: `src/LaboratoryManagement.Desktop/appsettings.json` (`Server=(localdb)\MSSQLLocalDB;Database=LaboratoryManagement`).
For design-time tools you can override with env var `LAB_CONNECTION`.

On the first launch a wizard creates the first Administrator (no default/hardcoded account). Then **Settings → Load demo data** adds sample
patients, doctors and orders (set test prices first, or enable "allow zero-price tests").

## Modules
Dashboard · New order (panels, discount, payment) · Orders (receipt, refund, cancel) · Patients (duplicate detection, history) · Doctors ·
Samples (barcode labels, collect/receive/reject) · Results (flagging vs. reference ranges, revisions) · Verification (lock, amendment with
reason, report versions) · Test catalog (tests, panels, parameters, ranges) · Reports (CSV export) · Users & roles (permissions) · Audit trail ·
Backup/restore (LocalDB BACKUP/RESTORE, verify, retention) · Settings (lab info, numbering, report template).

## Architecture
`Domain` (entities, enums, permissions) → `Application` (services, permission checks, DTOs) → `Infrastructure` (EF Core, configurations, seeders,
backup) → `Desktop` (WPF views/viewmodels, printing). Context-per-operation, no cascade deletes, rowversion concurrency, audit on every change,
idempotent insert-only seeding (re-seeding never overwrites admin edits).

## Tests
`dotnet test` – password hashing, range flagging, seeding idempotency, order/payment/refund, permission enforcement, result verification, lock and amendment history (EF InMemory).

## Known limitations
- No installer (WiX/MSIX not included); PDF via "Microsoft Print to PDF"/XPS only; reports export CSV, not .xlsx.
- Doctor commission is computed but there is no commission payment ledger.
- Scheduled backup runs only while the app is open.
- Amending a report requires resubmitting all tests of the order.
- Not validated for clinical, regulatory or data-protection compliance.
