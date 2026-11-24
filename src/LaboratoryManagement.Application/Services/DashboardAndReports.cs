using System.Data;
using System.Globalization;
using System.Text;
using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LaboratoryManagement.Application.Services;

public record DashboardCard(string Title, string Value, string? Hint = null);
public record ChartPoint(string Label, decimal Value);

public class DashboardData
{
    public List<DashboardCard> Cards { get; set; } = new();
    public List<ChartPoint> DailyRegistrations { get; set; } = new();
    public List<ChartPoint> MonthlyRevenue { get; set; } = new();
    public List<ChartPoint> TopTests { get; set; } = new();
    public List<ChartPoint> CategoryDistribution { get; set; } = new();
}

public sealed class DashboardService
{
    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly SettingsService _settings;

    public DashboardService(IAppDbContextFactory factory, ICurrentUser user, SettingsService settings)
    { _factory = factory; _user = user; _settings = settings; }

    public async Task<DashboardData> LoadAsync()
    {
        _user.Require(Permissions.DashboardView);
        await using var db = _factory.Create();
        var cur = _settings.Get(SettingKeys.Currency, "PKR");
        var todayStart = DateTime.Today.ToUniversalTime();
        var todayEnd = todayStart.AddDays(1);
        var d = new DashboardData();
        var canOrders = _user.Has(Permissions.OrdersView);
        var canFinance = _user.Has(Permissions.FinanceReports);

        if (_user.Has(Permissions.PatientsView))
            d.Cards.Add(new("Today's patients", (await db.Patients.CountAsync(p => p.RegisteredAt >= todayStart && p.RegisteredAt < todayEnd)).ToString()));
        if (canOrders)
            d.Cards.Add(new("Today's orders", (await db.Orders.CountAsync(o => o.OrderedAt >= todayStart && o.OrderedAt < todayEnd && o.Status != OrderStatus.Cancelled)).ToString()));
        if (_user.Has(Permissions.SamplesManage))
            d.Cards.Add(new("Pending samples", (await db.Samples.CountAsync(s => (s.Status == SampleStatus.PendingCollection || s.Status == SampleStatus.Collected)
                && s.Order.Status != OrderStatus.Cancelled)).ToString(), "Awaiting collection / receipt"));
        if (_user.Has(Permissions.ResultsEnter))
            d.Cards.Add(new("Tests in progress", (await db.OrderTests.CountAsync(t => (t.Status == OrderTestStatus.Pending || t.Status == OrderTestStatus.InProgress)
                && t.Order.Status != OrderStatus.Cancelled)).ToString()));
        if (_user.Has(Permissions.ResultsVerify) || _user.Has(Permissions.ResultsEnter))
            d.Cards.Add(new("Awaiting verification", (await db.Reports.CountAsync(r => r.Status == ReportStatus.ReadyForVerification)).ToString()));
        if (canOrders)
            d.Cards.Add(new("Completed reports", (await db.Reports.CountAsync(r => r.Status == ReportStatus.Verified || r.Status == ReportStatus.Printed)).ToString()));
        if (canFinance)
        {
            var collected = await db.Payments.Where(p => p.PaidAt >= todayStart && p.PaidAt < todayEnd)
                .SumAsync(p => p.Type == PaymentType.Payment ? p.Amount : -p.Amount);
            d.Cards.Add(new("Today's collections", $"{cur} {collected:N0}", "Payments less refunds"));
            var outstanding = await db.Orders.Where(o => o.Status != OrderStatus.Cancelled && o.Balance > 0).SumAsync(o => (decimal?)o.Balance) ?? 0;
            d.Cards.Add(new("Outstanding balances", $"{cur} {outstanding:N0}"));
        }

        // Charts
        if (_user.Has(Permissions.PatientsView))
        {
            var from = todayStart.AddDays(-6);
            var regs = await db.Patients.Where(p => p.RegisteredAt >= from).Select(p => p.RegisteredAt).ToListAsync();
            for (var i = 6; i >= 0; i--)
            {
                var day = DateTime.Today.AddDays(-i);
                d.DailyRegistrations.Add(new(day.ToString("dd MMM"), regs.Count(r => TimeHelper.ToLocal(r).Date == day)));
            }
        }
        if (canFinance)
        {
            var firstMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-5);
            var from = firstMonth.ToUniversalTime();
            var pays = await db.Payments.Where(p => p.PaidAt >= from).Select(p => new { p.PaidAt, p.Amount, p.Type }).ToListAsync();
            for (var i = 0; i < 6; i++)
            {
                var m = firstMonth.AddMonths(i);
                var sum = pays.Where(p => { var l = TimeHelper.ToLocal(p.PaidAt); return l.Year == m.Year && l.Month == m.Month; })
                    .Sum(p => p.Type == PaymentType.Payment ? p.Amount : -p.Amount);
                d.MonthlyRevenue.Add(new(m.ToString("MMM yy"), sum));
            }
        }
        if (canOrders)
        {
            var lines = await db.OrderTests.Where(t => t.Order.Status != OrderStatus.Cancelled)
                .GroupBy(t => new { t.LabTest.Name }).Select(g => new { g.Key.Name, C = g.Count() })
                .OrderByDescending(x => x.C).Take(8).ToListAsync();
            d.TopTests = lines.Select(x => new ChartPoint(x.Name, x.C)).ToList();
            var cats = await db.OrderTests.Where(t => t.Order.Status != OrderStatus.Cancelled)
                .GroupBy(t => t.LabTest.Category.Name).Select(g => new { Name = g.Key, C = g.Count() })
                .OrderByDescending(x => x.C).Take(8).ToListAsync();
            d.CategoryDistribution = cats.Select(x => new ChartPoint(x.Name, x.C)).ToList();
        }
        return d;
    }
}

