using System.Collections.ObjectModel;
using System.Windows.Input;
using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Designer.ViewModels;

/// <summary>A system offered in the Select system panel: "62mm Casement – uPVC".</summary>
public sealed record SystemChoice(string Id, string Name, string Detail)
{
    public override string ToString() => Name;
}

/// <summary>
/// The Select system panel shown when a new design is made: choose the brand (the maker of the system's profiles), then
/// one of its systems, and Confirm; the design is made in that system. × keeps the system it was made in (the default).
/// The last system chosen is offered first the next time.
/// </summary>
public sealed class SystemPickerViewModel : ViewModelBase
{
    /// <summary>Systems whose frame profile names no maker are listed under this brand.</summary>
    public const string OtherBrand = "Other";

    private readonly Func<IProductLibrary> _library;
    private readonly Func<Guid, string, string?> _apply;
    private List<(string Brand, ProductSystem System)> _all = new();
    private Guid _frameId;

    /// <param name="apply">Puts the frame in the system; returns an error, or null.</param>
    public SystemPickerViewModel(Func<IProductLibrary> library, Func<Guid, string, string?> apply)
    {
        _library = library;
        _apply = apply;
        ConfirmCommand = new RelayCommand(Confirm, () => SelectedSystem is not null);
        CloseCommand = new RelayCommand(() => IsOpen = false);
    }

    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }

    private bool _isOpen;
    public bool IsOpen { get => _isOpen; private set => SetProperty(ref _isOpen, value); }

    /// <summary>"For W1".</summary>
    public string Subtitle { get; private set; } = "";

    public ObservableCollection<string> Brands { get; } = new();
    public ObservableCollection<SystemChoice> Systems { get; } = new();

    private string? _selectedBrand;
    public string? SelectedBrand
    {
        get => _selectedBrand;
        set
        {
            if (!SetProperty(ref _selectedBrand, value)) return;
            FillSystems(null);
        }
    }

    private SystemChoice? _selectedSystem;
    public SystemChoice? SelectedSystem
    {
        get => _selectedSystem;
        set
        {
            if (!SetProperty(ref _selectedSystem, value)) return;
            OnPropertyChanged(nameof(DetailText));
            ((RelayCommand)ConfirmCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>What the chosen system is: material, windows or doors, frame profile and glass range.</summary>
    public string DetailText => _selectedSystem?.Detail ?? "";

    /// <summary>The system chosen last (offered first next time).</summary>
    public string? LastSystemId { get; private set; }

    private string? _message;
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }

    /// <summary>True when there is a choice to make: at least two active systems.</summary>
    public bool HasChoice => _library().Systems.Count(s => s.IsActive) >= 2;

    /// <summary>Asks for the system of a new frame (nothing happens with fewer than two systems).</summary>
    public void Open(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var library = _library();
        _all = library.Systems.Where(s => s.IsActive)
            .Select(s => (Brand: BrandOf(s, library), System: s))
            .OrderBy(x => x.Brand == OtherBrand).ThenBy(x => x.Brand, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.System.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (_all.Count < 2) return;

        _frameId = frame.Id;
        Subtitle = string.IsNullOrWhiteSpace(frame.Design.Reference) ? "For the new design" : $"For {frame.Design.Reference}";
        OnPropertyChanged(nameof(Subtitle));
        Message = null;
        string? preferred = _all.Any(x => x.System.Id == LastSystemId) ? LastSystemId : frame.SystemId ?? library.Defaults.SystemId;
        var start = _all.FirstOrDefault(x => x.System.Id == preferred);
        Brands.Clear();
        foreach (string brand in _all.Select(x => x.Brand).Distinct()) Brands.Add(brand);
        _selectedBrand = start.System is not null ? start.Brand : Brands.First();
        OnPropertyChanged(nameof(SelectedBrand));
        FillSystems(start.System?.Id);
        IsOpen = true;
    }

    /// <summary>The brand of a system: the maker of its frame profile (or of its first profile), else "Other".</summary>
    public static string BrandOf(ProductSystem system, IProductLibrary library)
    {
        foreach (string? id in new[] { system.FrameProfileId, system.SashProfileId, system.MullionProfileId })
            if (id is not null && library.FindProfile(id)?.Manufacturer is { Length: > 0 } maker)
                return maker.Trim();
        return OtherBrand;
    }

    private void FillSystems(string? select)
    {
        var library = _library();
        Systems.Clear();
        foreach (var (_, s) in _all.Where(x => x.Brand == _selectedBrand))
        {
            string frame = library.FindProfile(s.FrameProfileId)?.Name ?? "";
            string detail = string.Join("  ·  ", new[]
            {
                EditorText.MaterialName(s.Material), EditorText.UseName(s.Use), frame,
                s.GlassRangeText.Length > 0 ? $"glass {s.GlassRangeText}" : "", s.Description ?? ""
            }.Where(t => !string.IsNullOrWhiteSpace(t)));
            Systems.Add(new SystemChoice(s.Id, s.Name, detail));
        }
        SelectedSystem = Systems.FirstOrDefault(c => c.Id == select) ?? Systems.FirstOrDefault();
    }

    private void Confirm()
    {
        if (SelectedSystem is not { } choice) return;
        if (_apply(_frameId, choice.Id) is { } error)
        {
            Message = error;
            return;
        }
        LastSystemId = choice.Id;
        IsOpen = false;
    }
}
