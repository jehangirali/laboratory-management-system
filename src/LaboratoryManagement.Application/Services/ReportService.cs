using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LaboratoryManagement.Application.Services;

public class ReportRowModel
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Reference { get; set; } = "";
    public ResultFlag Flag { get; set; }
    public string? Comment { get; set; }
}

public class ReportSectionModel
{
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Specimen { get; set; } = "";
    public string? Method { get; set; }
    public List<ReportRowModel> Rows { get; set; } = new();
}

public class ReportModel
{
    // Laboratory
    public string LabName { get; set; } = "";
    public string LabAddress { get; set; } = "";
    public string LabPhone { get; set; } = "";
    public string LabEmail { get; set; } = "";
    public string LabLicense { get; set; } = "";
    public string LogoPath { get; set; } = "";
    public string Footer { get; set; } = "";
    public string Disclaimer { get; set; } = "";

    // Patient / order
    public int OrderId { get; set; }
    public string OrderNo { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string PatientNo { get; set; } = "";
    public string FatherName { get; set; } = "";
    public string Gender { get; set; } = "";
    public string AgeText { get; set; } = "";
    public string Phone { get; set; } = "";
    public string DoctorName { get; set; } = "";
    public DateTime OrderedAtUtc { get; set; }
    public DateTime? CollectedAtUtc { get; set; }
    public DateTime ReportDateUtc { get; set; }
    public string SampleNos { get; set; } = "";

    // Verification
    public ReportStatus Status { get; set; }
    public bool IsVerified { get; set; }
    public bool IsDirectlyFinalized { get; set; }
    public bool IsAmended { get; set; }
    public string? AmendmentReason { get; set; }
    public string VerifierName { get; set; } = "";
    public string VerifierDesignation { get; set; } = "";
    public string SignatureText { get; set; } = "";
    public DateTime? VerifiedAtUtc { get; set; }

    // Layout
    public ReportTemplateType Template { get; set; }
    public double FontSize { get; set; } = 11;
    public double MarginMm { get; set; } = 12;
    public string PageSize { get; set; } = "A4";
    public bool ShowReference { get; set; } = true;
    public bool ShowFatherName { get; set; } = true;
    public bool ShowPhone { get; set; } = true;
    public bool ShowSignature { get; set; } = true;

    public List<ReportSectionModel> Sections { get; set; } = new();
}

public sealed class ReportService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly SettingsService _settings;

    public ReportService(IAppDbContextFactory factory, ICurrentUser user, SettingsService settings)
    { _factory = factory; _user = user; _settings = settings; }

    /// <summary>Builds a report from live data. Full workflow permits unverified previews; simplified workflow
    /// requires every test to be saved before a report can be printed.</summary>
    public async Task<ReportModel> BuildAsync(int orderId)
    {
        if (!_user.Has(Permissions.ReportsPrint) && !_user.Has(Permissions.ResultsVerify) && !_user.Has(Permissions.ResultsEnter))
            throw new PermissionDeniedException(Permissions.ReportsPrint);

        await using var db = _factory.Create();
        var o = await db.Orders.AsNoTracking().AsSplitQuery()
            .Include(x => x.Patient).Include(x => x.Doctor).Include(x => x.Report).Include(x => x.Samples)
            .Include(x => x.Tests).ThenInclude(t => t.LabTest).ThenInclude(l => l.Category)
            .Include(x => x.Tests).ThenInclude(t => t.LabTest).ThenInclude(l => l.SpecimenType)
            .Include(x => x.Tests).ThenInclude(t => t.LabTest).ThenInclude(l => l.Parameters)
            .Include(x => x.Tests).ThenInclude(t => t.Results)
            .FirstAsync(x => x.Id == orderId);

        if (!_settings.FullWorkflow && o.Report?.Status != ReportStatus.Verified && o.Report?.Status != ReportStatus.Printed)
            throw new BusinessRuleException("Save all test results before printing this report.");
        return BuildFromOrder(o);
    }