public record OperationalReportInfo(string Code, string Name, string Permission, bool UsesDates = true);

public sealed class OperationalReportService
{
    public static readonly IReadOnlyList<OperationalReportInfo> Reports = new[]
    {
        new OperationalReportInfo("daily-orders", "Daily patients / orders", Permissions.OrdersView),
        new OperationalReportInfo("test-stats", "Test-wise statistics", Permissions.OrdersView),
        new OperationalReportInfo("collections", "Daily collections & payments", Permissions.FinanceReports),
        new OperationalReportInfo("revenue", "Revenue summary (by day)", Permissions.FinanceReports),
        new OperationalReportInfo("outstanding", "Outstanding balances", Permissions.FinanceReports),
        new OperationalReportInfo("discounts", "Discounts given", Permissions.FinanceReports),
        new OperationalReportInfo("doctors", "Doctor referrals & commission", Permissions.CommissionsView),
        new OperationalReportInfo("pending-samples", "Pending samples", Permissions.SamplesManage),
        new OperationalReportInfo("pending-results", "Pending results", Permissions.ResultsEnter),
        new OperationalReportInfo("cancelled", "Cancelled orders", Permissions.OrdersView),
    };

    private readonly IAppDbContextFactory _factory;
    private readonly ICurrentUser _user;
    private readonly SettingsService _settings;

    public OperationalReportService(IAppDbContextFactory factory, ICurrentUser user, SettingsService settings)
    { _factory = factory; _user = user; _settings = settings; }

    public IEnumerable<OperationalReportInfo> Available() => Reports.Where(r => _user.Has(r.Permission));

