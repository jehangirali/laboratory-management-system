using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LaboratoryManagement.Application.Services;

public class CreateOrderRequest
{
    public int PatientId { get; set; }
    public int? DoctorId { get; set; }
    public List<int> TestIds { get; set; } = new();
    public decimal Discount { get; set; }
    public decimal InitialPayment { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? PaymentReference { get; set; }
    public string? Notes { get; set; }
}

public record CreateOrderResult(int OrderId, string OrderNo, int? PaymentId);

public class OrderFilter
{
    public DateTime FromLocal { get; set; } = DateTime.Today;
    public DateTime ToLocal { get; set; } = DateTime.Today;
    public string? Text { get; set; }
    public OrderStatus? Status { get; set; }
    public bool OnlyWithBalance { get; set; }
}

public sealed class OrderService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly SettingsService _settings;

    public OrderService(IAppDbContextFactory factory, ICurrentUser user, IAuditService audit, SettingsService settings)
    { _factory = factory; _user = user; _audit = audit; _settings = settings; }

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
            var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == req.PatientId && p.IsActive)
                          ?? throw new BusinessRuleException("Select a patient.");
            if (req.DoctorId != null && !await db.Doctors.AnyAsync(d => d.Id == req.DoctorId && d.IsActive))
                throw new BusinessRuleException("The selected doctor is not available.");

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
            var localDay = TimeHelper.ToLocal(now).ToString("yyyyMMdd");
            var seq = await NumberGenerator.NextAsync(db, $"ORDER-{localDay}");
            var order = new LabOrder
            {
                OrderNo = $"{_settings.Get(SettingKeys.PrefixOrder, "ORD")}-{localDay}-{seq:D4}",
                PatientId = patient.Id, DoctorId = req.DoctorId, OrderedAt = now, Subtotal = subtotal, Discount = req.Discount,
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
                            Status = SampleStatus.PendingCollection, CreatedAt = now
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

            _audit.Add(db, "Order created", "LabOrder", order.OrderNo,
                       $"Patient={patient.PatientNo}; Tests={string.Join(",", selected.Select(t => t.Code))}; Subtotal={subtotal:0.00}; Discount={req.Discount:0.00}; Paid={req.InitialPayment:0.00}");
            await db.SaveChangesAsync();
            return new CreateOrderResult(order.Id, order.OrderNo, payment?.Id);
        });
    }

    private async Task<Payment> NewPaymentAsync(IAppDbContext db, LabOrder order, decimal amount, PaymentType type, int methodId, string? reference, string? reason)
    {
        var n = await NumberGenerator.NextAsync(db, "RECEIPT");
        var p = new Payment
        {
            ReceiptNo = $"{_settings.Get(SettingKeys.PrefixReceipt, "RCP")}-{n:D6}",
            Amount = amount, Type = type, PaymentMethodId = methodId, PaidAt = DateTime.UtcNow,
            ReceivedByUserId = _user.UserId, Reference = reference, Reason = reason
        };
        order.Payments.Add(p);
        _audit.Add(db, type == PaymentType.Payment ? "Payment received" : "Refund issued", "Payment", p.ReceiptNo,
                   $"Order={order.OrderNo}; Amount={amount:0.00}; Reason={reason}");
        return p;
    }

    public async Task<int> AddPaymentAsync(int orderId, decimal amount, int methodId, string? reference)
    {
        _user.Require(Permissions.PaymentsReceive);
        if (amount <= 0) throw new BusinessRuleException("Enter a payment amount greater than zero.");
        return await Retry.RunAsync(async () =>
        {
            await using var db = _factory.Create();
            var o = await db.Orders.Include(x => x.Payments).FirstAsync(x => x.Id == orderId);
            if (o.Status == OrderStatus.Cancelled) throw new BusinessRuleException("This order is cancelled.");
            if (amount > o.Balance) throw new BusinessRuleException($"Amount exceeds the outstanding balance ({o.Balance:0.00}).");
            var p = await NewPaymentAsync(db, o, amount, PaymentType.Payment, methodId, reference, null);
            Recalculate(o, o.Payments);
            await db.SaveChangesAsync();
            return p.Id;
        });
    }

    public async Task<int> RefundAsync(int orderId, decimal amount, int methodId, string reason)
    {
        _user.Require(Permissions.OrdersRefund);
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("A reason is required for a refund.");
        if (amount <= 0) throw new BusinessRuleException("Enter a refund amount greater than zero.");
        return await Retry.RunAsync(async () =>
        {
            await using var db = _factory.Create();
            var o = await db.Orders.Include(x => x.Payments).FirstAsync(x => x.Id == orderId);
            if (amount > o.PaidAmount) throw new BusinessRuleException($"Refund exceeds the amount paid ({o.PaidAmount:0.00}).");
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
        _audit.Add(db, "Order cancelled", "LabOrder", o.OrderNo, $"Reason: {reason}; Paid={o.PaidAmount:0.00} (refund separately if required)");
        await db.SaveChangesAsync();
    }

    public async Task<List<LabOrder>> SearchAsync(OrderFilter f)
    {
        _user.Require(Permissions.OrdersView);
        await using var db = _factory.Create();
        var (from, to) = TimeHelper.LocalDayRangeUtc(f.FromLocal, f.ToLocal);
        var q = db.Orders.AsNoTracking().Include(o => o.Patient).Include(o => o.Doctor).Include(o => o.Report)
            .Where(o => o.OrderedAt >= from && o.OrderedAt < to);
        if (f.Status != null) q = q.Where(o => o.Status == f.Status);
        if (f.OnlyWithBalance) q = q.Where(o => o.Balance > 0 && o.Status != OrderStatus.Cancelled);
        if (!string.IsNullOrWhiteSpace(f.Text))
        {
            var s = f.Text.Trim();
            q = q.Where(o => o.OrderNo.Contains(s) || o.Patient.FullName.Contains(s) || o.Patient.PatientNo.Contains(s)
                          || (o.Patient.Phone != null && o.Patient.Phone.Contains(s)) || o.Samples.Any(x => x.Barcode == s));
        }
        return await q.OrderByDescending(o => o.OrderedAt).Take(500).ToListAsync();
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
