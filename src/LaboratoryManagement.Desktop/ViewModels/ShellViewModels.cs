using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Application.Services;
using LaboratoryManagement.Desktop.Common;
using LaboratoryManagement.Desktop.Services;
using LaboratoryManagement.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace LaboratoryManagement.Desktop.ViewModels;

public sealed class LoginViewModel : DialogViewModel
{
    private readonly AuthService _auth; private readonly IDialogService _dialogs;
    private static readonly string RememberFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaboratoryManagement", "lastuser.txt");
    public LoginViewModel(AuthService auth, IDialogService dialogs)
    {
        _auth = auth; _dialogs = dialogs;
        try { if (File.Exists(RememberFile)) { Username = File.ReadAllText(RememberFile).Trim(); RememberUsername = Username.Length > 0; } } catch { }
    }
    public string AppName { get; set; } = "Laboratory Management System";
    public string Version { get; set; } = "";
    private string _username = ""; public string Username { get => _username; set => Set(ref _username, value); }
    public string Password { get; set; } = "";
    public bool RememberUsername { get; set; }
    private string? _message; public string? Message { get => _message; set => Set(ref _message, value); }

    public ICommand LoginCommand => new AsyncRelayCommand(async () =>
    {
        Message = null;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password)) { Message = "Enter your username and password."; return; }
        IsBusy = true;
        try
        {
            var (ok, msg) = await _auth.LoginAsync(Username, Password);
            if (!ok) { Message = msg; return; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RememberFile)!);
                if (RememberUsername) File.WriteAllText(RememberFile, Username); else if (File.Exists(RememberFile)) File.Delete(RememberFile);
            }
            catch { /* non-critical */ }
            if (msg == "MUSTCHANGE")
            {
                Ui.Info("You must choose a new password before continuing.");
                _dialogs.ChangePassword(true);
            }
            Close(true);
        }
        finally { IsBusy = false; }
    });
}

public sealed class FirstAdminViewModel : DialogViewModel
{
    private readonly AuthService _auth;
    public FirstAdminViewModel(AuthService auth) => _auth = auth;
    private string _username = ""; public string Username { get => _username; set => Set(ref _username, value); }
    private string _full = ""; public string FullName { get => _full; set => Set(ref _full, value); }
    public string Password { get; set; } = "";
    public string Confirm { get; set; } = "";
    public ICommand CreateCommand => new AsyncRelayCommand(async () =>
    {
        if (Password != Confirm) throw new BusinessRuleException("The passwords do not match.");
        await _auth.CreateFirstAdministratorAsync(Username, FullName, Password);
        Ui.Info("Administrator account created. Please sign in.");
        Close(true);
    });
}

public sealed class NavigatorProxy : INavigator
{
    public INavigator? Current { get; set; }
    public void Go<T>(Action<T>? init = null) where T : ViewModelBase => Current?.Go(init);
}

public record NavItem(string Title, string Glyph, Type ViewModelType, Func<bool> Visible);

public sealed class MainViewModel : ViewModelBase, INavigator
{
    private readonly IServiceProvider _sp; private readonly AuthService _auth; private readonly IDialogService _dialogs;
    public ICurrentUser User { get; }
    public event Action? LogoutRequested;

    public MainViewModel(IServiceProvider sp, ICurrentUser user, AuthService auth, IDialogService dialogs, SettingsService settings, OperationalReportService reports, NavigatorProxy proxy)
    {
        proxy.Current = this;
        _sp = sp; User = user; _auth = auth; _dialogs = dialogs;
        LabName = settings.Get(SettingKeys.LabName);
        NavItems = new ObservableCollection<NavItem>(new[]
        {
            new NavItem("Dashboard", "", typeof(DashboardViewModel), () => user.Has(Permissions.DashboardView)),
            new NavItem("New order", "", typeof(NewOrderViewModel), () => user.Has(Permissions.OrdersCreate)),
            new NavItem("Orders", "", typeof(OrdersViewModel), () => user.Has(Permissions.OrdersView)),
            new NavItem("Patients", "", typeof(PatientsViewModel), () => user.Has(Permissions.PatientsView)),
            new NavItem("Doctors", "", typeof(DoctorsViewModel), () => user.Has(Permissions.DoctorsView)),
            new NavItem("Samples", "", typeof(SamplesViewModel), () => user.Has(Permissions.SamplesManage)),
            new NavItem("Results", "", typeof(ResultsViewModel), () => user.Has(Permissions.ResultsEnter)),
            new NavItem("Verification", "", typeof(VerificationViewModel), () => user.Has(Permissions.ResultsVerify) || user.Has(Permissions.ReportsAmend)),
            new NavItem("Test catalog", "", typeof(TestsViewModel), () => user.Has(Permissions.TestsView)),
            new NavItem("Reports", "", typeof(ReportsViewModel), () => reports.Available().Any()),
            new NavItem("Users & roles", "", typeof(UsersViewModel), () => user.Has(Permissions.UsersManage)),
            new NavItem("Audit trail", "", typeof(AuditViewModel), () => user.Has(Permissions.AuditView)),
            new NavItem("Backup", "", typeof(BackupViewModel), () => user.Has(Permissions.BackupManage)),
            new NavItem("Settings", "", typeof(SettingsViewModel), () => user.Has(Permissions.SettingsManage)),
        }.Where(n => n.Visible()));
    }

    public string LabName { get; }
    public ObservableCollection<NavItem> NavItems { get; }
    private NavItem? _selectedNav;
    public NavItem? SelectedNav { get => _selectedNav; set { if (Set(ref _selectedNav, value) && value != null) _ = ShowAsync(value.ViewModelType, null); } }
    private ViewModelBase? _current; public ViewModelBase? CurrentPage { get => _current; private set => Set(ref _current, value); }

    private async Task ShowAsync(Type type, Action<ViewModelBase>? init)
    {
        try
        {
            var vm = (ViewModelBase)_sp.GetRequiredService(type);
            init?.Invoke(vm);
            CurrentPage = vm;
            await vm.LoadAsync();
        }
        catch (Exception ex) { Ui.HandleException(ex); }
    }

    public void Go<T>(Action<T>? init = null) where T : ViewModelBase
    {
        var item = NavItems.FirstOrDefault(n => n.ViewModelType == typeof(T));
        if (item == null) { Ui.Warning("You do not have access to that page."); return; }
        _selectedNav = item; OnPropertyChanged(nameof(SelectedNav));
        _ = ShowAsync(typeof(T), init == null ? null : vm => init((T)vm));
    }

    public void Start()
    {
        SelectedNav = NavItems.FirstOrDefault();
    }

    public ICommand LogoutCommand => new AsyncRelayCommand(async () => { await _auth.LogoutAsync(); LogoutRequested?.Invoke(); });
    public ICommand ChangePasswordCommand => new RelayCommand(() => _dialogs.ChangePassword());
}
