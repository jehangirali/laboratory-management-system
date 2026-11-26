# Laboratory Management System (demo build)

WPF / .NET 10 / EF Core 10 (Code First) / encrypted SQLite (SQLCipher), MVVM.

> **Status: working demo / starting point – NOT production-ready.**
> Build and test locally with the .NET 10 SDK using the commands below.
> Seeded test prices are 0 and reference ranges are *examples* (flagged `IsExample`) – the laboratory/pathologist must review them before any clinical use.

## Prerequisites
- Windows 10/11, Visual Studio 2022+ or `dotnet` CLI with **.NET 10 SDK**
- `dotnet tool install --global dotnet-ef`

## First run
```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/LaboratoryManagement.Desktop
```

The encrypted database is `%ProgramData%\LaboratoryManagement\lab.db`. On first launch the application creates this
directory and a random 256-bit SQLCipher key, protected with Windows DPAPI for the current user, at
`%ProgramData%\LaboratoryManagement\db.key`. Keep the key file with the database when backing up; it cannot be
decrypted by another Windows user or machine. No key is stored in `appsettings.json`.

The application is single-user and refuses to start if another instance is running. Keep the database on a local
drive, not a network share. Configure backup destination, scheduled backups, and retention in Settings; backups use
the encrypted SQLite backup operation and are verified before being reported as successful. Restore creates a
safety backup of the current database, replaces it from the selected backup, and verifies the restored database.

EF Core migrations are applied automatically at startup. For design-time migration commands, `LAB_CONNECTION` may
override the factory's in-memory default; use a local, unencrypted SQLite file for EF tooling, for example:

```powershell
$env:LAB_CONNECTION = "Data Source=$env:TEMP\lab-design.db"
dotnet ef migrations add AddFeature --project src/LaboratoryManagement.Infrastructure --startup-project src/LaboratoryManagement.Desktop
```

The **Use full workflow** setting is off by default. In simplified mode, new samples are marked received automatically,
results can be saved directly to complete tests, and completed results can be corrected with a required reason and a
result-revision history entry. Reports always show the latest saved results. Enable it to require sample collection,
lab receipt, and separate result verification. Existing
databases receive the setting with a `false` default without overwriting an administrator's value. Workflow changes
take effect after signing out and back in.

On the first launch a wizard creates the first Administrator (no default/hardcoded account). Then **Settings → Load demo data** adds sample
patients, doctors and orders (set test prices first, or enable "allow zero-price tests").

## Modules
Dashboard · New order (panels, discount, payment) · Orders (receipt, refund, cancel) · Patients (duplicate detection, history) · Doctors ·
Samples (barcode labels, collect/receive/reject in full workflow) · Results (flagging vs. reference ranges, revisions) · Verification (full workflow lock, amendment with
reason) · Test catalog (tests, panels, parameters, ranges) · Reports (CSV export) · Users & roles (permissions) ·
Backup/restore (encrypted SQLite backups, verification, retention) · Settings (lab info, numbering, report template).

## Architecture
`Domain` (entities, enums, permissions) → `Application` (services, permission checks, DTOs) → `Infrastructure` (EF Core, configurations, seeders,
backup) → `Desktop` (WPF views/viewmodels, printing). Context-per-operation, no cascade deletes, whole-rupee billing, no audit/login history or report snapshots,
idempotent insert-only seeding (re-seeding never overwrites admin edits).

## Tests
`dotnet test` – password hashing, range flagging, seeding idempotency, order/payment/refund, permission enforcement, result verification, lock and amendment history (EF InMemory).

## Known limitations
- No installer (WiX/MSIX not included); PDF via "Microsoft Print to PDF"/XPS only; reports export CSV, not .xlsx.
- Doctor commission is computed but there is no commission payment ledger.
- Scheduled backup runs only while the app is open.
- Amending a report requires resubmitting all tests of the order.
- Not validated for clinical, regulatory or data-protection compliance.
