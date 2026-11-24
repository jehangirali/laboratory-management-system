using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LaboratoryManagement.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<LoginHistory> LoginHistory => Set<LoginHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<TestCategory> TestCategories => Set<TestCategory>();
    public DbSet<SpecimenType> SpecimenTypes => Set<SpecimenType>();
    public DbSet<SampleContainer> SampleContainers => Set<SampleContainer>();
    public DbSet<MeasurementUnit> MeasurementUnits => Set<MeasurementUnit>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<LabTest> LabTests => Set<LabTest>();
    public DbSet<PanelItem> PanelItems => Set<PanelItem>();
    public DbSet<TestParameter> TestParameters => Set<TestParameter>();
    public DbSet<ReferenceRange> ReferenceRanges => Set<ReferenceRange>();
    public DbSet<LabOrder> Orders => Set<LabOrder>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderTest> OrderTests => Set<OrderTest>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Sample> Samples => Set<Sample>();
    public DbSet<ResultValue> ResultValues => Set<ResultValue>();
    public DbSet<ResultRevision> ResultRevisions => Set<ResultRevision>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReportVersion> ReportVersions => Set<ReportVersion>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(256);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Safety: no cascade deletes anywhere. Patients, orders, payments, samples, results and reports are never
        // removed implicitly; deactivate instead.
        foreach (var fk in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
}

/// <summary>Used by `dotnet ef` only. Override the target with the LAB_CONNECTION environment variable.</summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("LAB_CONNECTION")
                 ?? @"Server=(localdb)\MSSQLLocalDB;Database=LaboratoryManagement;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;
        return new AppDbContext(options);
    }
}

public sealed class AppDbContextFactoryAdapter : IAppDbContextFactory
{
    private readonly IDbContextFactory<AppDbContext> _inner;
    public AppDbContextFactoryAdapter(IDbContextFactory<AppDbContext> inner) => _inner = inner;
    public IAppDbContext Create() => _inner.CreateDbContext();
}
