using System.IO;
using System.Windows;
using System.Windows.Threading;
using LaboratoryManagement.Application.Interfaces;
using LaboratoryManagement.Application.Services;
using LaboratoryManagement.Desktop.Common;
using LaboratoryManagement.Desktop.Printing;
using LaboratoryManagement.Desktop.Services;
using LaboratoryManagement.Desktop.ViewModels;
using LaboratoryManagement.Desktop.Views;
using LaboratoryManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Threading;

namespace LaboratoryManagement.Desktop;

public partial class App : System.Windows.Application
{
    public static IHost AppHost { get; private set; } = null!;
    private DispatcherTimer? _backupTimer;
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _instanceMutex = new Mutex(false, @"Global\LaboratoryManagementSystem");
            try { _ownsInstanceMutex = _instanceMutex.WaitOne(0); }
            catch (AbandonedMutexException) { _ownsInstanceMutex = true; }
            if (!_ownsInstanceMutex)
            {
                MessageBox.Show("Laboratory Management is already running.", "Laboratory Management", MessageBoxButton.OK, MessageBoxImage.Information);
                _instanceMutex.Dispose();
                _instanceMutex = null;
                Shutdown();
                return;
            }
            SQLitePCL.Batteries_V2.Init();
        }
        catch (Exception ex)
        {
            MessageBox.Show("The application could not initialize its single-instance or SQLite support: " + ex.Message,
                "Laboratory Management", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaboratoryManagement", "logs");
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
            .WriteTo.File(Path.Combine(logDir, "lab-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30).CreateLogger();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            Ui.Error("An unexpected error occurred. It has been logged.\n\n" + args.Exception.GetBaseException().Message);
            args.Handled = true;
        };

        try
        {
            AppHost = Host.CreateDefaultBuilder()
                .UseContentRoot(AppContext.BaseDirectory)
                .UseSerilog()
                .ConfigureServices((ctx, s) =>
                {
                    s.AddLaboratoryApplication();
                    s.AddLaboratoryInfrastructure(ctx.Configuration);
                    s.AddSingleton<IDialogService, DialogService>();
                    s.AddSingleton<DocumentFactory>();
                    s.AddSingleton<NavigatorProxy>();
                    s.AddSingleton<INavigator>(sp => sp.GetRequiredService<NavigatorProxy>());
                    foreach (var t in new[]
                    {
                        typeof(LoginViewModel), typeof(FirstAdminViewModel), typeof(MainViewModel), typeof(DashboardViewModel), typeof(PatientsViewModel),
                        typeof(DoctorsViewModel), typeof(TestsViewModel), typeof(NewOrderViewModel), typeof(OrdersViewModel), typeof(SamplesViewModel),
                        typeof(ResultsViewModel), typeof(VerificationViewModel), typeof(ReportsViewModel), typeof(UsersViewModel), typeof(SettingsViewModel),
                        typeof(BackupViewModel), typeof(PaymentDialogViewModel), typeof(ChangePasswordViewModel),
                        typeof(PatientEditorViewModel), typeof(DoctorEditorViewModel), typeof(UserEditorViewModel), typeof(TestEditorViewModel),
                        typeof(OrderDetailViewModel), typeof(PatientHistoryViewModel)
                    }) s.AddTransient(t);
                })
                .Build();
            Ui.Logger = AppHost.Services.GetRequiredService<ILoggerFactory>().CreateLogger("UI");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host build failed");
            MessageBox.Show("The application could not start: " + ex.Message, "Laboratory Management", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1); return;
        }

        var splash = new SplashWindow();
        splash.Show();
        var progress = new Progress<string>(s => splash.SetStatus(s));
        var init = await AppHost.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync(progress);
        splash.Close();

        if (!init.Success)
        {
            var w = new ErrorWindow(init.Error ?? "Initialization failed", init.Diagnostics);
            w.ShowDialog();
            Shutdown(1); return;
        }

        if (!init.AdminExists)
        {
            var wizard = new FirstAdminWindow { DataContext = AppHost.Services.GetRequiredService<FirstAdminViewModel>() };
            if (wizard.ShowDialog() != true) { Shutdown(); return; }
        }
        RunLoginLoop();
    }

    private void RunLoginLoop()
    {
        while (true)
        {
            var login = new LoginWindow { DataContext = AppHost.Services.GetRequiredService<LoginViewModel>() };
            if (login.ShowDialog() != true) { Shutdown(); return; }

            var main = new MainWindow();
            var vm = AppHost.Services.GetRequiredService<MainViewModel>();
            var logout = false;
            vm.LogoutRequested += () => { logout = true; main.Close(); };
            main.DataContext = vm;
            MainWindow = main;
            StartBackupTimer();
            vm.Start();
            main.ShowDialog(); // modal loop keeps the login cycle simple
            _backupTimer?.Stop();
            if (!logout)
            {
                // Window closed with the X button: sign out and exit.
                _ = AppHost.Services.GetRequiredService<AuthService>().LogoutAsync();
                Shutdown(); return;
            }
        }
    }

    private void StartBackupTimer()
    {
        _backupTimer?.Stop();
        _backupTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _backupTimer.Tick += async (_, _) =>
        {
            try { await AppHost.Services.GetRequiredService<IBackupService>().RunScheduledIfDueAsync(); }
            catch (Exception ex) { Log.Warning(ex, "Scheduled backup check failed"); }
        };
        _backupTimer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        AppHost?.Dispose();
        if (_ownsInstanceMutex)
        {
            _instanceMutex?.ReleaseMutex();
            _ownsInstanceMutex = false;
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
