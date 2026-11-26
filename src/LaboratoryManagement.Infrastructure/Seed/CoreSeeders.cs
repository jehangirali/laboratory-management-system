using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using LaboratoryManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LaboratoryManagement.Infrastructure.Seed;

public interface ISeeder
{
    string Name { get; }
    int Order { get; }
    Task SeedAsync(AppDbContext db, CancellationToken ct);
}

public interface IDataSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

/// <summary>Runs all focused seeders in order. Idempotent: inserts missing records only, never overwrites or deletes.</summary>
public sealed class DataSeeder : IDataSeeder
{
    public const string SeedVersion = "1";
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IEnumerable<ISeeder> _seeders;
    private readonly ILogger<DataSeeder> _log;

    public DataSeeder(IDbContextFactory<AppDbContext> factory, IEnumerable<ISeeder> seeders, ILogger<DataSeeder> log)
    { _factory = factory; _seeders = seeders; _log = log; }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var useTx = db.Database.IsRelational();
        var tx = useTx ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            foreach (var s in _seeders.OrderBy(s => s.Order))
            {
                var before = db.ChangeTracker.Entries().Count(e => e.State == EntityState.Added);
                await s.SeedAsync(db, ct);
                await db.SaveChangesAsync(ct);
                var added = db.ChangeTracker.Entries().Count(e => e.State == EntityState.Unchanged) ; // informational only
                _log.LogInformation("Seeder {Seeder} completed", s.Name);
            }
            if (tx != null) await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Seeding failed; rolling back");
            if (tx != null) await tx.RollbackAsync(ct);
            throw;
        }
        finally { if (tx != null) await tx.DisposeAsync(); }
    }
}

public sealed class PermissionSeeder : ISeeder
{
    public string Name => nameof(PermissionSeeder);
    public int Order => 10;
    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = (await db.Permissions.Select(p => p.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, group, desc) in Domain.Permissions.All)
            if (!existing.Contains(code)) db.Permissions.Add(new Permission { Code = code, Group = group, Description = desc });
    }
}

public sealed class RoleSeeder : ISeeder
{
    public string Name => nameof(RoleSeeder);
    public int Order => 20;

    private static readonly Dictionary<string, string[]> Defaults = new()
    {
        [SystemRoles.Administrator] = Domain.Permissions.All.Select(p => p.Code).ToArray(),
        [SystemRoles.Receptionist] = new[]
        {
            Domain.Permissions.DashboardView, Domain.Permissions.PatientsView, Domain.Permissions.PatientsManage, Domain.Permissions.DoctorsView, Domain.Permissions.DoctorsManage,
            Domain.Permissions.TestsView, Domain.Permissions.OrdersView, Domain.Permissions.OrdersCreate, Domain.Permissions.PaymentsReceive, Domain.Permissions.SamplesManage,
            Domain.Permissions.ReportsPrint
        },
        [SystemRoles.LabTechnician] = new[]
        {
            Domain.Permissions.DashboardView, Domain.Permissions.PatientsView, Domain.Permissions.TestsView, Domain.Permissions.OrdersView,
            Domain.Permissions.SamplesManage, Domain.Permissions.ResultsEnter
        },
        [SystemRoles.Pathologist] = new[]
        {
            Domain.Permissions.DashboardView, Domain.Permissions.PatientsView, Domain.Permissions.TestsView, Domain.Permissions.OrdersView,
            Domain.Permissions.ResultsEnter, Domain.Permissions.ResultsVerify, Domain.Permissions.ReportsPrint, Domain.Permissions.ReportsAmend
        },
        [SystemRoles.Accountant] = new[]
        {
            Domain.Permissions.DashboardView, Domain.Permissions.PatientsView, Domain.Permissions.OrdersView, Domain.Permissions.PaymentsReceive,
            Domain.Permissions.OrdersRefund, Domain.Permissions.OrdersDiscount, Domain.Permissions.FinanceReports, Domain.Permissions.CommissionsView
        },
    };

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var perms = await db.Permissions.ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var (roleName, codes) in Defaults)
        {
            var role = await db.Roles.Include(r => r.RolePermissions).FirstOrDefaultAsync(r => r.Name == roleName, ct);
            var isNew = role == null;
            if (role == null)
            {
                role = new Role { Name = roleName, IsSystem = true, Description = "Default role (permissions editable)" };
                db.Roles.Add(role);
            }
            // New role: grant defaults. Existing role: only add permissions that did not exist when the role was created
            // would be ambiguous, so administrator-edited permission sets are left untouched — except the Administrator
            // role, which always receives newly introduced permissions.
            if (isNew || roleName == SystemRoles.Administrator)
            {
                foreach (var code in codes)
                    if (perms.TryGetValue(code, out var pid) && role.RolePermissions.All(rp => rp.PermissionId != pid))
                        role.RolePermissions.Add(new RolePermission { PermissionId = pid });
            }
        }
    }
}

