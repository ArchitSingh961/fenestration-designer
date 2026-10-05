using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Mark.Designer.Views;

/// <summary>
/// Puts a search box at the top of a ComboBox's drop-down: typing narrows the list to the items whose text holds every
/// word typed (name, code, detail…). Down moves into the list, Enter takes the first match, Esc closes. The box shows
/// once the list has <see cref="GetMinItems"/> items or more (8 by default); short lists stay as they are. The choice
/// itself is never changed by searching. Turned on for every ComboBox by the apps' implicit ComboBox style
/// (<c>views:SearchCombo.IsSearchable="True"</c>); editable ComboBoxes are left alone.
/// <para>
/// With <c>FiltersItems="False"</c> the list is not narrowed here: the search text goes to <see cref="QueryProperty"/>
/// (bind it to the view model, which fills the list itself, e.g. a large library), and the box is always shown.
/// </para>
/// </summary>
public static class SearchCombo
{
    public static readonly DependencyProperty IsSearchableProperty = DependencyProperty.RegisterAttached(
        "IsSearchable", typeof(bool), typeof(SearchCombo), new PropertyMetadata(false, OnIsSearchableChanged));

    public static bool GetIsSearchable(DependencyObject element) => (bool)element.GetValue(IsSearchableProperty);
    public static void SetIsSearchable(DependencyObject element, bool value) => element.SetValue(IsSearchableProperty, value);

    /// <summary>The list length from which the search box is shown.</summary>
    public static readonly DependencyProperty MinItemsProperty = DependencyProperty.RegisterAttached(
        "MinItems", typeof(int), typeof(SearchCombo), new PropertyMetadata(8));

    public static int GetMinItems(DependencyObject element) => (int)element.GetValue(MinItemsProperty);
    public static void SetMinItems(DependencyObject element, int value) => element.SetValue(MinItemsProperty, value);

    /// <summary>False: the list is filled by the view model from <see cref="QueryProperty"/> instead of narrowed here.</summary>
    public static readonly DependencyProperty FiltersItemsProperty = DependencyProperty.RegisterAttached(
        "FiltersItems", typeof(bool), typeof(SearchCombo), new PropertyMetadata(true));

    public static bool GetFiltersItems(DependencyObject element) => (bool)element.GetValue(FiltersItemsProperty);
    public static void SetFiltersItems(DependencyObject element, bool value) => element.SetValue(FiltersItemsProperty, value);

    /// <summary>The text in the search box (two-way by default).</summary>
    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query", typeof(string), typeof(SearchCombo),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static string? GetQuery(DependencyObject element) => (string?)element.GetValue(QueryProperty);
    public static void SetQuery(DependencyObject element, string? value) => element.SetValue(QueryProperty, value);

    /// <summary>The search box put in the drop-down (once, the first time it opens).</summary>
    private static readonly DependencyProperty PartsProperty = DependencyProperty.RegisterAttached(
        "Parts", typeof(Parts), typeof(SearchCombo), new PropertyMetadata(null));

    private sealed class Parts
    {
        public required FrameworkElement Header { get; init; }
        public required TextBox Box { get; init; }
        public required TextBlock NoMatches { get; init; }
    }

