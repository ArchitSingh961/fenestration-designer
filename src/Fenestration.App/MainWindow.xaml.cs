using System.Windows;

namespace Fenestration.App;

/// <summary>
/// Main application window. Contains only UI-specific glue; all behaviour lives in view models.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Give the viewport keyboard focus so its shortcuts (F, G, Space-pan) work immediately.</summary>
    private void Window_Loaded(object sender, RoutedEventArgs e) => Viewport.Focus();
}