public sealed class UnitSeeder : ISeeder
{
    public string Name => nameof(UnitSeeder);
    public int Order => 30;
    public static readonly string[] Units =
    {
        "g/dL", "mg/dL", "mmol/L", "U/L", "IU/L", "x10^3/µL", "x10^6/µL", "%", "fL", "pg", "mm/hr", "ng/mL", "pg/mL", "µIU/mL",
        "mIU/mL", "ng/dL", "µg/dL", "sec", "ratio", "mEq/L", "/HPF", "mg/L", "IU/mL", "U/mL", "µg/mL", "µmol/L", "g/L", "cells/µL"
    };

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = (await db.MeasurementUnits.Select(u => u.Code).ToListAsync(ct)).ToHashSet();
        foreach (var u in Units)
            if (!existing.Contains(u)) db.MeasurementUnits.Add(new MeasurementUnit { Code = u, Symbol = u });
    }
}

public sealed class SpecimenSeeder : ISeeder
{
    public string Name => nameof(SpecimenSeeder);
    public int Order => 40;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var specimens = new (string Code, string Name)[]
        {
            ("BLOOD", "Whole Blood"), ("SERUM", "Serum"), ("PLASMA", "Plasma"), ("URINE", "Urine"), ("STOOL", "Stool"),
            ("SWAB", "Swab"), ("CSF", "CSF"), ("SPUTUM", "Sputum"), ("FLUID", "Body Fluid"), ("OTHER", "Other")
        };
        var containers = new (string Code, string Name)[]
        {
            ("EDTA", "EDTA Tube (Lavender)"), ("SST", "Gel / Serum Separator Tube (Gold)"), ("PLAIN", "Plain Tube (Red)"),
            ("CITRATE", "Citrate Tube (Blue)"), ("FLUORIDE", "Fluoride Tube (Grey)"), ("HEPARIN", "Heparin Tube (Green)"),
            ("URINECUP", "Urine Container"), ("STOOLCUP", "Stool Container"), ("SWABTUBE", "Swab Transport Tube")
        };
        var sc = (await db.SpecimenTypes.Select(x => x.Code).ToListAsync(ct)).ToHashSet();
        foreach (var (c, n) in specimens) if (!sc.Contains(c)) db.SpecimenTypes.Add(new SpecimenType { Code = c, Name = n });
        var cc = (await db.SampleContainers.Select(x => x.Code).ToListAsync(ct)).ToHashSet();
        foreach (var (c, n) in containers) if (!cc.Contains(c)) db.SampleContainers.Add(new SampleContainer { Code = c, Name = n });
    }
}

public sealed class TestCategorySeeder : ISeeder
{
    public string Name => nameof(TestCategorySeeder);
    public int Order => 50;
    public static readonly (string Code, string Name)[] Categories =
    {
        ("HEM", "Hematology"), ("BIO", "Biochemistry / Clinical Chemistry"), ("SERO", "Serology"), ("IMM", "Immunology"),
        ("MICRO", "Microbiology"), ("URINE", "Urine Analysis"), ("STOOL", "Stool Analysis"), ("HORM", "Hormones"),
        ("THY", "Thyroid"), ("CARD", "Cardiac Markers"), ("TUM", "Tumor Markers"), ("COAG", "Coagulation"),
        ("ELEC", "Electrolytes"), ("MOL", "Molecular Diagnostics"), ("SPEC", "Special Chemistry"), ("PROF", "Profiles / Panels")
    };

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = (await db.TestCategories.Select(c => c.Code).ToListAsync(ct)).ToHashSet();
        var order = 0;
        foreach (var (code, name) in Categories)
        {
            order += 10;
            if (!existing.Contains(code)) db.TestCategories.Add(new TestCategory { Code = code, Name = name, DisplayOrder = order });
        }
    }
}

