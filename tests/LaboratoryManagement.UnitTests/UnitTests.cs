using System.Globalization;
using System.Text;
using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Application.Services;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using LaboratoryManagement.Infrastructure.Persistence;
using LaboratoryManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LaboratoryManagement.UnitTests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _h = new();

    [Fact]
    public void Hash_then_verify_succeeds_and_wrong_password_fails()
    {
        var hash = _h.Hash("Correct-Horse-9");
        Assert.True(_h.Verify("Correct-Horse-9", hash));
        Assert.False(_h.Verify("correct-horse-9", hash));
        Assert.DoesNotContain("Correct-Horse-9", hash);
    }

    [Fact]
    public void Same_password_produces_different_hashes()
        => Assert.NotEqual(_h.Hash("Abcdef12"), _h.Hash("Abcdef12"));

    [Theory]
    [InlineData("short1")]
    [InlineData("alllettersnodigits")]
    [InlineData("1234567890")]
    public void Weak_passwords_are_rejected(string pw) => Assert.NotNull(AuthService.ValidatePassword(pw));

    [Fact]
    public void Strong_password_is_accepted() => Assert.Null(AuthService.ValidatePassword("Lab-pass-2026"));
}

public class MoneyTests
{
    [Theory]
    [InlineData("1250", true, 1250L)]
    [InlineData("1,250", true, 1250L)]
    [InlineData("12.50", false, 0L)]
    [InlineData("", false, 0L)]
    public void Whole_rupee_parser_rejects_fractional_values(string text, bool expected, long amount)
    {
        var parsed = MoneyParser.TryParseWholeRupees(text, CultureInfo.InvariantCulture, out var actual);
        Assert.Equal(expected, parsed);
        Assert.Equal(amount, actual);
    }

