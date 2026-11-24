using System.Text.RegularExpressions;
using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Application.Services;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using LaboratoryManagement.Infrastructure.Persistence;
using LaboratoryManagement.Infrastructure.Seed;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LaboratoryManagement.Infrastructure.Services;

public static class ConnectionStringSafety
{
    /// <summary>Removes credentials from text that may contain a connection string.</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return Regex.Replace(text, @"(?i)(password|pwd|user id|uid)\s*=\s*[^;]*", "$1=***");
    }
}

public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IDataSeeder _seeder;
    private readonly IConfiguration _config;
    private readonly ILogger<DatabaseInitializer> _log;
    private readonly IServiceProvider _sp;

    public DatabaseInitializer(IDbContextFactory<AppDbContext> factory, IDataSeeder seeder, IConfiguration config,
                               ILogger<DatabaseInitializer> log, IServiceProvider sp)
    { _factory = factory; _seeder = seeder; _config = config; _log = log; _sp = sp; }

    public async Task<InitResult> InitializeAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            progress?.Report("Reading database configuration...");
            var cs = _config.GetConnectionString("LabDb");
            if (string.IsNullOrWhiteSpace(cs)) return new(false, false, "No connection string 'LabDb' is configured in appsettings.json.", null);
            try { _ = new SqlConnectionStringBuilder(cs); }
            catch (Exception ex) { return new(false, false, "The connection string is not valid: " + ex.Message, null); }

            await using var db = await _factory.CreateDbContextAsync(ct);

            progress?.Report("Checking database connectivity...");
            var exists = await db.Database.CanConnectAsync(ct);
            var hasMigrations = db.Database.GetMigrations().Any();

            if (hasMigrations)
            {
                var pending = exists ? (await db.Database.GetPendingMigrationsAsync(ct)).ToList() : new List<string>();
                var applied = exists ? (await db.Database.GetAppliedMigrationsAsync(ct)).ToList() : new List<string>();
                if (exists && applied.Count > 0 && pending.Count > 0)
                {
                    progress?.Report("Backing up the database before upgrade...");
                    try
                    {
                        var backup = _sp.GetRequiredService<IBackupService>();
                        await backup.BackupAsync("PreUpgrade", null, ct);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Pre-upgrade backup failed");
                        return new(false, false, "A backup could not be created before applying database updates, so the upgrade was stopped to protect your data.", ConnectionStringSafety.Redact(ex.Message));
                    }
                }
                progress?.Report(exists ? "Applying database updates..." : "Creating database...");
                await db.Database.MigrateAsync(ct);
                _log.LogInformation("Migrations applied: {Count}", pending.Count);
            }
            else
            {
                // DEMO FALLBACK: no migration has been generated yet. Never used once InitialCreate exists.
                _log.LogWarning("No EF Core migrations found. Falling back to EnsureCreated (demo only). Generate InitialCreate - see README.");
                progress?.Report("Creating database (demo mode, no migrations found)...");
                await db.Database.EnsureCreatedAsync(ct);
            }

            progress?.Report("Seeding reference data...");
            await _seeder.SeedAsync(ct);

            progress?.Report("Checking administrator account...");
            var adminExists = await db.UserRoles.AnyAsync(ur => ur.Role.Name == SystemRoles.Administrator && ur.User.IsActive, ct);
            progress?.Report("Ready.");
            return new(true, adminExists, null, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Database initialization failed");
            return new(false, false, "The database could not be initialized.",
                       ConnectionStringSafety.Redact(ex.GetBaseException().Message));
        }
    }
}

