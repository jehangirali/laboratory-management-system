using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text;
using System.Windows.Input;
using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Application.Services;
using LaboratoryManagement.Desktop.Common;
using LaboratoryManagement.Desktop.Services;
using LaboratoryManagement.Domain;
using LaboratoryManagement.Domain.Entities;
using Microsoft.Win32;

namespace LaboratoryManagement.Desktop.ViewModels;

public sealed class SamplesViewModel : ViewModelBase
{
    private readonly SampleService _svc; private readonly IDialogService _dialogs; private readonly SettingsService _settings;
    public SamplesViewModel(SampleService svc, IDialogService dialogs, SettingsService settings)
    { _svc = svc; _dialogs = dialogs; _settings = settings; }

    public bool RequireCollection => _settings.GetBool(SettingKeys.RequireSampleCollection, true);
    public bool RequireReceive => _settings.GetBool(SettingKeys.RequireReceiveInLab, true);

    public DateTime From { get; set; } = DateTime.Today;
    public DateTime To { get; set; } = DateTime.Today;
    public string[] StatusOptions { get; } = { "All", "PendingCollection", "Collected", "ReceivedInLab", "Processing", "Completed", "RecollectionRequired", "Rejected" };
    public string StatusFilter { get; set; } = "All";
    private string _scan = ""; public string ScanText { get => _scan; set => Set(ref _scan, value); }
    private string? _scanInfo; public string? ScanInfo { get => _scanInfo; set => Set(ref _scanInfo, value); }
    public ObservableCollection<SampleRow> Samples { get; } = new();
    public SampleRow? Selected { get; set; }
    public override Task LoadAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        var list = await _svc.GetListAsync(Enum.TryParse<SampleStatus>(StatusFilter, out var s) ? s : null, From, To);
        Samples.Clear(); foreach (var r in list) Samples.Add(r);
    }

    private async Task AfterChange(int sampleId)
    {
        await RefreshAsync();
        Selected = Samples.FirstOrDefault(x => x.SampleId == sampleId);
        OnPropertyChanged(nameof(Selected));
    }

    public ICommand RefreshCommand => new AsyncRelayCommand(RefreshAsync);

    /// <summary>Barcode scanners type the code followed by Enter.</summary>
    public ICommand ScanCommand => new AsyncRelayCommand(async () =>
    {
        var code = ScanText?.Trim();
        if (string.IsNullOrEmpty(code)) return;
        var row = await _svc.FindByBarcodeAsync(code);
        ScanText = "";
        if (row == null) { ScanInfo = $"No sample found for barcode '{code}'."; return; }
        ScanInfo = $"{row.Barcode}: {row.PatientName} ({row.PatientNo}) - {row.Specimen} - {row.Status} - Order {row.OrderNo}";
        Samples.Clear(); Samples.Add(row); Selected = row; OnPropertyChanged(nameof(Selected));
    });

    public ICommand CollectCommand => new AsyncRelayCommand(async () => { if (Selected == null) return; var id = Selected.SampleId; await _svc.CollectAsync(id); await AfterChange(id); });
    public ICommand ReceiveCommand => new AsyncRelayCommand(async () => { if (Selected == null) return; var id = Selected.SampleId; await _svc.ReceiveAsync(id); await AfterChange(id); });
    public ICommand RejectCommand => new AsyncRelayCommand(async () =>
    {
        if (Selected == null) return;
        var reason = _dialogs.Prompt("Reject sample", "Rejection reason (e.g. hemolyzed, insufficient volume, wrong container)");
        if (reason == null) return;
        var id = Selected.SampleId; await _svc.RejectAsync(id, reason); await AfterChange(id);
    });
    public ICommand RecollectCommand => new AsyncRelayCommand(async () =>
    {
        if (Selected == null) return;
        var newId = await _svc.RequestRecollectionAsync(Selected.SampleId);
        await RefreshAsync();
        if (Ui.Confirm("A replacement sample was created. Print its label now?")) _dialogs.PreviewLabels(new[] { newId });
    });
    public ICommand LabelCommand => new RelayCommand(() => { if (Selected != null) _dialogs.PreviewLabels(new[] { Selected.SampleId }); });
    public ICommand LabelAllCommand => new RelayCommand(() => _dialogs.PreviewLabels(Samples.Select(s => s.SampleId)));
    public ICommand OrderCommand => new RelayCommand(() => { if (Selected != null) _dialogs.ShowOrder(Selected.OrderId); });
}

