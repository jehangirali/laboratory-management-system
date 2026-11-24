using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Services;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
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
    }
}

public class WorkflowTests
{
    private static async Task<(TestEnv env, OrderService orders, int patientId, int cashId)> SetupAsync(params string[] perms)
    {
        var env = await TestEnv.CreateSeededAsync(perms);
        await env.SetPricesAsync(100);
        var patients = new PatientService(env.Factory, env.User, env.Audit, env.Settings);
        var p = await patients.SaveAsync(new Patient { FullName = "Test Patient", Gender = Gender.Male, DateOfBirth = DateTime.Today.AddYears(-30) });
        int cash; await using (var db = env.Factory.CreateDbContext()) cash = (await db.PaymentMethods.FirstAsync(m => m.Code == "CASH")).Id;
        return (env, new OrderService(env.Factory, env.User, env.Audit, env.Settings), p.Id, cash);
    }

    private static async Task<int> TestId(TestEnv env, string code) { await using var db = env.Factory.CreateDbContext(); return (await db.LabTests.FirstAsync(t => t.Code == code)).Id; }

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
    public async Task Disabled_workflow_steps_allow_direct_result_entry_and_print()
    {
        var env = await TestEnv.CreateSeededAsync();
        await env.SetPricesAsync(100);
        await env.Settings.SetInternalAsync(SettingKeys.RequireSampleCollection, "false");
        await env.Settings.SetInternalAsync(SettingKeys.RequireReceiveInLab, "false");
        await env.Settings.SetInternalAsync(SettingKeys.RequireVerification, "false");

        var patients = new PatientService(env.Factory, env.User, env.Audit, env.Settings);
        var pid = (await patients.SaveAsync(new Patient { FullName = "Direct Print", Gender = Gender.Male })).Id;
        var orders = new OrderService(env.Factory, env.User, env.Audit, env.Settings);
        var res = await orders.CreateAsync(new CreateOrderRequest { PatientId = pid, TestIds = new() { await TestId(env, "HB") } });
        var results = new ResultService(env.Factory, env.User, env.Audit, env.Settings);
        var worklist = await results.GetWorklistAsync(true);
        var otId = worklist.Single().OrderTestId;
        var sheet = await results.LoadSheetAsync(otId);
        sheet.Rows[0].Value = "14.2";

        await results.SaveAsync(otId, sheet.Rows, true);
        var reports = new ReportService(env.Factory, env.User, env.Settings, env.Audit);
        var model = await reports.BuildAsync(res.OrderId);

        Assert.True(model.IsVerified);
        Assert.True(model.IsDirectlyFinalized);
        Assert.Equal("14.2", model.Sections.Single().Rows.Single().Value);

        await using var db = env.Factory.Create();
        var finalizedTest = await db.OrderTests.SingleAsync(t => t.OrderId == res.OrderId);
        Assert.Equal(OrderTestStatus.Verified, finalizedTest.Status);
        Assert.True((await db.Reports.SingleAsync(r => r.OrderId == res.OrderId)).DirectlyFinalized);

        await env.Settings.SetInternalAsync(SettingKeys.RequireVerification, "true");
        var verification = new VerificationService(env.Factory, env.User, env.Audit, reports);
        Assert.Empty(await verification.GetPendingAsync());
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
        var patients = new PatientService(env.Factory, env.User, env.Audit, env.Settings);
        var p = await patients.SaveAsync(new Patient { FullName = "X", Gender = Gender.Female });
        var orders = new OrderService(env.Factory, env.User, env.Audit, env.Settings);
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
        var samples = new SampleService(env.Factory, env.User, env.Audit, env.Settings);
        var results = new ResultService(env.Factory, env.User, env.Audit, env.Settings);
        var reports = new ReportService(env.Factory, env.User, env.Settings, env.Audit);
        var verify = new VerificationService(env.Factory, env.User, env.Audit, reports);

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

        var versions = await reports.GetVersionsAsync(res.OrderId);
        Assert.Equal(2, versions.Count);
        var original = await reports.LoadVersionAsync(versions[0].Id);
        Assert.Equal("14.2", original.Sections[0].Rows[0].Value);       // original retained
        var revs = await results.GetRevisionsAsync(otId);
        Assert.Contains(revs, r => r.IsAmendment && r.OldValue == "14.2" && r.NewValue == "13.8");
    }
}
