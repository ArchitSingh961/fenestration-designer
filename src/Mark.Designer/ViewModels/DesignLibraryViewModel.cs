using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Designer.Rendering;

namespace Mark.Designer.ViewModels;

/// <summary>One design in the library panel: its picture, name and the command that applies it.</summary>
public sealed class DesignLibraryItem
{
    public DesignLibraryItem(DesignTemplate template, ImageSource? thumbnail, ICommand apply, ProductSystem? system = null)
    {
        Template = template;
        Thumbnail = thumbnail;
        ApplyCommand = apply;
        System = system;
    }

    public DesignTemplate Template { get; }

    /// <summary>The company's own system the design is made in, or null (the frame's system is kept).</summary>
    public ProductSystem? System { get; }

    /// <summary>What a drag carries: the template id, with "@system id" for a design in one of the company's systems.</summary>
    public string DragId => System is null ? Template.Id : $"{Template.Id}@{System.Id}";

    public string Name => System is null ? Template.Name : $"{Template.Name} · {System.Name}";

    public ImageSource? Thumbnail { get; }

    /// <summary>Applies the design to the selected opening or frame (click). Dragging onto an opening also works.</summary>
    public ICommand ApplyCommand { get; }
}

/// <summary>A heading in the library panel with its designs, e.g. "Tilt &amp; turn designs".</summary>
public sealed record DesignLibrarySection(string Title, IReadOnlyList<DesignLibraryItem> Items);

/// <summary>A button of the library rail.</summary>
/// <param name="Glyph">A Segoe MDL2 Assets / Fluent icon character.</param>
public sealed record DesignLibraryCategory(string Name, string Glyph, string ToolTip);

/// <summary>A company's own system and the ready-made designs offered in it.</summary>
public sealed record SystemDesigns(ProductSystem System, IReadOnlyList<DesignTemplate> Templates);

/// <summary>
/// The design library panel: a rail of categories (Frame tools, Dividers, Openable, Sliding, Mesh) and the sections
/// of designs of the selected category. Designs come from <see cref="DesignTemplates"/>; their thumbnails are drawn
/// with the same renderer as the drawing, so the picture is exactly what applying the design gives.
/// </summary>
public sealed class DesignLibraryViewModel : ViewModelBase
{
    /// <summary>The rail entry that shows the new-frame and snap tools rather than designs.</summary>
    public const string FrameCategory = "Frame";

    private readonly Dictionary<string, IReadOnlyList<DesignLibrarySection>> _sections = new();
    private readonly Func<DesignTemplate, string?, string?> _apply;
    private readonly DesignRules _rules;
    private readonly IReadOnlyList<DesignLibraryCategory> _builtIn;
    private DesignLibraryCategory? _company;
    private IReadOnlyList<SystemDesigns> _companyDesigns = Array.Empty<SystemDesigns>();

    /// <param name="apply">Applies a design to the current target; returns an error message or null.</param>
    public DesignLibraryViewModel(DesignRules rules, Func<DesignTemplate, string?> apply)
        : this(rules, (template, _) => apply(template))
    {
    }

    /// <param name="apply">Applies a design (in a system of the company's own, or null) to the current target; returns an
    /// error message or null.</param>
    public DesignLibraryViewModel(DesignRules rules, Func<DesignTemplate, string?, string?> apply)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _builtIn = new[]
        {
            new DesignLibraryCategory(FrameCategory, "", "New frame, snapping"),
            new DesignLibraryCategory(DesignTemplates.Dividers, "", "Dividers: mullions and transoms"),
            new DesignLibraryCategory(DesignTemplates.Openable, "", "Openable: casement, tilt & turn, twin sash, pivot"),
            new DesignLibraryCategory(DesignTemplates.Sliding, "", "Sliding: horizontal, vertical, monorail"),
            new DesignLibraryCategory(DesignTemplates.Mesh, "", "Insect mesh")
        };
        SelectCategoryCommand = new RelayCommand(p => { if (p is string name) SelectedCategory = name; });
        _selectedCategory = FrameCategory;
    }

    /// <summary>The rail: frame tools, the built-in categories and, when the company has systems of its own, its category.</summary>
    public IReadOnlyList<DesignLibraryCategory> Categories => _company is null ? _builtIn : _builtIn.Append(_company).ToList();

    /// <summary>The name of the company's own category, or null when it has no systems of its own.</summary>
    public string? CompanyCategory => _company?.Name;

    /// <summary>
    /// Offers the company's own systems as a category named after the company: a section per system with the designs
    /// that suit it. No systems: no such category.
    /// </summary>
    public void SetCompanyDesigns(string? companyName, IReadOnlyList<SystemDesigns> designs)
    {
        ArgumentNullException.ThrowIfNull(designs);
        if (_company is not null) _sections.Remove(_company.Name);
        var kept = designs.Where(d => d.Templates.Count > 0).ToList();
        string name = string.IsNullOrWhiteSpace(companyName) ? "Own" : companyName.Trim();
        _company = kept.Count == 0 ? null : new DesignLibraryCategory(name, "\uE734", $"{name}: designs in your own systems");
        _companyDesigns = kept;
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(CompanyCategory));
        if (Categories.All(c => c.Name != _selectedCategory))
            SelectedCategory = FrameCategory;
        else
            OnPropertyChanged(nameof(Sections));
    }

    public ICommand SelectCategoryCommand { get; }

    private string _selectedCategory;
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (!SetProperty(ref _selectedCategory, value)) return;
            OnPropertyChanged(nameof(Sections));
            OnPropertyChanged(nameof(IsFramePanel));
            OnPropertyChanged(nameof(Title));
        }
    }

    /// <summary>True while the Frame tools (not a design category) are shown.</summary>
    public bool IsFramePanel => _selectedCategory == FrameCategory;

    public string Title => IsFramePanel ? "FRAME TOOLS" : _selectedCategory.ToUpperInvariant();

    /// <summary>The selected category's sections, built (with thumbnails) the first time it is shown.</summary>
    public IReadOnlyList<DesignLibrarySection> Sections
    {
        get
        {
            if (IsFramePanel) return Array.Empty<DesignLibrarySection>();
            if (!_sections.TryGetValue(_selectedCategory, out var sections))
            {
                sections = _company is not null && _selectedCategory == _company.Name
                    ? _companyDesigns.Select(d => new DesignLibrarySection(d.System.Name,
                        d.Templates.Select(t => CreateItem(t, d.System)).ToList())).ToList()
                    : DesignTemplates.InCategory(_selectedCategory)
                    .GroupBy(t => t.Section)
                    .Select(g => new DesignLibrarySection(g.Key, g.Select(CreateItem).ToList()))
                    .ToList();
                _sections[_selectedCategory] = sections;
            }
            return sections;
        }
    }

    private string? _message;
    /// <summary>Why the last design could not be applied, or null.</summary>
    public string? Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value))
                OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    private DesignLibraryItem CreateItem(DesignTemplate template) => CreateItem(template, null);

    private DesignLibraryItem CreateItem(DesignTemplate template, ProductSystem? system)
        => new(template, DesignThumbnails.For(template, _rules), new RelayCommand(() => Message = _apply(template, system?.Id)), system);
}
