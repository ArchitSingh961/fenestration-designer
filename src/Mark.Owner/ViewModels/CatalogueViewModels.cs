using System.Collections.ObjectModel;
using System.Windows.Input;
using Mark.Designer.ViewModels;
using Mark.Licensing;
using Mark.Licensing.Api;

namespace Mark.Owner.ViewModels;

/// <summary>A feature with a tick box in the package editor.</summary>
public sealed class FeatureChoiceRow : ViewModelBase
{
    public FeatureChoiceRow(Feature feature, bool isChecked)
    {
        Feature = feature;
        _isChecked = isChecked || feature.IsCore;
    }

    public Feature Feature { get; }

    public string Name => Feature.Name;

    public string Description => Feature.IsBuilt ? Feature.Description : $"{Feature.Description} (coming in a later version)";

    public bool CanChange => !Feature.IsCore;

    public string Tag => Feature.IsCore ? "Always included" : Feature.IsBuilt ? "" : "Later";

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value || Feature.IsCore);
    }
}

public sealed record FeatureChoiceGroup(string Area, IReadOnlyList<FeatureChoiceRow> Features);

/// <summary>
/// The Packages page: the named sets of features sold to companies (Basic, Professional, Complete…). Pick features by
/// ticking them; companies with the package get the change at their next check-in.
/// </summary>
public sealed class PackagesViewModel : OwnerPage
{
    private readonly Action<IReadOnlyList<PackageInfo>> _loaded;

    /// <param name="loaded">Told about the packages after every load (the account editor offers them).</param>
    public PackagesViewModel(OwnerApiClient api, IOwnerDialogs dialogs, Action sessionEnded, Action<IReadOnlyList<PackageInfo>> loaded)
        : base(api, dialogs, sessionEnded)
    {
        _loaded = loaded;
        NewCommand = new RelayCommand(() => Edit(null));
        SaveCommand = new AsyncCommand(SaveAsync, () => HasEditor);
        DeleteCommand = new AsyncCommand(DeleteAsync, () => _editing is { Id: var id } && id != Guid.Empty);
    }

    public ObservableCollection<PackageInfo> Packages { get; } = new();

    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }

    private PackageInfo? _selected;
    public PackageInfo? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
                Edit(value);
        }
    }

    private PackageInfo? _editing;
    private string _name = "";
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _description = "";
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public IReadOnlyList<FeatureChoiceGroup> FeatureGroups { get; private set; } = Array.Empty<FeatureChoiceGroup>();

    public bool HasEditor { get; private set; }

    public string EditorTitle => _editing is { Id: var id } && id != Guid.Empty ? _editing.Name : "New package";

    public string UsedByText => _editing is { UsedBy: > 0 } p ? $"Used by {p.UsedBy} compan{(p.UsedBy == 1 ? "y" : "ies")}" : "Not used by any company";

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var packages = await Api.PackagesAsync();
            Packages.Clear();
            foreach (var package in packages) Packages.Add(package);
            _loaded(packages);
        });
    }

    private void Edit(PackageInfo? package)
    {
        _editing = package ?? new PackageInfo(Guid.Empty, "", null, FeatureCatalog.CoreIds.ToList());
        Name = _editing.Name;
        Description = _editing.Description ?? "";
        var features = _editing.Features.ToHashSet();
        FeatureGroups = FeatureCatalog.Areas
            .Select(area => new FeatureChoiceGroup(area, FeatureCatalog.All.Where(f => f.Area == area)
                .Select(f => new FeatureChoiceRow(f, features.Contains(f.Id))).ToList()))
            .ToList();
        HasEditor = true;
        if (package is null)
        {
            _selected = null;
            OnPropertyChanged(nameof(Selected));
        }
        OnPropertyChanged(nameof(FeatureGroups));
        OnPropertyChanged(nameof(HasEditor));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(UsedByText));
        ((AsyncCommand)SaveCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)DeleteCommand).RaiseCanExecuteChanged();
        Show(null);
    }

    public async Task SaveAsync()
    {
        if (_editing is null) return;
        var package = new PackageInfo(_editing.Id, Name, Description,
            FeatureGroups.SelectMany(g => g.Features).Where(f => f.IsChecked).Select(f => f.Feature.Id).ToList());
        PackageInfo? saved = null;
        if (!await RunAsync(async () => saved = await Api.SavePackageAsync(package))) return;
        await LoadAsync();
        _selected = Packages.FirstOrDefault(p => p.Id == saved!.Id);
        OnPropertyChanged(nameof(Selected));
        Edit(_selected);
        Show($"Saved the package \"{saved!.Name}\". Companies with it get the change at their next check-in.");
    }

    private async Task DeleteAsync()
    {
        if (_editing is not { } package || package.Id == Guid.Empty) return;
        if (!Dialogs.Confirm("Delete package", $"Delete the package \"{package.Name}\"?")) return;
        if (!await RunAsync(() => Api.DeletePackageAsync(package.Id))) return;
        HasEditor = false;
        _editing = null;
        OnPropertyChanged(nameof(HasEditor));
        await LoadAsync();
        Show($"Deleted the package \"{package.Name}\".");
    }
}

/// <summary>A product with a tick box in the company type editor.</summary>
public sealed class ProductChoiceRow : ViewModelBase
{
    public ProductChoiceRow(Product product, bool isChecked)
    {
        Product = product;
        _isChecked = isChecked;
    }

    public Product Product { get; }