    [Fact]
    public async Task Sqlcipher_file_requires_key_and_migrations_keep_queries_translatable()
    {
        SQLitePCL.Batteries_V2.Init();
        var directory = Path.Combine(Path.GetTempPath(), "LaboratoryManagementTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "lab.db");
        try
        {
            var runtime = SqliteDatabaseRuntime.Create(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await using (var connection = new SqliteConnection(runtime.ConnectionString))
            {
                await connection.OpenAsync();
                await runtime.ConnectionInitializer.InitializeAsync(connection);
                Assert.Equal("wal", await ScalarAsync(connection, "PRAGMA journal_mode;"));
                Assert.Equal(2L, await ScalarAsync(connection, "PRAGMA synchronous;"));
                Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys;"));
                Assert.Equal(5000L, await ScalarAsync(connection, "PRAGMA busy_timeout;"));
            }

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(runtime.ConnectionString)
                .AddInterceptors(new SqliteConnectionInterceptor(runtime.ConnectionInitializer))
                .Options;
            await using (var db = new AppDbContext(options))
            {
                await db.Database.MigrateAsync();
                Assert.Contains("20261125180034_RemoveAuditAndReportVersions", await db.Database.GetAppliedMigrationsAsync());

                var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = start.AddDays(1);
                var patient = new Patient { PatientNo = "P-TEST", FullName = "SQLite Test" };
                var method = new PaymentMethod { Code = "CASH", Name = "Cash" };
                var order = new LabOrder
                {
                    Patient = patient, OrderNo = "ORD-TEST", OrderedAt = start.AddHours(12),
                    Subtotal = 500, NetAmount = 500, PaidAmount = 125, Balance = 375
                };
                db.Orders.Add(order);
                db.Payments.Add(new Payment
                {
                    Order = order, ReceiptNo = "RCP-TEST", Amount = 125, PaymentMethod = method,
                    PaidAt = start.AddHours(12), ReceivedByUserId = 1
                });
                await db.SaveChangesAsync();

                var outstanding = await db.Orders.Where(x => x.OrderedAt >= start && x.OrderedAt < end)
                    .SumAsync(x => (long?)x.Balance);
                var collected = await db.Payments.Where(x => x.PaidAt >= start && x.PaidAt < end)
                    .SumAsync(x => x.Type == PaymentType.Payment ? x.Amount : -x.Amount);
                var grouped = await db.Orders.Where(x => x.OrderedAt >= start && x.OrderedAt < end)
                    .GroupBy(x => x.Status).Select(g => new { g.Key, Total = g.Sum(x => x.Balance) }).SingleAsync();
                Assert.Equal(375L, outstanding);
                Assert.Equal(125L, collected);
                Assert.Equal(375L, grouped.Total);
            }

            var backupPath = Path.Combine(directory, "backup.db");
            await using (var source = new SqliteConnection(runtime.ConnectionString))
            {
                await source.OpenAsync();
                await runtime.ConnectionInitializer.InitializeAsync(source);
                await using var command = source.CreateCommand();
                command.CommandText = $"VACUUM INTO '{backupPath.Replace("'", "''")}'";
                await command.ExecuteNonQueryAsync();
            }
            await using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Pooling = false }.ToString()))
            {
                await backup.OpenAsync();
                await runtime.ConnectionInitializer.InitializeAsync(backup);
                Assert.Equal("ok", await ScalarAsync(backup, "PRAGMA integrity_check;"));
            }

            var header = await File.ReadAllBytesAsync(path);
            Assert.NotEqual("SQLite format 3\0", Encoding.ASCII.GetString(header, 0, 16));
            await using var plain = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await plain.OpenAsync();
            await using var commandWithoutKey = plain.CreateCommand();
            commandWithoutKey.CommandText = "SELECT name FROM sqlite_master;";
            await Assert.ThrowsAsync<SqliteException>(() => commandWithoutKey.ExecuteScalarAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Backup_and_restore_preserve_the_encrypted_database_state()
    {
        SQLitePCL.Batteries_V2.Init();
        var directory = Path.Combine(Path.GetTempPath(), "LaboratoryManagementTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var runtime = SqliteDatabaseRuntime.Create(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "lab.db"), Pooling = false
            }.ToString());
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(runtime.ConnectionString)
                .AddInterceptors(new SqliteConnectionInterceptor(runtime.ConnectionInitializer))
                .Options;
            var factory = new SqliteTestFactory(options);
            await using (var db = factory.CreateDbContext()) await db.Database.MigrateAsync();
            await using (var db = factory.CreateDbContext())
            {
                db.AppSettings.Add(new AppSetting { Key = SettingKeys.BackupFolder, Value = directory });
                db.AppSettings.Add(new AppSetting { Key = "Restore.Test", Value = "before" });
                await db.SaveChangesAsync();
            }

            var user = new CurrentUser();
            user.Set(1, "tester", "Test User", "Administrator", "Signature", new[] { "Administrator" }, Permissions.All.Select(p => p.Code).ToArray());
            var settings = new SettingsService(factory, user);
            await settings.RefreshAsync();
            var backupService = new BackupService(factory, runtime, NullLogger<BackupService>.Instance, user, settings);
            var backup = await backupService.BackupAsync("Manual", directory);
            Assert.True(backup.Success, backup.Message);
            await using (var db = factory.CreateDbContext())
            {
                (await db.AppSettings.SingleAsync(x => x.Key == "Restore.Test")).Value = "after";
                await db.SaveChangesAsync();
            }

            var restore = await backupService.RestoreAsync(backup.FilePath);
            Assert.True(restore.Ok, restore.Message);
            await using var verify = factory.CreateDbContext();
            Assert.Equal("before", (await verify.AppSettings.SingleAsync(x => x.Key == "Restore.Test")).Value);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}

internal sealed class SqliteTestFactory : IAppDbContextFactory, IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;
    public SqliteTestFactory(DbContextOptions<AppDbContext> options) => _options = options;
    public IAppDbContext Create() => CreateDbContext();
    public AppDbContext CreateDbContext() => new(_options);
}

public class ReferenceRangeEvaluatorTests
{
    private static ReferenceRange R(decimal? lo, decimal? hi, decimal? cl = null, decimal? ch = null, Gender? g = null, decimal min = 0, decimal? max = null)
        => new() { Low = lo, High = hi, CriticalLow = cl, CriticalHigh = ch, Gender = g, MinAgeYears = min, MaxAgeYears = max };