public sealed class BackupService : IBackupService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IConfiguration _config;
    private readonly ILogger<BackupService> _log;
    private readonly ICurrentUser _user;
    private readonly SettingsService _settings;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public BackupService(IDbContextFactory<AppDbContext> factory, IConfiguration config, ILogger<BackupService> log,
                         ICurrentUser user, SettingsService settings)
    { _factory = factory; _config = config; _log = log; _user = user; _settings = settings; }

    private SqlConnectionStringBuilder Builder() => new(_config.GetConnectionString("LabDb"));

    private string DefaultFolder()
    {
        var f = _settings.Get(SettingKeys.BackupFolder);
        return string.IsNullOrWhiteSpace(f)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LaboratoryManagement", "Backups") : f;
    }

    public async Task<BackupRecord> BackupAsync(string kind, string? folder = null, CancellationToken ct = default)
    {
        if (kind == "Manual") _user.Require(Permissions.BackupManage);
        await Gate.WaitAsync(ct);
        var rec = new BackupRecord { Kind = kind, CreatedBy = _user.IsAuthenticated ? _user.Username : "system" };
        try
        {
            var dir = folder ?? DefaultFolder();
            Directory.CreateDirectory(dir);
            var b = Builder();
            var dbName = b.InitialCatalog;
            var file = Path.Combine(dir, $"{dbName}_{DateTime.Now:yyyyMMdd_HHmmss}_{kind}.bak");
            rec.FilePath = file;
            await using (var conn = new SqlConnection(b.ConnectionString))
            {
                await conn.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandTimeout = 600;
                cmd.CommandText = $"BACKUP DATABASE [{dbName.Replace("]", "]]")}] TO DISK = @f WITH INIT, CHECKSUM, FORMAT, NAME = @n";
                cmd.Parameters.AddWithValue("@f", file);
                cmd.Parameters.AddWithValue("@n", $"{dbName} {kind} backup");
                await cmd.ExecuteNonQueryAsync(ct);
            }
            var (ok, msg) = await VerifyAsync(file, ct);
            rec.Success = ok; rec.Message = ok ? "Backup completed and verified." : "Backup file created but verification failed: " + msg;
            rec.SizeBytes = File.Exists(file) ? new FileInfo(file).Length : 0;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Backup failed");
            rec.Success = false; rec.Message = ConnectionStringSafety.Redact(ex.GetBaseException().Message);
        }
        finally { Gate.Release(); }

        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            db.BackupRecords.Add(rec);
            db.AuditLogs.Add(new AuditLog { Username = rec.CreatedBy ?? "system", Action = rec.Success ? "Backup created" : "Backup FAILED", EntityType = "Backup", EntityId = Path.GetFileName(rec.FilePath), Details = rec.Message });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not record backup history (database may be unavailable)"); }
        return rec;
    }

    public async Task<IReadOnlyList<BackupRecord>> GetHistoryAsync(CancellationToken ct = default)
    {
        _user.Require(Permissions.BackupManage);
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BackupRecords.AsNoTracking().OrderByDescending(b => b.At).Take(200).ToListAsync(ct);
    }

    public async Task<(bool Ok, string Message)> VerifyAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(Builder().ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 600;
            cmd.CommandText = "RESTORE VERIFYONLY FROM DISK = @f WITH CHECKSUM";
            cmd.Parameters.AddWithValue("@f", filePath);
            await cmd.ExecuteNonQueryAsync(ct);
            return (true, "Backup file is valid.");
        }
        catch (Exception ex) { return (false, ConnectionStringSafety.Redact(ex.GetBaseException().Message)); }
    }

    public async Task<(bool Ok, string Message)> RestoreAsync(string filePath, CancellationToken ct = default)
    {
        _user.Require(Permissions.BackupManage);
        var (valid, vmsg) = await VerifyAsync(filePath, ct);
        if (!valid) return (false, "The selected file failed the integrity check: " + vmsg);

        // Safety copy of the current state first.
        var safety = await BackupAsync("PreUpgrade", null, ct);
        if (!safety.Success) return (false, "Restore cancelled: a safety backup of the current database could not be created. " + safety.Message);

        var b = Builder();
        var dbName = b.InitialCatalog.Replace("]", "]]");
        var master = new SqlConnectionStringBuilder(b.ConnectionString) { InitialCatalog = "master" };
        try
        {
            SqlConnection.ClearAllPools();
            await using var conn = new SqlConnection(master.ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 900;
            cmd.CommandText =
                $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"RESTORE DATABASE [{dbName}] FROM DISK = @f WITH REPLACE; " +
                $"ALTER DATABASE [{dbName}] SET MULTI_USER;";
            cmd.Parameters.AddWithValue("@f", filePath);
            await cmd.ExecuteNonQueryAsync(ct);
            _log.LogWarning("Database restored from {File}", filePath);
            return (true, "Database restored. Please restart the application now.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Restore failed");
            try
            {
                await using var conn = new SqlConnection(master.ConnectionString);
                await conn.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"ALTER DATABASE [{dbName}] SET MULTI_USER;";
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch { /* best effort */ }
            return (false, "Restore failed: " + ConnectionStringSafety.Redact(ex.GetBaseException().Message));
        }
    }

    public async Task RunScheduledIfDueAsync(CancellationToken ct = default)
    {
        if (!_settings.GetBool(SettingKeys.BackupScheduleEnabled)) return;
        var hours = Math.Max(1, _settings.GetInt(SettingKeys.BackupIntervalHours, 24));
        DateTime.TryParse(_settings.Get("Backup.LastScheduledRun"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var last);
        if (last != default && DateTime.UtcNow - last < TimeSpan.FromHours(hours)) return;

        var rec = await BackupAsync("Scheduled", null, ct);
        await _settings.SetInternalAsync("Backup.LastScheduledRun", DateTime.UtcNow.ToString("o"));
        if (!rec.Success) return;

        // Retention: keep the newest N scheduled backups.
        var keep = Math.Max(1, _settings.GetInt(SettingKeys.BackupKeepCount, 14));
        await using var db = await _factory.CreateDbContextAsync(ct);
        var old = await db.BackupRecords.Where(r => r.Kind == "Scheduled" && r.Success).OrderByDescending(r => r.At).Skip(keep).ToListAsync(ct);
        foreach (var o in old) { try { if (File.Exists(o.FilePath)) File.Delete(o.FilePath); } catch { } }
    }
}

public static class InfrastructureRegistration
{
    public static IServiceCollection AddLaboratoryInfrastructure(this IServiceCollection s, IConfiguration config)
    {
        var cs = config.GetConnectionString("LabDb") ?? throw new InvalidOperationException("Connection string 'LabDb' is missing.");
        s.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer(cs, sql => sql.CommandTimeout(60)));
        s.AddSingleton<IAppDbContextFactory, AppDbContextFactoryAdapter>();
        s.AddSingleton<ISeeder, PermissionSeeder>();
        s.AddSingleton<ISeeder, RoleSeeder>();
        s.AddSingleton<ISeeder, UnitSeeder>();
        s.AddSingleton<ISeeder, SpecimenSeeder>();
        s.AddSingleton<ISeeder, TestCategorySeeder>();
        s.AddSingleton<ISeeder, ApplicationSettingsSeeder>();
        s.AddSingleton<ISeeder, LaboratoryTestSeeder>();
        s.AddSingleton<IDataSeeder, DataSeeder>();
        s.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        s.AddSingleton<IBackupService, BackupService>();
        return s;
    }
}
