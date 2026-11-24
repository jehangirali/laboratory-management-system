using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LaboratoryManagement.Application.Services;

public class SampleRow
{
    public int SampleId { get; set; }
    public int OrderId { get; set; }
    public string Barcode { get; set; } = "";
    public string OrderNo { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string PatientNo { get; set; } = "";
    public string Specimen { get; set; } = "";
    public string Container { get; set; } = "";
    public string Tests { get; set; } = "";
    public SampleStatus Status { get; set; }
    public DateTime OrderedAt { get; set; }
    public DateTime? CollectedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public string? RejectionReason { get; set; }
    public bool IsRecollection { get; set; }
}

public sealed class SampleService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly SettingsService _settings;

    public SampleService(IAppDbContextFactory factory, ICurrentUser user, IAuditService audit, SettingsService settings)
    { _factory = factory; _user = user; _audit = audit; _settings = settings; }

    private static IQueryable<Sample> Base(IAppDbContext db) =>
        db.Samples.AsNoTracking().Include(s => s.Order).ThenInclude(o => o.Patient)
            .Include(s => s.SpecimenType).Include(s => s.Container)
            .Include(s => s.Tests).ThenInclude(t => t.LabTest);

    private static SampleRow ToRow(Sample s) => new()
    {
        SampleId = s.Id, OrderId = s.OrderId, Barcode = s.Barcode, OrderNo = s.Order.OrderNo,
        PatientName = s.Order.Patient.FullName, PatientNo = s.Order.Patient.PatientNo,
        Specimen = s.SpecimenType?.Name ?? "", Container = s.Container?.Name ?? "",
        Tests = string.Join(", ", s.Tests.Select(t => t.LabTest.Name)), Status = s.Status, OrderedAt = s.Order.OrderedAt,
        CollectedAt = s.CollectedAt, ReceivedAt = s.ReceivedAt, RejectionReason = s.RejectionReason,
        IsRecollection = s.RecollectionOfSampleId != null
    };

