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

    /// <summary>"Used with" from the ticked systems and opening types; null when nothing is ticked (= any).</summary>
    public static UsedWith? UsedWith(UsedWith? original, IEnumerable<CheckChoice<string>> systems,
        IEnumerable<CheckChoice<OpeningType>>? openings = null)
    {
        var systemIds = systems.Where(s => s.IsChecked).Select(s => s.Value).ToList();
        var openingTypes = openings?.Where(o => o.IsChecked).Select(o => o.Value).ToList() ?? original?.OpeningTypes.ToList() ?? new();
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

    public SystemEditorViewModel(ProductSystem system, IProductLibrary library, bool isNew = false)
    {
        _original = system;
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
        FrameProfile = Pick(FrameChoices, system.FrameProfileId);
        MullionProfile = Pick(MullionChoices, system.MullionProfileId);
        TransomProfile = Pick(TransomChoices, system.TransomProfileId);
        SashProfile = Pick(SashChoices, system.SashProfileId);
        MeshProfile = Pick(MeshChoices, system.MeshSashProfileId);
        Glass = Pick(GlassChoices, system.GlassId);
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

    public LibraryChoice FrameProfile { get; set; }
    public LibraryChoice MullionProfile { get; set; }
    public LibraryChoice TransomProfile { get; set; }
    public LibraryChoice SashProfile { get; set; }
    public LibraryChoice MeshProfile { get; set; }
    public LibraryChoice Glass { get; set; }

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
            GlassMaxThicknessMm = EditorText.Parse(GlassMax, "Thickest glass", errors)
        };
        error = errors.Count == 0 ? null : string.Join(" ", errors);
        return errors.Count == 0 ? system : null;
    }
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
