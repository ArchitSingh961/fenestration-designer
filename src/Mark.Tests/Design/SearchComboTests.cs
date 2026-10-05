using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Mark.Designer.Views;
using Xunit;

namespace Mark.Tests.Design;

/// <summary>A long drop-down gets a search box at the top of its list, which narrows the list without changing the choice.</summary>
public class SearchComboTests
{
    private static void OnSta(Action work)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { error = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException(error.ToString(), error);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) return found;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find<T>(VisualTreeHelper.GetChild(root, i)) is { } inner)
                return inner;
        return null;
    }

    private static (Window Window, ComboBox Combo) Show(IEnumerable<string> items)
    {
        var combo = new ComboBox { ItemsSource = items.ToList(), Width = 200 };
        SearchCombo.SetIsSearchable(combo, true);
        var window = new Window
        {
            Content = combo, Width = 300, Height = 200, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None
        };
        window.Show();
        Pump();
        return (window, combo);
    }

    private static TextBox? SearchBox(ComboBox combo)
        => combo.Template.FindName("PART_Popup", combo) is Popup { Child: { } child } ? Find<TextBox>(child) : null;

    private static IEnumerable<string> Visible(ComboBox combo)
        => combo.Items.Cast<string>().Where(i => combo.ItemContainerGenerator.ContainerFromItem(i) is UIElement { Visibility: Visibility.Visible });

    [Fact]
    public void ALongList_GetsASearchBox_ThatNarrowsIt_AndEnterTakesTheFirstMatch()
    {
        OnSta(() =>
        {
            var glass = new[] { "4mm Clear", "5mm Clear", "6mm Clear", "(6+12+6)24mm DGU", "(8+12+8)28mm DGU", "30mm DGU",
                                "(10+12+8)30mm DGU", "32mm DGU", "5MM PINHEAD GLASS" };
            var (window, combo) = Show(glass);
            combo.SelectedItem = "6mm Clear";
            combo.IsDropDownOpen = true;
            Pump();

            var box = SearchBox(combo);
            Assert.NotNull(box);
            Assert.True(box!.IsVisible);

            box.Text = "30 dgu";                                                             // every word, any order, any case
            Pump();
            Assert.Equal(new[] { "30mm DGU", "(10+12+8)30mm DGU" }, Visible(combo));
            Assert.Equal("6mm Clear", combo.SelectedItem);                                  // searching does not change the choice

            box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Enter)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            });
            Assert.Equal("30mm DGU", combo.SelectedItem);
            Assert.False(combo.IsDropDownOpen);
            Pump();
            Assert.Equal("", box.Text);                                                       // a fresh list next time
            window.Close();
        });
    }

    [Fact]
    public void AShortList_HasNoSearchBox()
    {
        OnSta(() =>
        {
            var (window, combo) = Show(new[] { "Casement", "Sliding" });
            combo.IsDropDownOpen = true;
            Pump();

            Assert.False(SearchBox(combo)?.IsVisible ?? false);
            window.Close();
        });
    }
}