public sealed class ApplicationSettingsSeeder : ISeeder
{
    public string Name => nameof(ApplicationSettingsSeeder);
    public int Order => 60;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LaboratoryManagement", "Backups");
        var defaults = new (string Key, string Value, string Group, string Desc)[]
        {
            (SettingKeys.LabName, "Sample Diagnostic Laboratory", "Laboratory", "Laboratory name shown on reports"),
            (SettingKeys.LabAddress, "Street Address, City, Pakistan", "Laboratory", "Address printed in report header"),
            (SettingKeys.LabPhone, "+92-000-0000000", "Laboratory", "Phone"),
            (SettingKeys.LabEmail, "info@example.com", "Laboratory", "Email"),
            (SettingKeys.LabLicense, "", "Laboratory", "Registration / license details printed on reports"),
            (SettingKeys.LabLogoPath, "", "Laboratory", "Full path to the logo image (PNG/JPG)"),
            (SettingKeys.Currency, "PKR", "General", "Currency code"),
            (SettingKeys.DateTimeFormat, "dd-MMM-yyyy HH:mm", "General", "Date/time display format"),
            (SettingKeys.PrefixPatient, "P", "Numbering", "Patient ID prefix"),
            (SettingKeys.PrefixOrder, "ORD", "Numbering", "Order number prefix"),
            (SettingKeys.PrefixReceipt, "RCP", "Numbering", "Receipt / invoice prefix"),
            (SettingKeys.PrefixDoctor, "DR", "Numbering", "Doctor ID prefix"),
            (SettingKeys.AllowZeroPrice, "false", "Billing", "Allow ordering tests whose price is 0 (not recommended)"),
            (SettingKeys.CommissionEnabled, "false", "Billing", "Enable doctor commission reporting"),
            (SettingKeys.RequireSampleCollection, "true", "Workflow", "Require sample collection before result entry"),
            (SettingKeys.RequireReceiveInLab, "true", "Workflow", "Require receive-in-lab before result entry"),
            (SettingKeys.RequireVerification, "true", "Workflow", "Require report verification before results can be printed"),
            (SettingKeys.FullWorkflowEnabled, "false", "Workflow", "Use sample collection, lab receipt and result verification workflow"),
            (SettingKeys.ReportTemplate, "Auto", "Report", "Auto, General, Hematology or Profile"),
            (SettingKeys.ReportFontSize, "11", "Report", "Base font size (points)"),
            (SettingKeys.ReportMarginMm, "12", "Report", "Page margin in millimetres"),
            (SettingKeys.ReportPageSize, "A4", "Report", "A4 or Letter"),
            (SettingKeys.ReportShowReference, "true", "Report", "Show reference range column"),
            (SettingKeys.ReportShowFather, "true", "Report", "Show father/husband name"),
            (SettingKeys.ReportShowPhone, "true", "Report", "Show patient phone"),
            (SettingKeys.ReportShowSignature, "true", "Report", "Show verifier signature block"),
            (SettingKeys.ReportFooter, "This is a computer-generated report.", "Report", "Footer text"),
            (SettingKeys.ReportDisclaimer, "Results relate only to the sample tested. Clinical correlation is advised. Reference ranges are laboratory-configured.", "Report", "Disclaimer"),
            (SettingKeys.LabelWidthMm, "50", "Barcode", "Sample label width (mm)"),
            (SettingKeys.LabelHeightMm, "30", "Barcode", "Sample label height (mm)"),
            (SettingKeys.BackupFolder, backupDir, "Backup", "Backup destination folder"),
            (SettingKeys.BackupScheduleEnabled, "false", "Backup", "Run scheduled backups while the application is open"),
            (SettingKeys.BackupIntervalHours, "24", "Backup", "Hours between scheduled backups"),
            (SettingKeys.BackupKeepCount, "14", "Backup", "Number of automatic backups to keep"),
            ("Seed.Version", DataSeeder.SeedVersion, "System", "Seed data version (informational)"),
            ("Backup.LastScheduledRun", "", "System", "Internal"),
        };
        var existing = (await db.AppSettings.Select(s => s.Key).ToListAsync(ct)).ToHashSet();
        foreach (var (k, v, g, d) in defaults)
            if (!existing.Contains(k)) db.AppSettings.Add(new AppSetting { Key = k, Value = v, Group = g, Description = d });

        var methods = new (string Code, string Name)[] { ("CASH", "Cash"), ("BANK", "Bank Transfer"), ("CARD", "Card"), ("EASYPAISA", "Easypaisa"), ("JAZZCASH", "JazzCash") };
        var mc = (await db.PaymentMethods.Select(m => m.Code).ToListAsync(ct)).ToHashSet();
        foreach (var (c, n) in methods) if (!mc.Contains(c)) db.PaymentMethods.Add(new PaymentMethod { Code = c, Name = n });
    }
}
