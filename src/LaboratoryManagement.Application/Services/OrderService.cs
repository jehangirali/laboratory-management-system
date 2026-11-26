using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LaboratoryManagement.Application.Services;

public class CreateOrderRequest
{
    public int? PatientId { get; set; }
    public string? PatientName { get; set; }
    public int? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public List<int> TestIds { get; set; } = new();
    public long Discount { get; set; }
    public long InitialPayment { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? PaymentReference { get; set; }
    public string? Notes { get; set; }
}

public record CreateOrderResult(int OrderId, string OrderNo, int? PaymentId);

public class OrderFilter
{
    public DateTime? FromLocal { get; set; }
    public DateTime? ToLocal { get; set; }
    public string? Text { get; set; }
    public OrderStatus? Status { get; set; }
    public bool OnlyWithBalance { get; set; }
}

public sealed class OrderService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly SettingsService _settings;

    public OrderService(IAppDbContextFactory factory, ICurrentUser user, SettingsService settings)
    { _factory = factory; _user = user; _settings = settings; }

    public static void Recalculate(LabOrder o, IEnumerable<Payment> payments)
    {
        var list = payments.ToList();
        var paid = list.Where(p => p.Type == PaymentType.Payment).Sum(p => p.Amount);
        var refunded = list.Where(p => p.Type == PaymentType.Refund).Sum(p => p.Amount);
        o.NetAmount = o.Subtotal - o.Discount;
        o.PaidAmount = paid - refunded;
        o.Balance = o.NetAmount - o.PaidAmount;
        if (o.PaidAmount <= 0) o.PaymentStatus = refunded > 0 ? PaymentStatus.Refunded : PaymentStatus.Unpaid;
        else if (o.PaidAmount >= o.NetAmount) o.PaymentStatus = PaymentStatus.Paid;
        else o.PaymentStatus = PaymentStatus.Partial;
    }

