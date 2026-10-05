using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Calculation;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Designer.ViewModels;

/// <summary>A single row in the properties panel.</summary>
public record PropertyItem(string Name, string Value, string Unit = "");

/// <summary>
/// Presents the current selection. It is rebuilt from the domain model whenever the selection or the
/// design changes, so it never holds a second copy of the design state. The editable fields are
/// just text the user is typing; Apply hands the parsed values to the host, which runs an undoable
/// command. The model is only changed if the command's validation passes.
/// </summary>
public class PropertiesViewModel : ViewModelBase
{
    public PropertiesViewModel()
    {
        ApplyFrameSizeCommand = new RelayCommand(ApplyFrameSize, () => IsFrameEditable);
        ApplyPositionCommand = new RelayCommand(ApplyPosition, () => IsDivisionEditable);
    }

    /// <summary>Host callback: resize the selected frame. Returns an error message, or null on success.</summary>
    public Func<double, double, string?>? ResizeFrame { get; set; }

    /// <summary>Host callback: move the selected division. Returns an error message, or null on success.</summary>
    public Func<double, string?>? MoveDivision { get; set; }

    /// <summary>Host callback: give the selection this library glass type. Returns an error message, or null.</summary>
    public Func<string, string?>? AssignGlass { get; set; }

    /// <summary>Host callback: make the selection from this library profile. Returns an error message, or null.</summary>
    public Func<string, string?>? AssignProfile { get; set; }

    /// <summary>Host callback: put the selected frame(s) in this system (null = none). Returns an error message, or null.</summary>
    public Func<string?, string?>? AssignSystem { get; set; }

    /// <summary>The system of the frame the selection is in (pickers offer what fits it), or null.</summary>
    private string? _systemId;

    /// <summary>The openings of what is selected: pickers offer only items for casement or sliding as they are.</summary>
    private IReadOnlyList<OpeningType> _openings = Array.Empty<OpeningType>();

    /// <summary>Stores new design information for the selected frame; returns an error message or null.</summary>
    public Func<DesignInfo, string?>? SetDesignInfo { get; set; }

    /// <summary>Sets the opening type (null = keep) and mesh (null = keep) of panels; returns an error message or null.</summary>
    public Func<IReadOnlyList<GlassPanel>, OpeningType?, bool?, string?>? SetOpening { get; set; }

    private DesignInfoEditorViewModel? _designEditor;
    /// <summary>Design reference, quantity, location… of the selected frame, or null.</summary>
    public DesignInfoEditorViewModel? DesignEditor
    {
        get => _designEditor;
        private set => SetProperty(ref _designEditor, value);
    }

    private OpeningEditorViewModel? _openingEditor;
    /// <summary>Opening type and mesh of the openings the selection stands for, or null.</summary>
    public OpeningEditorViewModel? OpeningEditor
    {
        get => _openingEditor;
        private set => SetProperty(ref _openingEditor, value);
    }

    /// <summary>The product library the pickers offer.</summary>
    public IProductLibrary Library { get; set; } = ProductLibrary.Empty;

    /// <summary>Host callback: the calculation of the current design (kept up to date by the host).</summary>
    public Func<CalculationResult>? CalculationSource { get; set; }

    private string _header = "NO SELECTION";
    public string Header
    {
        get => _header;
        private set => SetProperty(ref _header, value);
    }

    /// <summary>Read-only information rows.</summary>
    public ObservableCollection<PropertyItem> Items { get; } = new();

    // ── Frame editing ───────────────────────────────────────────────

    private bool _isFrameEditable;
    public bool IsFrameEditable
    {
        get => _isFrameEditable;
        private set => SetProperty(ref _isFrameEditable, value);
    }

    private string _widthText = "";
    public string WidthText
    {
        get => _widthText;
        set => SetProperty(ref _widthText, value);
    }

    private string _heightText = "";
    public string HeightText
    {
        get => _heightText;
        set => SetProperty(ref _heightText, value);
    }

    public ICommand ApplyFrameSizeCommand { get; }

    // ── Division editing ────────────────────────────────────────────

