using System.Windows;
using LaboratoryManagement.Desktop.Common;
using LaboratoryManagement.Desktop.ViewModels;

namespace LaboratoryManagement.Desktop.Views;

public partial class FirstAdminWindow : DialogWindowBase
{
    public FirstAdminWindow() { InitializeComponent(); }
    private void Pw_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is FirstAdminViewModel vm) { vm.Password = Pw1.Password; vm.Confirm = Pw2.Password; }
    }
}
