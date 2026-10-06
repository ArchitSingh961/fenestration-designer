using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Designer.ViewModels;

/// <summary>An edit form in the library manager: builds the definition it describes, or says what is wrong.</summary>
public interface ILibraryEditor
{
    bool IsNew { get; }

    string Id { get; }

    string Title { get; }

    /// <summary>The definition, or null with <paramref name="error"/>.</summary>
    object? Build(out string? error);
}

/// <summary>A choice in a drop-down: a library id (or null) with its name.</summary>
public sealed record LibraryChoice(string? Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A tick box for a system or an opening type (the "used with" lists).</summary>
public sealed class CheckChoice<T> : ViewModelBase
{
    public CheckChoice(T value, string name, bool isChecked)
    {
        Value = value;
        Name = name;
        _isChecked = isChecked;
    }

    public T Value { get; }

    public string Name { get; }

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}

/// <summary>Helpers shared by the library edit forms.</summary>
public static class EditorText
{
    public static string Number(double value, bool blankIfZero = false)
        => blankIfZero && value == 0 ? "" : value.ToString("0.###", CultureInfo.InvariantCulture);

    public static double Parse(string text, string field, List<string> errors, bool blankIsZero = true)
    {
        if (blankIsZero && string.IsNullOrWhiteSpace(text)) return 0;
        if (PropertiesViewModel.TryParse(text, out double value)) return value;
        errors.Add($"{field} must be a number.");
        return 0;
    }

    public static string? Optional(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>The systems of the library as tick boxes, ticked for <paramref name="selected"/>.</summary>
    public static ObservableCollection<CheckChoice<string>> SystemChoices(IProductLibrary library, IEnumerable<string>? selected)
    {
        var set = (selected ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        return new(library.Systems.Select(x => new CheckChoice<string>(x.Id, x.Name, set.Contains(x.Id))));
    }

    /// <summary>Every opening type as tick boxes, ticked for <paramref name="selected"/>.</summary>
    public static ObservableCollection<CheckChoice<OpeningType>> OpeningChoices(IEnumerable<OpeningType>? selected)
    {
        var set = (selected ?? Array.Empty<OpeningType>()).ToHashSet();
        return new(Enum.GetValues<OpeningType>().Select(o => new CheckChoice<OpeningType>(o, o.DisplayName(), set.Contains(o))));
    }

    /// <summary>"Used with" from the ticked systems and the opening types; null when nothing is marked (= any).</summary>
    public static UsedWith? UsedWith(UsedWith? original, IEnumerable<CheckChoice<string>> systems,
        IReadOnlyList<OpeningType>? openings = null)
    {
        var systemIds = systems.Where(s => s.IsChecked).Select(s => s.Value).ToList();
        var openingTypes = openings?.ToList() ?? original?.OpeningTypes.ToList() ?? new();
        if (systemIds.Count == 0 && openingTypes.Count == 0 && (original?.Use ?? ProductUse.WindowAndDoor) == ProductUse.WindowAndDoor)
            return null;
        return (original ?? new UsedWith()) with { SystemIds = systemIds, OpeningTypes = openingTypes };
    }

    public static string MaterialName(SystemMaterial material) => material == SystemMaterial.Upvc ? "uPVC" : "Aluminium";

    public static string UseName(ProductUse use) => use switch
    {
        ProductUse.Window => "Windows",
        ProductUse.Door => "Doors",
        _ => "Windows and doors"
    };
}

/// <summary>The edit form of a product system: material, use, default profiles and glass, and the glass it takes.</summary>
public sealed class SystemEditorViewModel : ViewModelBase, ILibraryEditor
{
    private readonly ProductSystem _original;
    private readonly IProductLibrary _library;

    public SystemEditorViewModel(ProductSystem system, IProductLibrary library, bool isNew = false)
    {
        _original = system;
        _library = library;
        IsNew = isNew;
        Id = system.Id;
        Name = system.Name;
        Material = system.Material;
        Use = system.Use;
        Description = system.Description ?? "";
        IsActive = system.IsActive;
        GlassMin = EditorText.Number(system.GlassMinThicknessMm, true);
        GlassMax = EditorText.Number(system.GlassMaxThicknessMm, true);

        List<LibraryChoice> ProfilesFor(ProfileType role)
            => library.Profiles.Where(p => p.Supports(role)).Select(p => new LibraryChoice(p.Id, p.Name)).Prepend(None).ToList();
        FrameChoices = ProfilesFor(ProfileType.Frame);
        MullionChoices = ProfilesFor(ProfileType.Mullion);
        TransomChoices = ProfilesFor(ProfileType.Transom);
        SashChoices = ProfilesFor(ProfileType.Sash);
        MeshChoices = ProfilesFor(ProfileType.MeshSash);
        GlassChoices = library.Glass.Select(g => new LibraryChoice(g.Id, $"{g.Name} ({EditorText.Number(g.ThicknessMm)} mm)")).Prepend(None).ToList();
        _frameProfile = Pick(FrameChoices, system.FrameProfileId);
        _mullionProfile = Pick(MullionChoices, system.MullionProfileId);
        _transomProfile = Pick(TransomChoices, system.TransomProfileId);
        _sashProfile = Pick(SashChoices, system.SashProfileId);
        _meshProfile = Pick(MeshChoices, system.MeshSashProfileId);
        _glass = Pick(GlassChoices, system.GlassId);

        // Everything else that can go with the system: profiles (not frames) and materials, ticked as they are.
        var ticked = system.Items.ToDictionary(i => i.ItemId, StringComparer.Ordinal);
        foreach (var p in library.Profiles.Where(p => !p.Supports(ProfileType.Frame) && (p.IsActive || ticked.ContainsKey(p.Id))))
            KitRows.Add(new KitItemRow(p.Id, p.Name, p.Code, "Profile · " + string.Join("/", p.Roles.Select(RoleName)), true,
                ticked.GetValueOrDefault(p.Id), library, KitChanged));
        foreach (var m in library.Materials.Where(m => m.IsActive || ticked.ContainsKey(m.Id)))
            KitRows.Add(new KitItemRow(m.Id, m.Name, m.Code, m.Category.ToString(), false, ticked.GetValueOrDefault(m.Id), library, KitChanged));
        FillFromFrameCommand = new RelayCommand(() => FillFromFrame(replace: false));
        ShowKitRows();
    }

    private static string RoleName(ProfileType role) => role switch
    {
        ProfileType.MeshSash => "mesh sash",
        ProfileType.GlazingBead => "bead",
        ProfileType.Generic => "other",
        _ => role.ToString().ToLowerInvariant()
    };

    // ── Items in the system: tick what goes together ────────────────

    /// <summary>Every item that can go with the system, ticked or not.</summary>
    public List<KitItemRow> KitRows { get; } = new();

    /// <summary>The rows shown (search, ticked only).</summary>
    public System.Collections.ObjectModel.ObservableCollection<KitItemRow> ShownKitRows { get; } = new();

    public static IReadOnlyList<KitUseChoice> KitUses => KitUseChoice.All;

    private string _kitSearch = "";
    public string KitSearch
    {
        get => _kitSearch;
        set
        {
            if (SetProperty(ref _kitSearch, value)) ShowKitRows();
        }
    }

    private bool _tickedOnly;
    public bool TickedOnly
    {
        get => _tickedOnly;
        set
        {
            if (SetProperty(ref _tickedOnly, value)) ShowKitRows();
        }
    }

    public string KitCountText
    {
        get
        {
            int n = KitRows.Count(r => r.IsTicked);
            return n == 0 ? "Nothing ticked yet: choose the frame, and its items are ticked for you."
                : $"{n} item{(n == 1 ? "" : "s")} ticked: they are added to every design in this system.";
        }
    }

    private string? _kitNote;
    /// <summary>What filling from the frame did.</summary>
    public string? KitNote { get => _kitNote; private set => SetProperty(ref _kitNote, value); }

    /// <summary>Ticks the items that belong with the frame (and sets the sash, mesh and divisions if none).</summary>
    public System.Windows.Input.ICommand FillFromFrameCommand { get; }

    // Ticks were changed by hand after the last fill: a new frame adds to them instead of replacing them.
    private bool _ticksByHand;
    private bool _filling;

    private void KitChanged()
    {
        if (!_filling) _ticksByHand = true;
        OnPropertyChanged(nameof(KitCountText));
        if (TickedOnly) ShowKitRows();
    }

    private void ShowKitRows()
    {
        var words = (KitSearch ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        ShownKitRows.Clear();
        foreach (var row in KitRows.Where(r => (!TickedOnly || r.IsTicked) && words.All(w => r.SearchText.Contains(w, StringComparison.OrdinalIgnoreCase)))
                     .OrderByDescending(r => r.IsTicked))
            ShownKitRows.Add(row);
        OnPropertyChanged(nameof(KitCountText));
    }

    /// <summary>
    /// The frame was chosen: tick the items of its series (or marked as used with this system) and fill the sash, mesh
    /// shutter, mullion and transom when they are not set. With <paramref name="replace"/> (and no ticks by hand), the
    /// items of the previous frame are unticked first.
    /// </summary>
    private void FillFromFrame(bool replace)
    {
        if (FrameProfile?.Id is not { } frameId || _library.FindProfile(frameId) is not { } frame)
        {
            KitNote = "Choose the frame first.";
            return;
        }
        var ids = SystemKit.ItemsWithFrame(frame, IsNew ? null : _original.Id, _library);
        _filling = true;
        try
        {
            if (replace && !_ticksByHand)
                foreach (var row in KitRows) row.IsTicked = false;

            LibraryChoice Fill(LibraryChoice current, IReadOnlyList<LibraryChoice> choices, ProfileType role)
                => current.Id is not null ? current
                    : choices.FirstOrDefault(c => c.Id is not null && ids.Contains(c.Id) && _library.FindProfile(c.Id)?.Supports(role) == true) ?? current;
            SashProfile = Fill(SashProfile, SashChoices, ProfileType.Sash);
            MeshProfile = Fill(MeshProfile, MeshChoices, ProfileType.MeshSash);
            MullionProfile = Fill(MullionProfile, MullionChoices, ProfileType.Mullion);
            TransomProfile = Fill(TransomProfile, TransomChoices, ProfileType.Transom);

            // The slots are not ticked again (they are already in every design), nor steel (it goes inside its profile).
            var slots = new[] { SashProfile.Id, MeshProfile.Id, MullionProfile.Id, TransomProfile.Id }.OfType<string>().ToHashSet();
            int added = 0;
            foreach (var row in KitRows.Where(r => ids.Contains(r.Id) && !slots.Contains(r.Id) && !r.IsTicked))
            {
                if (_library.FindProfile(row.Id) is { } p && p.Roles.All(r => r == ProfileType.Reinforcement)) continue;
                row.IsTicked = true;
                added++;
            }
            KitNote = ids.Count == 0
                ? $"Nothing in the library is marked as going with '{frame.Name}' (same series, or used with this system): tick the items yourself."
                : $"Ticked {added} item{(added == 1 ? "" : "s")} that go with '{frame.Name}'. Check where each goes, and untick what this system does not use.";
            _ticksByHand = false;
        }
        finally
        {
            _filling = false;
        }
        ShowKitRows();
    }

    private static readonly LibraryChoice None = new(null, "(none)");

    private static LibraryChoice Pick(IReadOnlyList<LibraryChoice> choices, string? id) => choices.FirstOrDefault(c => c.Id == id) ?? choices[0];

    public bool IsNew { get; }
    public string Title => IsNew ? "New system" : $"System: {Id}";

    public string Id { get; set; }
    public string Name { get; set; }
    public SystemMaterial Material { get; set; }
    public ProductUse Use { get; set; }
    public string Description { get; set; }
    public bool IsActive { get; set; }
    public string GlassMin { get; set; }
    public string GlassMax { get; set; }

    public static IReadOnlyList<SystemMaterial> Materials { get; } = Enum.GetValues<SystemMaterial>();
    public static IReadOnlyList<ProductUse> Uses { get; } = Enum.GetValues<ProductUse>();

    public IReadOnlyList<LibraryChoice> FrameChoices { get; }
    public IReadOnlyList<LibraryChoice> MullionChoices { get; }
    public IReadOnlyList<LibraryChoice> TransomChoices { get; }
    public IReadOnlyList<LibraryChoice> SashChoices { get; }
    public IReadOnlyList<LibraryChoice> MeshChoices { get; }
    public IReadOnlyList<LibraryChoice> GlassChoices { get; }

    private LibraryChoice _frameProfile, _mullionProfile, _transomProfile, _sashProfile, _meshProfile, _glass;

    /// <summary>The frame; choosing it fills in the rest (sash, mesh, divisions and the items that go with it).</summary>
    public LibraryChoice FrameProfile
    {
        get => _frameProfile;
        set
        {
            if (SetProperty(ref _frameProfile, value) && value?.Id is not null) FillFromFrame(replace: true);
        }
    }

    public LibraryChoice MullionProfile { get => _mullionProfile; set => SetProperty(ref _mullionProfile, value); }
    public LibraryChoice TransomProfile { get => _transomProfile; set => SetProperty(ref _transomProfile, value); }
    public LibraryChoice SashProfile { get => _sashProfile; set => SetProperty(ref _sashProfile, value); }
    public LibraryChoice MeshProfile { get => _meshProfile; set => SetProperty(ref _meshProfile, value); }
    public LibraryChoice Glass { get => _glass; set => SetProperty(ref _glass, value); }

    public object? Build(out string? error)
    {
        var errors = new List<string>();
        var system = _original with
        {
            Id = Id.Trim(), Name = Name.Trim(), Material = Material, Use = Use, Description = EditorText.Optional(Description),
            IsActive = IsActive,
            FrameProfileId = FrameProfile?.Id, MullionProfileId = MullionProfile?.Id, TransomProfileId = TransomProfile?.Id,
            SashProfileId = SashProfile?.Id, MeshSashProfileId = MeshProfile?.Id, GlassId = Glass?.Id,
            GlassMinThicknessMm = EditorText.Parse(GlassMin, "Thinnest glass", errors),
            GlassMaxThicknessMm = EditorText.Parse(GlassMax, "Thickest glass", errors),
            Items = KitRows.Where(r => r.IsTicked).Select(r => r.ToItem(errors)).ToList()
        };
        error = errors.Count == 0 ? null : string.Join(" ", errors);
        return errors.Count == 0 ? system : null;
    }
}

/// <summary>A place an item ticked in a system can go, as offered in the drop-down.</summary>
public sealed record KitUseChoice(KitUse Use, string Name)
{
    public static IReadOnlyList<KitUseChoice> All { get; } = new[]
    {
        new KitUseChoice(KitUse.EachSlidingSash, "each sliding sash"),
        new KitUseChoice(KitUse.EachOpenableSash, "each opening sash"),
        new KitUseChoice(KitUse.HingesBySize, "hinges by sash height (2 / 3)"),
        new KitUseChoice(KitUse.SashCorners, "sash corners (4 per sash)"),
        new KitUseChoice(KitUse.EachSashBar, "each sash bar"),
        new KitUseChoice(KitUse.EachMeshBar, "each mesh shutter bar"),
        new KitUseChoice(KitUse.EachFrameBar, "each frame bar"),
        new KitUseChoice(KitUse.BottomFrameBar, "bottom frame bar"),
        new KitUseChoice(KitUse.TopAndBottomFrameBars, "top and bottom frame bars"),
        new KitUseChoice(KitUse.PerMetreOfFrame, "per metre of frame"),
        new KitUseChoice(KitUse.PerMetreOfSash, "per metre of sash"),
        new KitUseChoice(KitUse.EachWindow, "once per window")
    };
}

/// <summary>An item in a system's tick list: ticked or not, where it goes and how many.</summary>
public sealed class KitItemRow : ViewModelBase
{
    private readonly IProductLibrary _library;
    private readonly Action _changed;

    public KitItemRow(string id, string name, string? code, string kind, bool isProfile, KitItem? ticked, IProductLibrary library, Action changed)
    {
        Id = id;
        Name = name;
        Code = string.IsNullOrWhiteSpace(code) ? id : code!;
        Kind = kind;
        IsProfile = isProfile;
        _library = library;
        _changed = changed;
        _isTicked = ticked is not null;
        var item = ticked ?? SystemKit.DefaultFor(id, library);
        _use = KitUseChoice.All.First(c => c.Use == item.Use);
        _quantityText = EditorText.Number(item.Quantity);
        _lengthText = EditorText.Number(item.LengthMm, true);
    }

    public string Id { get; }
    public string Name { get; }
    public string Code { get; }
    public string Kind { get; }
    public bool IsProfile { get; }
    public string SearchText => $"{Code} {Name} {Kind}";

    private bool _isTicked;
    public bool IsTicked
    {
        get => _isTicked;
        set
        {
            if (!SetProperty(ref _isTicked, value)) return;
            if (value)
            {
                // Freshly ticked: where it usually goes.
                var item = SystemKit.DefaultFor(Id, _library);
                Use = KitUseChoice.All.First(c => c.Use == item.Use);
                QuantityText = EditorText.Number(item.Quantity);
            }
            _changed();
        }
    }

    private KitUseChoice _use;
    public KitUseChoice Use { get => _use; set => SetProperty(ref _use, value); }

    private string _quantityText;
    public string QuantityText { get => _quantityText; set => SetProperty(ref _quantityText, value); }

    private string _lengthText;
    /// <summary>Profiles: a fixed cut length (empty = cut to what it goes with).</summary>
    public string LengthText { get => _lengthText; set => SetProperty(ref _lengthText, value); }

    public KitItem ToItem(List<string> errors) => new()
    {
        ItemId = Id,
        Use = Use.Use,
        Quantity = EditorText.Parse(QuantityText, $"How many of '{Name}'", errors),
        LengthMm = IsProfile ? EditorText.Parse(LengthText, $"The length of '{Name}'", errors) : 0
    };
}

/// <summary>One part line of a bundle in its edit form.</summary>
public sealed class BundlePartRow : ViewModelBase
{
    public BundlePartRow(IReadOnlyList<LibraryChoice> items, BundlePart part)
    {
        Items = items;
        _item = items.FirstOrDefault(i => i.Id == part.ItemId) ?? new LibraryChoice(part.ItemId, $"(missing: {part.ItemId})");
        Basis = part.Basis;
        QuantityText = EditorText.Number(part.Quantity);
        Measure = part.Measure;
        Side = part.Side;
        DeductionText = EditorText.Number(part.CutDeductionMm, true);
        StepsText = string.Join(", ", part.Steps.Select(s => $"{EditorText.Number(s.UpToMm)}:{EditorText.Number(s.Quantity)}"));
    }

    public IReadOnlyList<LibraryChoice> Items { get; }

    private LibraryChoice? _item;
    public LibraryChoice? Item
    {
        get => _item;
        set => SetProperty(ref _item, value);
    }

    private PartBasis _basis;
    public PartBasis Basis
    {
        get => _basis;
        set
        {
            if (SetProperty(ref _basis, value))
                OnPropertyChanged(nameof(IsBySize));
        }
    }

    public bool IsBySize => Basis == PartBasis.BySize;

    public string QuantityText { get; set; }
    public SizeMeasure Measure { get; set; }
    public BarSide Side { get; set; }
    public string DeductionText { get; set; }

    /// <summary>"1200:2, 9999:3" — up to 1200 mm: 2, above: 3.</summary>
    public string StepsText { get; set; }

    public static IReadOnlyList<PartBasis> Bases { get; } = Enum.GetValues<PartBasis>();
    public static IReadOnlyList<SizeMeasure> Measures { get; } = Enum.GetValues<SizeMeasure>();
    public static IReadOnlyList<BarSide> Sides { get; } = Enum.GetValues<BarSide>();

    public BundlePart? Build(List<string> errors)
    {
        if (Item?.Id is not { } itemId)
        {
            errors.Add("Choose the item of every part.");
            return null;
        }
        var steps = new List<SizeStep>();
        if (Basis == PartBasis.BySize)
        {
            foreach (var pair in StepsText.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var bits = pair.Split(':');
                if (bits.Length != 2 || !PropertiesViewModel.TryParse(bits[0], out double upTo) || !PropertiesViewModel.TryParse(bits[1], out double qty))
                {
                    errors.Add($"Sizes of '{Item.Name}' must be like 1200:2, 9999:3 (up to 1200 mm: 2, up to 9999 mm: 3).");
                    return null;
                }
                steps.Add(new SizeStep { UpToMm = upTo, Quantity = qty });
            }
        }
        return new BundlePart
        {
            ItemId = itemId,
            Basis = Basis,
            Quantity = Basis == PartBasis.BySize ? 0 : EditorText.Parse(QuantityText, $"Quantity of '{Item.Name}'", errors, blankIsZero: false),
            Measure = Measure,
            Side = Side,
            CutDeductionMm = EditorText.Parse(DeductionText, $"Cut deduction of '{Item.Name}'", errors),
            Steps = steps
        };
    }
}

/// <summary>
/// The edit form of a bundle: its system, what it goes with (a profile's bars, or openings of some types) and its parts,
/// each with how many go with each bar or opening.
/// </summary>
public sealed class BundleEditorViewModel : ViewModelBase, ILibraryEditor
{
    private readonly Bundle _original;
    private readonly IReadOnlyList<LibraryChoice> _items;

    public BundleEditorViewModel(Bundle bundle, IProductLibrary library, bool isNew = false)
    {
        _original = bundle;
        IsNew = isNew;
        Id = bundle.Id;
        Name = bundle.Name;
        Description = bundle.Description ?? "";
        IsActive = bundle.IsActive;
        SystemChoices = library.Systems.Select(x => new LibraryChoice(x.Id, x.Name)).Prepend(new LibraryChoice(null, "Any system")).ToList();
        System = SystemChoices.FirstOrDefault(c => c.Id == bundle.SystemId) ?? SystemChoices[0];
        ProfileChoices = library.Profiles.Select(p => new LibraryChoice(p.Id, p.Name)).ToList();
        _goesWithProfile = !bundle.IsOpeningSet || isNew;
        Profile = ProfileChoices.FirstOrDefault(c => c.Id == bundle.ProfileId);
        OpeningTypes = EditorText.OpeningChoices(bundle.OpeningTypes);
        _items = library.Profiles.Select(p => new LibraryChoice(p.Id, $"{p.Name} (profile)"))
            .Concat(library.Materials.Select(m => new LibraryChoice(m.Id, m.Name))).ToList();
        Parts = new ObservableCollection<BundlePartRow>(bundle.Parts.Select(p => new BundlePartRow(_items, p)));
        AddPartCommand = new RelayCommand(() => Parts.Add(new BundlePartRow(_items, new BundlePart { ItemId = "" })));
        RemovePartCommand = new RelayCommand(p =>
        {
            if (p is BundlePartRow row) Parts.Remove(row);
        });
    }

    public bool IsNew { get; }
    public string Title => IsNew ? "New bundle" : $"Bundle: {Id}";

    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool IsActive { get; set; }

    public IReadOnlyList<LibraryChoice> SystemChoices { get; }
    public LibraryChoice System { get; set; }

    private bool _goesWithProfile;
    /// <summary>True: the parts go with every bar of <see cref="Profile"/>. False: with every opening of the ticked types.</summary>
    public bool GoesWithProfile
    {
        get => _goesWithProfile;
        set
        {
            if (SetProperty(ref _goesWithProfile, value))
                OnPropertyChanged(nameof(GoesWithOpenings));
        }
    }

    public bool GoesWithOpenings
    {
        get => !_goesWithProfile;
        set => GoesWithProfile = !value;
    }

    public IReadOnlyList<LibraryChoice> ProfileChoices { get; }
    public LibraryChoice? Profile { get; set; }

    public ObservableCollection<CheckChoice<OpeningType>> OpeningTypes { get; }

    public ObservableCollection<BundlePartRow> Parts { get; }

    public ICommand AddPartCommand { get; }
    public ICommand RemovePartCommand { get; }

    public object? Build(out string? error)
    {
        var errors = new List<string>();
        if (GoesWithProfile && Profile?.Id is null) errors.Add("Choose the profile the bundle goes with.");
        var parts = Parts.Select(p => p.Build(errors)).OfType<BundlePart>().ToList();
        var bundle = _original with
        {
            Id = Id.Trim(), Name = Name.Trim(), Description = EditorText.Optional(Description), IsActive = IsActive,
            SystemId = System?.Id,
            ProfileId = GoesWithProfile ? Profile?.Id : null,
            OpeningTypes = OpeningTypes.Where(o => o.IsChecked).Select(o => o.Value).ToList(),
            Parts = parts
        };
        error = errors.Count == 0 ? null : string.Join(" ", errors.Distinct());
        return errors.Count == 0 ? bundle : null;
    }
}
