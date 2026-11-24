using System.Windows;
using System.Windows.Input;
using LaboratoryManagement.Application.Common;
using Microsoft.Extensions.Logging;

namespace LaboratoryManagement.Desktop.Common;

public static class Ui
{
    public static ILogger? Logger { get; set; }
    private static Window? Owner => System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                    ?? System.Windows.Application.Current?.MainWindow;

    private static MessageBoxResult Box(string message, string title, MessageBoxButton b, MessageBoxImage i)
    {
        var o = Owner;
        return o == null ? MessageBox.Show(message, title, b, i) : MessageBox.Show(o, message, title, b, i);
    }
    public static void Info(string message, string title = "Laboratory Management") => Box(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    public static void Warning(string message, string title = "Please check") => Box(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    public static void Error(string message, string title = "Error") => Box(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    public static bool Confirm(string message, string title = "Please confirm") => Box(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void HandleException(Exception ex)
    {
        switch (ex)
        {
            case BusinessRuleException: Warning(ex.Message); break;
            case PermissionDeniedException: Warning(ex.Message, "Access denied"); break;
            default:
                Logger?.LogError(ex, "Unhandled error in command");
                Error("An unexpected error occurred. Details were written to the log file.\n\n" + ex.GetBaseException().Message);
                break;
        }
    }
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _can;
    public RelayCommand(Action<object?> execute, Func<object?, bool>? can = null) { _execute = execute; _can = can; }
    public RelayCommand(Action execute, Func<bool>? can = null) : this(_ => execute(), can == null ? null : _ => can()) { }
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) { try { _execute(parameter); } catch (Exception ex) { Ui.HandleException(ex); } }
}

public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _can;
    private bool _running;
    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? can = null) { _execute = execute; _can = can; }
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? can = null) : this(_ => execute(), can == null ? null : _ => can()) { }
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => !_running && (_can?.Invoke(parameter) ?? true);
    public async void Execute(object? parameter)
    {
        _running = true; CommandManager.InvalidateRequerySuggested();
        try { await _execute(parameter); }
        catch (Exception ex) { Ui.HandleException(ex); }
        finally { _running = false; CommandManager.InvalidateRequerySuggested(); }
    }
}

public abstract class ViewModelBase : ObservableBase
{
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }
    private string? _status;
    public string? Status { get => _status; set => Set(ref _status, value); }

    /// <summary>Called by the shell after the page is created.</summary>
    public virtual Task LoadAsync() => Task.CompletedTask;

    protected async Task RunAsync(Func<Task> work)
    {
        IsBusy = true;
        try { await work(); }
        catch (Exception ex) { Ui.HandleException(ex); }
        finally { IsBusy = false; }
    }
}

public abstract class DialogViewModel : ViewModelBase
{
    public event Action<bool>? CloseRequested;
    /// <summary>Asynchronous initialisation executed by the dialog window when it loads.</summary>
    public Func<Task>? Initializer { get; set; }
    protected void Close(bool result) => CloseRequested?.Invoke(result);
    public ICommand CancelCommand => new RelayCommand(() => Close(false));
}

/// <summary>Base window for modal dialogs; closes itself when the view model asks.</summary>
public class DialogWindowBase : Window
{
    public DialogWindowBase()
    {
        SetResourceReference(StyleProperty, typeof(Window)); // implicit Window style does not apply to derived window types
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is DialogViewModel o) o.CloseRequested -= OnClose;
            if (e.NewValue is DialogViewModel n) n.CloseRequested += OnClose;
        };
        Loaded += async (_, _) =>
        {
            try
            {
                if (DataContext is DialogViewModel d && d.Initializer != null) await d.Initializer();
                if (DataContext is ViewModelBase vm) await vm.LoadAsync();
            }
            catch (Exception ex) { Ui.HandleException(ex); OnClose(false); }
        };
    }

    private void OnClose(bool result)
    {
        try { DialogResult = result; } catch (InvalidOperationException) { }
        Close();
    }
}