    internal ReportModel BuildFromOrder(LabOrder o)
    {
        var m = NewModel();
        m.OrderId = o.Id; m.OrderNo = o.OrderNo; m.PatientName = o.Patient.FullName; m.PatientNo = o.Patient.PatientNo;
        m.FatherName = o.Patient.FatherOrHusbandName ?? ""; m.Gender = o.Patient.Gender.ToString(); m.AgeText = o.Patient.AgeText;
        m.Phone = o.Patient.Phone ?? ""; m.DoctorName = o.Doctor?.Name ?? "Self / Not specified";
        m.OrderedAtUtc = o.OrderedAt; m.CollectedAtUtc = o.Samples.Where(s => s.CollectedAt != null).Select(s => s.CollectedAt).Min();
        m.SampleNos = string.Join(", ", o.Samples.Where(s => s.Status != SampleStatus.Rejected).Select(s => s.Barcode));
        var rep = o.Report;
        m.Status = rep?.Status ?? ReportStatus.Pending;
        m.IsVerified = _settings.FullWorkflow
            ? !_settings.GetBool(SettingKeys.RequireVerification, true)
                || (rep != null && (rep.Status == ReportStatus.Verified || rep.Status == ReportStatus.Printed))
            : rep != null && (rep.Status == ReportStatus.Verified || rep.Status == ReportStatus.Printed);
        m.IsDirectlyFinalized = rep?.DirectlyFinalized ?? false;
        m.IsAmended = rep?.IsAmended ?? false; m.AmendmentReason = rep?.AmendmentReason;
        m.VerifierName = rep?.VerifierName ?? ""; m.VerifierDesignation = rep?.VerifierDesignation ?? ""; m.SignatureText = rep?.SignatureText ?? "";
        m.VerifiedAtUtc = rep?.VerifiedAt; m.ReportDateUtc = rep?.VerifiedAt ?? DateTime.UtcNow;

        foreach (var t in o.Tests.Where(t => t.Status != OrderTestStatus.Cancelled).OrderBy(t => t.LabTest.Category.DisplayOrder).ThenBy(t => t.LabTest.Name))
        {
            var sec = new ReportSectionModel
            {
                Title = t.LabTest.Name, Category = t.LabTest.Category.Name, Specimen = t.LabTest.SpecimenType?.Name ?? "", Method = t.LabTest.Method
            };
            foreach (var p in t.LabTest.Parameters.Where(p => p.IsActive).OrderBy(p => p.DisplayOrder))
            {
                var r = t.Results.FirstOrDefault(x => x.TestParameterId == p.Id);
                sec.Rows.Add(new ReportRowModel
                {
                    Name = p.Name, Value = r?.Value ?? "", Unit = r?.Unit ?? "", Reference = r?.ReferenceText ?? "",
                    Flag = r?.Flag ?? ResultFlag.None, Comment = r?.Comment
                });
            }
            m.Sections.Add(sec);
        }
        m.Template = ChooseTemplate(o, m);
        return m;
    }

    internal void FinalizeReport(LabOrder order, bool directlyFinalized)
    {
        var report = order.Report ??= new Report { Order = order };
        var now = DateTime.UtcNow;
        report.IsAmended |= report.Status == ReportStatus.Amended;
        report.Status = ReportStatus.Verified;
        if (directlyFinalized) report.DirectlyFinalized = true;
        report.VerifiedByUserId = _user.UserId;
        report.VerifierName = _user.FullName;
        report.VerifierDesignation = _user.Designation;
        report.VerifierRoles = string.Join(", ", _user.Roles);
        report.SignatureText = string.IsNullOrWhiteSpace(_user.SignatureText) ? _user.FullName : _user.SignatureText;
        report.VerifiedAt = now;
        report.PrintedAt = null;
        order.Status = OrderStatus.Completed;
        foreach (var sample in order.Samples.Where(s => s.Status == SampleStatus.Processing || s.Status == SampleStatus.ReceivedInLab))
            sample.Status = SampleStatus.Completed;
    }

