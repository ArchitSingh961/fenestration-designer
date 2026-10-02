using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using Fenestration.Core.Design;
using Fenestration.Designer.Rendering;

namespace Fenestration.Designer.ViewModels;

/// <summary>One design in the library panel: its picture, name and the command that applies it.</summary>
public sealed class DesignLibraryItem
{
    public DesignLibraryItem(DesignTemplate template, ImageSource? thumbnail, ICommand apply)
    {
        Template = template;
        Thumbnail = thumbnail;
        ApplyCommand = apply;
    }

    public DesignTemplate Template { get; }

    public string Name => Template.Name;

    public ImageSource? Thumbnail { get; }

    /// <summary>Applies the design to the selected opening or frame (click). Dragging onto an opening also works.</summary>
    public ICommand ApplyCommand { get; }
}

/// <summary>A heading in the library panel with its designs, e.g. "Tilt &amp; turn designs".</summary>
public sealed record DesignLibrarySection(string Title, IReadOnlyList<DesignLibraryItem> Items);

/// <summary>A button of the library rail.</summary>
/// <param name="Glyph">A Segoe MDL2 Assets / Fluent icon character.</param>
public sealed record DesignLibraryCategory(string Name, string Glyph, string ToolTip);

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
    private readonly Func<DesignTemplate, string?> _apply;
    private readonly DesignRules _rules;

    /// <param name="apply">Applies a design to the current target; returns an error message or null.</param>
    public DesignLibraryViewModel(DesignRules rules, Func<DesignTemplate, string?> apply)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        Categories = new[]
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

    public IReadOnlyList<DesignLibraryCategory> Categories { get; }

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
                sections = DesignTemplates.InCategory(_selectedCategory)
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

    private DesignLibraryItem CreateItem(DesignTemplate template)
        => new(template, DesignThumbnails.For(template, _rules), new RelayCommand(() => Message = _apply(template)));
}
