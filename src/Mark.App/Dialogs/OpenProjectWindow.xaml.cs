using System.Windows;
using System.Windows.Input;
using Mark.Designer.ViewModels;

namespace Mark.App.Dialogs;

/// <summary>Shows a <see cref="ProjectListViewModel"/>; closes with OK when a project is chosen. UI glue only.</summary>
public partial class OpenProjectWindow : Window
{
    public OpenProjectWindow(ProjectListViewModel projects)
    {
        InitializeComponent();
        DataContext = projects;
    }

    private void Open_Click(object sender, RoutedEventArgs e) => Finish(((ProjectListViewModel)DataContext).Result is not null);

    private void List_DoubleClick(object sender, MouseButtonEventArgs e) => Finish(((ProjectListViewModel)DataContext).Result is not null);

    private void Finish(bool chosen)
    {
        if (chosen) DialogResult = true;
    }
}