    private bool _isDivisionEditable;
    public bool IsDivisionEditable
    {
        get => _isDivisionEditable;
        private set => SetProperty(ref _isDivisionEditable, value);
    }

    private string _positionText = "";
    public string PositionText
    {
        get => _positionText;
        set => SetProperty(ref _positionText, value);
    }

    private string _positionLabel = "Position";
    /// <summary>"From left" for a mullion, "From top" for a transom.</summary>
    public string PositionLabel
    {
        get => _positionLabel;
        private set => SetProperty(ref _positionLabel, value);
    }

    public ICommand ApplyPositionCommand { get; }

    // ── Library assignments ─────────────────────────────────────────

    private LibraryPickerViewModel? _profilePicker;
    /// <summary>Profile selection for the selected division or frame; null when not applicable.</summary>
    public LibraryPickerViewModel? ProfilePicker
    {
        get => _profilePicker;
        private set => SetProperty(ref _profilePicker, value);
    }

    private LibraryPickerViewModel? _systemPicker;
    /// <summary>The product system of the selected frame(s); null when not applicable.</summary>
    public LibraryPickerViewModel? SystemPicker
    {
        get => _systemPicker;
        private set => SetProperty(ref _systemPicker, value);
    }

    private LibraryPickerViewModel? _glassPicker;
    /// <summary>Glass selection for the selected glass panel, or for all panels of the selected frame.</summary>
    public LibraryPickerViewModel? GlassPicker
    {
        get => _glassPicker;
        private set => SetProperty(ref _glassPicker, value);
    }

    // ── Calculation ─────────────────────────────────────────────────

    /// <summary>Calculated values for the selection (manufacturing size, cut length, weight, cost).</summary>
    public ObservableCollection<PropertyItem> CalculationItems { get; } = new();

    private bool _hasCalculation;
    public bool HasCalculation
    {
        get => _hasCalculation;
        private set => SetProperty(ref _hasCalculation, value);
    }

    private string? _calculationMessage;
    /// <summary>The first calculation issue concerning the selection (e.g. a product missing from the library).</summary>
    public string? CalculationMessage
    {
        get => _calculationMessage;
        private set
        {
            if (SetProperty(ref _calculationMessage, value))
                OnPropertyChanged(nameof(HasCalculationMessage));
        }
    }

    public bool HasCalculationMessage => !string.IsNullOrEmpty(_calculationMessage);

    // ── Messages ────────────────────────────────────────────────────

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

    // ── Display ─────────────────────────────────────────────────────

    public void ShowNothing()
    {
        Reset("NO SELECTION");
    }

