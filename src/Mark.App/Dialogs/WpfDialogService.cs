using System.Windows;
using Mark.Designer.ViewModels;
using Mark.Designer.Views;
using Microsoft.Win32;

namespace Mark.App.Dialogs;

/// <summary>The WPF implementation of <see cref="IDialogService"/>: message boxes, file dialogs and the app's windows.</summary>
public sealed class WpfDialogService : IDialogService
{
    private static Window? Owner => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                    ?? Application.Current?.MainWindow;

    public bool Confirm(string title, string message)
        => MessageBox.Show(Owner!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Inform(string title, string message)
        => MessageBox.Show(Owner!, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public string? PromptText(string title, string label, string initialText)
    {
        var window = new TextPromptWindow(title, label, initialText) { Owner = Owner };
        return window.ShowDialog() == true ? window.Text : null;
    }

    public Guid? ChooseProject(ProjectListViewModel projects)
    {
        var window = new OpenProjectWindow(projects) { Owner = Owner };
        return window.ShowDialog() == true ? projects.Result : null;
    }

    public string? ChooseOpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? ChooseSaveFile(string title, string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName, OverwritePrompt = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public void ShowLibraryManager(LibraryManagerViewModel manager)
        => new LibraryManagerWindow(manager) { Owner = Owner }.ShowDialog();
}
