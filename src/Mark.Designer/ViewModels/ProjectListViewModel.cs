using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>A saved project as listed in the Open dialog.</summary>
public sealed record ProjectRow(Guid Id, string Name, string Modified);

/// <summary>
/// The Open dialog: lists the saved projects (from <see cref="IProjectRepository.List"/>, most recently modified first),
/// filters them by name as you type, and deletes one after confirmation. The project open in the designer cannot be
/// deleted here. Reads the list once and on delete; typing filters in memory.
/// </summary>
public sealed class ProjectListViewModel : ViewModelBase
{
    private readonly IProjectRepository _projects;
    private readonly Guid _openProjectId;
    private readonly IDialogService? _dialogs;
    private IReadOnlyList<ProjectSummary> _all = Array.Empty<ProjectSummary>();

    public ProjectListViewModel(IProjectRepository projects, Guid openProjectId, IDialogService? dialogs = null)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _openProjectId = openProjectId;
        _dialogs = dialogs;
        DeleteCommand = new RelayCommand(Delete, () => SelectedProject is not null);
        Reload();
    }

    public ObservableCollection<ProjectRow> Projects { get; } = new();

    private string? _searchText;
    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                Filter();
        }
    }

    private ProjectRow? _selectedProject;
    public ProjectRow? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
                ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
        }
    }

    private string? _message;
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public ICommand DeleteCommand { get; }

    /// <summary>The project the dialog should open (the selection), or null.</summary>
    public Guid? Result => SelectedProject?.Id;

    private void Reload()
    {
        _all = _projects.List();
        Filter();
        Message = _all.Count == 0 ? "No saved projects yet." : null;
    }

    private void Filter()
    {
        Projects.Clear();
        var words = (SearchText ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in _all.Where(p => words.All(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase))))
            Projects.Add(new ProjectRow(p.Id, p.Name,
                p.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        SelectedProject = Projects.FirstOrDefault();
    }

    private void Delete()
    {
        if (SelectedProject is not { } row)
            return;
        if (row.Id == _openProjectId)
        {
            Message = "This project is open in the designer. Open another project first to delete it.";
            return;
        }
        if (_dialogs is not null && !_dialogs.Confirm("Delete project", $"Delete '{row.Name}' permanently?"))
            return;
        try
        {
            _projects.Delete(row.Id);
            Reload();
            Message = $"Deleted '{row.Name}'.";
        }
        catch (DataStoreException ex)
        {
            Message = ex.Message;
        }
    }
}
