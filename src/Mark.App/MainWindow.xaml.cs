using System.ComponentModel;
using System.Windows;
using Mark.Designer.ViewModels;

namespace Mark.App;

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

    /// <summary>A double-click on an enquiry opens it in the form.</summary>
    private void EnquiryRow_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListViewItem { DataContext: EnquiryRow row } && DataContext is MainViewModel vm)
            vm.Enquiries.EditCommand.Execute(row);
    }

    /// <summary>Give the viewport keyboard focus so its shortcuts (F, G, Space-pan) work immediately.</summary>
    private void Window_Loaded(object sender, RoutedEventArgs e) => Viewport.Focus();

    /// <summary>Ask before closing over unsaved changes (the decision is the view model's).</summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.ConfirmDiscardChanges())
            e.Cancel = true;
    }
}
