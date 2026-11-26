using System.Text.RegularExpressions;
using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Application.Services;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using LaboratoryManagement.Infrastructure.Persistence;
using LaboratoryManagement.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
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
        return Regex.Replace(text, @"(?i)(password|pwd|key|user id|uid)\s*=\s*[^;]*", "$1=***");
    }
}

public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IDataSeeder _seeder;
    private readonly ILogger<DatabaseInitializer> _log;
    private readonly IServiceProvider _sp;

    public DatabaseInitializer(IDbContextFactory<AppDbContext> factory, IDataSeeder seeder,
                               ILogger<DatabaseInitializer> log, IServiceProvider sp)
    { _factory = factory; _seeder = seeder; _log = log; _sp = sp; }

    public async Task<InitResult> InitializeAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            progress?.Report("Reading database configuration...");
            await using var db = await _factory.CreateDbContextAsync(ct);

            progress?.Report("Checking database connectivity...");
            var exists = await db.Database.CanConnectAsync(ct);
            var hasMigrations = db.Database.GetMigrations().Any();
            if (!hasMigrations)
                throw new InvalidOperationException("No database migrations are available. Generate the InitialCreate migration before starting the application.");

            var pending = exists ? (await db.Database.GetPendingMigrationsAsync(ct)).ToList() : new List<string>();
            var applied = exists ? (await db.Database.GetAppliedMigrationsAsync(ct)).ToList() : new List<string>();
            if (exists && applied.Count > 0 && pending.Count > 0)
            {
                progress?.Report("Backing up the database before upgrade...");
                try
                {
                    var backup = _sp.GetRequiredService<IBackupService>();
                    var record = await backup.BackupAsync("PreUpgrade", null, ct);
                    if (!record.Success)
                        return new(false, false, "A backup could not be created before applying database updates, so the upgrade was stopped to protect your data.", record.Message);
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
    private readonly SqliteDatabaseRuntime _database;
    private readonly ILogger<BackupService> _log;
    private readonly ICurrentUser _user;
    private readonly SettingsService _settings;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public BackupService(IDbContextFactory<AppDbContext> factory, SqliteDatabaseRuntime database, ILogger<BackupService> log,
                         ICurrentUser user, SettingsService settings)
    { _factory = factory; _database = database; _log = log; _user = user; _settings = settings; }

    private async Task<SqliteConnection> OpenConnectionAsync(string databasePath, CancellationToken ct)
    {
        SqliteDatabaseRuntime.EnsureLocalPath(databasePath);
        var builder = new SqliteConnectionStringBuilder(_database.ConnectionString)
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            await connection.OpenAsync(ct);
            await _database.ConnectionInitializer.InitializeAsync(connection, ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

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
            var file = Path.Combine(dir, $"lab_{DateTime.Now:yyyyMMdd_HHmmss}_{kind}.db");
            SqliteDatabaseRuntime.EnsureLocalPath(file);
            rec.FilePath = file;
            await using (var conn = await OpenConnectionAsync(_database.DatabasePath, ct))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandTimeout = 600;
                cmd.CommandText = $"VACUUM INTO '{file.Replace("'", "''")}'";
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
            if (!File.Exists(filePath)) return (false, "Backup file does not exist.");
            await using var conn = await OpenConnectionAsync(filePath, ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 600;
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = Convert.ToString(await cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                return (false, "SQLite integrity_check returned: " + result);
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
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
        if (string.Equals(Path.GetFullPath(filePath), _database.DatabasePath, StringComparison.OrdinalIgnoreCase))
            return (false, "The selected file is already the active database.");

        // Safety copy of the current state first.
        var safety = await BackupAsync("PreUpgrade", null, ct);
        if (!safety.Success) return (false, "Restore cancelled: a safety backup of the current database could not be created. " + safety.Message);

        var safetyPath = safety.FilePath;
        await Gate.WaitAsync(ct);
        try
        {
            SqliteConnection.ClearAllPools();
            DeleteSidecars();
            File.Copy(filePath, _database.DatabasePath, true);
            var restored = await VerifyAsync(_database.DatabasePath, ct);
            if (!restored.Ok) throw new InvalidOperationException("Restored database failed integrity_check: " + restored.Message);
            _log.LogWarning("Database restored from {File}", filePath);
            return (true, "Database restored. Please restart the application now.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Restore failed");
            try
            {
                SqliteConnection.ClearAllPools();
                DeleteSidecars();
                File.Copy(safetyPath, _database.DatabasePath, true);
                var rollback = await VerifyAsync(_database.DatabasePath, CancellationToken.None);
                if (!rollback.Ok) throw new InvalidOperationException("Safety copy failed integrity_check: " + rollback.Message);
                return (false, "Restore failed; the safety copy was restored. " + ConnectionStringSafety.Redact(ex.GetBaseException().Message));
            }
            catch (Exception rollbackException)
            {
                _log.LogCritical(rollbackException, "Restore failed and the safety copy could not be restored");
                return (false, "Restore failed and recovery from the safety copy also failed. " + ConnectionStringSafety.Redact(rollbackException.GetBaseException().Message));
            }
        }
        finally { Gate.Release(); }

        void DeleteSidecars()
        {
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var path = _database.DatabasePath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
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
        var database = SqliteDatabaseRuntime.Create(cs);
        var interceptor = new SqliteConnectionInterceptor(database.ConnectionInitializer);
        s.AddSingleton(database);
        s.AddSingleton(interceptor);
        s.AddDbContextFactory<AppDbContext>(o =>
        {
            o.UseSqlite(database.ConnectionString, sqlite => sqlite.CommandTimeout(60));
            o.AddInterceptors(interceptor);
        });
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
