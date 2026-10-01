using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Fenestration.Designer.Views;

/// <summary>
/// Binds an enum to a group of RadioButtons: true when the value equals the ConverterParameter;
/// checking a button sets the enum to its parameter.
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null && parameter is string name && string.Equals(value.ToString(), name, StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}