    public string Name => Product.DisplayName();

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}

/// <summary>
/// The Company types page: kinds of companies (uPVC fabricator, Aluminium fabricator, Trial…) with the products,
/// package and validity a new account of that kind starts with.
/// </summary>
public sealed class CompanyTypesViewModel : OwnerPage
{
    private readonly Func<IReadOnlyList<PackageInfo>> _packages;
    private readonly Action<IReadOnlyList<CompanyTypeInfo>> _loaded;

    public CompanyTypesViewModel(OwnerApiClient api, IOwnerDialogs dialogs, Action sessionEnded,
        Func<IReadOnlyList<PackageInfo>> packages, Action<IReadOnlyList<CompanyTypeInfo>> loaded)
        : base(api, dialogs, sessionEnded)
    {
        _packages = packages;
        _loaded = loaded;
        NewCommand = new RelayCommand(() => Edit(null));
        SaveCommand = new AsyncCommand(SaveAsync, () => HasEditor);
        DeleteCommand = new AsyncCommand(DeleteAsync, () => _editing is { Id: var id } && id != Guid.Empty);
    }

    public ObservableCollection<CompanyTypeRow> Types { get; } = new();

    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }

    private CompanyTypeRow? _selected;
    public CompanyTypeRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
                Edit(value.Type);
        }
    }

    private CompanyTypeInfo? _editing;

    private string _name = "";
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public IReadOnlyList<ProductChoiceRow> Products { get; private set; } = Array.Empty<ProductChoiceRow>();

    public IReadOnlyList<PackageInfo> Packages => _packages();

    private PackageInfo? _package;
    public PackageInfo? Package
    {
        get => _package;
        set => SetProperty(ref _package, value);
    }

    private string _validityDaysText = "365";
    public string ValidityDaysText
    {
        get => _validityDaysText;
        set => SetProperty(ref _validityDaysText, value);
    }

    public bool HasEditor { get; private set; }

    public string EditorTitle => _editing is { Id: var id } && id != Guid.Empty ? _editing.Name : "New company type";

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var types = await Api.CompanyTypesAsync();
            var packages = _packages();
            Types.Clear();
            foreach (var type in types) Types.Add(new CompanyTypeRow(type, packages));
            _loaded(types);
        });
    }

    private void Edit(CompanyTypeInfo? type)
    {
        _editing = type ?? new CompanyTypeInfo(Guid.Empty, "", new[] { Product.Upvc }, _packages().FirstOrDefault()?.Id, 365);
        Name = _editing.Name;
        Products = Licensing.Products.All.Select(p => new ProductChoiceRow(p, _editing.Products.Contains(p))).ToList();
        Package = _packages().FirstOrDefault(p => p.Id == _editing.PackageId);
        ValidityDaysText = _editing.ValidityDays.ToString();
        HasEditor = true;
        if (type is null)
        {
            _selected = null;
            OnPropertyChanged(nameof(Selected));
        }
        OnPropertyChanged(nameof(Products));
        OnPropertyChanged(nameof(Packages));
        OnPropertyChanged(nameof(HasEditor));
        OnPropertyChanged(nameof(EditorTitle));
        ((AsyncCommand)SaveCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)DeleteCommand).RaiseCanExecuteChanged();
        Show(null);
    }

    public async Task SaveAsync()
    {
        if (_editing is null) return;
        if (!int.TryParse(ValidityDaysText, out int days) || days < 1)
        {
            Show("Enter the number of days a new account is valid (e.g. 365, or 14 for a trial).", true);
            return;
        }
        var type = new CompanyTypeInfo(_editing.Id, Name, Products.Where(p => p.IsChecked).Select(p => p.Product).ToList(), Package?.Id, days);
        CompanyTypeInfo? saved = null;
        if (!await RunAsync(async () => saved = await Api.SaveCompanyTypeAsync(type))) return;
        await LoadAsync();
        _selected = Types.FirstOrDefault(t => t.Type.Id == saved!.Id);
        OnPropertyChanged(nameof(Selected));
        Edit(_selected?.Type);
        Show($"Saved the company type \"{saved!.Name}\".");
    }

    private async Task DeleteAsync()
    {
        if (_editing is not { } type || type.Id == Guid.Empty) return;
        if (!Dialogs.Confirm("Delete company type", $"Delete the company type \"{type.Name}\"? Companies of this type keep everything they have."))
            return;
        if (!await RunAsync(() => Api.DeleteCompanyTypeAsync(type.Id))) return;
        HasEditor = false;
        _editing = null;
        OnPropertyChanged(nameof(HasEditor));
        await LoadAsync();
        Show($"Deleted the company type \"{type.Name}\".");
    }
}

/// <summary>A company type in the list: "uPVC · Professional · 365 days · 4 companies".</summary>
public sealed class CompanyTypeRow
{
    public CompanyTypeRow(CompanyTypeInfo type, IReadOnlyList<PackageInfo> packages)
    {
        Type = type;
        string package = packages.FirstOrDefault(p => p.Id == type.PackageId)?.Name ?? "no package";
        Detail = $"{string.Join(" + ", type.Products.Select(p => p.DisplayName()))} · {package} · {type.ValidityDays} days · " +
                 $"{type.UsedBy} compan{(type.UsedBy == 1 ? "y" : "ies")}";
    }

    public CompanyTypeInfo Type { get; }

    public string Name => Type.Name;

    public string Detail { get; }
}