public sealed class ResultsViewModel : ViewModelBase
{
    private readonly ResultService _svc; private readonly IDialogService _dialogs; private readonly SettingsService _settings;
    public ResultsViewModel(ResultService svc, IDialogService dialogs, SettingsService settings)
    { _svc = svc; _dialogs = dialogs; _settings = settings; }

    public bool RequireVerification => _settings.GetBool(SettingKeys.RequireVerification, true);
    public string SubmitLabel => RequireVerification ? "Submit for verification" : "Save and finalize";

    public ObservableCollection<WorklistItem> Worklist { get; } = new();
    private string _search = ""; public string SearchText { get => _search; set => Set(ref _search, value); }
    private bool _incReady; public bool IncludeReady { get => _incReady; set => Set(ref _incReady, value); }
    private WorklistItem? _selected;
    public WorklistItem? Selected { get => _selected; set { if (Set(ref _selected, value)) _ = OpenSheetAsync(); } }
    private ResultSheet? _sheet; public ResultSheet? Sheet { get => _sheet; private set { Set(ref _sheet, value); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(Banner)); } }
    public ObservableCollection<ResultRowDto> Rows { get; } = new();
    public bool CanEdit => Sheet != null && !Sheet.IsLocked;
    public string? Banner => Sheet == null ? null
        : Sheet.IsLocked ? "This result is verified and locked. A report amendment must be started from the Verification page."
        : Sheet.IsAmendment ? $"AMENDMENT IN PROGRESS - reason: {Sheet.AmendmentReason}" : null;

    public override Task LoadAsync() => RefreshAsync();
    public async Task RefreshAsync()
    {
        var list = await _svc.GetWorklistAsync(IncludeReady, SearchText);
        Worklist.Clear(); foreach (var w in list) Worklist.Add(w);
    }
    public ICommand RefreshCommand => new AsyncRelayCommand(RefreshAsync);

    private async Task OpenSheetAsync()
    {
        Rows.Clear(); Sheet = null;
        if (Selected == null) return;
        try
        {
            var sheet = await _svc.LoadSheetAsync(Selected.OrderTestId);
            foreach (var r in sheet.Rows) Rows.Add(r);
            Sheet = sheet;
        }
        catch (Exception ex) { Ui.HandleException(ex); }
    }

    private async Task SaveAsync(bool submit)
    {
        if (Sheet == null) return;
        var id = Sheet.OrderTestId;
        await _svc.SaveAsync(id, Rows.ToList(), submit);
        if (Rows.Any(r => r.Flag is ResultFlag.CriticalHigh or ResultFlag.CriticalLow))
            Ui.Warning("One or more values are outside the configured CRITICAL limits. Follow your laboratory's critical-value communication policy.", "Critical value");
        await RefreshAsync();
        Selected = Worklist.FirstOrDefault(w => w.OrderTestId == id);
        if (Selected == null) { Rows.Clear(); Sheet = null; }
        Status = submit ? "Submitted for verification." : "Draft saved.";
    }
    public ICommand SaveDraftCommand => new AsyncRelayCommand(() => SaveAsync(false));
    public ICommand SubmitCommand => new AsyncRelayCommand(() => SaveAsync(true));
    public ICommand HistoryCommand => new AsyncRelayCommand(async () =>
    {
        if (Sheet == null) return;
        var revs = await _svc.GetRevisionsAsync(Sheet.OrderTestId);
        if (revs.Count == 0) { Ui.Info("No revisions yet."); return; }
        var sb = new StringBuilder();
        foreach (var r in revs)
            sb.AppendLine($"{TimeHelper.ToLocal(r.ChangedAt):dd-MMM HH:mm}  {r.ChangedByName}: {r.ResultValue.TestParameter.Name}  '{r.OldValue}' -> '{r.NewValue}'{(r.IsAmendment ? "  [AMENDMENT]" : "")}  {r.Reason}");
        Ui.Info(sb.ToString(), "Result revision history");
    });
}

public sealed class FlatReportRow
{
    public string Section { get; set; } = ""; public string Parameter { get; set; } = ""; public string Value { get; set; } = "";
    public string Unit { get; set; } = ""; public string Reference { get; set; } = ""; public ResultFlag Flag { get; set; }
    public string FlagText => ReferenceRangeEvaluator.FlagText(Flag); public string? Comment { get; set; }
}

