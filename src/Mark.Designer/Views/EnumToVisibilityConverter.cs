using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Mark.Designer.Views;

/// <summary>Visible when an enum value equals the ConverterParameter (e.g. the page being shown), otherwise collapsed.</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null && parameter is string name && string.Equals(value.ToString(), name, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
