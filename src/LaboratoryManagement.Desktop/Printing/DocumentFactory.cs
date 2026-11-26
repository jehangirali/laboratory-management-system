using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Services;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;

namespace LaboratoryManagement.Desktop.Printing;

/// <summary>Builds printable documents (reports, receipts, sample labels, patient history). All are FixedDocuments so the
/// same object feeds preview, printing and XPS export. Templates only change layout; stored results are untouched.</summary>
public sealed class DocumentFactory
{
    private readonly SettingsService _settings;
    public DocumentFactory(SettingsService settings) => _settings = settings;

    private static double Pt(double points) => points * 96.0 / 72.0;
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x33));
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(0x9A, 0xA5, 0xB1));
    private static readonly Brush Band = new SolidColorBrush(Color.FromRgb(0xE8, 0xEF, 0xEE));

    private static TextBlock Tb(string text, double size, FontWeight? weight = null, Brush? fg = null, TextAlignment align = TextAlignment.Left, bool wrap = true)
        => new() { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = fg ?? Ink, TextAlignment = align, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };

    private static string Local(DateTime? utc, string fmt = "dd-MMM-yyyy HH:mm") => utc == null ? "-" : TimeHelper.ToLocal(utc.Value).ToString(fmt, CultureInfo.InvariantCulture);

    private FrameworkElement LabHeader(string lab, string addr, string phone, string email, string license, string logo, double fs, string? rightTitle, string? rightNote, Brush? rightBrush = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!string.IsNullOrWhiteSpace(logo) && File.Exists(logo))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(logo); bmp.EndInit(); bmp.Freeze();
                grid.Children.Add(new Image { Source = bmp, Height = 52, Margin = new Thickness(0, 0, 10, 0), Stretch = Stretch.Uniform });
            }
            catch { /* ignore unreadable logo */ }
        }
        var left = new StackPanel();
        left.Children.Add(Tb(lab, fs * 1.7, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E))));
        if (!string.IsNullOrWhiteSpace(addr)) left.Children.Add(Tb(addr, fs * 0.85));
        left.Children.Add(Tb(string.Join("   ", new[] { phone, email }.Where(s => !string.IsNullOrWhiteSpace(s))), fs * 0.85));
        if (!string.IsNullOrWhiteSpace(license)) left.Children.Add(Tb(license, fs * 0.8, null, Brushes.DimGray));
        Grid.SetColumn(left, 1); grid.Children.Add(left);
        if (rightTitle != null)
        {
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(Tb(rightTitle, fs * 1.1, FontWeights.Bold, rightBrush, TextAlignment.Right));
            if (rightNote != null) right.Children.Add(Tb(rightNote, fs * 0.8, null, rightBrush ?? Brushes.DimGray, TextAlignment.Right));
            Grid.SetColumn(right, 2); grid.Children.Add(right);
        }
        return grid;
    }

    private static FrameworkElement Rule() => new Border { Height = 1, Background = Line, Margin = new Thickness(0, 4, 0, 4) };

    // ------------------------------------------------------------------ Patient report
    public FixedDocument BuildReport(ReportModel m)
    {
        var fs = Pt(m.FontSize);
        var pageSize = PageSizes.For(m.PageSize);
        var margin = new Thickness(PageSizes.MmToDip(m.MarginMm));
        var compact = m.Template == ReportTemplateType.Hematology;
        var title = m.Template switch { ReportTemplateType.Hematology => "HEMATOLOGY REPORT", ReportTemplateType.Profile => "PROFILE / PANEL REPORT", _ => "LABORATORY REPORT" };
        var unverified = !m.IsVerified;
        var amendNote = m.IsAmended ? "AMENDED REPORT" : null;

        FrameworkElement Header(int page, int total)
        {
            var panel = new StackPanel();
            string? rt = unverified ? "PRELIMINARY - NOT VERIFIED" : amendNote;
            panel.Children.Add(LabHeader(m.LabName, m.LabAddress, m.LabPhone, m.LabEmail, m.LabLicense, m.LogoPath, fs, rt,
                unverified ? "Not valid for clinical use" : (m.IsAmended ? "Corrected report" : null),
                (unverified || m.IsAmended) ? Brushes.Firebrick : null));
            panel.Children.Add(Rule());
            panel.Children.Add(Tb(title, fs * 1.05, FontWeights.SemiBold, null, TextAlignment.Center));

            var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            for (var i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
            var rows = new List<(string, string, string, string)>
            {
                ("Patient:", m.PatientName, "Order / Sample:", $"{m.OrderNo}" + (string.IsNullOrWhiteSpace(m.SampleNos) ? "" : $" / {m.SampleNos}")),
                ("Patient ID:", m.PatientNo, "Registered:", Local(m.OrderedAtUtc)),
                ("Age / Sex:", $"{m.AgeText} / {m.Gender}", "Collected:", Local(m.CollectedAtUtc)),
                ("Referred by:", m.DoctorName, "Reported:", m.IsVerified ? Local(m.ReportDateUtc) : "Pending"),
            };
            if (m.ShowFatherName && !string.IsNullOrWhiteSpace(m.FatherName)) rows.Add(("Father/Husband:", m.FatherName, m.ShowPhone ? "Phone:" : "", m.ShowPhone ? m.Phone : ""));
            else if (m.ShowPhone && !string.IsNullOrWhiteSpace(m.Phone)) rows.Add(("Phone:", m.Phone, "", ""));
            for (var r = 0; r < rows.Count; r++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var cells = new[] { rows[r].Item1, rows[r].Item2, rows[r].Item3, rows[r].Item4 };
                for (var c = 0; c < 4; c++)
                {
                    var t = Tb(cells[c], fs * 0.95, c % 2 == 0 ? FontWeights.SemiBold : FontWeights.Normal);
                    t.Margin = new Thickness(0, 1, c % 2 == 0 ? 6 : 14, 1);
                    Grid.SetRow(t, r); Grid.SetColumn(t, c); g.Children.Add(t);
                }
            }
            panel.Children.Add(g);
            panel.Children.Add(Rule());
            return panel;
        }

        FrameworkElement Footer(int page, int total)
        {
            var p = new StackPanel();
            p.Children.Add(Rule());
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            if (!string.IsNullOrWhiteSpace(m.Disclaimer)) left.Children.Add(Tb(m.Disclaimer, fs * 0.75, null, Brushes.DimGray));
            if (!string.IsNullOrWhiteSpace(m.Footer)) left.Children.Add(Tb(m.Footer, fs * 0.75, null, Brushes.DimGray));
            g.Children.Add(left);
            var pg = Tb($"Page {page} of {total}", fs * 0.8, null, Brushes.DimGray, TextAlignment.Right);
            pg.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(pg, 1); g.Children.Add(pg);
            p.Children.Add(g);
            return p;
        }

        var body = new FlowDocument { FontFamily = new FontFamily("Segoe UI, Arial"), FontSize = fs, Foreground = Ink, PagePadding = margin };
        foreach (var sec in m.Sections)
        {
            var heading = new Paragraph(new Run(sec.Title.ToUpperInvariant())) { FontWeight = FontWeights.Bold, FontSize = fs * (compact ? 1.05 : 1.15), Margin = new Thickness(0, compact ? 4 : 10, 0, 2), KeepWithNext = true };
            if (m.Template == ReportTemplateType.Profile) { heading.Background = Band; heading.Padding = new Thickness(4, 2, 4, 2); }
            body.Blocks.Add(heading);
            var sub = string.Join("  |  ", new[] { sec.Specimen.Length > 0 ? "Specimen: " + sec.Specimen : "", string.IsNullOrWhiteSpace(sec.Method) ? "" : "Method: " + sec.Method }.Where(s => s.Length > 0));
            if (sub.Length > 0) body.Blocks.Add(new Paragraph(new Run(sub)) { FontSize = fs * 0.8, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 3), KeepWithNext = true });

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, compact ? 2 : 6) };
            var widths = m.ShowReference ? new[] { 3.0, 1.4, 1.1, 2.2, 0.6 } : new[] { 3.5, 1.8, 1.4, 0.0001, 0.6 };
            foreach (var w in widths) table.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });
            var group = new TableRowGroup(); table.RowGroups.Add(group);
            var head = new TableRow { Background = Band };
            foreach (var h in new[] { "Test / Parameter", "Result", "Unit", m.ShowReference ? "Reference Range" : "", "Flag" })
                head.Cells.Add(new TableCell(new Paragraph(new Run(h)) { FontWeight = FontWeights.SemiBold, FontSize = fs * 0.9 }) { Padding = new Thickness(4, 2, 4, 2), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) });
            group.Rows.Add(head);
            foreach (var r in sec.Rows)
            {
                var abnormal = r.Flag != ResultFlag.None;
                var crit = r.Flag is ResultFlag.CriticalHigh or ResultFlag.CriticalLow;
                var vBrush = crit ? Brushes.Firebrick : abnormal ? Brushes.DarkOrange : Ink;
                var row = new TableRow();
                var pad = new Thickness(4, compact ? 1 : 2.5, 4, compact ? 1 : 2.5);
                TableCell Cell(string text, bool bold = false, Brush? br = null, TextAlignment al = TextAlignment.Left)
                    => new(new Paragraph(new Run(text)) { FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = br ?? Ink, TextAlignment = al, Margin = new Thickness(0) }) { Padding = pad };
                row.Cells.Add(Cell(r.Name));
                row.Cells.Add(Cell(r.Value, abnormal, vBrush));
                row.Cells.Add(Cell(r.Unit));
                row.Cells.Add(Cell(m.ShowReference ? r.Reference : ""));
                row.Cells.Add(Cell(ReferenceRangeEvaluator.FlagText(r.Flag), true, vBrush, TextAlignment.Center));
                group.Rows.Add(row);
                if (!string.IsNullOrWhiteSpace(r.Comment))
                {
                    var cr = new TableRow();
                    var cc = new TableCell(new Paragraph(new Run("Comment: " + r.Comment)) { FontSize = fs * 0.85, FontStyle = FontStyles.Italic, Margin = new Thickness(0) }) { ColumnSpan = 5, Padding = new Thickness(14, 0, 4, 2) };
                    cr.Cells.Add(cc); group.Rows.Add(cr);
                }
            }
            body.Blocks.Add(table);
        }

        if (m.IsAmended && !string.IsNullOrWhiteSpace(m.AmendmentReason))
            body.Blocks.Add(new Paragraph(new Run("Amendment note: " + m.AmendmentReason)) { FontSize = fs * 0.85, FontStyle = FontStyles.Italic, Foreground = Brushes.Firebrick });

        if (m.IsVerified && m.ShowSignature)
        {
            var sig = new Paragraph { Margin = new Thickness(0, 18, 0, 0), KeepTogether = true, TextAlignment = TextAlignment.Right };
            sig.Inlines.Add(new Run(string.IsNullOrWhiteSpace(m.SignatureText) ? m.VerifierName : m.SignatureText) { FontStyle = FontStyles.Italic, FontSize = fs * 1.2, FontFamily = new FontFamily("Segoe Script, Segoe UI") });
            sig.Inlines.Add(new LineBreak());
            sig.Inlines.Add(new Run(m.VerifierName) { FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrWhiteSpace(m.VerifierDesignation)) { sig.Inlines.Add(new LineBreak()); sig.Inlines.Add(new Run(m.VerifierDesignation) { FontSize = fs * 0.9 }); }
            sig.Inlines.Add(new LineBreak());
            sig.Inlines.Add(new Run($"Electronically verified on {Local(m.VerifiedAtUtc)}") { FontSize = fs * 0.8, Foreground = Brushes.DimGray });
            body.Blocks.Add(sig);
        }
        body.Blocks.Add(new Paragraph(new Run("*** End of report ***")) { TextAlignment = TextAlignment.Center, FontSize = fs * 0.8, Foreground = Brushes.DimGray });

        return PagedDocumentBuilder.Build(body, pageSize, margin, Header, Footer);
    }

    // ------------------------------------------------------------------ Receipt
    public FixedDocument BuildReceipt(Payment payment, LabOrder order)
    {
        var fs = Pt(10.5);
        var cur = _settings.Get(SettingKeys.Currency, "PKR");
        var pageSize = new Size(PageSizes.MmToDip(148), PageSizes.MmToDip(210)); // A5
        var margin = new Thickness(PageSizes.MmToDip(10));
        FrameworkElement Header(int p, int t)
        {
            var sp = new StackPanel();
            sp.Children.Add(LabHeader(_settings.Get(SettingKeys.LabName), _settings.Get(SettingKeys.LabAddress), _settings.Get(SettingKeys.LabPhone),
                _settings.Get(SettingKeys.LabEmail), _settings.Get(SettingKeys.LabLicense), _settings.Get(SettingKeys.LabLogoPath), fs, payment.Type == PaymentType.Refund ? "REFUND VOUCHER" : "PAYMENT RECEIPT", payment.ReceiptNo));
            sp.Children.Add(Rule());
            return sp;
        }
        FrameworkElement Footer(int p, int t) => Tb("Thank you. This is a computer-generated receipt.", fs * 0.8, null, Brushes.DimGray, TextAlignment.Center);

        var body = new FlowDocument { FontFamily = new FontFamily("Segoe UI, Arial"), FontSize = fs, Foreground = Ink };
        void Line2(string a, string b) { var p = new Paragraph { Margin = new Thickness(0, 1, 0, 1) }; p.Inlines.Add(new Run(a + " ") { FontWeight = FontWeights.SemiBold }); p.Inlines.Add(new Run(b)); body.Blocks.Add(p); }
        Line2("Date:", Local(payment.PaidAt));
        Line2("Order No:", order.OrderNo);
        Line2("Patient:", $"{order.Patient.FullName} ({order.Patient.PatientNo})");
        Line2("Referred by:", order.Doctor?.Name ?? "-");

        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 8) };
        table.Columns.Add(new TableColumn { Width = new GridLength(4, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1.4, GridUnitType.Star) });
        var g = new TableRowGroup(); table.RowGroups.Add(g);
        TableRow R(string a, string b, bool bold = false, bool top = false)
        {
            var row = new TableRow();
            row.Cells.Add(new TableCell(new Paragraph(new Run(a)) { FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Margin = new Thickness(0) }) { Padding = new Thickness(2, 2, 2, 2), BorderBrush = Line, BorderThickness = new Thickness(0, top ? 1 : 0, 0, 0) });
            row.Cells.Add(new TableCell(new Paragraph(new Run(b)) { TextAlignment = TextAlignment.Right, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Margin = new Thickness(0) }) { Padding = new Thickness(2), BorderBrush = Line, BorderThickness = new Thickness(0, top ? 1 : 0, 0, 0) });
            return row;
        }
        g.Rows.Add(R("Test / Panel", $"Price ({cur})", true));
        foreach (var l in order.Lines) g.Rows.Add(R(l.LabTest.Name, l.Price.ToString("N0")));
        g.Rows.Add(R("Subtotal", order.Subtotal.ToString("N0"), false, true));
        g.Rows.Add(R("Discount", "- " + order.Discount.ToString("N0")));
        g.Rows.Add(R("Net amount", order.NetAmount.ToString("N0"), true));
        g.Rows.Add(R($"{(payment.Type == PaymentType.Refund ? "Refunded now" : "Received now")} ({payment.PaymentMethod.Name})", payment.Amount.ToString("N0"), true, true));
        g.Rows.Add(R("Total paid to date", order.PaidAmount.ToString("N0")));
        g.Rows.Add(R("Balance due", order.Balance.ToString("N0"), true));
        body.Blocks.Add(table);
        if (!string.IsNullOrWhiteSpace(payment.Reason)) Line2("Reason:", payment.Reason!);
        Line2("Payment status:", order.PaymentStatus.ToString());
        return PagedDocumentBuilder.Build(body, pageSize, margin, Header, Footer);
    }

    // ------------------------------------------------------------------ Sample labels (one page per label)
    public FixedDocument BuildLabels(IEnumerable<SampleRow> samples)
    {
        var w = PageSizes.MmToDip(_settings.GetDouble(SettingKeys.LabelWidthMm, 50));
        var h = PageSizes.MmToDip(_settings.GetDouble(SettingKeys.LabelHeightMm, 30));
        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = new Size(w, h);
        foreach (var s in samples)
        {
            var page = new FixedPage { Width = w, Height = h, Background = Brushes.White };
            var sp = new StackPanel { Width = w - 8, Margin = new Thickness(4, 2, 4, 2) };
            sp.Children.Add(Tb(s.PatientName, 9, FontWeights.Bold, null, TextAlignment.Left, false));
            sp.Children.Add(Tb($"{s.PatientNo}  |  {s.OrderNo}", 7.5, null, null, TextAlignment.Left, false));
            sp.Children.Add(Tb($"{s.Specimen} / {s.Container}", 7.5, null, null, TextAlignment.Left, false));
            var bc = Code128.Create(s.Barcode, Math.Max(18, h * 0.38), 1.0);
            var vb = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = w - 8, Child = bc, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(vb);
            sp.Children.Add(Tb(s.Barcode, 8, FontWeights.SemiBold, null, TextAlignment.Center, false));
            page.Children.Add(sp);
            var size = new Size(w, h); page.Measure(size); page.Arrange(new Rect(size)); page.UpdateLayout();
            var pc = new PageContent(); ((System.Windows.Markup.IAddChild)pc).AddChild(page); doc.Pages.Add(pc);
        }
        return doc;
    }

    // ------------------------------------------------------------------ Patient history
    public FixedDocument BuildPatientHistory(Patient p, IEnumerable<LabOrder> orders)
    {
        var fs = Pt(10.5);
        var pageSize = PageSizes.For("A4"); var margin = new Thickness(PageSizes.MmToDip(14));
        FrameworkElement Header(int a, int b)
        {
            var sp = new StackPanel();
            sp.Children.Add(LabHeader(_settings.Get(SettingKeys.LabName), _settings.Get(SettingKeys.LabAddress), _settings.Get(SettingKeys.LabPhone),
                _settings.Get(SettingKeys.LabEmail), _settings.Get(SettingKeys.LabLicense), _settings.Get(SettingKeys.LabLogoPath), fs, "PATIENT HISTORY", null));
            sp.Children.Add(Rule());
            sp.Children.Add(Tb($"{p.FullName}   |   {p.PatientNo}   |   {p.Gender}, {p.AgeText}   |   {p.Phone}", fs, FontWeights.SemiBold));
            sp.Children.Add(Rule());
            return sp;
        }
        FrameworkElement Footer(int a, int b) => Tb($"Page {a} of {b}   -   Printed {DateTime.Now:dd-MMM-yyyy HH:mm}", fs * 0.8, null, Brushes.DimGray, TextAlignment.Right);
        var body = new FlowDocument { FontFamily = new FontFamily("Segoe UI, Arial"), FontSize = fs };
        var t = new Table { CellSpacing = 0 };
        foreach (var wd in new[] { 1.6, 1.6, 4, 1.2, 1.2 }) t.Columns.Add(new TableColumn { Width = new GridLength(wd, GridUnitType.Star) });
        var g = new TableRowGroup(); t.RowGroups.Add(g);
        TableRow Row(params string[] c)
        {
            var r = new TableRow();
            foreach (var x in c) r.Cells.Add(new TableCell(new Paragraph(new Run(x)) { Margin = new Thickness(0) }) { Padding = new Thickness(3), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 0.5) });
            return r;
        }
        g.Rows.Add(Row("Date", "Order", "Tests", "Net", "Report"));
        foreach (var o in orders)
            g.Rows.Add(Row(Local(o.OrderedAt, "dd-MMM-yyyy"), o.OrderNo, string.Join(", ", o.Lines.Select(l => l.LabTest.Name)), o.NetAmount.ToString("N0"), o.Report?.Status.ToString() ?? "-"));
        body.Blocks.Add(t);
        return PagedDocumentBuilder.Build(body, pageSize, margin, Header, Footer);
    }
}
