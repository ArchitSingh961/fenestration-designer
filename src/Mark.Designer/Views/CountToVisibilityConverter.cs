using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Mark.Designer.Views;

/// <summary>Visible when a count is above zero (or, with <see cref="Invert"/>, when it is zero).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool any = value is int count && count > 0;
        return any != Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