    private static void OnIsSearchableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox combo) return;
        combo.DropDownOpened -= OnOpened;
        combo.DropDownClosed -= OnClosed;
        combo.PreviewKeyDown -= OnComboKeyDown;
        if (e.NewValue is not true) return;
        combo.DropDownOpened += OnOpened;
        combo.DropDownClosed += OnClosed;
        combo.PreviewKeyDown += OnComboKeyDown;
    }

    private static void OnOpened(object? sender, EventArgs e)
    {
        var combo = (ComboBox)sender!;
        if (combo.IsEditable) return;
        var parts = (Parts?)combo.GetValue(PartsProperty) ?? Inject(combo);
        if (parts is null) return;
        bool external = !GetFiltersItems(combo);
        bool show = external || combo.Items.Count >= GetMinItems(combo);
        parts.Header.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        parts.Box.Text = "";
        Filter(combo, parts);
        // After the ComboBox has put the focus on the chosen item.
        combo.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (combo.IsDropDownOpen) parts.Box.Focus();
        });
    }

    private static void OnClosed(object? sender, EventArgs e)
    {
        var combo = (ComboBox)sender!;
        if (combo.GetValue(PartsProperty) is Parts { Box.Text.Length: > 0 } parts)
            parts.Box.Text = "";
    }

    /// <summary>Puts the search box above the drop-down's list. Returns null if the template has no list to put it on.</summary>
    private static Parts? Inject(ComboBox combo)
    {
        if (combo.Template?.FindName("PART_Popup", combo) is not Popup { Child: { } child }) return null;
        if (FindScrollViewer(child) is not { } list || VisualTreeHelper.GetParent(list) is not Decorator holder) return null;

        var muted = new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA3));
        var box = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(26, 6, 6, 6),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(box, "Search the list");
        var hint = new TextBlock
        {
            Text = "Search…", Foreground = muted, Margin = new Thickness(29, 0, 0, 0), IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        hint.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(TextBox.Text))
        {
            Source = box, Converter = new EmptyToVisibleConverter()
        });
        var glass = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = muted,
            Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        };
        var field = new Grid { Children = { box, hint, glass } };
        var frame = new Border
        {
            Child = field, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD5, 0xDA, 0xE1)), Background = Brushes.White,
            Margin = new Thickness(8, 8, 8, 6),
        };
        var noMatches = new TextBlock
        {
            Text = "No matches", Foreground = muted, Margin = new Thickness(12, 4, 12, 8), Visibility = Visibility.Collapsed,
        };
        var header = new StackPanel
        {
            Children =
            {
                frame,
                new Border
                {
                    Height = 1, Margin = new Thickness(8, 0, 8, 2),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xD5, 0xDA, 0xE1)),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                },
                noMatches,
            }
        };

        holder.Child = null;
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(list);
        holder.Child = dock;

        var parts = new Parts { Header = header, Box = box, NoMatches = noMatches };
        combo.SetValue(PartsProperty, parts);
        box.TextChanged += (_, _) =>
        {
            SetQuery(combo, box.Text);
            Filter(combo, parts);
        };
        box.PreviewKeyDown += (_, e) => OnBoxKeyDown(combo, e);
        combo.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (combo.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated && combo.IsDropDownOpen)
                Filter(combo, parts);
        };
        return parts;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found)
                return found;
        return null;
    }

    private static void Filter(ComboBox combo, Parts parts)
    {
        bool local = GetFiltersItems(combo);
        string[] words = (parts.Box.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int shown = 0;
        foreach (object item in combo.Items)
        {
            bool match = !local || words.Length == 0 || Matches(combo, item, words);
            if (match) shown++;
            if (combo.ItemContainerGenerator.ContainerFromItem(item) is UIElement container)
                container.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
        }
        parts.NoMatches.Visibility = shown == 0 && words.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>True when the item's text (shown name, code, detail…) holds every word.</summary>
    public static bool Matches(ComboBox combo, object? item, IReadOnlyList<string> words)
    {
        string text = TextOf(item, combo.DisplayMemberPath);
        return words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] SearchedProperties = { "Name", "Detail", "Code", "Id", "Series", "Manufacturer", "Text" };

    /// <summary>The searchable text of a list item.</summary>
    public static string TextOf(object? item, string? displayMemberPath = null)
    {
        if (item is null) return "";
        if (item is string s) return s;
        if (item is ComboBoxItem { Content: var content }) return TextOf(content);
        var parts = new List<string> { item.ToString() ?? "" };
        var type = item.GetType();
        foreach (string name in SearchedProperties.Prepend(displayMemberPath).Distinct())
            if (!string.IsNullOrEmpty(name) && type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { } p
                && p.GetIndexParameters().Length == 0 && p.GetValue(item) is { } value)
                parts.Add(value.ToString() ?? "");
        return string.Join(" ", parts);
    }

    private static IEnumerable<ComboBoxItem> VisibleContainers(ComboBox combo)
    {
        foreach (object item in (IEnumerable)combo.Items)
            if (combo.ItemContainerGenerator.ContainerFromItem(item) is ComboBoxItem { Visibility: Visibility.Visible } c)
                yield return c;
    }

    private static void OnBoxKeyDown(ComboBox combo, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (VisibleContainers(combo).FirstOrDefault() is { } first)
                {
                    first.Focus();
                    e.Handled = true;
                }
                break;
            case Key.Enter:
                if (VisibleContainers(combo).FirstOrDefault() is { } match)
                {
                    combo.SelectedItem = combo.ItemContainerGenerator.ItemFromContainer(match);
                    combo.IsDropDownOpen = false;
                }
                e.Handled = true;
                break;
            case Key.Escape:
                combo.IsDropDownOpen = false;
                combo.Focus();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Up from the first item goes back to the search box; typing a letter in the list searches.</summary>
    private static void OnComboKeyDown(object sender, KeyEventArgs e)
    {
        var combo = (ComboBox)sender;
        if (!combo.IsDropDownOpen || combo.GetValue(PartsProperty) is not Parts { Header.Visibility: Visibility.Visible } parts) return;
        if (e.OriginalSource is not ComboBoxItem item) return;
        if (e.Key == Key.Up && VisibleContainers(combo).FirstOrDefault() == item)
        {
            parts.Box.Focus();
            parts.Box.CaretIndex = parts.Box.Text.Length;
            e.Handled = true;
        }
        else if (e.Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9)
        {
            parts.Box.Focus();                                           // the key then types into the search box
            parts.Box.CaretIndex = parts.Box.Text.Length;
        }
    }

    private sealed class EmptyToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