    /// <summary>
    /// Several objects selected (Shift+click, box selection, Ctrl+A). Offers the glass and profile pickers for
    /// everything the selection stands for (a frame stands for its panes and its outer members), so a material can
    /// be changed for many objects in one step, and shows their glass area and summed cost.
    /// </summary>
    /// <param name="objects">The selected frames, profiles and glass panels, in project order.</param>
    public void ShowMultiple(int count, IReadOnlyList<object>? objects = null)
    {
        Reset($"{count} OBJECTS SELECTED");
        if (objects is null || objects.Count == 0) return;

        var frames = objects.OfType<Frame>().ToList();
        var profiles = objects.OfType<Profile>().ToList();
        var panels = objects.OfType<GlassPanel>().ToList();
        AddCount("Frames", frames.Count);
        AddCount("Mullions", profiles.Count(p => p.ProfileType == ProfileType.Mullion));
        AddCount("Transoms", profiles.Count(p => p.ProfileType == ProfileType.Transom));
        AddCount("Glass panels", panels.Count);

        // What the selection stands for: selected frames contribute their panes and outer members.
        var panes = panels.Concat(frames.SelectMany(f => f.GlassPanels)).Distinct().ToList();
        var members = profiles.Concat(frames.SelectMany(f => f.Profiles.Where(p => p.ProfileType == ProfileType.Frame)))
            .Distinct().ToList();
        var systems = frames.Select(f => f.SystemId).Distinct().ToList();
        _systemId = systems.Count == 1 ? systems[0] : null;
        _openings = panes.Select(p => p.Opening).ToList();
        if (frames.Count > 0 && Library.Systems.Count > 0)
            SystemPicker = CreateSystemPicker(frames);
        if (members.Count > 0)
            ProfilePicker = CreateProfilePicker(members.All(p => p.ProfileType == ProfileType.Frame) ? "Frame profile" : "Profile", members);
        if (panes.Count > 0)
        {
            GlassPicker = CreateGlassPicker(panes.Count == 1 ? "Glass" : "All glass", panes);
            OpeningEditor = CreateOpeningEditor(panes);
        }

        // Objects inside a selected frame are already counted in that frame's total.
        var inSelectedFrames = frames.SelectMany(f => f.Profiles.Select(p => p.Id).Concat(f.GlassPanels.Select(g => g.Id)))
            .ToHashSet();
        var ids = inSelectedFrames.Concat(frames.Select(f => f.Id)).Concat(profiles.Select(p => p.Id))
            .Concat(panels.Select(g => g.Id)).ToHashSet();
        ShowCalculation(ids, result =>
        {
            if (panes.Count > 0)
            {
                double area = panes.Sum(p => result.FindGlass(p.Id)?.AreaM2 ?? 0);
                CalculationItems.Add(new PropertyItem("Glass area", area.ToString("0.###", CultureInfo.InvariantCulture), "m²"));
            }
            decimal cost = frames.Sum(f => result.FindFrame(f.Id)?.Cost.Total ?? 0)
                           + profiles.Where(p => !inSelectedFrames.Contains(p.Id)).Sum(p => result.FindProfile(p.Id)?.Cost ?? 0)
                           + panels.Where(g => !inSelectedFrames.Contains(g.Id)).Sum(g => result.FindGlass(g.Id)?.Cost ?? 0);
            AddCost("Cost", cost, result.Currency);
        });
    }

    private void AddCount(string name, int count)
    {
        if (count > 0)
            Items.Add(new PropertyItem(name, count.ToString(CultureInfo.InvariantCulture)));
    }

    public void ShowFrame(Frame frame)
    {
        Reset("FRAME");
        _systemId = frame.SystemId;
        _openings = frame.GlassPanels.Select(p => p.Opening).ToList();
        IsFrameEditable = true;
        WidthText = Format(frame.Width);
        HeightText = Format(frame.Height);

        if (frame.SystemId is { } systemId)
            Items.Add(new PropertyItem("System", Library.FindSystem(systemId)?.Name ?? $"Missing: {systemId}"));
        Items.Add(new PropertyItem("X", Format(frame.X), "mm"));
        Items.Add(new PropertyItem("Y", Format(frame.Y), "mm"));
        if (frame.Profiles.FirstOrDefault(p => p.ProfileType == ProfileType.Frame) is { } outer)
            Items.Add(new PropertyItem("Profile", Format(outer.Thickness), "mm"));
        Items.Add(new PropertyItem("Mullions", frame.Profiles.Count(p => p.ProfileType == ProfileType.Mullion).ToString(CultureInfo.InvariantCulture)));
        Items.Add(new PropertyItem("Transoms", frame.Profiles.Count(p => p.ProfileType == ProfileType.Transom).ToString(CultureInfo.InvariantCulture)));
        Items.Add(new PropertyItem("Glass panels", frame.GlassPanels.Count.ToString(CultureInfo.InvariantCulture)));

        if (Library.Systems.Count > 0)
            SystemPicker = CreateSystemPicker(new[] { frame });
        DesignEditor = new DesignInfoEditorViewModel(frame.Design,
            info => SetDesignInfo is { } set ? set(info) : "The design details cannot be changed here.");
        if (frame.GlassPanels.Count > 0)
            OpeningEditor = CreateOpeningEditor(frame.GlassPanels);

        var outerMembers = frame.Profiles.Where(p => p.ProfileType == ProfileType.Frame).ToList();
        if (outerMembers.Count > 0)
            ProfilePicker = CreateProfilePicker("Frame profile", outerMembers);
        if (frame.GlassPanels.Count > 0)
            GlassPicker = CreateGlassPicker(frame.GlassPanels.Count == 1 ? "Glass" : "All glass", frame.GlassPanels);

        var ids = frame.Profiles.Select(p => p.Id).Concat(frame.GlassPanels.Select(g => g.Id)).Append(frame.Id).ToHashSet();
        ShowCalculation(ids, result =>
        {
            if (result.FindFrame(frame.Id) is not { } f) return;
            AddCost("Profiles", f.Cost.Profiles, result.Currency);
            AddCost("Glass", f.Cost.Glass, result.Currency);
            AddCost("Materials", f.Cost.Materials, result.Currency);
            AddCost("Frame total", f.Cost.Total, result.Currency);
            CalculationItems.Add(new PropertyItem("Weight", Format(f.WeightKg), "kg"));
        });
    }

