using System.Windows;
using LaboratoryManagement.Desktop.Common;
using LaboratoryManagement.Desktop.ViewModels;

namespace LaboratoryManagement.Desktop.Views;

public partial class ChangePasswordWindow : DialogWindowBase
{
    public ChangePasswordWindow() { InitializeComponent(); }

    // PasswordBox cannot be data-bound; UI-only glue.
    private void Pw_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChangePasswordViewModel vm) { vm.Current = Cur.Password; vm.New = New.Password; vm.Confirm = Conf.Password; }
    }
}
