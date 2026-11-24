using System.Windows;

namespace LaboratoryManagement.Desktop.Views;

public partial class SplashWindow : Window
{
    public SplashWindow() { InitializeComponent(); }
    public void SetStatus(string text) => StatusText.Text = text;
}