    private ReportModel NewModel()
    {
        var s = _settings;
        return new ReportModel
        {
            LabName = s.Get(SettingKeys.LabName), LabAddress = s.Get(SettingKeys.LabAddress), LabPhone = s.Get(SettingKeys.LabPhone),
            LabEmail = s.Get(SettingKeys.LabEmail), LabLicense = s.Get(SettingKeys.LabLicense), LogoPath = s.Get(SettingKeys.LabLogoPath),
            Footer = s.Get(SettingKeys.ReportFooter), Disclaimer = s.Get(SettingKeys.ReportDisclaimer),
            FontSize = s.GetDouble(SettingKeys.ReportFontSize, 11), MarginMm = s.GetDouble(SettingKeys.ReportMarginMm, 12),
            PageSize = s.Get(SettingKeys.ReportPageSize, "A4"), ShowReference = s.GetBool(SettingKeys.ReportShowReference, true),
            ShowFatherName = s.GetBool(SettingKeys.ReportShowFather, true), ShowPhone = s.GetBool(SettingKeys.ReportShowPhone, true),
            ShowSignature = s.GetBool(SettingKeys.ReportShowSignature, true)
        };
    }

    private ReportTemplateType ChooseTemplate(LabOrder o, ReportModel m)
    {
        var setting = _settings.Get(SettingKeys.ReportTemplate, "Auto");
        if (Enum.TryParse<ReportTemplateType>(setting, true, out var forced)) return forced;
        if (m.Sections.Count > 1) return ReportTemplateType.Profile;
        var only = m.Sections.FirstOrDefault();
        if (only != null && only.Category.StartsWith("Hematology", StringComparison.OrdinalIgnoreCase)) return ReportTemplateType.Hematology;
        return ReportTemplateType.General;
    }

    public async Task MarkPrintedAsync(int orderId)
    {
        _user.Require(Permissions.ReportsPrint);
        await using var db = _factory.Create();
        var r = await db.Reports.FirstOrDefaultAsync(x => x.OrderId == orderId);
        if (r == null || r.Status != ReportStatus.Verified && r.Status != ReportStatus.Printed) return;
        r.Status = ReportStatus.Printed; r.PrintedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}

public class PendingVerificationItem
{
    public int OrderId { get; set; }
    public string OrderNo { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string PatientNo { get; set; } = "";
    public string Tests { get; set; } = "";
    public bool IsAmendment { get; set; }
    public bool IsDirectlyFinalized { get; set; }
    public DateTime OrderedAt { get; set; }
}

public sealed class VerificationService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly ReportService _reports;

    public VerificationService(IAppDbContextFactory factory, ICurrentUser user, ReportService reports)
    { _factory = factory; _user = user; _reports = reports; }

    public async Task<List<PendingVerificationItem>> GetPendingAsync()
    {
        _user.Require(Permissions.ResultsVerify);
        await using var db = _factory.Create();
        var orders = await db.Orders.AsNoTracking().Include(o => o.Patient).Include(o => o.Report)
            .Include(o => o.Tests).ThenInclude(t => t.LabTest)
            .Where(o => o.Status != OrderStatus.Cancelled && o.Tests.Any(t => t.Status == OrderTestStatus.ReadyForVerification))
            .OrderBy(o => o.OrderedAt).Take(300).ToListAsync();
        return orders.Select(o =>
        {
            var active = o.Tests.Where(t => t.Status != OrderTestStatus.Cancelled).ToList();
            return new { o, ready = active.All(t => t.Status is OrderTestStatus.ReadyForVerification or OrderTestStatus.Verified) };
        }).Where(x => x.ready).Select(x => new PendingVerificationItem
        {
            OrderId = x.o.Id, OrderNo = x.o.OrderNo, PatientName = x.o.Patient.FullName, PatientNo = x.o.Patient.PatientNo,
            Tests = string.Join(", ", x.o.Tests.Where(t => t.Status == OrderTestStatus.ReadyForVerification).Select(t => t.LabTest.Name)),
            IsAmendment = x.o.Report?.Status == ReportStatus.Amended, OrderedAt = x.o.OrderedAt
        }).ToList();
    }