    /// <param name="systemId">The system of the frame the member is in (the picker offers what fits it).</param>
    /// <param name="openings">The openings of the profile's frame: only profiles for casement or sliding as they are are offered.</param>
    public void ShowProfile(Profile profile, string? systemId = null, IReadOnlyList<OpeningType>? openings = null)
    {
        bool isDivision = Members.IsDivision(profile);
        Reset(profile.ProfileType.ToString().ToUpperInvariant());
        _systemId = systemId;
        _openings = openings ?? Array.Empty<OpeningType>();

        if (isDivision)
        {
            IsDivisionEditable = true;
            PositionLabel = profile.ProfileType == ProfileType.Mullion ? "From left" : "From top";
            PositionText = Format(Members.DivisionPosition(profile));
        }
        else
        {
            Items.Add(new PropertyItem("Start X", Format(profile.StartPoint.X), "mm"));
            Items.Add(new PropertyItem("Start Y", Format(profile.StartPoint.Y), "mm"));
            Items.Add(new PropertyItem("End X", Format(profile.EndPoint.X), "mm"));
            Items.Add(new PropertyItem("End Y", Format(profile.EndPoint.Y), "mm"));
        }

        Items.Add(new PropertyItem("Length", Format(profile.Length), "mm"));
        Items.Add(new PropertyItem("Thickness", Format(profile.Thickness), "mm"));
        Items.Add(new PropertyItem("Angle", Format(profile.Angle), "°"));

        ProfilePicker = CreateProfilePicker("Profile", new[] { profile });
        ShowCalculation(new HashSet<Guid> { profile.Id }, result =>
        {
            if (result.FindProfile(profile.Id) is not { } line) return;
            CalculationItems.Add(new PropertyItem("Cut length", Format(line.CutLengthMm), "mm"));
            CalculationItems.Add(new PropertyItem("Cuts", $"{Format(line.StartCutAngle)}° / {Format(line.EndCutAngle)}°"));
            if (!line.IsResolved) return;
            CalculationItems.Add(new PropertyItem("Weight", Format(line.WeightKg), "kg"));
            AddCost("Cost", line.Cost, result.Currency);
        });
    }

    /// <param name="systemId">The system of the frame the panel is in (the picker offers glass that fits it).</param>
    public void ShowGlass(GlassPanel panel, string? systemId = null)
    {
        Reset("GLASS");
        _systemId = systemId;
        _openings = new[] { panel.Opening };
        Items.Add(new PropertyItem("Width", Format(panel.Boundary.Width), "mm"));
        Items.Add(new PropertyItem("Height", Format(panel.Boundary.Height), "mm"));
        Items.Add(new PropertyItem("Area", (panel.Boundary.Area / 1_000_000.0).ToString("0.###", CultureInfo.InvariantCulture), "m²"));
        Items.Add(new PropertyItem("Thickness", Format(panel.Thickness), "mm"));
        Items.Add(new PropertyItem("Opening", panel.Opening.DisplayName()));
        if (panel.HasMesh)
            Items.Add(new PropertyItem("Mesh", "Insect mesh shutter"));

        OpeningEditor = CreateOpeningEditor(new[] { panel });
        GlassPicker = CreateGlassPicker("Glass", new[] { panel });
        ShowCalculation(new HashSet<Guid> { panel.Id }, result =>
        {
            if (result.FindGlass(panel.Id) is not { } line) return;
            CalculationItems.Add(new PropertyItem("Glass size", $"{Format(line.WidthMm)} × {Format(line.HeightMm)}", "mm"));
            CalculationItems.Add(new PropertyItem("Glass area", line.AreaM2.ToString("0.###", CultureInfo.InvariantCulture), "m²"));
            if (!line.IsResolved) return;
            if (line.ChargeableAreaM2 > line.AreaM2)
                CalculationItems.Add(new PropertyItem("Charged as", line.ChargeableAreaM2.ToString("0.###", CultureInfo.InvariantCulture), "m²"));
            if (line.WeightKg is { } weight)
                CalculationItems.Add(new PropertyItem("Weight", Format(weight), "kg"));
            AddCost("Cost", line.Cost, result.Currency);
        });
    }

