using System.Windows;
using Mark.Designer.ViewModels;

namespace Mark.App.Dialogs;

/// <summary>Shows a <see cref="LibraryManagerViewModel"/>. UI glue only: all behaviour is in the view model.</summary>
public partial class LibraryManagerWindow : Window
{
    public LibraryManagerWindow(LibraryManagerViewModel manager)
    {
        InitializeComponent();
        DataContext = manager;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
