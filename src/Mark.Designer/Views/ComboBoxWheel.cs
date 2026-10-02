using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Mark.Designer.Views;

/// <summary>
/// Stops the mouse wheel from silently changing a ComboBox's choice (WPF changes a focused, closed ComboBox's selection
/// on every wheel notch, which is easy to do by accident while scrolling a form). With
/// <c>views:ComboBoxWheel.ScrollsPage="True"</c> the wheel scrolls the page instead; it still moves through the list
/// while the drop-down is open.
/// </summary>
public static class ComboBoxWheel
{
    public static readonly DependencyProperty ScrollsPageProperty = DependencyProperty.RegisterAttached(
        "ScrollsPage", typeof(bool), typeof(ComboBoxWheel), new PropertyMetadata(false, OnChanged));

    public static bool GetScrollsPage(DependencyObject element) => (bool)element.GetValue(ScrollsPageProperty);

    public static void SetScrollsPage(DependencyObject element, bool value) => element.SetValue(ScrollsPageProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox combo) return;
        combo.PreviewMouseWheel -= OnWheel;
        if (e.NewValue is true)
            combo.PreviewMouseWheel += OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var combo = (ComboBox)sender;
        if (combo.IsDropDownOpen) return;
        e.Handled = true;
        if (VisualTreeHelper.GetParent(combo) is UIElement parent)
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = combo
            });
    }
}
