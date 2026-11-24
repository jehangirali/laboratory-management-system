using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LaboratoryManagement.Application.Services;

public class WorklistItem
{
    public int OrderTestId { get; set; }
    public int OrderId { get; set; }
    public string OrderNo { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string PatientNo { get; set; } = "";
    public string TestName { get; set; } = "";
    public string Barcode { get; set; } = "";
    public SampleStatus? SampleStatus { get; set; }
    public OrderTestStatus Status { get; set; }
    public DateTime OrderedAt { get; set; }
}

public class ResultRowDto : ObservableBase
{
    public int ParameterId { get; set; }
    public string ParameterName { get; set; } = "";
    public ResultType ResultType { get; set; }
    public string Unit { get; set; } = "";
    public string ReferenceText { get; set; } = "";
    public ReferenceRange? Range { get; set; }
    public string[]? Options { get; set; }

    private string? _value;
    public string? Value
    {
        get => _value;
        set
        {
            if (Set(ref _value, value))
            {
                Flag = ReferenceRangeEvaluator.Evaluate(ResultType, value, Range);
                OnPropertyChanged(nameof(FlagText));
            }
        }
    }

    private ResultFlag _flag;
    public ResultFlag Flag { get => _flag; set { if (Set(ref _flag, value)) OnPropertyChanged(nameof(FlagText)); } }
    public string FlagText => ReferenceRangeEvaluator.FlagText(Flag);

    private string? _comment;
    public string? Comment { get => _comment; set => Set(ref _comment, value); }
}

public class ResultSheet
{
    public int OrderTestId { get; set; }
    public int OrderId { get; set; }
    public string OrderNo { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string PatientInfo { get; set; } = "";
    public string TestName { get; set; } = "";
    public OrderTestStatus Status { get; set; }
    public bool IsAmendment { get; set; }
    public string? AmendmentReason { get; set; }
    public bool IsLocked => Status == OrderTestStatus.Verified || Status == OrderTestStatus.Cancelled;
    public List<ResultRowDto> Rows { get; set; } = new();
}

public sealed class ResultService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly SettingsService _settings;

    public ResultService(IAppDbContextFactory factory, ICurrentUser user, IAuditService audit, SettingsService settings)
    { _factory = factory; _user = user; _audit = audit; _settings = settings; }

    public static string[]? OptionsFor(ResultType t) => t switch
    {
        ResultType.PositiveNegative => new[] { "Positive", "Negative" },
        ResultType.ReactiveNonReactive => new[] { "Reactive", "Non-Reactive" },
        _ => null
    };

    public async Task<List<WorklistItem>> GetWorklistAsync(bool includeReadyForVerification, string? text = null)
    {
        _user.Require(Permissions.ResultsEnter);
        await using var db = _factory.Create();
        var q = db.OrderTests.AsNoTracking()
            .Where(t => t.Order.Status != OrderStatus.Cancelled && t.Status != OrderTestStatus.Verified && t.Status != OrderTestStatus.Cancelled);
        if (!includeReadyForVerification) q = q.Where(t => t.Status != OrderTestStatus.ReadyForVerification);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var s = text.Trim();
            q = q.Where(t => t.Order.OrderNo.Contains(s) || t.Order.Patient.FullName.Contains(s) || (t.Sample != null && t.Sample.Barcode == s));
        }
        return await q.OrderBy(t => t.Order.OrderedAt).Take(500).Select(t => new WorklistItem
        {
            OrderTestId = t.Id, OrderId = t.OrderId, OrderNo = t.Order.OrderNo, PatientName = t.Order.Patient.FullName,
            PatientNo = t.Order.Patient.PatientNo, TestName = t.LabTest.Name, Barcode = t.Sample != null ? t.Sample.Barcode : "",
            SampleStatus = t.Sample != null ? t.Sample.Status : (SampleStatus?)null, Status = t.Status, OrderedAt = t.Order.OrderedAt
        }).ToListAsync();
    }