public sealed class VerificationViewModel : ViewModelBase
{
    private readonly VerificationService _svc; private readonly ReportService _reports; private readonly IDialogService _dialogs; private readonly ICurrentUser _user; private readonly SettingsService _settings;
    public VerificationViewModel(VerificationService svc, ReportService reports, IDialogService dialogs, ICurrentUser user, SettingsService settings)
    { _svc = svc; _reports = reports; _dialogs = dialogs; _user = user; _settings = settings; }

    public bool RequireVerification => _settings.GetBool(SettingKeys.RequireVerification, true);
    public bool CanVerify => _user.Has(Permissions.ResultsVerify);
    public bool CanAmend => _user.Has(Permissions.ReportsAmend);
    public ObservableCollection<PendingVerificationItem> Pending { get; } = new();
    public ObservableCollection<FlatReportRow> Preview { get; } = new();
    private PendingVerificationItem? _sel;
    public PendingVerificationItem? Selected { get => _sel; set { if (Set(ref _sel, value)) _ = LoadPreviewAsync(); } }
    private ReportModel? _model;

    public ObservableCollection<PendingVerificationItem> Verified { get; } = new();
    public PendingVerificationItem? SelectedVerified { get; set; }
    public string VerifiedSearch { get; set; } = "";

    public override async Task LoadAsync() { await RefreshAsync(); }
    private async Task RefreshAsync()
    {
        var list = await _svc.GetPendingAsync();
        Pending.Clear(); foreach (var p in list) Pending.Add(p);
        if (CanAmend) await SearchVerifiedAsync();
    }
    private async Task SearchVerifiedAsync()
    {
        var list = await _svc.SearchVerifiedAsync(VerifiedSearch);
        Verified.Clear(); foreach (var p in list) Verified.Add(p);
    }
    private async Task LoadPreviewAsync()
    {
        Preview.Clear(); _model = null;
        if (Selected == null) return;
        try
        {
            _model = await _reports.BuildAsync(Selected.OrderId);
            foreach (var s in _model.Sections)
                foreach (var r in s.Rows)
                    Preview.Add(new FlatReportRow { Section = s.Title, Parameter = r.Name, Value = r.Value, Unit = r.Unit, Reference = r.Reference, Flag = r.Flag, Comment = r.Comment });
        }
        catch (Exception ex) { Ui.HandleException(ex); }
    }

    public ICommand RefreshCommand => new AsyncRelayCommand(RefreshAsync);
    public ICommand SearchVerifiedCommand => new AsyncRelayCommand(SearchVerifiedAsync);
    public ICommand PreviewCommand => new RelayCommand(() => { if (_model != null) _dialogs.ShowReportModel(_model, $"Draft report {_model.OrderNo}"); });
    public ICommand VerifyCommand => new AsyncRelayCommand(async () =>
    {
        if (Selected == null) return;
        if (!Ui.Confirm($"Verify and approve the report for {Selected.PatientName} ({Selected.OrderNo})?\n\nAfter verification the results are locked; changes require a controlled amendment.", "Verify report")) return;
        await _svc.VerifyAsync(Selected.OrderId);
        Ui.Info("Report verified.");
        Selected = null;
        await RefreshAsync();
    });
    public ICommand AmendCommand => new AsyncRelayCommand(async () =>
    {
        if (SelectedVerified == null) return;
        var reason = _dialogs.Prompt("Start report amendment", "Reason for amendment (recorded permanently; the original version is retained)");
        if (reason == null) return;
        await _svc.BeginAmendmentAsync(SelectedVerified.OrderId, reason);
        Ui.Info("Amendment started. A technician must correct and re-submit the affected results in 'Results', then the report must be verified again.");
        await RefreshAsync();
    });
    public ICommand ViewVerifiedCommand => new RelayCommand(() => { if (SelectedVerified != null) _dialogs.PreviewReport(SelectedVerified.OrderId); });
}

public sealed class ReportsViewModel : ViewModelBase
{
    private readonly OperationalReportService _svc;
    public ReportsViewModel(OperationalReportService svc) => _svc = svc;
    public List<OperationalReportInfo> Options { get; private set; } = new();
    private OperationalReportInfo? _selected; public OperationalReportInfo? Selected { get => _selected; set => Set(ref _selected, value); }
    public DateTime From { get; set; } = DateTime.Today.AddDays(-6);
    public DateTime To { get; set; } = DateTime.Today;
    private DataTable? _table; public DataTable? Table { get => _table; private set { Set(ref _table, value); OnPropertyChanged(nameof(View)); } }
    public DataView? View => Table?.DefaultView;

