using System.Globalization;
using System.Windows.Data;

namespace Mark.Designer.Views;

/// <summary>
/// Width of one segment of a proportional bar: values[0] is the fraction (0–1), values[1] the bar's available width.
/// </summary>
public sealed class FractionToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double fraction || values[1] is not double width
            || !double.IsFinite(fraction) || !double.IsFinite(width))
            return 0.0;
        return Math.Max(0, Math.Floor(Math.Clamp(fraction, 0, 1) * width));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
