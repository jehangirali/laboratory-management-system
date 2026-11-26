using LaboratoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LaboratoryManagement.Application.Interfaces;

public interface IAppDbContext : IDisposable, IAsyncDisposable
{
    DbSet<Permission> Permissions { get; }
    DbSet<Role> Roles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<User> Users { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<AppSetting> AppSettings { get; }
    DbSet<NumberSequence> NumberSequences { get; }
    DbSet<TestCategory> TestCategories { get; }
    DbSet<SpecimenType> SpecimenTypes { get; }
    DbSet<SampleContainer> SampleContainers { get; }
    DbSet<MeasurementUnit> MeasurementUnits { get; }
    DbSet<PaymentMethod> PaymentMethods { get; }
    DbSet<Patient> Patients { get; }
    DbSet<Doctor> Doctors { get; }
    DbSet<LabTest> LabTests { get; }
    DbSet<PanelItem> PanelItems { get; }
    DbSet<TestParameter> TestParameters { get; }
    DbSet<ReferenceRange> ReferenceRanges { get; }
    DbSet<LabOrder> Orders { get; }
    DbSet<OrderLine> OrderLines { get; }
    DbSet<OrderTest> OrderTests { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Sample> Samples { get; }
    DbSet<ResultValue> ResultValues { get; }
    DbSet<ResultRevision> ResultRevisions { get; }
    DbSet<Report> Reports { get; }
    DbSet<BackupRecord> BackupRecords { get; }

    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates short-lived contexts (one per operation), which suits a desktop app.</summary>
public interface IAppDbContextFactory
{
    IAppDbContext Create();
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    int UserId { get; }
    string Username { get; }
    string FullName { get; }
    string? Designation { get; }
    string? SignatureText { get; }
    IReadOnlyCollection<string> Roles { get; }
    bool Has(string permission);
    /// <summary>Throws <see cref="Common.PermissionDeniedException"/> when the permission is missing.</summary>
    void Require(string permission);
    void Set(int userId, string username, string fullName, string? designation, string? signature,
             IEnumerable<string> roles, IEnumerable<string> permissions);
    void Clear();
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface IBackupService
{
    Task<BackupRecord> BackupAsync(string kind, string? folder = null, CancellationToken ct = default);
    Task<IReadOnlyList<BackupRecord>> GetHistoryAsync(CancellationToken ct = default);
    Task<(bool Ok, string Message)> VerifyAsync(string filePath, CancellationToken ct = default);
    /// <summary>Restores the database from a backup file. The application should be restarted afterwards.</summary>
    Task<(bool Ok, string Message)> RestoreAsync(string filePath, CancellationToken ct = default);
    Task RunScheduledIfDueAsync(CancellationToken ct = default);
}

public record InitResult(bool Success, bool AdminExists, string? Error, string? Diagnostics);

public interface IDatabaseInitializer
{
    Task<InitResult> InitializeAsync(IProgress<string>? progress = null, CancellationToken ct = default);
}