    public override Task LoadAsync()
    {
        Options = _svc.Available().ToList(); Selected = Options.FirstOrDefault();
        OnPropertyChanged(nameof(Options));
        return Task.CompletedTask;
    }
    public ICommand RunCommand => new AsyncRelayCommand(async () =>
    {
        if (Selected == null) return;
        Table = await _svc.RunAsync(Selected.Code, From, To);
        Status = $"{Table.Rows.Count} row(s)";
    });
    public ICommand ExportCommand => new RelayCommand(() =>
    {
        if (Table == null) { Ui.Info("Run a report first."); return; }
        var dlg = new SaveFileDialog { Filter = "CSV (opens in Excel)|*.csv", FileName = $"{Selected?.Code}_{DateTime.Today:yyyyMMdd}.csv" };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, OperationalReportService.ToCsv(Table), new UTF8Encoding(true));
        Ui.Info("Exported to " + dlg.FileName);
    });
}

public sealed class PermissionChoice : ObservableBase
{
    public int Id { get; set; } public string Code { get; set; } = ""; public string Group { get; set; } = ""; public string Description { get; set; } = "";
    private bool _g; public bool IsGranted { get => _g; set => Set(ref _g, value); }
}

public sealed class UsersViewModel : ViewModelBase
{
    private readonly UserService _svc; private readonly IDialogService _dialogs;
    public UsersViewModel(UserService svc, IDialogService dialogs) { _svc = svc; _dialogs = dialogs; }
    public ObservableCollection<User> Users { get; } = new();
    public User? Selected { get; set; }
    public ObservableCollection<Role> Roles { get; } = new();
    public ObservableCollection<PermissionChoice> Permissions { get; } = new();
    private Role? _role;
    public Role? SelectedRole { get => _role; set { if (Set(ref _role, value)) _ = LoadPermsAsync(); } }

    public override async Task LoadAsync()
    {
        var u = await _svc.GetUsersAsync(); Users.Clear(); foreach (var x in u) Users.Add(x);
        var r = await _svc.GetRolesAsync(); Roles.Clear(); foreach (var x in r) Roles.Add(x);
    }
    private async Task LoadPermsAsync()
    {
        Permissions.Clear();
        if (SelectedRole == null) return;
        foreach (var (p, g) in await _svc.GetRolePermissionsAsync(SelectedRole.Id))
            Permissions.Add(new PermissionChoice { Id = p.Id, Code = p.Code, Group = p.Group, Description = p.Description, IsGranted = g });
    }
    public ICommand NewCommand => new AsyncRelayCommand(async () => { if (_dialogs.EditUser(null)) await LoadAsync(); });
    public ICommand EditCommand => new AsyncRelayCommand(async () => { if (Selected != null && _dialogs.EditUser(Selected.Id)) await LoadAsync(); });
    public ICommand SavePermissionsCommand => new AsyncRelayCommand(async () =>
    {
        if (SelectedRole == null) return;
        await _svc.SaveRolePermissionsAsync(SelectedRole.Id, Permissions.Where(p => p.IsGranted).Select(p => p.Id));
        Ui.Info("Role permissions saved. Users must sign in again for changes to apply.");
        await LoadPermsAsync();
    });
}

