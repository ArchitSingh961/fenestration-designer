using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Mark.Designer.Views;

/// <summary>Visible only when every bound value is true (e.g. "is an order" and "may use production orders").</summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
