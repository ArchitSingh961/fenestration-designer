using Fenestration.Designer.ViewModels;

namespace Fenestration.Tests.Data;

/// <summary>A scripted <see cref="IDialogService"/>: answers are set by the test; every call is recorded.</summary>
internal sealed class FakeDialogs : IDialogService
{
    public bool ConfirmAnswer { get; set; } = true;
    public string? PromptAnswer { get; set; }
    public Func<ProjectListViewModel, Guid?> ChooseProjectAnswer { get; set; } = list => list.Result;
    public string? OpenFileAnswer { get; set; }
    public string? SaveFileAnswer { get; set; }
    public Action<LibraryManagerViewModel>? OnLibraryManager { get; set; }

    public List<string> Confirms { get; } = new();
    public List<string> Prompts { get; } = new();

    public bool Confirm(string title, string message)
    {
        Confirms.Add(message);
        return ConfirmAnswer;
    }

    public string? PromptText(string title, string label, string initialText)
    {
        Prompts.Add(initialText);
        return PromptAnswer;
    }

    public Guid? ChooseProject(ProjectListViewModel projects) => ChooseProjectAnswer(projects);

    public string? ChooseOpenFile(string title, string filter) => OpenFileAnswer;

    public string? ChooseSaveFile(string title, string filter, string fileName) => SaveFileAnswer;

    public void ShowLibraryManager(LibraryManagerViewModel manager) => OnLibraryManager?.Invoke(manager);
}