    public async Task<List<SampleRow>> GetListAsync(SampleStatus? status, DateTime fromLocal, DateTime toLocal, string? text = null)
    {
        _user.Require(Permissions.SamplesManage);
        await using var db = _factory.Create();
        var (f, t) = TimeHelper.LocalDayRangeUtc(fromLocal, toLocal);
        var q = Base(db).Where(s => s.CreatedAt >= f && s.CreatedAt < t && s.Order.Status != OrderStatus.Cancelled);
        if (status != null) q = q.Where(s => s.Status == status);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var x = text.Trim();
            q = q.Where(s => s.Barcode.Contains(x) || s.Order.OrderNo.Contains(x) || s.Order.Patient.FullName.Contains(x));
        }
        var list = await q.OrderByDescending(s => s.CreatedAt).Take(500).ToListAsync();
        return list.Select(ToRow).ToList();
    }

    public async Task<SampleRow?> FindByBarcodeAsync(string barcode)
    {
        _user.Require(Permissions.SamplesManage);
        await using var db = _factory.Create();
        var code = barcode.Trim();
        var s = await Base(db).FirstOrDefaultAsync(x => x.Barcode == code);
        return s == null ? null : ToRow(s);
    }

    public async Task<List<SampleRow>> GetByOrderAsync(int orderId)
    {
        await using var db = _factory.Create();
        var list = await Base(db).Where(s => s.OrderId == orderId).OrderBy(s => s.Id).ToListAsync();
        return list.Select(ToRow).ToList();
    }

    public async Task<List<SampleRow>> GetByIdsAsync(IEnumerable<int> ids)
    {
        await using var db = _factory.Create();
        var idList = ids.ToList();
        var list = await Base(db).Where(s => idList.Contains(s.Id)).ToListAsync();
        return list.Select(ToRow).ToList();
    }

    public async Task CollectAsync(int sampleId)
    {
        _user.Require(Permissions.SamplesManage);
        await using var db = _factory.Create();
        var s = await db.Samples.Include(x => x.Order).FirstAsync(x => x.Id == sampleId);
        if (s.Order.Status == OrderStatus.Cancelled) throw new BusinessRuleException("The order is cancelled.");
        if (s.Status != SampleStatus.PendingCollection) throw new BusinessRuleException($"Sample is already '{s.Status}'.");
        s.Status = SampleStatus.Collected; s.CollectedAt = DateTime.UtcNow; s.CollectedByUserId = _user.UserId;
        if (s.Order.Status == OrderStatus.Registered) s.Order.Status = OrderStatus.InProgress;
        _audit.Add(db, "Sample collected", "Sample", s.Barcode, $"Order={s.Order.OrderNo}");
        await db.SaveChangesAsync();
    }

    public async Task ReceiveAsync(int sampleId)
    {
        _user.Require(Permissions.SamplesManage);
        await using var db = _factory.Create();
        var s = await db.Samples.Include(x => x.Order).FirstAsync(x => x.Id == sampleId);
        if (s.Status != SampleStatus.Collected && s.Status != SampleStatus.PendingCollection)
            throw new BusinessRuleException($"Sample is '{s.Status}' and cannot be received.");
        if (s.Status == SampleStatus.PendingCollection)
        {
            // Receiving directly (e.g. sample brought in by the patient) records collection at the same time.
            s.CollectedAt = DateTime.UtcNow; s.CollectedByUserId = _user.UserId;
        }
        s.Status = SampleStatus.ReceivedInLab; s.ReceivedAt = DateTime.UtcNow; s.ReceivedByUserId = _user.UserId;
        if (s.Order.Status == OrderStatus.Registered) s.Order.Status = OrderStatus.InProgress;
        _audit.Add(db, "Sample received in lab", "Sample", s.Barcode, $"Order={s.Order.OrderNo}");
        await db.SaveChangesAsync();
    }

    public async Task RejectAsync(int sampleId, string reason)
    {
        _user.Require(Permissions.SamplesManage);
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("A rejection reason is required.");
        await using var db = _factory.Create();
        var s = await db.Samples.Include(x => x.Order).FirstAsync(x => x.Id == sampleId);
        if (s.Status is SampleStatus.Completed or SampleStatus.Rejected or SampleStatus.RecollectionRequired)
            throw new BusinessRuleException($"Sample is '{s.Status}' and cannot be rejected.");
        s.Status = SampleStatus.RecollectionRequired; s.RejectionReason = reason.Trim(); s.RejectedAt = DateTime.UtcNow;
        _audit.Add(db, "Sample rejected", "Sample", s.Barcode, $"Order={s.Order.OrderNo}; Reason: {reason}");
        await db.SaveChangesAsync();
    }

    /// <summary>Creates a replacement sample for a rejected one. Tests move to the new sample; the rejected sample is kept as history.</summary>
    public async Task<int> RequestRecollectionAsync(int rejectedSampleId)
    {
        _user.Require(Permissions.SamplesManage);
        return await Retry.RunAsync(async () =>
        {
            await using var db = _factory.Create();
            var old = await db.Samples.Include(x => x.Tests).Include(x => x.Order).FirstAsync(x => x.Id == rejectedSampleId);
            if (old.Status != SampleStatus.RecollectionRequired) throw new BusinessRuleException("Reject the sample first.");
            var day = TimeHelper.ToLocal(DateTime.UtcNow).ToString("yyMMdd");
            var seq = await NumberGenerator.NextAsync(db, $"BARCODE-{day}");
            var ns = new Sample
            {
                OrderId = old.OrderId, Barcode = $"{day}{seq:D5}", SpecimenTypeId = old.SpecimenTypeId, ContainerId = old.ContainerId,
                Status = SampleStatus.PendingCollection, RecollectionOfSampleId = old.Id
            };
            db.Samples.Add(ns);
            foreach (var t in old.Tests.Where(t => t.Status != OrderTestStatus.Verified)) t.Sample = ns;
            old.Status = SampleStatus.Rejected;
            _audit.Add(db, "Recollection requested", "Sample", old.Barcode, $"New barcode {ns.Barcode}; Order={old.Order.OrderNo}");
            await db.SaveChangesAsync();
            return ns.Id;
        });
    }
}
