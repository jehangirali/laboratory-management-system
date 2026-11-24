using System.Windows;
using System.Windows.Controls;
using LaboratoryManagement.Desktop.Common;
using LaboratoryManagement.Desktop.ViewModels;

namespace LaboratoryManagement.Desktop.Views;

public partial class LoginWindow : DialogWindowBase
{
    private bool _sync;
    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (string.IsNullOrEmpty(UserBox.Text)) UserBox.Focus(); else PwBox.Focus(); };
    }

    // PasswordBox cannot be data-bound; this is UI-only glue (show/hide password).
    private void Pw_Changed(object sender, RoutedEventArgs e)
    {
        if (_sync) return;
        _sync = true; PwText.Text = PwBox.Password; _sync = false;
        if (DataContext is LoginViewModel vm) vm.Password = PwBox.Password;
    }
    private void PwText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_sync) return;
        _sync = true; PwBox.Password = PwText.Text; _sync = false;
        if (DataContext is LoginViewModel vm) vm.Password = PwText.Text;
    }
    private void ShowPw_Changed(object sender, RoutedEventArgs e)
    {
        var show = ShowPw.IsChecked == true;
        PwText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PwBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }
    private void Exit_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
