using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace LaboratoryManagement.Desktop.Printing;

/// <summary>Hosts an arbitrary Visual (a flow-document page) inside the element tree at its exact coordinates.</summary>
internal sealed class VisualHost : FrameworkElement
{
    private readonly Visual _visual;
    public VisualHost(Visual visual, double w, double h) { _visual = visual; Width = w; Height = h; AddVisualChild(visual); }
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _visual;
}

public static class PageSizes
{
    public static Size For(string name) => name.Equals("Letter", StringComparison.OrdinalIgnoreCase) ? new Size(816, 1056) : new Size(793.7, 1122.5);
    public static double MmToDip(double mm) => mm * 96.0 / 25.4;
}

/// <summary>Paginates a FlowDocument body and wraps every page with a repeating header and footer, producing a
/// FixedDocument that can be previewed, printed or exported to XPS.</summary>
public static class PagedDocumentBuilder
{
    public static FixedDocument Build(FlowDocument body, Size pageSize, Thickness margin,
        Func<int, int, FrameworkElement>? header, Func<int, int, FrameworkElement>? footer)
    {
        var contentWidth = pageSize.Width - margin.Left - margin.Right;
        double headerH = 0, footerH = 0;
        if (header != null) { var h = header(1, 1); h.Measure(new Size(contentWidth, double.PositiveInfinity)); headerH = h.DesiredSize.Height + 6; }
        if (footer != null) { var f = footer(1, 1); f.Measure(new Size(contentWidth, double.PositiveInfinity)); footerH = f.DesiredSize.Height + 6; }

        body.PageWidth = pageSize.Width;
        body.PageHeight = pageSize.Height;
        body.ColumnWidth = double.PositiveInfinity;
        body.PagePadding = new Thickness(margin.Left, margin.Top + headerH, margin.Right, margin.Bottom + footerH);
        body.IsOptimalParagraphEnabled = false;

        var paginator = ((IDocumentPaginatorSource)body).DocumentPaginator;
        paginator.PageSize = pageSize;
        paginator.ComputePageCount();
        var total = Math.Max(1, paginator.PageCount);

        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = pageSize;
        for (var i = 0; i < total; i++)
        {
            var page = new FixedPage { Width = pageSize.Width, Height = pageSize.Height, Background = Brushes.White };
            var dp = paginator.GetPage(i); // not disposed: the visual must outlive this scope
            page.Children.Add(new VisualHost(dp.Visual, pageSize.Width, pageSize.Height));
            if (header != null)
            {
                var h = header(i + 1, total); h.Width = contentWidth;
                FixedPage.SetLeft(h, margin.Left); FixedPage.SetTop(h, margin.Top);
                page.Children.Add(h);
            }
            if (footer != null)
            {
                var f = footer(i + 1, total); f.Width = contentWidth;
                FixedPage.SetLeft(f, margin.Left); FixedPage.SetTop(f, pageSize.Height - margin.Bottom - footerH + 6);
                page.Children.Add(f);
            }
            var size = new Size(pageSize.Width, pageSize.Height);
            page.Measure(size); page.Arrange(new Rect(size)); page.UpdateLayout();
            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            doc.Pages.Add(content);
        }
        return doc;
    }
}
