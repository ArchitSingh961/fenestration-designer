using System.Collections.ObjectModel;

namespace Fenestration.Designer.ViewModels;

/// <summary>One library item offered in a picker.</summary>
/// <param name="Detail">Secondary text, e.g. "8 mm · Toughened · 1,450.00/m²".</param>
public sealed record LibraryOption(string Id, string Name, string Detail)
{
    public override string ToString() => Name;
}

/// <summary>
/// A searchable library selection (glass type or profile) inside the properties panel. It holds no design state:
/// the options come from a search callback over the library, and choosing one calls the host, which runs an
/// undoable command. The panel is rebuilt from the model afterwards, so this object is short-lived.
/// Only the first <see cref="MaxOptions"/> matches are listed; typing in <see cref="SearchText"/> narrows them,
/// so large libraries stay usable.
/// </summary>
public sealed class LibraryPickerViewModel : ViewModelBase
{
    public const int MaxOptions = 50;

    private readonly Func<string?, IReadOnlyList<LibraryOption>> _search;
    private readonly Func<string, string?> _apply;
    private bool _updating;

    /// <param name="label">e.g. "Glass" or "Profile".</param>
    /// <param name="currentText">What is assigned now, e.g. "6mm Clear (default)", "Mixed" or "Missing: X".</param>
    /// <param name="currentId">The assigned definition id, or null (default, mixed or none).</param>
    /// <param name="search">Returns the matching options for a search text (null = all).</param>
    /// <param name="apply">Assigns a definition id; returns an error message, or null on success.</param>
    public LibraryPickerViewModel(string label, string currentText, string? currentId,
        Func<string?, IReadOnlyList<LibraryOption>> search, Func<string, string?> apply)
    {
        Label = label;
        CurrentText = currentText;
        CurrentId = currentId;
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        Refresh();
    }

    public string Label { get; }

    public string CurrentText { get; }

    public string? CurrentId { get; }

    public ObservableCollection<LibraryOption> Options { get; } = new();

    private string? _searchText;
    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                Refresh();
        }
    }

    private string _resultSummary = "";
    /// <summary>e.g. "12 matches" or "showing 50 of 340".</summary>
    public string ResultSummary
    {
        get => _resultSummary;
        private set => SetProperty(ref _resultSummary, value);
    }

    private LibraryOption? _selectedOption;
    /// <summary>Choosing an option other than the current one assigns it straight away (like any dropdown).</summary>
    public LibraryOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (_updating || value is null || value.Id == CurrentId)
            {
                SetProperty(ref _selectedOption, value);
                return;
            }

            ErrorMessage = _apply(value.Id);
            // On failure, show the assignment that is still in effect.
            SetProperty(ref _selectedOption, ErrorMessage is null ? value : Options.FirstOrDefault(o => o.Id == CurrentId));
        }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    private void Refresh()
    {
        _updating = true;
        try
        {
            var matches = _search(_searchText);
            Options.Clear();
            foreach (var option in matches.Take(MaxOptions))
                Options.Add(option);
            ResultSummary = matches.Count > MaxOptions
                ? $"showing {MaxOptions} of {matches.Count}"
                : matches.Count == 1 ? "1 match" : $"{matches.Count} matches";
            SelectedOption = Options.FirstOrDefault(o => o.Id == CurrentId);
        }
        finally
        {
            _updating = false;
        }
    }
}
