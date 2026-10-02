using Mark.Core.Models;

namespace Mark.Designer.ViewModels;

/// <summary>An entry of the opening-type list.</summary>
public sealed record OpeningOption(OpeningType Type, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Chooses how the selected opening(s) open and whether they have a mesh shutter. Each change is applied at once
/// as one undoable step. With several openings of different types the type shows as "Mixed" (null) until one is
/// chosen; ticking Mesh then changes only the mesh and keeps each opening's type.
/// </summary>
public sealed class OpeningEditorViewModel : ViewModelBase
{
    private readonly Func<OpeningType?, bool?, string?> _apply;
    private bool _loading;

    public static IReadOnlyList<OpeningOption> AllOptions { get; } =
        Enum.GetValues<OpeningType>().Select(t => new OpeningOption(t, t.DisplayName())).ToList();

    /// <param name="label">"Opening", or "Openings (3)" for several.</param>
    /// <param name="apply">Applies (type or null = keep, mesh or null = keep); returns an error message or null.</param>
    public OpeningEditorViewModel(string label, IReadOnlyList<GlassPanel> panels, Func<OpeningType?, bool?, string?> apply)
    {
        ArgumentNullException.ThrowIfNull(panels);
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        Label = label;
        _loading = true;
        var types = panels.Select(p => p.Opening).Distinct().ToList();
        SelectedOption = types.Count == 1 ? AllOptions.First(o => o.Type == types[0]) : null;
        var meshes = panels.Select(p => p.HasMesh).Distinct().ToList();
        HasMesh = meshes.Count == 1 ? meshes[0] : null;
        _loading = false;
    }

    public string Label { get; }

    public IReadOnlyList<OpeningOption> Options => AllOptions;

    private OpeningOption? _selectedOption;
    public OpeningOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            var previous = _selectedOption;
            if (!SetProperty(ref _selectedOption, value) || _loading || value is null) return;
            ErrorMessage = _apply(value.Type, null);
            if (ErrorMessage is not null)
            {
                _selectedOption = previous;   // rejected: show what the model still has
                OnPropertyChanged(nameof(SelectedOption));
            }
        }
    }

    private bool? _hasMesh;
    /// <summary>Mesh shutter on/off; null = some have one and some don't.</summary>
    public bool? HasMesh
    {
        get => _hasMesh;
        set
        {
            var previous = _hasMesh;
            if (!SetProperty(ref _hasMesh, value) || _loading || value is null) return;
            ErrorMessage = _apply(null, value);
            if (ErrorMessage is not null)
            {
                _hasMesh = previous;
                OnPropertyChanged(nameof(HasMesh));
            }
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
}
