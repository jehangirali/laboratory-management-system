using System.Windows;
using System.Windows.Documents;
using System.Globalization;
using LaboratoryManagement.Application.Services;
using LaboratoryManagement.Desktop.Printing;
using LaboratoryManagement.Desktop.ViewModels;
using LaboratoryManagement.Desktop.Views;
using LaboratoryManagement.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace LaboratoryManagement.Desktop.Services;

public interface IDialogService
{
    string? Prompt(string title, string label, string initial = "");
    PaymentRequest? AskPayment(string title, long maxAmount, bool isRefund);
    Patient? EditPatient(int? id, string? suggestedName = null);
    Doctor? EditDoctor(int? id, string? suggestedName = null);
    bool EditTest(int? id, bool isPanel);
    bool EditUser(int? id);
    void ShowOrder(int orderId);
    void ShowPatientHistory(int patientId);
    void ChangePassword(bool forced = false);
    void PreviewReport(int orderId);
    void PreviewReceipt(int paymentId);
    void PreviewLabels(IEnumerable<int> sampleIds);
    void ShowDocument(Func<FixedDocument> builder, string title, Action? onPrinted = null);
    void ShowReportModel(ReportModel model, string title);
}

public sealed class DialogService : IDialogService
{
    private readonly IServiceProvider _sp;
    public DialogService(IServiceProvider sp) => _sp = sp;

    private static Window? Owner => System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                    ?? System.Windows.Application.Current.MainWindow;

    private static bool Show(Window w) { w.Owner = Owner; return w.ShowDialog() == true; }

    public string? Prompt(string title, string label, string initial = "")
    {
        var vm = new PromptViewModel { Title = title, Label = label, Text = initial };
        Show(new PromptWindow { DataContext = vm });
        return vm.Accepted ? vm.Text.Trim() : null;
    }

    public PaymentRequest? AskPayment(string title, long maxAmount, bool isRefund)
    {
        var vm = _sp.GetRequiredService<PaymentDialogViewModel>();
        vm.Title = title; vm.MaxAmount = maxAmount; vm.IsRefund = isRefund; vm.Amount = maxAmount.ToString(CultureInfo.CurrentCulture);
        vm.Caption = isRefund ? $"Maximum refundable: {maxAmount:N0}" : $"Outstanding balance: {maxAmount:N0}";
        Show(new PaymentWindow { DataContext = vm });
        return vm.Result;
    }

    public Patient? EditPatient(int? id, string? suggestedName = null)
    {
        var vm = _sp.GetRequiredService<PatientEditorViewModel>();
        vm.Initializer = () => vm.InitAsync(id, suggestedName);
        return Show(new PatientEditorWindow { DataContext = vm }) ? vm.Saved : null;
    }

    public Doctor? EditDoctor(int? id, string? suggestedName = null)
    {
        var vm = _sp.GetRequiredService<DoctorEditorViewModel>();
        vm.Initializer = () => vm.InitAsync(id, suggestedName);
        return Show(new DoctorEditorWindow { DataContext = vm }) ? vm.Saved : null;
    }

    public bool EditTest(int? id, bool isPanel)
    {
        var vm = _sp.GetRequiredService<TestEditorViewModel>();
        vm.Initializer = () => vm.InitAsync(id, isPanel);
        return Show(new TestEditorWindow { DataContext = vm });
    }

    public bool EditUser(int? id)
    {
        var vm = _sp.GetRequiredService<UserEditorViewModel>();
        vm.Initializer = () => vm.InitAsync(id);
        return Show(new UserEditorWindow { DataContext = vm });
    }

    public void ShowOrder(int orderId)
    {
        var vm = _sp.GetRequiredService<OrderDetailViewModel>();
        vm.Initializer = () => vm.InitAsync(orderId);
        Show(new OrderDetailWindow { DataContext = vm });
    }

    public void ShowPatientHistory(int patientId)
    {
        var vm = _sp.GetRequiredService<PatientHistoryViewModel>();
        vm.Initializer = () => vm.InitAsync(patientId);
        Show(new PatientHistoryWindow { DataContext = vm });
    }

    public void ChangePassword(bool forced = false)
    {
        var vm = _sp.GetRequiredService<ChangePasswordViewModel>();
        vm.Forced = forced;
        Show(new ChangePasswordWindow { DataContext = vm });
    }

    public void PreviewReport(int orderId) => _ = PreviewReportAsync(orderId);

    private async Task PreviewReportAsync(int orderId)
    {
        try
        {
            var reports = _sp.GetRequiredService<ReportService>();
            var model = await reports.BuildAsync(orderId);
            ShowReportModel(model, $"Report {model.OrderNo}");
        }
        catch (Exception ex) { Common.Ui.HandleException(ex); }
    }

    public void ShowReportModel(ReportModel model, string title)
    {
        if (model.Sections.Count > 1)
        {
            var selector = new ReportPrintSelectionViewModel(model.Sections);
            if (!Show(new ReportPrintSelectionWindow { DataContext = selector }))
                return;
            model.Sections = selector.SelectedSections;
        }
        var docs = _sp.GetRequiredService<DocumentFactory>();
        var reports = _sp.GetRequiredService<ReportService>();
        var canMark = model.IsVerified && model.OrderId != 0;
        ShowDocument(() => docs.BuildReport(model), title, canMark ? () => _ = reports.MarkPrintedAsync(model.OrderId) : null);
    }

    public void PreviewReceipt(int paymentId) => _ = PreviewReceiptAsync(paymentId);
    private async Task PreviewReceiptAsync(int paymentId)
    {
        try
        {
            var (p, o) = await _sp.GetRequiredService<OrderService>().GetReceiptAsync(paymentId);
            var docs = _sp.GetRequiredService<DocumentFactory>();
            ShowDocument(() => docs.BuildReceipt(p, o), $"Receipt {p.ReceiptNo}");
        }
        catch (Exception ex) { Common.Ui.HandleException(ex); }
    }

    public void PreviewLabels(IEnumerable<int> sampleIds) => _ = PreviewLabelsAsync(sampleIds.ToList());
    private async Task PreviewLabelsAsync(List<int> ids)
    {
        try
        {
            var rows = await _sp.GetRequiredService<SampleService>().GetByIdsAsync(ids);
            if (rows.Count == 0) { Common.Ui.Info("There are no sample labels to print."); return; }
            var docs = _sp.GetRequiredService<DocumentFactory>();
            ShowDocument(() => docs.BuildLabels(rows), "Sample labels");
        }
        catch (Exception ex) { Common.Ui.HandleException(ex); }
    }

    public void ShowDocument(Func<FixedDocument> builder, string title, Action? onPrinted = null)
    {
        var w = new DocumentPreviewWindow(builder, title, onPrinted) { Owner = Owner };
        w.ShowDialog();
    }
}