    // ── Library pickers ─────────────────────────────────────────────

    private OpeningEditorViewModel CreateOpeningEditor(IReadOnlyList<GlassPanel> panels)
        => new(panels.Count == 1 ? "Opening" : $"Openings ({panels.Count})", panels,
            (type, mesh) => SetOpening is { } set ? set(panels, type, mesh) : "The opening cannot be changed here.");

    private LibraryPickerViewModel CreateGlassPicker(string label, IReadOnlyList<GlassPanel> panels)
    {
        var ids = panels.Select(p => p.GlassDefinitionId).Distinct().ToList();
        string? currentId = ids.Count == 1 ? ids[0] : null;
        string currentText = ids.Count > 1 ? "Mixed" : DescribeGlass(currentId);
        var system = Library.FindSystem(_systemId);
        return new LibraryPickerViewModel(label, currentText, currentId,
            text => Library.SearchGlass(new LibraryQuery(text))
                .Where(g => (g.UsedWith?.FitsSystem(_systemId) ?? true) && (system?.AcceptsGlass(g.ThicknessMm) ?? true)
                            && (g.UsedWith?.FitsOpenings(_openings) ?? true))
                .Select(g => new LibraryOption(g.Id, g.Name,
                    Join($"{Format(g.ThicknessMm)} mm", g.Category, $"{Money(g.CostPerSquareMetre)}/m²"))).ToList(),
            id => AssignGlass is { } assign ? assign(id) : "The glass cannot be changed here.");
    }

