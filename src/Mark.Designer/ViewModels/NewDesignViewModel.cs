using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Designer.ViewModels;

/// <summary>What the New design panel sets on the design just made.</summary>
/// <param name="SystemId">The system, or null to keep the one it was made in.</param>
/// <param name="GlassId">The glass for all its panes, or null to keep it.</param>
public sealed record NewDesignChoice(string? SystemId, string? GlassId, string Reference, int Quantity, string Location);

/// <summary>
/// The New design panel shown when a design is made: its reference, quantity and location, the brand (asked when the
/// catalogue has systems of two or more makers, and not for a design made in its own system) and the glass for all its
/// panes. The system is not asked: the design decides it (a sliding design goes in the brand's sliding system, a
/// casement design in its casement system; see <see cref="SystemMatch"/>). Apply sets everything in one undo step;
/// Cancel keeps the design as it was made. The last brand and glass chosen are offered first the next time.
/// </summary>
public sealed class NewDesignViewModel : ViewModelBase
{
    private readonly Func<IProductLibrary> _library;
    private readonly Func<Guid, NewDesignChoice, string?> _apply;
    private Guid _frameId;
    private string? _frameSystemId;
    private IReadOnlyList<OpeningType> _openings = Array.Empty<OpeningType>();

    /// <param name="apply">Sets the choice on the frame; returns an error, or null.</param>
    public NewDesignViewModel(Func<IProductLibrary> library, Func<Guid, NewDesignChoice, string?> apply)
    {
        _library = library;
        _apply = apply;
        ConfirmCommand = new RelayCommand(Confirm);
        CloseCommand = new RelayCommand(() => IsOpen = false);
    }

    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }

    private bool _isOpen;
    public bool IsOpen { get => _isOpen; private set => SetProperty(ref _isOpen, value); }

    /// <summary>"1200 × 1500 mm".</summary>
    public string Subtitle { get; private set; } = "";

    private string _reference = "";
    public string Reference { get => _reference; set => SetProperty(ref _reference, value); }

    private string _quantityText = "1";
    public string QuantityText { get => _quantityText; set => SetProperty(ref _quantityText, value); }

    private string _location = "";
    public string Location { get => _location; set => SetProperty(ref _location, value); }

    /// <summary>True when the brand is asked: systems of two or more makers, and the design was not made in its own system.</summary>
    public bool AsksBrand { get; private set; }

    public ObservableCollection<string> Brands { get; } = new();

    private string? _selectedBrand;
    public string? SelectedBrand
    {
        get => _selectedBrand;
        set
        {
            if (!SetProperty(ref _selectedBrand, value)) return;
            UpdateSystem();
        }
    }

    /// <summary>The system the design will be in: decided by the design (and the brand).</summary>
    public string? SystemId { get; private set; }

    /// <summary>The glass that fits the system and the design's openings.</summary>
    public ObservableCollection<LibraryOption> GlassOptions { get; } = new();

    private LibraryOption? _selectedGlass;
    public LibraryOption? SelectedGlass { get => _selectedGlass; set => SetProperty(ref _selectedGlass, value); }

    /// <summary>True when the design has glass to choose.</summary>
    public bool AsksGlass => _openings.Count > 0;

    /// <summary>The design has panes but no glass of the library fits its system.</summary>
    public bool HasNoGlass => AsksGlass && GlassOptions.Count == 0;

    /// <summary>The brand chosen last (offered first next time).</summary>
    public string? LastBrand { get; private set; }

    /// <summary>The glass chosen last (offered first next time, when it fits).</summary>
    public string? LastGlassId { get; private set; }

    private string? _message;
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }

    /// <summary>Asks for the details of a new frame.</summary>
    /// <param name="askBrand">False for a design made in its own system (the brand is not asked).</param>
    public void Open(Frame frame, bool askBrand = true)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var library = _library();
        _frameId = frame.Id;
        _frameSystemId = frame.SystemId;
        _openings = frame.GlassPanels.Select(p => p.Opening).ToList();
        Reference = frame.Design.Reference;
        QuantityText = frame.Design.Quantity.ToString(CultureInfo.CurrentCulture);
        Location = frame.Design.Location;
        Subtitle = $"{Format(frame.Width)} × {Format(frame.Height)} mm";
        Message = null;

        var brands = library.Systems.Where(s => s.IsActive).Select(s => SystemMatch.BrandOf(s, library)).Distinct()
            .OrderBy(b => b == SystemMatch.OtherBrand).ThenBy(b => b, StringComparer.OrdinalIgnoreCase).ToList();
        AsksBrand = askBrand && brands.Count >= 2;
        Brands.Clear();
        if (AsksBrand)
        {
            foreach (string brand in brands) Brands.Add(brand);
            string? current = library.FindSystem(frame.SystemId ?? library.Defaults.SystemId) is { } s ? SystemMatch.BrandOf(s, library) : null;
            _selectedBrand = brands.Contains(LastBrand ?? "") ? LastBrand : brands.Contains(current ?? "") ? current : brands[0];
        }
        else
            _selectedBrand = null;
        OnPropertyChanged(nameof(SelectedBrand));
        UpdateSystem();
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(AsksBrand));
        OnPropertyChanged(nameof(AsksGlass));
        IsOpen = true;
    }

    /// <summary>The design decides the system, within the brand chosen; the glass follows it.</summary>
    private void UpdateSystem()
    {
        var library = _library();
        SystemId = AsksBrand
            ? SystemMatch.For(library, _openings.Select(o => (OpeningType?)o), _frameSystemId ?? library.Defaults.SystemId, _selectedBrand)
            : _frameSystemId;
        OnPropertyChanged(nameof(SystemId));
        FillGlass();
    }

    /// <summary>The glass of the system the design will be in; keeps the choice when it still fits.</summary>
    private void FillGlass()
    {
        var library = _library();
        var system = library.FindSystem(SystemId);
        string? keep = _selectedGlass?.Id;
        GlassOptions.Clear();
        if (_openings.Count > 0)
            foreach (var g in library.SearchGlass(new LibraryQuery(null))
                         .Where(g => (g.UsedWith?.FitsSystem(SystemId) ?? true) && (system?.AcceptsGlass(g.ThicknessMm) ?? true)
                                     && (g.UsedWith?.FitsOpenings(_openings) ?? true)))
                GlassOptions.Add(new LibraryOption(g.Id, g.Name, string.Join(" · ",
                    new[] { $"{Format(g.ThicknessMm)} mm", g.Category }.Where(t => !string.IsNullOrWhiteSpace(t)))));
        string? fallback = (library.FindGlass(system?.GlassId) ?? library.DefaultGlass)?.Id;
        SelectedGlass = GlassOptions.FirstOrDefault(o => o.Id == keep)
                        ?? GlassOptions.FirstOrDefault(o => o.Id == LastGlassId)
                        ?? GlassOptions.FirstOrDefault(o => o.Id == fallback)
                        ?? GlassOptions.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoGlass));
    }

    private void Confirm()
    {
        if (!int.TryParse(QuantityText?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int quantity))
        {
            Message = "Enter the quantity as a whole number.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Reference))
        {
            Message = "Enter the design ref., e.g. W1.";
            return;
        }
        var choice = new NewDesignChoice(SystemId != _frameSystemId ? SystemId : null, AsksGlass ? SelectedGlass?.Id : null,
            Reference.Trim(), quantity, Location ?? "");
        if (_apply(_frameId, choice) is { } error)
        {
            Message = error;
            return;
        }
        if (AsksBrand) LastBrand = _selectedBrand;
        if (choice.GlassId is not null) LastGlassId = choice.GlassId;
        IsOpen = false;
    }

    private static string Format(double mm) => mm.ToString("0.#", CultureInfo.CurrentCulture);
}
