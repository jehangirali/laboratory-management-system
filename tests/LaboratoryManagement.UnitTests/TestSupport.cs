using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Application.Services;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using LaboratoryManagement.Infrastructure.Persistence;
using LaboratoryManagement.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LaboratoryManagement.UnitTests;

public sealed class TestFactory : IAppDbContextFactory, IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;
    public TestFactory(string name = null!)
        => _options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name ?? Guid.NewGuid().ToString()).Options;
    public IAppDbContext Create() => new AppDbContext(_options);
    public AppDbContext CreateDbContext() => new(_options);
}

public sealed class TestEnv
{
    public TestFactory Factory { get; } = new();
    public CurrentUser User { get; } = new();
    public IAuditService Audit { get; }
    public SettingsService Settings { get; }
    public DataSeeder Seeder { get; }

    public TestEnv()
    {
        Audit = new AuditService(Factory, User);
        Settings = new SettingsService(Factory, User, Audit);
        Seeder = new DataSeeder(Factory, AllSeeders(), NullLogger<DataSeeder>.Instance);
    }

    public static ISeeder[] AllSeeders() => new ISeeder[]
    {
        new PermissionSeeder(), new RoleSeeder(), new UnitSeeder(), new SpecimenSeeder(), new TestCategorySeeder(),
        new ApplicationSettingsSeeder(), new LaboratoryTestSeeder()
    };

    public static async Task<TestEnv> CreateSeededAsync(params string[] permissions)
    {
        var env = new TestEnv();
        await env.Seeder.SeedAsync();
        var perms = permissions.Length == 0 ? Permissions.All.Select(p => p.Code).ToArray() : permissions;
        env.User.Set(1, "tester", "Test User", "Pathologist", "Dr Test", new[] { "Administrator" }, perms);
        await env.Settings.RefreshAsync();
        return env;
    }

    public async Task SetPricesAsync(decimal price)
    {
        await using var db = Factory.Create();
        foreach (var t in await db.LabTests.ToListAsync()) { t.Price = price; }
        await db.SaveChangesAsync();
    }
}