    [Theory]
    [InlineData("14", ResultFlag.None)]
    [InlineData("11", ResultFlag.Low)]
    [InlineData("18", ResultFlag.High)]
    [InlineData("6.5", ResultFlag.CriticalLow)]
    [InlineData("21", ResultFlag.CriticalHigh)]
    [InlineData("abc", ResultFlag.None)]
    [InlineData("", ResultFlag.None)]
    public void Numeric_values_are_flagged_against_configured_range(string value, ResultFlag expected)
        => Assert.Equal(expected, ReferenceRangeEvaluator.Evaluate(ResultType.Numeric, value, R(13, 17, 7, 20)));

    [Fact]
    public void No_range_means_no_flag() => Assert.Equal(ResultFlag.None, ReferenceRangeEvaluator.Evaluate(ResultType.Numeric, "5", null));

    [Fact]
    public void Qualitative_abnormal_when_different_from_normal_text()
    {
        var r = new ReferenceRange { NormalText = "Non-Reactive" };
        Assert.Equal(ResultFlag.Abnormal, ReferenceRangeEvaluator.Evaluate(ResultType.ReactiveNonReactive, "Reactive", r));
        Assert.Equal(ResultFlag.None, ReferenceRangeEvaluator.Evaluate(ResultType.ReactiveNonReactive, "non-reactive", r));
    }

    [Fact]
    public void Gender_specific_range_is_preferred()
    {
        var any = R(1, 2); var male = R(13, 17, g: Gender.Male); var female = R(12, 15, g: Gender.Female);
        var list = new[] { any, male, female };
        Assert.Same(male, ReferenceRangeEvaluator.Select(list, Gender.Male, 30));
        Assert.Same(female, ReferenceRangeEvaluator.Select(list, Gender.Female, 30));
    }

    [Fact]
    public void Other_gender_displays_both_sex_specific_ranges_without_selecting_one_for_flags()
    {
        var male = R(13, 17, g: Gender.Male);
        var female = R(12, 15, g: Gender.Female);
        var ranges = new[] { male, female };

        Assert.Null(ReferenceRangeEvaluator.Select(ranges, Gender.Other, 30));
        Assert.Equal("Male: 13 - 17; Female: 12 - 15",
            ReferenceRangeEvaluator.FormatForPatient(ranges, Gender.Other, 30));
    }

    [Fact]
    public void Other_gender_flags_values_only_when_outside_both_sex_specific_ranges()
    {
        var ranges = new[]
        {
            R(13, 17, 7, 20, Gender.Male),
            R(12, 15, 7, 20, Gender.Female)
        };

        Assert.Equal(ResultFlag.None, ReferenceRangeEvaluator.EvaluateForPatient(ResultType.Numeric, "16", ranges, Gender.Other, 30));
        Assert.Equal(ResultFlag.Low, ReferenceRangeEvaluator.EvaluateForPatient(ResultType.Numeric, "11", ranges, Gender.Other, 30));
        Assert.Equal(ResultFlag.High, ReferenceRangeEvaluator.EvaluateForPatient(ResultType.Numeric, "18", ranges, Gender.Other, 30));
        Assert.Equal(ResultFlag.CriticalLow, ReferenceRangeEvaluator.EvaluateForPatient(ResultType.Numeric, "6", ranges, Gender.Other, 30));
    }

    [Fact]
    public void Age_bands_select_the_matching_range()
    {
        var child = R(10, 14, min: 0, max: 12); var adult = R(13, 17, min: 12);
        Assert.Same(child, ReferenceRangeEvaluator.Select(new[] { child, adult }, Gender.Male, 5));
        Assert.Same(adult, ReferenceRangeEvaluator.Select(new[] { child, adult }, Gender.Male, 40));
    }

    [Fact]
    public void Range_text_formats()
    {
        Assert.Equal("13 - 17", ReferenceRangeEvaluator.FormatRange(R(13, 17)));
        Assert.Equal("< 200", ReferenceRangeEvaluator.FormatRange(R(null, 200)));
        Assert.Equal("> 40", ReferenceRangeEvaluator.FormatRange(R(40, null)));
    }
}

public class SeedingTests
{
    [Fact]
    public async Task Seeding_twice_creates_no_duplicates()
    {
        var env = new TestEnv();
        await env.Seeder.SeedAsync();
        int Count(Func<LaboratoryManagement.Infrastructure.Persistence.AppDbContext, int> f) { using var db = env.Factory.CreateDbContext(); return f(db); }
        var before = (Count(d => d.LabTests.Count()), Count(d => d.Permissions.Count()), Count(d => d.Roles.Count()), Count(d => d.AppSettings.Count()), Count(d => d.ReferenceRanges.Count()));
        await env.Seeder.SeedAsync();
        var after = (Count(d => d.LabTests.Count()), Count(d => d.Permissions.Count()), Count(d => d.Roles.Count()), Count(d => d.AppSettings.Count()), Count(d => d.ReferenceRanges.Count()));
        Assert.Equal(before, after);
        Assert.True(before.Item1 > 40);
    }