    public async Task<CreateOrderResult> CreateAsync(CreateOrderRequest req)
    {
        _user.Require(Permissions.OrdersCreate);
        if (req.Discount > 0) _user.Require(Permissions.OrdersDiscount);
        if (req.InitialPayment > 0) _user.Require(Permissions.PaymentsReceive);
        if (req.TestIds.Count == 0) throw new BusinessRuleException("Select at least one test or panel.");
        if (req.Discount < 0 || req.InitialPayment < 0) throw new BusinessRuleException("Amounts cannot be negative.");
        if (req.InitialPayment > 0 && req.PaymentMethodId == null) throw new BusinessRuleException("Select a payment method.");

        return await Retry.RunAsync(async () =>
        {
            await using var db = _factory.Create();
            Patient? patient;
            if (req.PatientId.HasValue)
            {
                patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == req.PatientId.Value && p.IsActive)
                          ?? throw new BusinessRuleException("Select an active patient.");
            }
            else
            {
                var patientName = req.PatientName?.Trim();
                if (string.IsNullOrWhiteSpace(patientName)) throw new BusinessRuleException("Enter a patient name.");
                var normalizedPatientName = patientName.ToLowerInvariant();
                patient = await db.Patients.Where(p => p.IsActive
                        && (p.FullName.ToLower() == normalizedPatientName || p.PatientNo.ToLower() == normalizedPatientName
                            || p.Phone == patientName || p.Cnic == patientName))
                    .OrderByDescending(p => p.RegisteredAt).FirstOrDefaultAsync();
                if (patient == null)
                {
                    _user.Require(Permissions.PatientsManage);
                    var patientNo = await NumberGenerator.NextAsync(db, "PATIENT");
                    patient = new Patient
                    {
                        PatientNo = $"{_settings.Get(SettingKeys.PrefixPatient, "P")}-{patientNo:D6}",
                        FullName = patientName, Gender = Gender.Other, RegisteredAt = DateTime.UtcNow
                    };
                    db.Patients.Add(patient);
                }
            }
            var resolvedPatient = patient ?? throw new BusinessRuleException("Enter a patient name.");

            Doctor? doctor = null;
            if (req.DoctorId.HasValue)
            {
                doctor = await db.Doctors.FirstOrDefaultAsync(d => d.Id == req.DoctorId.Value && d.IsActive)
                         ?? throw new BusinessRuleException("The selected doctor is not available.");
            }
            else if (!string.IsNullOrWhiteSpace(req.DoctorName))
            {
                var doctorName = req.DoctorName.Trim();
                var normalizedDoctorName = doctorName.ToLowerInvariant();
                doctor = await db.Doctors.Where(d => d.IsActive
                        && (d.Name.ToLower() == normalizedDoctorName || d.DoctorNo.ToLower() == normalizedDoctorName))
                    .OrderBy(d => d.Name).FirstOrDefaultAsync();
                if (doctor == null)
                {
                    var doctorNo = await NumberGenerator.NextAsync(db, "DOCTOR");
                    doctor = new Doctor
                    {
                        DoctorNo = $"{_settings.Get(SettingKeys.PrefixDoctor, "DR")}-{doctorNo:D4}",
                        Name = doctorName, IsActive = true
                    };
                    db.Doctors.Add(doctor);
                }
            }

            var ids = req.TestIds.Distinct().ToList();
            var selected = await db.LabTests
                .Include(t => t.PanelItems).ThenInclude(i => i.ComponentTest)
                .Where(t => ids.Contains(t.Id)).ToListAsync();
            if (selected.Count != ids.Count) throw new BusinessRuleException("One or more selected tests no longer exist.");
            foreach (var t in selected)
                if (!t.IsActive) throw new BusinessRuleException($"'{t.Name}' is inactive and cannot be ordered.");

            var allowZero = _settings.GetBool(SettingKeys.AllowZeroPrice, false);
            foreach (var t in selected.Where(t => t.Price <= 0))
                if (!allowZero)
                    throw new BusinessRuleException($"The price of '{t.Name}' has not been set. An administrator must review test prices before billing.");

            // Expand panels; prevent the same test being performed twice.
            var performed = new List<(LabTest Test, LabTest Line)>();
            foreach (var line in selected)
            {
                var tests = line.IsPanel ? line.PanelItems.OrderBy(i => i.DisplayOrder).Select(i => i.ComponentTest).ToList() : new List<LabTest> { line };
                foreach (var t in tests)
                {
                    if (!t.IsActive) throw new BusinessRuleException($"'{t.Name}' (part of {line.Name}) is inactive.");
                    var dup = performed.FirstOrDefault(p => p.Test.Id == t.Id);
                    if (dup.Test != null)
                        throw new BusinessRuleException($"'{t.Name}' is included more than once ('{dup.Line.Name}' and '{line.Name}'). Remove one of them.");
                    performed.Add((t, line));
                }
            }

            var subtotal = selected.Sum(t => t.Price);
            if (req.Discount > subtotal) throw new BusinessRuleException("Discount cannot exceed the subtotal.");
            var net = subtotal - req.Discount;
            if (req.InitialPayment > net) throw new BusinessRuleException("Payment cannot exceed the net amount.");

            var now = DateTime.UtcNow;
            var fullWorkflow = _settings.FullWorkflow;
            var localDay = TimeHelper.ToLocal(now).ToString("yyyyMMdd");
            var seq = await NumberGenerator.NextAsync(db, $"ORDER-{localDay}");
            var order = new LabOrder
            {
                OrderNo = $"{_settings.Get(SettingKeys.PrefixOrder, "ORD")}-{localDay}-{seq:D4}",
                PatientId = resolvedPatient.Id, Patient = resolvedPatient, DoctorId = doctor?.Id, Doctor = doctor,
                OrderedAt = now, Subtotal = subtotal, Discount = req.Discount,
                NetAmount = net, Notes = req.Notes, CreatedByUserId = _user.UserId, Status = OrderStatus.Registered
            };
            db.Orders.Add(order);

            var lineMap = new Dictionary<int, OrderLine>();
            foreach (var t in selected)
            {
                var ol = new OrderLine { LabTest = t, Price = t.Price };
                order.Lines.Add(ol);
                lineMap[t.Id] = ol;
            }

            // Samples: one per distinct specimen type + container.
            var samples = new Dictionary<(int?, int?), Sample>();
            var bcDay = TimeHelper.ToLocal(now).ToString("yyMMdd");
            foreach (var (test, line) in performed)
            {
                Sample? sample = null;
                if (test.SpecimenTypeId != null)
                {
                    var key = (test.SpecimenTypeId, test.ContainerId);
                    if (!samples.TryGetValue(key, out sample))
                    {
                        var bseq = await NumberGenerator.NextAsync(db, $"BARCODE-{bcDay}");
                        sample = new Sample
                        {
                            Barcode = $"{bcDay}{bseq:D5}", SpecimenTypeId = test.SpecimenTypeId, ContainerId = test.ContainerId,
                            Status = fullWorkflow ? SampleStatus.PendingCollection : SampleStatus.ReceivedInLab,
                            CollectedAt = fullWorkflow ? null : now,
                            ReceivedAt = fullWorkflow ? null : now,
                            CollectedByUserId = fullWorkflow ? null : _user.UserId,
                            ReceivedByUserId = fullWorkflow ? null : _user.UserId,
                            CreatedAt = now
                        };
                        order.Samples.Add(sample);
                        samples[key] = sample;
                    }
                }
                order.Tests.Add(new OrderTest { LabTestId = test.Id, OrderLine = lineMap[line.Id], Sample = sample, Status = OrderTestStatus.Pending });
            }

            order.Report = new Report { Status = ReportStatus.Pending };

            Payment? payment = null;
            if (req.InitialPayment > 0)
            {
                payment = await NewPaymentAsync(db, order, req.InitialPayment, PaymentType.Payment, req.PaymentMethodId!.Value, req.PaymentReference, null);
            }
            Recalculate(order, order.Payments);

            await db.SaveChangesAsync();
            return new CreateOrderResult(order.Id, order.OrderNo, payment?.Id);
        });
    }

    private async Task<Payment> NewPaymentAsync(IAppDbContext db, LabOrder order, long amount, PaymentType type, int methodId, string? reference, string? reason)
    {
        var n = await NumberGenerator.NextAsync(db, "RECEIPT");
        var p = new Payment
        {
            ReceiptNo = $"{_settings.Get(SettingKeys.PrefixReceipt, "RCP")}-{n:D6}",
            Amount = amount, Type = type, PaymentMethodId = methodId, PaidAt = DateTime.UtcNow,
            ReceivedByUserId = _user.UserId, Reference = reference, Reason = reason
        };
        order.Payments.Add(p);
        return p;
    }

    public async Task<int> AddPaymentAsync(int orderId, long amount, int methodId, string? reference)
    {
        _user.Require(Permissions.PaymentsReceive);
        if (amount <= 0) throw new BusinessRuleException("Enter a payment amount greater than zero.");
        return await Retry.RunAsync(async () =>
        {
            await using var db = _factory.Create();
            var o = await db.Orders.Include(x => x.Payments).FirstAsync(x => x.Id == orderId);
            if (o.Status == OrderStatus.Cancelled) throw new BusinessRuleException("This order is cancelled.");
            if (amount > o.Balance) throw new BusinessRuleException($"Amount exceeds the outstanding balance ({o.Balance:N0}).");
            var p = await NewPaymentAsync(db, o, amount, PaymentType.Payment, methodId, reference, null);
            Recalculate(o, o.Payments);
            await db.SaveChangesAsync();
            return p.Id;
        });
    }

    public async Task<int> RefundAsync(int orderId, long amount, int methodId, string reason)
    {
        _user.Require(Permissions.OrdersRefund);
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("A reason is required for a refund.");
        if (amount <= 0) throw new BusinessRuleException("Enter a refund amount greater than zero.");
        return await Retry.RunAsync(async () =>
        {
            await using var db = _factory.Create();
            var o = await db.Orders.Include(x => x.Payments).FirstAsync(x => x.Id == orderId);
            if (amount > o.PaidAmount) throw new BusinessRuleException($"Refund exceeds the amount paid ({o.PaidAmount:N0}).");
            var p = await NewPaymentAsync(db, o, amount, PaymentType.Refund, methodId, null, reason.Trim());
            Recalculate(o, o.Payments);
            await db.SaveChangesAsync();
            return p.Id;
        });
    }

    public async Task CancelAsync(int orderId, string reason)
    {
        _user.Require(Permissions.OrdersCancel);
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("A reason is required to cancel an order.");
        await using var db = _factory.Create();
        var o = await db.Orders.Include(x => x.Tests).Include(x => x.Samples).Include(x => x.Report).Include(x => x.Payments)
            .FirstAsync(x => x.Id == orderId);
        if (o.Status == OrderStatus.Cancelled) throw new BusinessRuleException("The order is already cancelled.");
        if (o.Tests.Any(t => t.Status == OrderTestStatus.Verified))
            throw new BusinessRuleException("Reports for this order have been verified. Use the report amendment workflow instead of cancelling.");
        o.Status = OrderStatus.Cancelled; o.CancelReason = reason.Trim(); o.CancelledAt = DateTime.UtcNow; o.CancelledByUserId = _user.UserId;
        foreach (var t in o.Tests) t.Status = OrderTestStatus.Cancelled;
        if (o.Report != null) o.Report.Status = ReportStatus.Pending;
        await db.SaveChangesAsync();
    }

    public async Task<List<LabOrder>> SearchAsync(OrderFilter f)
    {
        _user.Require(Permissions.OrdersView);
        await using var db = _factory.Create();
        var q = db.Orders.AsNoTracking().Include(o => o.Patient).Include(o => o.Doctor).Include(o => o.Report)
            .AsQueryable();
        if (f.FromLocal.HasValue)
        {
            var (from, _) = TimeHelper.LocalDayRangeUtc(f.FromLocal.Value, f.FromLocal.Value);
            q = q.Where(o => o.OrderedAt >= from);
        }
        if (f.ToLocal.HasValue)
        {
            var (_, to) = TimeHelper.LocalDayRangeUtc(f.ToLocal.Value, f.ToLocal.Value);
            q = q.Where(o => o.OrderedAt < to);
        }
        if (f.Status != null) q = q.Where(o => o.Status == f.Status);
        if (f.OnlyWithBalance) q = q.Where(o => o.Balance > 0 && o.Status != OrderStatus.Cancelled);
        if (!string.IsNullOrWhiteSpace(f.Text))
        {
            var s = f.Text.Trim();
            q = q.Where(o => o.OrderNo.Contains(s) || o.Patient.FullName.Contains(s) || o.Patient.PatientNo.Contains(s)
                          || (o.Patient.Phone != null && o.Patient.Phone.Contains(s)) || o.Samples.Any(x => x.Barcode == s));
        }
        return await q.OrderByDescending(o => o.OrderedAt).ToListAsync();
    }

    public async Task<LabOrder> GetDetailAsync(int orderId)
    {
        _user.Require(Permissions.OrdersView);
        await using var db = _factory.Create();
        return await db.Orders.AsNoTracking().AsSplitQuery()
            .Include(o => o.Patient).Include(o => o.Doctor).Include(o => o.Report)
            .Include(o => o.Lines).ThenInclude(l => l.LabTest)
            .Include(o => o.Payments).ThenInclude(p => p.PaymentMethod)
            .Include(o => o.Samples).ThenInclude(s => s.SpecimenType)
            .Include(o => o.Samples).ThenInclude(s => s.Container)
            .Include(o => o.Tests).ThenInclude(t => t.LabTest)
            .FirstAsync(o => o.Id == orderId);
    }

    public async Task<(Payment Payment, LabOrder Order)> GetReceiptAsync(int paymentId)
    {
        await using var db = _factory.Create();
        var p = await db.Payments.AsNoTracking().Include(x => x.PaymentMethod).FirstAsync(x => x.Id == paymentId);
        var o = await db.Orders.AsNoTracking().Include(x => x.Patient).Include(x => x.Doctor)
            .Include(x => x.Lines).ThenInclude(l => l.LabTest).Include(x => x.Payments).ThenInclude(x => x.PaymentMethod)
            .FirstAsync(x => x.Id == p.OrderId);
        var cashier = await db.Users.AsNoTracking().Where(u => u.Id == p.ReceivedByUserId).Select(u => u.FullName).FirstOrDefaultAsync();
        p.Reference ??= null;
        return (p, o);
    }
}
