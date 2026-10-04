using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Mark.Owner.ViewModels;

namespace Mark.Owner;

/// <summary>
/// MARK Owner's main window: Companies, Licence keys, Packages, Company types and Catalogue (behaviour in the view
/// models). Here only the account page's section menu: a click scrolls to the section, scrolling marks the section in view.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>The next scroll is the menu's own jump: keep the section that was clicked marked.</summary>
    private bool _jumping;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>The section of a menu entry: NavCompany → SecCompany.</summary>
    private FrameworkElement? SectionOf(RadioButton nav) => FindName("Sec" + nav.Name[3..]) as FrameworkElement;

    /// <summary>Where the section starts in the scrolled content (the same measure as the scroll offset).</summary>
    private double OffsetOf(FrameworkElement section)
        => section.TransformToAncestor(EditorScroll).Transform(new Point()).Y + EditorScroll.VerticalOffset;

    private void SectionNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton nav || SectionOf(nav) is not { IsVisible: true } section) return;
        double target = Math.Clamp(OffsetOf(section) - 12, 0, EditorScroll.ScrollableHeight);
        if (Math.Abs(target - EditorScroll.VerticalOffset) < 0.5) return;
        _jumping = true;
        EditorScroll.ScrollToVerticalOffset(target);
    }

    private void EditorScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0) return;
        if (_jumping)
        {
            _jumping = false;
            return;
        }
        // The last section whose top is in the upper third of the view; at the very end, the last section.
        RadioButton? current = null;
        double line = EditorScroll.VerticalOffset + EditorScroll.ViewportHeight / 3;
        bool atEnd = EditorScroll.VerticalOffset >= EditorScroll.ScrollableHeight - 1;
        foreach (var nav in SectionNav.Children.OfType<RadioButton>())
        {
            if (!nav.IsVisible || SectionOf(nav) is not { IsVisible: true } section) continue;
            if (current is null || atEnd || OffsetOf(section) <= line) current = nav;
        }
        if (current is not null) current.IsChecked = true;
    }

    /// <summary>Another account opened (not the same one saved again): start at its top.</summary>
    private void AccountPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not CompanyEditorViewModel editor) return;
        if (e.OldValue is CompanyEditorViewModel { Existing: { } before } && editor.Existing?.Id == before.Id) return;
        _jumping = false;
        NavCompany.IsChecked = true;
        // After the page is laid out (it may just have become visible), so nothing scrolls it down again.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            EditorScroll.ScrollToTop();
            NavCompany.IsChecked = true;
        });
    }
}
