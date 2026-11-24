using System.Windows;

namespace LaboratoryManagement.Desktop.Views;

public partial class ErrorWindow : Window
{
    public ErrorWindow(string message, string? details)
    {
        InitializeComponent();
        SetResourceReference(StyleProperty, typeof(Window));
        Msg.Text = message; Details.Text = details ?? "";
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
