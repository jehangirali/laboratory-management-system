using System.IO;
using System.IO.Packaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;
using LaboratoryManagement.Desktop.Common;
using Microsoft.Win32;

namespace LaboratoryManagement.Desktop.Views;

public partial class DocumentPreviewWindow : Window
{
    private readonly Func<FixedDocument> _builder;
    private readonly string _title;
    private readonly Action? _onPrinted;

    public DocumentPreviewWindow(Func<FixedDocument> builder, string title, Action? onPrinted = null)
    {
        InitializeComponent();
        SetResourceReference(StyleProperty, typeof(Window));
        _builder = builder; _title = title; _onPrinted = onPrinted;
        Title = title;
        Viewer.Document = builder();
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;
            var doc = _builder(); // fresh copy: a document already shown in the viewer cannot be re-parented
            dlg.PrintDocument(doc.DocumentPaginator, _title);
            _onPrinted?.Invoke();
        }
        catch (Exception ex) { Ui.HandleException(ex); }
    }

    private void Xps_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new SaveFileDialog { Filter = "XPS document (*.xps)|*.xps", FileName = _title.Replace(' ', '_') + ".xps" };
            if (dlg.ShowDialog() != true) return;
            if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
            var doc = _builder();
            using var xps = new XpsDocument(dlg.FileName, FileAccess.ReadWrite);
            var writer = XpsDocument.CreateXpsDocumentWriter(xps);
            writer.Write(doc.DocumentPaginator);
            Ui.Info("Saved to " + dlg.FileName);
        }
        catch (Exception ex) { Ui.HandleException(ex); }
    }
}
