using System.Windows;

namespace LaboratoryManagement.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent();
        SetResourceReference(StyleProperty, typeof(Window)); }
}
