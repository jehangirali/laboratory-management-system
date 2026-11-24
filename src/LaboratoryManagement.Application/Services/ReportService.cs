using System.Text.Json;
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
    public int VersionNo { get; set; }
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

public class ReportVersionInfo
{
    public int Id { get; set; }
    public int VersionNo { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Reason { get; set; }
    public string CreatedBy { get; set; } = "";
    public string Display => $"Version {VersionNo} - {TimeHelper.ToLocal(CreatedAt):dd-MMM-yyyy HH:mm}" + (VersionNo > 1 ? " (amended)" : " (original)");
}

public sealed class ReportService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly SettingsService _settings;
    private readonly IAuditService _audit;

    public ReportService(IAppDbContextFactory factory, ICurrentUser user, SettingsService settings, IAuditService audit)
    { _factory = factory; _user = user; _settings = settings; _audit = audit; }

    /// <summary>Builds the report from live data. Unverified reports can be previewed by verifiers/technicians only
    /// (the model reports IsVerified=false so the UI can watermark it).</summary>
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

        var m = NewModel();
        m.OrderId = o.Id; m.OrderNo = o.OrderNo; m.PatientName = o.Patient.FullName; m.PatientNo = o.Patient.PatientNo;
        m.FatherName = o.Patient.FatherOrHusbandName ?? ""; m.Gender = o.Patient.Gender.ToString(); m.AgeText = o.Patient.AgeText;
        m.Phone = o.Patient.Phone ?? ""; m.DoctorName = o.Doctor?.Name ?? "Self / Not specified";
        m.OrderedAtUtc = o.OrderedAt; m.CollectedAtUtc = o.Samples.Where(s => s.CollectedAt != null).Select(s => s.CollectedAt).Min();
        m.SampleNos = string.Join(", ", o.Samples.Where(s => s.Status != SampleStatus.Rejected).Select(s => s.Barcode));
        var rep = o.Report;
        m.Status = rep?.Status ?? ReportStatus.Pending;
        m.IsVerified = !_settings.GetBool(SettingKeys.RequireVerification, true)
            || (rep != null && (rep.Status == ReportStatus.Verified || rep.Status == ReportStatus.Printed));
        m.IsDirectlyFinalized = rep?.DirectlyFinalized ?? false;
        m.VersionNo = rep?.CurrentVersion ?? 0; m.IsAmended = rep?.IsAmended ?? false; m.AmendmentReason = rep?.AmendmentReason;
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

    public async Task<List<ReportVersionInfo>> GetVersionsAsync(int orderId)
    {
        await using var db = _factory.Create();
        return await db.ReportVersions.AsNoTracking().Where(v => v.Report.OrderId == orderId).OrderBy(v => v.VersionNo)
            .Select(v => new ReportVersionInfo { Id = v.Id, VersionNo = v.VersionNo, CreatedAt = v.CreatedAt, Reason = v.Reason, CreatedBy = v.CreatedByName })
            .ToListAsync();
    }

    public async Task<ReportModel> LoadVersionAsync(int versionId)
    {
        await using var db = _factory.Create();
        var v = await db.ReportVersions.AsNoTracking().FirstAsync(x => x.Id == versionId);
        return JsonSerializer.Deserialize<ReportModel>(v.SnapshotJson)!;
    }

    public static string Serialize(ReportModel m) => JsonSerializer.Serialize(m);

    public async Task MarkPrintedAsync(int orderId)
    {
        _user.Require(Permissions.ReportsPrint);
        await using var db = _factory.Create();
        var r = await db.Reports.FirstOrDefaultAsync(x => x.OrderId == orderId);
        if (r == null || r.Status != ReportStatus.Verified && r.Status != ReportStatus.Printed) return;
        var first = r.Status != ReportStatus.Printed;
        r.Status = ReportStatus.Printed; r.PrintedAt = DateTime.UtcNow;
        _audit.Add(db, first ? "Report printed" : "Report reprinted", "Report", orderId.ToString(), $"Version {r.CurrentVersion}");
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
    private readonly IAuditService _audit;
    private readonly ReportService _reports;

    public VerificationService(IAppDbContextFactory factory, ICurrentUser user, IAuditService audit, ReportService reports)
    { _factory = factory; _user = user; _audit = audit; _reports = reports; }

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
        // Snapshot content is produced from the data as it is right now.
        var model = await _reports.BuildAsync(orderId);

        await using var db = _factory.Create();
        var o = await db.Orders.Include(x => x.Tests).Include(x => x.Samples).Include(x => x.Report!).ThenInclude(r => r.Versions)
            .FirstAsync(x => x.Id == orderId);
        var report = o.Report ?? throw new BusinessRuleException("Order has no report record.");
        var active = o.Tests.Where(t => t.Status != OrderTestStatus.Cancelled).ToList();
        if (active.Any(t => t.Status != OrderTestStatus.ReadyForVerification && t.Status != OrderTestStatus.Verified))
            throw new BusinessRuleException("All tests must be submitted for verification before the report can be verified.");
        if (!active.Any(t => t.Status == OrderTestStatus.ReadyForVerification))
            throw new BusinessRuleException("Nothing to verify.");

        var amending = report.Status == ReportStatus.Amended;
        foreach (var t in active) t.Status = OrderTestStatus.Verified;
        report.CurrentVersion += 1;
        report.IsAmended = report.CurrentVersion > 1;
        report.Status = ReportStatus.Verified;
        report.VerifiedByUserId = _user.UserId; report.VerifierName = _user.FullName; report.VerifierDesignation = _user.Designation;
        report.VerifierRoles = string.Join(", ", _user.Roles);
        report.SignatureText = string.IsNullOrWhiteSpace(_user.SignatureText) ? _user.FullName : _user.SignatureText;
        report.VerifiedAt = DateTime.UtcNow; report.PrintedAt = null;

        model.IsVerified = true; model.Status = ReportStatus.Verified; model.VersionNo = report.CurrentVersion; model.IsAmended = report.IsAmended;
        model.VerifierName = report.VerifierName; model.VerifierDesignation = report.VerifierDesignation ?? "";
        model.SignatureText = report.SignatureText; model.VerifiedAtUtc = report.VerifiedAt; model.ReportDateUtc = report.VerifiedAt.Value;
        model.AmendmentReason = report.AmendmentReason;

        report.Versions.Add(new ReportVersion
        {
            VersionNo = report.CurrentVersion, SnapshotJson = ReportService.Serialize(model),
            Reason = amending ? report.AmendmentReason : "Original verified report",
            CreatedAt = DateTime.UtcNow, CreatedByUserId = _user.UserId, CreatedByName = _user.FullName
        });

        foreach (var s in o.Samples.Where(s => s.Status == SampleStatus.Processing || s.Status == SampleStatus.ReceivedInLab)) s.Status = SampleStatus.Completed;
        o.Status = OrderStatus.Completed;
        _audit.Add(db, amending ? "Amended report verified" : "Report verified", "Report", o.OrderNo, $"Version {report.CurrentVersion} by {_user.FullName}");
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

    /// <summary>Unlocks a verified report for correction. Version history keeps the original; a reason is mandatory.</summary>
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
        _audit.Add(db, "Report amendment started", "Report", o.OrderNo, $"Reason: {reason}; previous version {o.Report.CurrentVersion} retained");
        await db.SaveChangesAsync();
    }
}