    /// <summary>The product systems (and "No system"); choosing one puts the frames in it.</summary>
    private LibraryPickerViewModel CreateSystemPicker(IReadOnlyList<Frame> frames)
    {
        var ids = frames.Select(f => f.SystemId).Distinct().ToList();
        string? currentId = ids.Count == 1 ? ids[0] ?? NoSystem : null;
        string currentText = ids.Count > 1 ? "Mixed"
            : ids[0] is { } id ? Library.FindSystem(id)?.Name ?? $"Missing: {id}" : "No system";
        return new LibraryPickerViewModel("System", currentText, currentId,
            text =>
            {
                var words = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var options = Library.Systems.Where(x => x.IsActive && words.All(w => x.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    .Select(x => new LibraryOption(x.Id, x.Name, Join(x.Material == SystemMaterial.Upvc ? "uPVC" : "Aluminium",
                        x.Use == ProductUse.WindowAndDoor ? "windows and doors" : x.Use.ToString().ToLowerInvariant() + "s",
                        x.GlassRangeText.Length > 0 ? $"glass {x.GlassRangeText}" : null)))
                    .ToList();
                options.Add(new LibraryOption(NoSystem, "No system", "Library defaults"));
                return options;
            },
            id => AssignSystem is { } assign ? assign(id == NoSystem ? null : id) : "The system cannot be changed here.");
    }

    private const string NoSystem = "(none)";

    /// <summary>Offers only sections usable in every role among <paramref name="profiles"/> (e.g. mullion and transom).</summary>
    private LibraryPickerViewModel CreateProfilePicker(string label, IReadOnlyList<Profile> profiles)
    {
        var roles = profiles.Select(p => p.ProfileType).Distinct().ToList();
        var ids = profiles.Select(p => p.ProfileDefinitionId).Distinct().ToList();
        string? currentId = ids.Count == 1 ? ids[0] : null;
        string currentText = ids.Count > 1 ? "Mixed" : DescribeProfile(currentId, roles);
        return new LibraryPickerViewModel(label, currentText, currentId,
            text => Library.SearchProfiles(new LibraryQuery(text, roles.Count == 1 ? roles[0] : null))
                .Where(p => roles.All(p.Supports) && (p.UsedWith?.FitsSystem(_systemId) ?? true)
                            && (p.UsedWith?.FitsOpenings(_openings) ?? true))
                .Select(p => new LibraryOption(p.Id, p.Name,
                    Join($"{Format(p.FaceWidthMm)} mm", p.Series, $"{Money(p.CostPerMetre)}/m"))).ToList(),
            id => AssignProfile is { } assign ? assign(id) : "The profile cannot be changed here.");
    }

    private string DescribeGlass(string? id) => id is null
        ? (Library.FindGlass(Library.FindSystem(_systemId)?.GlassId) ?? Library.DefaultGlass) is { } d ? $"{d.Name} (default)" : "None assigned"
        : Library.FindGlass(id)?.Name ?? $"Missing: {id}";

    /// <summary>A null id means each member uses the library default for its role.</summary>
    private string DescribeProfile(string? id, IReadOnlyList<ProfileType> roles)
    {
        if (id is not null)
            return Library.FindProfile(id)?.Name ?? $"Missing: {id}";
        var system = Library.FindSystem(_systemId);
        var defaults = roles.Select(r => Library.FindProfile(system?.ProfileIdFor(r)) ?? Library.DefaultProfileFor(r)).Distinct().ToList();
        return defaults switch
        {
            [{ } d] => $"{d.Name} (default)",
            _ when defaults.All(d => d is null) => "None assigned",
            _ => "Library defaults"
        };
    }

    // ── Calculation ─────────────────────────────────────────────────

    /// <summary>Fills <see cref="CalculationItems"/> and the first issue about <paramref name="objectIds"/>.</summary>
    private void ShowCalculation(IReadOnlySet<Guid> objectIds, Action<CalculationResult> fill)
    {
        if (CalculationSource?.Invoke() is not { } result) return;
        fill(result);
        HasCalculation = CalculationItems.Count > 0;
        CalculationMessage = result.Issues
            .Where(i => i.ObjectId is { } id && objectIds.Contains(id))
            .OrderByDescending(i => i.Severity)
            .Select(i => i.Message)
            .FirstOrDefault();
    }

    private void AddCost(string name, decimal value, string currency)
        => CalculationItems.Add(new PropertyItem(name, Money(value), currency));

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    // ── Apply ───────────────────────────────────────────────────────

    private void ApplyFrameSize()
    {
        if (!TryParse(WidthText, out double width) || !TryParse(HeightText, out double height))
        {
            ErrorMessage = "Enter the width and height as numbers in mm.";
            return;
        }
        ErrorMessage = ResizeFrame?.Invoke(width, height);
    }

    private void ApplyPosition()
    {
        if (!TryParse(PositionText, out double position))
        {
            ErrorMessage = "Enter the position as a number in mm.";
            return;
        }
        ErrorMessage = MoveDivision?.Invoke(position);
    }

    /// <summary>Accepts the user's culture ("600,5") and invariant ("600.5") formats.</summary>
    public static bool TryParse(string? text, out double value)
    {
        text = text?.Trim().Replace("mm", "", StringComparison.OrdinalIgnoreCase).Trim();
        return (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
               && double.IsFinite(value);
    }

    private void Reset(string header)
    {
        Header = header;
        Items.Clear();
        IsFrameEditable = false;
        IsDivisionEditable = false;
        ErrorMessage = null;
        ProfilePicker = null;
        GlassPicker = null;
        SystemPicker = null;
        _systemId = null;
        _openings = Array.Empty<OpeningType>();
        DesignEditor = null;
        OpeningEditor = null;
        CalculationItems.Clear();
        HasCalculation = false;
        CalculationMessage = null;
        ((RelayCommand)ApplyFrameSizeCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ApplyPositionCommand).RaiseCanExecuteChanged();
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
