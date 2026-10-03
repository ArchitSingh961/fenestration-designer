using Mark.Core.Library;
using Mark.Designer.ViewModels;
using Mark.Licensing.Api;

namespace Mark.Owner.ViewModels;

/// <summary>A system or item with a tick box in the "what this company gets" list.</summary>
public sealed class CatalogueChoiceRow : ViewModelBase
{
    private readonly Action _changed;

    public CatalogueChoiceRow(string id, string name, string detail, bool isChecked, Action changed)
    {
        Id = id;
        Name = name;
        Detail = detail;
        _isChecked = isChecked;
        _changed = changed;
    }

    public string Id { get; }
    public string Name { get; }
    public string Detail { get; }

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked || IsCovered;
        set
        {
            if (IsCovered || !SetProperty(ref _isChecked, value)) return;
            _changed();
        }
    }

    /// <summary>The item comes with a ticked system anyway (shown ticked, cannot be unticked).</summary>
    private bool _isCovered;
    public bool IsCovered
    {
        get => _isCovered;
        set
        {
            if (!SetProperty(ref _isCovered, value)) return;
            OnPropertyChanged(nameof(IsChecked));
            OnPropertyChanged(nameof(CanChange));
            OnPropertyChanged(nameof(Note));
        }
    }

    public bool CanChange => !IsCovered;

    public string Note => IsCovered ? "with its system" : "";

    /// <summary>Ticked by the owner (not just covered by a system).</summary>
    public bool IsChosen => _isChecked;
}

public sealed record CatalogueChoiceGroup(string Title, IReadOnlyList<CatalogueChoiceRow> Rows);

/// <summary>
/// What a company (or a new account of a company type) gets from the master catalogue: tick whole systems (with
/// everything they use) and further single items. Items that come with a ticked system are shown ticked and fixed.
/// </summary>
public sealed class CatalogueChoiceViewModel : ViewModelBase
{
    private readonly ProductLibrary _master;
    private readonly List<CatalogueChoiceRow> _items;

    public CatalogueChoiceViewModel(ProductLibrary master, CompanyCatalogue? selection)
    {
        _master = master ?? throw new ArgumentNullException(nameof(master));
        var systems = (selection?.SystemIds ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        var items = (selection?.ItemIds ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);

        Systems = master.Systems.Select(x => new CatalogueChoiceRow(x.Id, x.Name,
            $"{EditorText.MaterialName(x.Material)} · {master.Bundles.Count(b => b.SystemId == x.Id)} bundles", systems.Contains(x.Id), Recount)).ToList();
        var profiles = master.Profiles.Select(p => new CatalogueChoiceRow(p.Id, p.Name, p.Series ?? "", items.Contains(p.Id), Recount)).ToList();
        var glass = master.Glass.Select(g => new CatalogueChoiceRow(g.Id, g.Name, g.Category ?? "", items.Contains(g.Id), Recount)).ToList();
        var materials = master.Materials.Select(m => new CatalogueChoiceRow(m.Id, m.Name, m.Category.ToString(), items.Contains(m.Id), Recount)).ToList();
        _items = profiles.Concat(glass).Concat(materials).ToList();
        ItemGroups = new[]
        {
            new CatalogueChoiceGroup("Profiles", profiles),
            new CatalogueChoiceGroup("Glass", glass),
            new CatalogueChoiceGroup("Hardware and accessories", materials)
        };
        Recount();
    }

    public IReadOnlyList<CatalogueChoiceRow> Systems { get; }

    public IReadOnlyList<CatalogueChoiceGroup> ItemGroups { get; }

    public bool HasSystems => Systems.Count > 0;

    /// <summary>"2 systems · 34 items".</summary>
    public string SummaryText { get; private set; } = "";

    /// <summary>What was ticked, for the server (items that come with a ticked system are not repeated).</summary>
    public CompanyCatalogue ToCatalogue()
        => new(Systems.Where(s => s.IsChosen).Select(s => s.Id).ToList(),
            _items.Where(i => i.IsChosen && !i.IsCovered).Select(i => i.Id).ToList());

    private void Recount()
    {
        if (_items is null) return;                               // during construction
        var chosen = ToCatalogue();
        var covered = CatalogueSelector.Select(_master, new CatalogueSelection { SystemIds = chosen.SystemIds });
        var coveredIds = covered.Profiles.Select(p => p.Id).Concat(covered.Glass.Select(g => g.Id)).Concat(covered.Materials.Select(m => m.Id))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var item in _items)
            item.IsCovered = coveredIds.Contains(item.Id);

        var all = CatalogueSelector.Select(_master, new CatalogueSelection { SystemIds = chosen.SystemIds, ItemIds = chosen.ItemIds });
        int itemCount = all.Profiles.Count + all.Glass.Count + all.Materials.Count;
        SummaryText = chosen.IsEmpty
            ? "Nothing ticked: the company keeps its own library."
            : $"{all.Systems.Count} system{(all.Systems.Count == 1 ? "" : "s")} · {all.Bundles.Count} bundles · {itemCount} items";
        OnPropertyChanged(nameof(SummaryText));
    }
}