    [Fact]
    public async Task Reseeding_does_not_overwrite_administrator_edits()
    {
        var env = new TestEnv();
        await env.Seeder.SeedAsync();
        await using (var db = env.Factory.CreateDbContext())
        {
            var t = await db.LabTests.FirstAsync(x => x.Code == "CBC"); t.Price = 777; t.Name = "My CBC";
            (await db.AppSettings.FirstAsync(s => s.Key == SettingKeys.LabName)).Value = "Acme Lab";
            await db.SaveChangesAsync();
        }
        await env.Seeder.SeedAsync();
        await using var check = env.Factory.CreateDbContext();
        Assert.Equal(777, (await check.LabTests.FirstAsync(x => x.Code == "CBC")).Price);
        Assert.Equal("My CBC", (await check.LabTests.FirstAsync(x => x.Code == "CBC")).Name);
        Assert.Equal("Acme Lab", (await check.AppSettings.FirstAsync(s => s.Key == SettingKeys.LabName)).Value);
    }

    [Fact]
    public async Task Seeded_prices_are_zero_and_ranges_are_flagged_as_examples()
    {
        var env = new TestEnv(); await env.Seeder.SeedAsync();
        await using var db = env.Factory.CreateDbContext();
        Assert.All(await db.LabTests.ToListAsync(), t => Assert.Equal(0, t.Price));
        Assert.All(await db.ReferenceRanges.ToListAsync(), r => Assert.True(r.IsExample));
    }

    [Fact]
    public async Task Panels_reference_component_tests_and_own_no_parameters()
    {
        var env = new TestEnv(); await env.Seeder.SeedAsync();
        await using var db = env.Factory.CreateDbContext();
        var lft = await db.LabTests.Include(t => t.PanelItems).Include(t => t.Parameters).FirstAsync(t => t.Code == "LFT");
        Assert.True(lft.IsPanel);
        Assert.Empty(lft.Parameters);
        Assert.True(lft.PanelItems.Count >= 5);
    }

    [Fact]
    public async Task Default_roles_receive_permissions_and_administrator_has_all()
    {
        var env = new TestEnv(); await env.Seeder.SeedAsync();
        await using var db = env.Factory.CreateDbContext();
        var admin = await db.Roles.Include(r => r.RolePermissions).FirstAsync(r => r.Name == SystemRoles.Administrator);
        Assert.Equal(Permissions.All.Count, admin.RolePermissions.Count);
        var recep = await db.Roles.Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission).FirstAsync(r => r.Name == SystemRoles.Receptionist);
        Assert.DoesNotContain(recep.RolePermissions, rp => rp.Permission.Code == Permissions.ResultsVerify);
        Assert.Equal("false", await db.AppSettings.Where(s => s.Key == SettingKeys.FullWorkflowEnabled).Select(s => s.Value).SingleAsync());
    }
}

public class WorkflowTests
{
    [Fact]
    public async Task Full_workflow_setting_applies_after_refresh()
    {
        var env = await TestEnv.CreateSeededAsync();
        Assert.False(env.Settings.FullWorkflow);

        await env.Settings.SaveAsync(new[]
        {
            new AppSetting { Key = SettingKeys.FullWorkflowEnabled, Value = "true" }
        });
        Assert.False(env.Settings.FullWorkflow);

        await env.Settings.RefreshAsync();
        Assert.True(env.Settings.FullWorkflow);
    }

    [Fact]
    public async Task Missing_full_workflow_setting_defaults_to_simplified_mode()
    {
        var env = await TestEnv.CreateSeededAsync();
        await using (var db = env.Factory.CreateDbContext())
        {
            db.AppSettings.Remove(await db.AppSettings.SingleAsync(s => s.Key == SettingKeys.FullWorkflowEnabled));
            await db.SaveChangesAsync();
        }

        await env.Settings.RefreshAsync();
        Assert.False(env.Settings.FullWorkflow);
    }

