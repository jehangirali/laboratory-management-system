using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LaboratoryManagement.Desktop.Printing;

/// <summary>Code 128 (subset B, ASCII 32-126) encoder and WPF renderer. No third-party dependency.</summary>
public static class Code128
{
    // Widths of bar/space elements for symbol values 0..105 (6 elements each), then the stop pattern (7 elements).
    private static readonly string[] Patterns =
    {
        "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
        "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
        "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
        "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
        "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
        "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
        "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
        "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
        "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
        "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
        "114131","311141","411131","211412","211214","211232"
    };
    private const string Stop = "2331112";
    private const int StartB = 104;

    /// <summary>Returns alternating bar/space module widths starting with a bar.</summary>
    public static List<int> Encode(string text)
    {
        var values = new List<int> { StartB };
        foreach (var ch in text)
        {
            if (ch < 32 || ch > 126) throw new ArgumentException($"Character '{ch}' cannot be encoded in Code 128 set B.");
            values.Add(ch - 32);
        }
        long sum = StartB;
        for (var i = 1; i < values.Count; i++) sum += (long)values[i] * i;
        values.Add((int)(sum % 103));

        var widths = new List<int>();
        foreach (var v in values) widths.AddRange(Patterns[v].Select(c => c - '0'));
        widths.AddRange(Stop.Select(c => c - '0'));
        return widths;
    }

    /// <summary>Creates a vector barcode with quiet zones. moduleWidth is in device-independent pixels.</summary>
    public static FrameworkElement Create(string text, double height, double moduleWidth = 1.5)
    {
        var widths = Encode(text);
        var quiet = 10 * moduleWidth;
        var total = widths.Sum() * moduleWidth + quiet * 2;
        var canvas = new Canvas { Width = total, Height = height, Background = Brushes.White, SnapsToDevicePixels = true };
        var x = quiet;
        for (var i = 0; i < widths.Count; i++)
        {
            var w = widths[i] * moduleWidth;
            if (i % 2 == 0)
            {
                var bar = new Rectangle { Width = w, Height = height, Fill = Brushes.Black };
                Canvas.SetLeft(bar, x);
                canvas.Children.Add(bar);
            }
            x += w;
        }
        return canvas;
    }
}