    public async Task VerifyAsync(int orderId)
    {
        _user.Require(Permissions.ResultsVerify);
        await using var db = _factory.Create();
        var o = await db.Orders
            .Include(x => x.Patient).Include(x => x.Doctor).Include(x => x.Samples).Include(x => x.Report)
            .Include(x => x.Tests).ThenInclude(t => t.LabTest).ThenInclude(l => l.Category)
            .Include(x => x.Tests).ThenInclude(t => t.LabTest).ThenInclude(l => l.SpecimenType)
            .Include(x => x.Tests).ThenInclude(t => t.LabTest).ThenInclude(l => l.Parameters)
            .Include(x => x.Tests).ThenInclude(t => t.Results)
            .FirstAsync(x => x.Id == orderId);
        var report = o.Report ?? throw new BusinessRuleException("Order has no report record.");
        var active = o.Tests.Where(t => t.Status != OrderTestStatus.Cancelled).ToList();
        if (active.Any(t => t.Status != OrderTestStatus.ReadyForVerification && t.Status != OrderTestStatus.Verified))
            throw new BusinessRuleException("All tests must be submitted for verification before the report can be verified.");
        if (!active.Any(t => t.Status == OrderTestStatus.ReadyForVerification))
            throw new BusinessRuleException("Nothing to verify.");

        foreach (var t in active) t.Status = OrderTestStatus.Verified;
        _reports.FinalizeReport(o, directlyFinalized: false);
        await db.SaveChangesAsync();
    }

    public async Task<List<PendingVerificationItem>> SearchVerifiedAsync(string? text)
    {
        _user.Require(Permissions.ReportsAmend);
        await using var db = _factory.Create();
        var q = db.Orders.AsNoTracking().Include(o => o.Patient).Include(o => o.Report).Include(o => o.Tests).ThenInclude(t => t.LabTest)
            .Where(o => o.Report != null && (o.Report.Status == ReportStatus.Verified || o.Report.Status == ReportStatus.Printed));
        if (!string.IsNullOrWhiteSpace(text))
        {
            var s = text.Trim();
            q = q.Where(o => o.OrderNo.Contains(s) || o.Patient.FullName.Contains(s) || o.Patient.PatientNo.Contains(s));
        }
        var list = await q.OrderByDescending(o => o.OrderedAt).Take(100).ToListAsync();
        return list.Select(o => new PendingVerificationItem
        {
            OrderId = o.Id, OrderNo = o.OrderNo, PatientName = o.Patient.FullName, PatientNo = o.Patient.PatientNo,
            Tests = string.Join(", ", o.Tests.Select(t => t.LabTest.Name)), IsDirectlyFinalized = o.Report!.DirectlyFinalized,
            OrderedAt = o.OrderedAt
        }).ToList();
    }

    /// <summary>Unlocks a verified report for correction; a reason is mandatory.</summary>
    public async Task BeginAmendmentAsync(int orderId, string reason)
    {
        _user.Require(Permissions.ReportsAmend);
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("An amendment reason is required.");
        await using var db = _factory.Create();
        var o = await db.Orders.Include(x => x.Tests).Include(x => x.Report).FirstAsync(x => x.Id == orderId);
        if (o.Report == null || (o.Report.Status != ReportStatus.Verified && o.Report.Status != ReportStatus.Printed))
            throw new BusinessRuleException("Only verified reports can be amended.");
        o.Report.Status = ReportStatus.Amended;
        o.Report.AmendmentReason = reason.Trim();
        foreach (var t in o.Tests.Where(t => t.Status == OrderTestStatus.Verified)) t.Status = OrderTestStatus.InProgress;
        o.Status = OrderStatus.InProgress;
        await db.SaveChangesAsync();
    }
}