    private static async Task<(TestEnv env, OrderService orders, int patientId, int cashId)> SetupAsync(params string[] perms)
    {
        var env = await TestEnv.CreateSeededAsync(perms);
        await env.Settings.SetInternalAsync(SettingKeys.FullWorkflowEnabled, "true");
        await env.SetPricesAsync(100);
        var patients = new PatientService(env.Factory, env.User, env.Settings);
        var p = await patients.SaveAsync(new Patient { FullName = "Test Patient", Gender = Gender.Male, DateOfBirth = DateTime.Today.AddYears(-30) });
        int cash; await using (var db = env.Factory.CreateDbContext()) cash = (await db.PaymentMethods.FirstAsync(m => m.Code == "CASH")).Id;
        return (env, new OrderService(env.Factory, env.User, env.Settings), p.Id, cash);
    }

    private static async Task<int> TestId(TestEnv env, string code) { await using var db = env.Factory.CreateDbContext(); return (await db.LabTests.FirstAsync(t => t.Code == code)).Id; }

    [Fact]
    public async Task Percentage_commission_rounds_half_rupee_away_from_zero()
    {
        var (env, orders, patientId, _) = await SetupAsync();
        await env.SetPricesAsync(1);
        await env.Settings.SetInternalAsync(SettingKeys.CommissionEnabled, "true");
        var doctor = await new DoctorService(env.Factory, env.User, env.Settings).SaveAsync(new Doctor
        {
            Name = "Commission test", CommissionType = CommissionType.Percentage, CommissionValue = 50
        });
        await orders.CreateAsync(new CreateOrderRequest
        {
            PatientId = patientId, DoctorId = doctor.Id, TestIds = new() { await TestId(env, "HB") }
        });

        var report = new OperationalReportService(env.Factory, env.User, env.Settings);
        var rows = await report.RunAsync("doctors", DateTime.Today, DateTime.Today);
        Assert.Equal(1L, rows.Rows[0]["Commission (calculated)"]);
    }

    [Fact]
    public async Task Order_creation_expands_panels_groups_samples_and_tracks_payment()
    {
        var (env, orders, pid, cash) = await SetupAsync();
        var res = await orders.CreateAsync(new CreateOrderRequest
        {
            PatientId = pid, TestIds = new() { await TestId(env, "CBC"), await TestId(env, "LFT") }, Discount = 20, InitialPayment = 80, PaymentMethodId = cash
        });
        var o = await orders.GetDetailAsync(res.OrderId);
        Assert.Equal(200, o.Subtotal); Assert.Equal(180, o.NetAmount); Assert.Equal(80, o.PaidAmount); Assert.Equal(100, o.Balance);
        Assert.Equal(PaymentStatus.Partial, o.PaymentStatus);
        Assert.Equal(2, o.Lines.Count);
        Assert.True(o.Tests.Count >= 6);            // CBC + LFT components
        Assert.Equal(o.Samples.Count, o.Samples.Select(s => s.Barcode).Distinct().Count());
        Assert.All(o.Samples, s => Assert.Equal(SampleStatus.PendingCollection, s.Status));
        Assert.Single(o.Payments);
    }

    [Fact]
    public async Task Order_search_defaults_to_all_dates_and_users()
    {
        var (env, orders, patientId, _) = await SetupAsync();
        var currentUserOrder = await orders.CreateAsync(new CreateOrderRequest
        {
            PatientId = patientId, TestIds = new() { await TestId(env, "HB") }
        });
        var anotherUserOrder = await orders.CreateAsync(new CreateOrderRequest
        {
            PatientId = patientId, TestIds = new() { await TestId(env, "HB") }
        });

        await using (var db = env.Factory.CreateDbContext())
        {
            var other = await db.Orders.SingleAsync(o => o.Id == anotherUserOrder.OrderId);
            other.CreatedByUserId = 2;
            other.OrderedAt = DateTime.UtcNow.AddDays(-30);
            await db.SaveChangesAsync();
        }

        var list = await orders.SearchAsync(new OrderFilter());
        Assert.Contains(list, o => o.Id == currentUserOrder.OrderId);
        Assert.Contains(list, o => o.Id == anotherUserOrder.OrderId);
    }

