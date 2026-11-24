using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LaboratoryManagement.Application.Common;
using LaboratoryManagement.Domain;

namespace LaboratoryManagement.Desktop.Common;

/// <summary>UTC DateTime (or nullable) to local text. ConverterParameter = format.</summary>
public class LocalDateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime dt) return "";
        var fmt = parameter as string ?? "dd-MMM-yyyy HH:mm";
        return TimeHelper.ToLocal(dt).ToString(fmt, CultureInfo.CurrentCulture);
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b && !b;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b && !b;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value == null || value is string s && string.IsNullOrEmpty(s)) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class FlagBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is ResultFlag f ? f : ResultFlag.None;
        return flag switch
        {
            ResultFlag.CriticalLow or ResultFlag.CriticalHigh => new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C)),
            ResultFlag.Low or ResultFlag.High or ResultFlag.Abnormal => new SolidColorBrush(Color.FromRgb(0xC2, 0x41, 0x0C)),
            _ => Brushes.Black
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Decimal? text box binding helper: empty text = null.</summary>
public class NullableDecimalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? d.ToString("0.####", CultureInfo.InvariantCulture) : "";
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string)?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : DependencyProperty.UnsetValue;
    }
}