    public async Task<DataTable> RunAsync(string code, DateTime fromLocal, DateTime toLocal)
    {
        var info = Reports.First(r => r.Code == code);
        _user.Require(info.Permission);
        var (from, to) = TimeHelper.LocalDayRangeUtc(fromLocal, toLocal);
        await using var db = _factory.Create();
        var t = new DataTable(info.Name);
        string L(DateTime utc) => TimeHelper.ToLocal(utc).ToString("dd-MMM-yyyy HH:mm");

        switch (code)
        {
            case "daily-orders":
            {
                var rows = await db.Orders.AsNoTracking().Include(o => o.Patient).Include(o => o.Doctor)
                    .Where(o => o.OrderedAt >= from && o.OrderedAt < to).OrderBy(o => o.OrderedAt).ToListAsync();
                Cols(t, "Date/Time", "Order No", "Patient", "Patient ID", "Doctor", "Status", "Net", "Paid", "Balance");
                foreach (var o in rows) t.Rows.Add(L(o.OrderedAt), o.OrderNo, o.Patient.FullName, o.Patient.PatientNo, o.Doctor?.Name ?? "", o.Status.ToString(), o.NetAmount, o.PaidAmount, o.Balance);
                break;
            }
            case "test-stats":
            {
                var rows = await db.OrderTests.AsNoTracking().Where(x => x.Order.OrderedAt >= from && x.Order.OrderedAt < to && x.Order.Status != OrderStatus.Cancelled)
                    .GroupBy(x => new { x.LabTest.Code, x.LabTest.Name, Cat = x.LabTest.Category.Name })
                    .Select(g => new { g.Key.Code, g.Key.Name, g.Key.Cat, Count = g.Count() }).OrderByDescending(x => x.Count).ToListAsync();
                Cols(t, "Code", "Test", "Category", "Count");
                foreach (var r in rows) t.Rows.Add(r.Code, r.Name, r.Cat, r.Count);
                break;
            }
            case "collections":
            {
                var rows = await db.Payments.AsNoTracking().Include(p => p.Order).ThenInclude(o => o.Patient).Include(p => p.PaymentMethod)
                    .Where(p => p.PaidAt >= from && p.PaidAt < to).OrderBy(p => p.PaidAt).ToListAsync();
                Cols(t, "Date/Time", "Receipt", "Order", "Patient", "Method", "Type", "Amount", "Reason");
                foreach (var p in rows) t.Rows.Add(L(p.PaidAt), p.ReceiptNo, p.Order.OrderNo, p.Order.Patient.FullName, p.PaymentMethod.Name, p.Type.ToString(),
                    p.Type == PaymentType.Payment ? p.Amount : -p.Amount, p.Reason ?? "");
                t.Rows.Add("", "", "", "", "", "TOTAL", rows.Sum(p => p.Type == PaymentType.Payment ? p.Amount : -p.Amount), "");
                break;
            }
            case "revenue":
            {
                var orders = await db.Orders.AsNoTracking().Where(o => o.OrderedAt >= from && o.OrderedAt < to && o.Status != OrderStatus.Cancelled).ToListAsync();
                Cols(t, "Date", "Orders", "Gross", "Discount", "Net", "Paid", "Balance");
                foreach (var g in orders.GroupBy(o => TimeHelper.ToLocal(o.OrderedAt).Date).OrderBy(g => g.Key))
                    t.Rows.Add(g.Key.ToString("dd-MMM-yyyy"), g.Count(), g.Sum(o => o.Subtotal), g.Sum(o => o.Discount), g.Sum(o => o.NetAmount), g.Sum(o => o.PaidAmount), g.Sum(o => o.Balance));
                t.Rows.Add("TOTAL", orders.Count, orders.Sum(o => o.Subtotal), orders.Sum(o => o.Discount), orders.Sum(o => o.NetAmount), orders.Sum(o => o.PaidAmount), orders.Sum(o => o.Balance));
                break;
            }
            case "outstanding":
            {
                var rows = await db.Orders.AsNoTracking().Include(o => o.Patient).Where(o => o.Balance > 0 && o.Status != OrderStatus.Cancelled).OrderBy(o => o.OrderedAt).ToListAsync();
                Cols(t, "Date", "Order No", "Patient", "Phone", "Net", "Paid", "Balance");
                foreach (var o in rows) t.Rows.Add(L(o.OrderedAt), o.OrderNo, o.Patient.FullName, o.Patient.Phone ?? "", o.NetAmount, o.PaidAmount, o.Balance);
                t.Rows.Add("", "", "", "TOTAL", rows.Sum(o => o.NetAmount), rows.Sum(o => o.PaidAmount), rows.Sum(o => o.Balance));
                break;
            }
            case "discounts":
            {
                var rows = await db.Orders.AsNoTracking().Include(o => o.Patient).Where(o => o.Discount > 0 && o.OrderedAt >= from && o.OrderedAt < to).OrderBy(o => o.OrderedAt).ToListAsync();
                Cols(t, "Date", "Order No", "Patient", "Subtotal", "Discount", "Net");
                foreach (var o in rows) t.Rows.Add(L(o.OrderedAt), o.OrderNo, o.Patient.FullName, o.Subtotal, o.Discount, o.NetAmount);
                break;
            }
            case "doctors":
            {
                var enabled = _settings.GetBool(SettingKeys.CommissionEnabled, false);
                var rows = await db.Orders.AsNoTracking().Include(o => o.Doctor)
                    .Where(o => o.DoctorId != null && o.OrderedAt >= from && o.OrderedAt < to && o.Status != OrderStatus.Cancelled).ToListAsync();
                if (enabled) Cols(t, "Doctor", "Doctor ID", "Orders", "Net revenue", "Commission rule", "Commission (calculated)");
                else Cols(t, "Doctor", "Doctor ID", "Orders", "Net revenue");
                foreach (var g in rows.GroupBy(o => o.DoctorId).OrderByDescending(g => g.Count()))
                {
                    var doc = g.First().Doctor!;
                    var net = g.Sum(o => o.NetAmount);
                    if (!enabled) { t.Rows.Add(doc.Name, doc.DoctorNo, g.Count(), net); continue; }
                    decimal comm = doc.CommissionType switch
                    {
                        CommissionType.Percentage => Math.Round(net * doc.CommissionValue / 100m, 2),
                        CommissionType.Fixed => doc.CommissionValue * g.Count(),
                        _ => 0m
                    };
                    var rule = doc.CommissionType switch
                    {
                        CommissionType.Percentage => $"{doc.CommissionValue:0.##}% of net",
                        CommissionType.Fixed => $"{doc.CommissionValue:0.##} per order",
                        _ => "None"
                    };
                    t.Rows.Add(doc.Name, doc.DoctorNo, g.Count(), net, rule, comm);
                }
                break;
            }
            case "pending-samples":
            {
                var rows = await db.Samples.AsNoTracking().Include(s => s.Order).ThenInclude(o => o.Patient).Include(s => s.SpecimenType)
                    .Where(s => (s.Status == SampleStatus.PendingCollection || s.Status == SampleStatus.Collected || s.Status == SampleStatus.RecollectionRequired) && s.Order.Status != OrderStatus.Cancelled)
                    .OrderBy(s => s.CreatedAt).ToListAsync();
                Cols(t, "Registered", "Barcode", "Order", "Patient", "Specimen", "Status");
                foreach (var s in rows) t.Rows.Add(L(s.CreatedAt), s.Barcode, s.Order.OrderNo, s.Order.Patient.FullName, s.SpecimenType?.Name ?? "", s.Status.ToString());
                break;
            }
            case "pending-results":
            {
                var rows = await db.OrderTests.AsNoTracking().Include(x => x.Order).ThenInclude(o => o.Patient).Include(x => x.LabTest)
                    .Where(x => x.Status != OrderTestStatus.Verified && x.Status != OrderTestStatus.Cancelled && x.Order.Status != OrderStatus.Cancelled)
                    .OrderBy(x => x.Order.OrderedAt).ToListAsync();
                Cols(t, "Ordered", "Order", "Patient", "Test", "Status", "Due (TAT)");
                foreach (var x in rows) t.Rows.Add(L(x.Order.OrderedAt), x.Order.OrderNo, x.Order.Patient.FullName, x.LabTest.Name, x.Status.ToString(), L(x.Order.OrderedAt.AddHours(x.LabTest.TurnaroundHours)));
                break;
            }
            case "cancelled":
            {
                var rows = await db.Orders.AsNoTracking().Include(o => o.Patient).Where(o => o.Status == OrderStatus.Cancelled && o.OrderedAt >= from && o.OrderedAt < to).OrderBy(o => o.OrderedAt).ToListAsync();
                Cols(t, "Date", "Order No", "Patient", "Net", "Paid (net of refunds)", "Cancel reason");
                foreach (var o in rows) t.Rows.Add(L(o.OrderedAt), o.OrderNo, o.Patient.FullName, o.NetAmount, o.PaidAmount, o.CancelReason ?? "");
                break;
            }
        }
        return t;
    }

    private static void Cols(DataTable t, params string[] names)
    {
        foreach (var n in names)
        {
            var numeric = n is "Net" or "Paid" or "Balance" or "Gross" or "Discount" or "Amount" or "Subtotal" or "Count" or "Orders"
                or "Net revenue" or "Commission (calculated)" or "Paid (net of refunds)" or "Commission";
            t.Columns.Add(n, numeric ? typeof(object) : typeof(string));
        }
    }

    public static string ToCsv(DataTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => Esc(c.ColumnName))));
        foreach (DataRow r in table.Rows)
            sb.AppendLine(string.Join(",", r.ItemArray.Select(v => Esc(v is decimal d ? d.ToString("0.00", CultureInfo.InvariantCulture) : Convert.ToString(v, CultureInfo.InvariantCulture)))));
        return sb.ToString();
    }

    private static string Esc(string? s)
    {
        s ??= "";
        // Guard against spreadsheet formula injection.
        if (s.Length > 0 && "=+-@".Contains(s[0]) && !decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) s = "'" + s;
        return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
