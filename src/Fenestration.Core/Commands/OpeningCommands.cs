using Fenestration.Core.Design;
using Fenestration.Core.Models;

namespace Fenestration.Core.Commands;

/// <summary>Sets how some openings of one frame open (see <see cref="FrameEditor.TrySetOpening"/>).</summary>
public sealed class SetOpeningCommand : FrameEditCommand
{
    private readonly DesignRules _rules;
    private readonly IReadOnlyList<Guid> _glassIds;
    private readonly bool? _mesh;

    /// <param name="opening">The new type, or null to keep each opening's type (and change only the mesh).</param>
    public SetOpeningCommand(Frame frame, IReadOnlyCollection<Guid> glassIds, OpeningType? opening, bool? mesh, DesignRules rules)
        : base(opening is { } o ? $"Make opening {o.DisplayName().ToLowerInvariant()}" : mesh == true ? "Add mesh" : "Remove mesh", frame)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _glassIds = glassIds?.ToList() ?? throw new ArgumentNullException(nameof(glassIds));
        Opening = opening;
        _mesh = mesh;
    }

    public OpeningType? Opening { get; }

    protected override void Apply(Frame frame) => FrameEditor.SetOpening(frame, _glassIds, Opening, _mesh, _rules);
}

/// <summary>Applies a design-library template to a frame or one of its openings (see <see cref="FrameEditor.TryApplyTemplate"/>).</summary>
public sealed class ApplyTemplateCommand : FrameEditCommand
{
    private readonly DesignRules _rules;
    private readonly Guid? _targetGlassId;

    public ApplyTemplateCommand(Frame frame, DesignTemplate template, Guid? targetGlassId, DesignRules rules)
        : base($"Apply design \"{template?.Name}\"", frame)
    {
        Template = template ?? throw new ArgumentNullException(nameof(template));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _targetGlassId = targetGlassId;
    }

    public DesignTemplate Template { get; }

    protected override void Apply(Frame frame) => FrameEditor.ApplyTemplate(frame, Template, _targetGlassId, _rules);
}

/// <summary>Changes a frame's design information: reference, quantity, location, floor distance, …</summary>
public sealed class SetDesignInfoCommand : FrameEditCommand
{
    private readonly DesignInfo _info;

    public SetDesignInfoCommand(Frame frame, DesignInfo info)
        : base("Edit design details", frame)
    {
        _info = info?.Copy() ?? throw new ArgumentNullException(nameof(info));
    }

    protected override void Apply(Frame frame) => FrameEditor.SetDesignInfo(frame, _info);
}