    [Fact]
    public async Task Order_creation_uses_or_creates_patient_and_doctor_from_names()
    {
        var (env, orders, _, _) = await SetupAsync(Permissions.OrdersCreate, Permissions.PatientsManage);
        var testId = await TestId(env, "HB");

        var first = await orders.CreateAsync(new CreateOrderRequest
        {
            PatientName = "Name Entry Patient", DoctorName = "Name Entry Doctor", TestIds = new() { testId }
        });
        var second = await orders.CreateAsync(new CreateOrderRequest
        {
            PatientName = "name entry patient", DoctorName = "name entry doctor", TestIds = new() { testId }
        });

        await using var db = env.Factory.CreateDbContext();
        var patient = await db.Patients.SingleAsync(p => p.FullName == "Name Entry Patient");
        var doctor = await db.Doctors.SingleAsync(d => d.Name == "Name Entry Doctor");
        Assert.Equal(Gender.Other, patient.Gender);
        Assert.Equal(patient.Id, (await db.Orders.SingleAsync(o => o.Id == first.OrderId)).PatientId);
        Assert.Equal(patient.Id, (await db.Orders.SingleAsync(o => o.Id == second.OrderId)).PatientId);
        Assert.Equal(doctor.Id, (await db.Orders.SingleAsync(o => o.Id == first.OrderId)).DoctorId);
        Assert.Equal(doctor.Id, (await db.Orders.SingleAsync(o => o.Id == second.OrderId)).DoctorId);
    }

    [Fact]
    public async Task Simplified_workflow_saves_completes_and_amends_without_verification_permission()
    {
        var (env, orders, pid, _) = await SetupAsync(Permissions.OrdersCreate, Permissions.PatientsManage,
            Permissions.ResultsEnter, Permissions.ReportsPrint);
        await env.Settings.SetInternalAsync(SettingKeys.FullWorkflowEnabled, "false");
        Assert.False(env.User.Has(Permissions.ResultsVerify));
        var res = await orders.CreateAsync(new CreateOrderRequest { PatientId = pid, TestIds = new() { await TestId(env, "HB") } });
        var reports = new ReportService(env.Factory, env.User, env.Settings);
        var results = new ResultService(env.Factory, env.User, env.Settings, reports);
        var sample = (await new SampleService(env.Factory, env.User, env.Settings).GetByOrderAsync(res.OrderId)).Single();
        Assert.Equal(SampleStatus.ReceivedInLab, sample.Status);
        Assert.NotNull(sample.CollectedAt);
        Assert.NotNull(sample.ReceivedAt);

        var worklist = await results.GetWorklistAsync(false);
        Assert.Equal(OrderTestStatus.Pending, worklist.Single().Status);
        var otId = worklist.Single().OrderTestId;
        var sheet = await results.LoadSheetAsync(otId);
        Assert.False(sheet.IsLocked);
        sheet.Rows[0].Value = "14.2";

        await results.SaveAsync(otId, sheet.Rows, false);

        await using var db = env.Factory.Create();
        var finalizedTest = await db.OrderTests.SingleAsync(t => t.OrderId == res.OrderId);
        Assert.Equal(OrderTestStatus.Verified, finalizedTest.Status);
        Assert.Equal(OrderStatus.Completed, (await db.Orders.SingleAsync(o => o.Id == res.OrderId)).Status);
        var storedSample = await db.Samples.SingleAsync(s => s.Id == sample.SampleId);
        Assert.Equal(env.User.UserId, storedSample.CollectedByUserId);
        Assert.Equal(env.User.UserId, storedSample.ReceivedByUserId);
        var report = await db.Reports.SingleAsync(r => r.OrderId == res.OrderId);
        Assert.Equal(ReportStatus.Verified, report.Status);
        Assert.True(report.DirectlyFinalized);
        Assert.True((await reports.BuildAsync(res.OrderId)).IsVerified);
        var firstRevisions = await results.GetRevisionsAsync(otId);
        Assert.Contains(firstRevisions, r => r.NewValue == "14.2" && !r.IsAmendment);

        var sheetAgain = await results.LoadSheetAsync(otId);
        sheetAgain.Rows[0].Value = "15.0";
        await Assert.ThrowsAsync<BusinessRuleException>(() => results.SaveAsync(otId, sheetAgain.Rows, false));
        await results.SaveAsync(otId, sheetAgain.Rows, false, "Transcription correction");

        var latest = await reports.BuildAsync(res.OrderId);
        Assert.True(latest.IsAmended);
        Assert.Equal("15.0", latest.Sections.Single().Rows.Single().Value);
        var revisions = await results.GetRevisionsAsync(otId);
        Assert.Contains(revisions, r => r.IsAmendment && r.OldValue == "14.2" && r.NewValue == "15.0"
            && r.Reason == "Transcription correction");
    }

