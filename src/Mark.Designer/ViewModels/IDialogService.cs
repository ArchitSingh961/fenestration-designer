namespace Mark.Designer.ViewModels;

/// <summary>
/// The dialogs view models need (confirmations, a name, a file, the Open and Library Manager windows). The WPF shell
/// implements it; tests pass a scripted fake. View models never create windows or file dialogs themselves.
/// </summary>
public interface IDialogService
{
    /// <summary>Asks a yes/no question. True = yes.</summary>
    bool Confirm(string title, string message);

    /// <summary>
    /// "Save changes to …?" with Save, Don't save and Cancel: true = save, false = don't save, null = cancel. Without its
    /// own window it asks <see cref="Confirm"/> whether to go on without saving (yes = don't save, no = cancel).
    /// </summary>
    bool? AskSaveChanges(string title, string question, string detail)
        => Confirm(title, $"{question}\n\n{detail}\n\nContinue without saving?") ? false : null;

    /// <summary>Asks for a line of text; null if cancelled.</summary>
    string? PromptText(string title, string label, string initialText);

    /// <summary>Shows the saved projects; returns the one to open, or null if cancelled.</summary>
    Guid? ChooseProject(ProjectListViewModel projects);

    /// <summary>A file to read, or null if cancelled. <paramref name="filter"/> is a standard file-dialog filter.</summary>
    string? ChooseOpenFile(string title, string filter);

    /// <summary>A file to write, or null if cancelled.</summary>
    string? ChooseSaveFile(string title, string filter, string fileName);

    /// <summary>Shows a message with an OK button (e.g. About MARK).</summary>
    void Inform(string title, string message) { }

    /// <summary>Shows the library manager (modal).</summary>
    void ShowLibraryManager(LibraryManagerViewModel manager);
}