    public async Task<ResultSheet> LoadSheetAsync(int orderTestId)
    {
        await using var db = _factory.Create();
        var ot = await db.OrderTests.AsNoTracking()
            .Include(t => t.Order).ThenInclude(o => o.Patient).Include(t => t.Order).ThenInclude(o => o.Report)
            .Include(t => t.LabTest).ThenInclude(l => l.Parameters).ThenInclude(p => p.ReferenceRanges)
            .Include(t => t.LabTest).ThenInclude(l => l.Parameters).ThenInclude(p => p.Unit)
            .Include(t => t.Results)
            .FirstAsync(t => t.Id == orderTestId);

        var patient = ot.Order.Patient;
        decimal? age = patient.DateOfBirth == null ? null : (decimal)((DateTime.Today - patient.DateOfBirth.Value).TotalDays / 365.25);
        var sheet = new ResultSheet
        {
            OrderTestId = ot.Id, OrderId = ot.OrderId, OrderNo = ot.Order.OrderNo, PatientName = patient.FullName,
            PatientInfo = $"{patient.PatientNo} | {patient.Gender} | {patient.AgeText}", TestName = ot.LabTest.Name, Status = ot.Status,
            IsAmendment = ot.Order.Report?.Status == ReportStatus.Amended, AmendmentReason = ot.Order.Report?.AmendmentReason
        };
        foreach (var p in ot.LabTest.Parameters.Where(p => p.IsActive).OrderBy(p => p.DisplayOrder))
        {
            var range = ReferenceRangeEvaluator.Select(p.ReferenceRanges, patient.Gender, age);
            var existing = ot.Results.FirstOrDefault(r => r.TestParameterId == p.Id);
            var row = new ResultRowDto
            {
                ParameterId = p.Id, ParameterName = p.Name, ResultType = p.ResultType, Unit = p.Unit?.Symbol ?? "",
                Range = range, ReferenceText = ReferenceRangeEvaluator.FormatRange(range), Options = OptionsFor(p.ResultType)
            };
            if (existing != null) { row.Value = existing.Value; row.Comment = existing.Comment; row.Flag = existing.Flag; }
            sheet.Rows.Add(row);
        }
        return sheet;
    }