    [Fact]
    public async Task Result_worklists_separate_pending_and_completed_and_sort_newest_first()
    {
        var (env, orders, patientId, _) = await SetupAsync(Permissions.OrdersCreate, Permissions.PatientsManage, Permissions.ResultsEnter);
        var testId = await TestId(env, "HB");
        var oldPending = await orders.CreateAsync(new CreateOrderRequest { PatientId = patientId, TestIds = new() { testId } });
        var newPending = await orders.CreateAsync(new CreateOrderRequest { PatientId = patientId, TestIds = new() { testId } });
        var oldCompleted = await orders.CreateAsync(new CreateOrderRequest { PatientId = patientId, TestIds = new() { testId } });
        var newCompleted = await orders.CreateAsync(new CreateOrderRequest { PatientId = patientId, TestIds = new() { testId } });

        await using (var db = env.Factory.CreateDbContext())
        {
            var orderedAt = DateTime.UtcNow;
            var dates = new Dictionary<int, DateTime>
            {
                [oldPending.OrderId] = orderedAt.AddDays(-4),
                [newPending.OrderId] = orderedAt.AddDays(-1),
                [oldCompleted.OrderId] = orderedAt.AddDays(-3),
                [newCompleted.OrderId] = orderedAt.AddDays(-2)
            };
            foreach (var order in await db.Orders.Where(o => dates.Keys.Contains(o.Id)).ToListAsync())
                order.OrderedAt = dates[order.Id];
            foreach (var test in await db.OrderTests.Where(t => t.OrderId == oldCompleted.OrderId || t.OrderId == newCompleted.OrderId).ToListAsync())
                test.Status = OrderTestStatus.Verified;
            await db.SaveChangesAsync();
        }

        var reports = new ReportService(env.Factory, env.User, env.Settings);
        var results = new ResultService(env.Factory, env.User, env.Settings, reports);
        var pending = await results.GetWorklistAsync(false);
        var completed = await results.GetWorklistAsync(false, completed: true);

        Assert.Equal(new[] { newPending.OrderId, oldPending.OrderId }, pending.Select(x => x.OrderId));
        Assert.Equal(new[] { newCompleted.OrderId, oldCompleted.OrderId }, completed.Select(x => x.OrderId));
    }

    [Fact]
    public async Task Same_test_in_panel_and_individually_is_rejected()
    {
        var (env, orders, pid, _) = await SetupAsync();
        var lftId = await TestId(env, "LFT");
        var altId = await TestId(env, "ALT");
        await Assert.ThrowsAsync<BusinessRuleException>(() => orders.CreateAsync(new CreateOrderRequest
        { PatientId = pid, TestIds = new() { lftId, altId } }));
    }

    [Fact]
    public async Task Unpriced_tests_cannot_be_billed()
    {
        var env = await TestEnv.CreateSeededAsync();
        var patients = new PatientService(env.Factory, env.User, env.Settings);
        var p = await patients.SaveAsync(new Patient { FullName = "X", Gender = Gender.Female });
        var orders = new OrderService(env.Factory, env.User, env.Settings);
        await Assert.ThrowsAsync<BusinessRuleException>(() => orders.CreateAsync(new CreateOrderRequest { PatientId = p.Id, TestIds = new() { TestIdSync(env, "CBC") } }));
    }
    private static int TestIdSync(TestEnv env, string code) { using var db = env.Factory.CreateDbContext(); return db.LabTests.First(t => t.Code == code).Id; }

