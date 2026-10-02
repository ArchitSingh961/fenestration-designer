using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Fenestration.Designer.Views;

/// <summary>Visible when the value is false, collapsed when it is true (the opposite of BooleanToVisibilityConverter).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}