public sealed class SettingRow : ObservableBase
{
    public string Key { get; set; } = ""; public string Group { get; set; } = ""; public string? Description { get; set; }
    private string _v = ""; public string Value { get => _v; set => Set(ref _v, value); }
}

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _svc; private readonly DemoDataService _demo; private readonly IDialogService _dialogs;
    public SettingsViewModel(SettingsService svc, DemoDataService demo, IDialogService dialogs) { _svc = svc; _demo = demo; _dialogs = dialogs; }
    public ObservableCollection<SettingRow> Settings { get; } = new();
    public SettingRow? Selected { get; set; }
    public override async Task LoadAsync()
    {
        var all = await _svc.GetAllAsync();
        Settings.Clear();
        foreach (var s in all.Where(s => s.Group != "System")) Settings.Add(new SettingRow { Key = s.Key, Group = s.Group, Description = s.Description, Value = s.Value });
    }
    public ICommand SaveCommand => new AsyncRelayCommand(async () =>
    {
        await _svc.SaveAsync(Settings.Select(s => new AppSetting { Key = s.Key, Value = s.Value }));
        Ui.Info("Settings saved.");
    });
    public ICommand BrowseLogoCommand => new RelayCommand(() =>
    {
        var row = Settings.FirstOrDefault(s => s.Key == SettingKeys.LabLogoPath); if (row == null) return;
        var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog() == true) row.Value = dlg.FileName;
    });
    public ICommand BrowseBackupCommand => new RelayCommand(() =>
    {
        var row = Settings.FirstOrDefault(s => s.Key == SettingKeys.BackupFolder); if (row == null) return;
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true) row.Value = dlg.FolderName;
    });
    public ICommand DemoCommand => new AsyncRelayCommand(async () =>
    {
        if (!Ui.Confirm("Load DEMO data?\n\nThis assigns arbitrary SAMPLE prices to every test whose price is not set, and adds a few fictional doctors/patients if none exist. Sample prices are clearly flagged and must be replaced by your real prices.")) return;
        Ui.Info(await _demo.LoadAsync());
    });
    public ICommand PasswordCommand => new RelayCommand(() => _dialogs.ChangePassword());
}

public sealed class AuditViewModel : ViewModelBase
{
    private readonly UserService _svc;
    public AuditViewModel(UserService svc) => _svc = svc;
    public ObservableCollection<AuditLog> Logs { get; } = new();
    public ObservableCollection<LoginHistory> Logins { get; } = new();
    public string? Text { get; set; }
    public DateTime? From { get; set; } = DateTime.Today.AddDays(-7);
    public DateTime? To { get; set; } = DateTime.Today;
    public override async Task LoadAsync() => await SearchAsync();
    private async Task SearchAsync()
    {
        var a = await _svc.GetAuditAsync(Text, From, To);
        Logs.Clear(); foreach (var x in a) Logs.Add(x);
        var l = await _svc.GetLoginHistoryAsync();
        Logins.Clear(); foreach (var x in l) Logins.Add(x);
    }
    public ICommand SearchCommand => new AsyncRelayCommand(SearchAsync);
}

public sealed class BackupViewModel : ViewModelBase
{
    private readonly IBackupService _svc; private readonly SettingsService _settings;
    public BackupViewModel(IBackupService svc, SettingsService settings) { _svc = svc; _settings = settings; }
    public ObservableCollection<BackupRecord> History { get; } = new();
    public BackupRecord? Selected { get; set; }
    public string Folder => _settings.Get(SettingKeys.BackupFolder);
    public override async Task LoadAsync() => await RefreshAsync();
    private async Task RefreshAsync()
    {
        var h = await _svc.GetHistoryAsync();
        History.Clear(); foreach (var x in h) History.Add(x);
        OnPropertyChanged(nameof(Folder));
    }
    public ICommand BackupCommand => new AsyncRelayCommand(async () =>
    {
        IsBusy = true;
        try
        {
            var r = await _svc.BackupAsync("Manual");
            if (r.Success) Ui.Info($"Backup created and verified:\n{r.FilePath}");
            else Ui.Error("Backup failed: " + r.Message);
        }
        finally { IsBusy = false; }
        await RefreshAsync();
    });
    public ICommand VerifyCommand => new AsyncRelayCommand(async () =>
    {
        if (Selected == null) return;
        var (ok, msg) = await _svc.VerifyAsync(Selected.FilePath);
        if (ok) Ui.Info(msg); else Ui.Error(msg);
    });
    public ICommand RestoreCommand => new AsyncRelayCommand(async () =>
    {
        string? file = Selected?.FilePath;
        if (file == null || !File.Exists(file))
        {
            var dlg = new OpenFileDialog { Filter = "SQL Server backup (*.bak)|*.bak" };
            if (dlg.ShowDialog() != true) return;
            file = dlg.FileName;
        }
        if (!Ui.Confirm($"RESTORE the database from:\n{file}\n\nAll current data will be REPLACED by the backup. A safety backup of the current data is taken first.\n\nContinue?", "Restore database")) return;
        if (!Ui.Confirm("This is your final confirmation. Restore now?", "Restore database")) return;
        IsBusy = true;
        try
        {
            var (ok, msg) = await _svc.RestoreAsync(file);
            if (ok) { Ui.Info(msg); System.Windows.Application.Current.Shutdown(); }
            else Ui.Error(msg);
        }
        finally { IsBusy = false; }
    });
}