    public async Task SaveAsync(int orderTestId, IList<ResultRowDto> rows, bool submitForVerification)
    {
        _user.Require(Permissions.ResultsEnter);
        await using var db = _factory.Create();
        var ot = await db.OrderTests
            .Include(t => t.Order).ThenInclude(o => o.Patient).Include(t => t.Order).ThenInclude(o => o.Report)
            .Include(t => t.Order).ThenInclude(o => o.Tests)
            .Include(t => t.Sample).Include(t => t.LabTest).ThenInclude(l => l.Parameters).ThenInclude(p => p.ReferenceRanges)
            .Include(t => t.LabTest).ThenInclude(l => l.Parameters).ThenInclude(p => p.Unit)
            .Include(t => t.Results)
            .FirstAsync(t => t.Id == orderTestId);

        if (ot.Order.Status == OrderStatus.Cancelled || ot.Status == OrderTestStatus.Cancelled) throw new BusinessRuleException("This test is cancelled.");
        if (ot.Status == OrderTestStatus.Verified)
            throw new BusinessRuleException("This result is verified and locked. A report amendment must be started by an authorized user.");
        var report = ot.Order.Report;
        var amending = report?.Status == ReportStatus.Amended;
        var requireCollection = _settings.GetBool(SettingKeys.RequireSampleCollection, true);
        var requireReceive = _settings.GetBool(SettingKeys.RequireReceiveInLab, true);
        if (ot.Sample != null && requireCollection && ot.Sample.Status == SampleStatus.PendingCollection)
            throw new BusinessRuleException($"The sample ({ot.Sample.Barcode}) must be collected first.");
        if (ot.Sample != null && requireReceive
            && ot.Sample.Status != SampleStatus.ReceivedInLab
            && ot.Sample.Status != SampleStatus.Processing
            && !(amending && ot.Sample.Status == SampleStatus.Completed))
            throw new BusinessRuleException($"The sample ({ot.Sample.Barcode}) must be received in the laboratory first (status: {ot.Sample.Status}).");

        var patient = ot.Order.Patient;
        decimal? age = patient.DateOfBirth == null ? null : (decimal)((DateTime.Today - patient.DateOfBirth.Value).TotalDays / 365.25);
        var now = DateTime.UtcNow;
        var changes = new List<string>();

        foreach (var row in rows)
        {
            var param = ot.LabTest.Parameters.First(p => p.Id == row.ParameterId);
            var range = ReferenceRangeEvaluator.Select(param.ReferenceRanges, patient.Gender, age);
            var value = string.IsNullOrWhiteSpace(row.Value) ? null : row.Value.Trim();
            if (param.ResultType == ResultType.Numeric && value != null && !ReferenceRangeEvaluator.TryParseNumber(value, out _))
                throw new BusinessRuleException($"'{param.Name}': '{value}' is not a valid number.");
            var flag = ReferenceRangeEvaluator.Evaluate(param.ResultType, value, range);
            var existing = ot.Results.FirstOrDefault(r => r.TestParameterId == param.Id);

            if (existing == null)
            {
                if (value == null && string.IsNullOrWhiteSpace(row.Comment)) continue;
                existing = new ResultValue
                {
                    TestParameterId = param.Id, Value = value, Flag = flag, Comment = row.Comment, Unit = param.Unit?.Symbol,
                    ReferenceText = ReferenceRangeEvaluator.FormatRange(range), EnteredByUserId = _user.UserId, EnteredAt = now
                };
                existing.Revisions.Add(new ResultRevision
                {
                    OldValue = null, NewValue = value, OldFlag = ResultFlag.None, NewFlag = flag, ChangedByUserId = _user.UserId,
                    ChangedByName = _user.FullName, ChangedAt = now, Reason = amending ? report!.AmendmentReason : "Initial entry", IsAmendment = amending
                });
                ot.Results.Add(existing);
                changes.Add($"{param.Name}: (none) -> {value}");
            }
            else if (existing.Value != value || existing.Comment != row.Comment)
            {
                if (existing.Value != value)
                {
                    existing.Revisions.Add(new ResultRevision
                    {
                        OldValue = existing.Value, NewValue = value, OldFlag = existing.Flag, NewFlag = flag, ChangedByUserId = _user.UserId,
                        ChangedByName = _user.FullName, ChangedAt = now, Reason = amending ? report!.AmendmentReason : "Edited before verification", IsAmendment = amending
                    });
                    changes.Add($"{param.Name}: {existing.Value} -> {value}");
                }
                existing.Value = value; existing.Comment = row.Comment; existing.Flag = flag; existing.Unit = param.Unit?.Symbol;
                existing.ReferenceText = ReferenceRangeEvaluator.FormatRange(range);
                existing.EnteredByUserId = _user.UserId; existing.EnteredAt = now;
            }
        }

        if (submitForVerification)
        {
            var missing = ot.LabTest.Parameters.Where(p => p.IsActive)
                .Where(p => string.IsNullOrWhiteSpace(ot.Results.FirstOrDefault(r => r.TestParameterId == p.Id)?.Value))
                .Select(p => p.Name).ToList();
            if (missing.Count > 0) throw new BusinessRuleException("Enter a value for all parameters before submitting: " + string.Join(", ", missing));
            ot.Status = _settings.GetBool(SettingKeys.RequireVerification, true)
                ? OrderTestStatus.ReadyForVerification
                : OrderTestStatus.Verified;
        }
        else ot.Status = OrderTestStatus.InProgress;

        if (ot.Sample != null && ot.Sample.Status == SampleStatus.ReceivedInLab) ot.Sample.Status = SampleStatus.Processing;
        if (ot.Order.Status == OrderStatus.Registered) ot.Order.Status = OrderStatus.InProgress;
        if (report != null && !amending)
        {
            var active = ot.Order.Tests.Where(t => t.Status != OrderTestStatus.Cancelled).ToList();
            if (!_settings.GetBool(SettingKeys.RequireVerification, true))
            {
                if (ot.Sample != null) ot.Sample.Status = SampleStatus.Completed;
                if (active.All(t => t.Status == OrderTestStatus.Verified))
                {
                    report.Status = ReportStatus.Verified;
                    report.CurrentVersion = Math.Max(1, report.CurrentVersion);
                    report.IsAmended = false;
                    report.DirectlyFinalized = true;
                    report.VerifiedByUserId = _user.UserId;
                    report.VerifierName = _user.FullName;
                    report.VerifierDesignation = _user.Designation;
                    report.SignatureText = string.IsNullOrWhiteSpace(_user.SignatureText) ? _user.FullName : _user.SignatureText;
                    report.VerifiedAt = DateTime.UtcNow;
                    ot.Order.Status = OrderStatus.Completed;
                }
                else
                {
                    report.Status = ReportStatus.InProgress;
                }
            }
            else
                report.Status = active.All(t => t.Status == OrderTestStatus.ReadyForVerification) ? ReportStatus.ReadyForVerification : ReportStatus.InProgress;
        }

        _audit.Add(db, submitForVerification ? "Results submitted" : "Results saved", "OrderTest", $"{ot.Order.OrderNo}/{ot.LabTest.Code}",
                   changes.Count == 0 ? "No value changes" : string.Join("; ", changes));
        await db.SaveChangesAsync();
    }

    public async Task<List<ResultRevision>> GetRevisionsAsync(int orderTestId)
    {
        await using var db = _factory.Create();
        return await db.ResultRevisions.AsNoTracking().Include(r => r.ResultValue).ThenInclude(v => v.TestParameter)
            .Where(r => r.ResultValue.OrderTestId == orderTestId).OrderBy(r => r.ChangedAt).ToListAsync();
    }
}