    [Fact]
    public async Task Permissions_are_enforced_in_services()
    {
        var (env, orders, pid, cash) = await SetupAsync(Permissions.OrdersCreate, Permissions.PatientsManage, Permissions.PatientsView, Permissions.TestsView);
        var cbc = await TestId(env, "CBC");
        await Assert.ThrowsAsync<PermissionDeniedException>(() => orders.CreateAsync(new CreateOrderRequest { PatientId = pid, TestIds = new() { cbc }, Discount = 10 }));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => orders.CreateAsync(new CreateOrderRequest { PatientId = pid, TestIds = new() { cbc }, InitialPayment = 10, PaymentMethodId = cash }));
        var ok = await orders.CreateAsync(new CreateOrderRequest { PatientId = pid, TestIds = new() { cbc } });
        await Assert.ThrowsAsync<PermissionDeniedException>(() => orders.CancelAsync(ok.OrderId, "x"));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => orders.RefundAsync(ok.OrderId, 1, cash, "x"));
    }

    [Fact]
    public async Task Refund_requires_reason_and_cannot_exceed_paid_and_payments_are_never_edited()
    {
        var (env, orders, pid, cash) = await SetupAsync();
        var r = await orders.CreateAsync(new CreateOrderRequest { PatientId = pid, TestIds = new() { await TestId(env, "HB") }, InitialPayment = 100, PaymentMethodId = cash });
        await Assert.ThrowsAsync<BusinessRuleException>(() => orders.RefundAsync(r.OrderId, 10, cash, " "));
        await Assert.ThrowsAsync<BusinessRuleException>(() => orders.RefundAsync(r.OrderId, 500, cash, "too much"));
        await orders.RefundAsync(r.OrderId, 100, cash, "Patient request");
        var o = await orders.GetDetailAsync(r.OrderId);
        Assert.Equal(PaymentStatus.Refunded, o.PaymentStatus);
        Assert.Equal(2, o.Payments.Count);                       // original payment is kept, refund is a new row
    }

    [Fact]
    public async Task Result_entry_verification_lock_and_amendment_keep_history()
    {
        var (env, orders, pid, cash) = await SetupAsync();
        var res = await orders.CreateAsync(new CreateOrderRequest { PatientId = pid, TestIds = new() { await TestId(env, "HB") } });
        var samples = new SampleService(env.Factory, env.User, env.Settings);
        var reports = new ReportService(env.Factory, env.User, env.Settings);
        var results = new ResultService(env.Factory, env.User, env.Settings, reports);
        var verify = new VerificationService(env.Factory, env.User, reports);

        var sample = (await samples.GetByOrderAsync(res.OrderId)).Single();
        // Results cannot be saved before the sample is received.
        var otId = (await results.GetWorklistAsync(true)).Single().OrderTestId;
        var sheet = await results.LoadSheetAsync(otId);
        sheet.Rows[0].Value = "14.2";
        await Assert.ThrowsAsync<BusinessRuleException>(() => results.SaveAsync(otId, sheet.Rows, false));

        await samples.CollectAsync(sample.SampleId); await samples.ReceiveAsync(sample.SampleId);
        await results.SaveAsync(otId, sheet.Rows, true);
        await verify.VerifyAsync(res.OrderId);

        // Locked after verification.
        sheet = await results.LoadSheetAsync(otId);
        Assert.True(sheet.IsLocked);
        sheet.Rows[0].Value = "15.0";
        await Assert.ThrowsAsync<BusinessRuleException>(() => results.SaveAsync(otId, sheet.Rows, true));

        // Controlled amendment.
        await Assert.ThrowsAsync<BusinessRuleException>(() => verify.BeginAmendmentAsync(res.OrderId, " "));
        await verify.BeginAmendmentAsync(res.OrderId, "Transcription error");
        sheet = await results.LoadSheetAsync(otId);
        sheet.Rows[0].Value = "13.8";
        await results.SaveAsync(otId, sheet.Rows, true);
        await verify.VerifyAsync(res.OrderId);

        var latest = await reports.BuildAsync(res.OrderId);
        Assert.True(latest.IsAmended);
        Assert.Equal("13.8", latest.Sections[0].Rows[0].Value);
        var revs = await results.GetRevisionsAsync(otId);
        Assert.Contains(revs, r => r.IsAmendment && r.OldValue == "14.2" && r.NewValue == "13.8");
    }
}
