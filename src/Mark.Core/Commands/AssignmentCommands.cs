using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Core.Commands;

/// <summary>
/// Changes the glass type of some panels of one frame after design (e.g. 6mm Clear → 8mm Toughened), without
/// recreating anything. One undo step; undo/redo restore the exact previous state through the
/// <see cref="FrameEditCommand"/> snapshots. Rejected with <see cref="DesignValidationException"/> (and not
/// recorded) if the id is not in the library.
/// </summary>
public sealed class AssignGlassCommand : FrameEditCommand
{
    private readonly IProductLibrary _library;
    private readonly DesignRules _rules;
    private readonly IReadOnlyList<Guid> _glassIds;

    public AssignGlassCommand(Frame frame, IReadOnlyCollection<Guid> glassIds, string definitionId,
        IProductLibrary library, DesignRules rules)
        : base($"Change glass to {library?.FindGlass(definitionId)?.Name ?? definitionId}", frame)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _rules = rules;
        _glassIds = glassIds?.ToList() ?? throw new ArgumentNullException(nameof(glassIds));
        DefinitionId = definitionId;
    }

    public string DefinitionId { get; }

    protected override void Apply(Frame frame) => FrameEditor.AssignGlass(frame, _glassIds, DefinitionId, _library, _rules);
}

/// <summary>
/// Changes the library profile of some members of one frame after design (e.g. 50mm Frame → 60mm Frame).
/// The face width comes from the library, so the glass is re-derived; see <see cref="FrameEditor.TryAssignProfile"/>.
/// </summary>
public sealed class AssignProfileCommand : FrameEditCommand
{
    private readonly IProductLibrary _library;
    private readonly DesignRules _rules;
    private readonly IReadOnlyList<Guid> _profileIds;

    public AssignProfileCommand(Frame frame, IReadOnlyCollection<Guid> profileIds, string definitionId,
        IProductLibrary library, DesignRules rules)
        : base($"Change profile to {library?.FindProfile(definitionId)?.Name ?? definitionId}", frame)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _rules = rules;
        _profileIds = profileIds?.ToList() ?? throw new ArgumentNullException(nameof(profileIds));
        DefinitionId = definitionId;
    }

    public string DefinitionId { get; }

    /// <summary>The four outer members of <paramref name="frame"/> (what "the frame profile" means to a user).</summary>
    public static AssignProfileCommand ForOuterFrame(Frame frame, string definitionId, IProductLibrary library, DesignRules rules)
        => new(frame, frame.Profiles.Where(p => p.ProfileType == ProfileType.Frame).Select(p => p.Id).ToList(),
            definitionId, library, rules);

    protected override void Apply(Frame frame) => FrameEditor.AssignProfile(frame, _profileIds, DefinitionId, _library, _rules);
}

/// <summary>
/// Puts a frame in a product system (or takes it out of one): its members and glass follow the system; see
/// <see cref="FrameEditor.TrySetSystem"/>.
/// </summary>
public sealed class SetFrameSystemCommand : FrameEditCommand
{
    private readonly IProductLibrary _library;
    private readonly DesignRules _rules;

    public SetFrameSystemCommand(Frame frame, string? systemId, IProductLibrary library, DesignRules rules)
        : base(systemId is null ? "Remove the system" : $"Change system to {library?.FindSystem(systemId)?.Name ?? systemId}", frame)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _rules = rules;
        SystemId = systemId;
    }

    public string? SystemId { get; }

    protected override void Apply(Frame frame) => FrameEditor.SetSystem(frame, SystemId, _library, _rules);
}

/// <summary>
/// Sets up a design just made, from the New design panel: its system, the glass of all its panes and its details
/// (reference, quantity, location), as ONE undo step. If any part is rejected, nothing changes.
/// </summary>
public sealed class SetUpNewDesignCommand : FrameEditCommand
{
    private readonly IProductLibrary _library;
    private readonly DesignRules _rules;
    private readonly string? _systemId;
    private readonly string? _glassId;
    private readonly DesignInfo _info;

    /// <param name="systemId">The system to put the frame in, or null to keep its system.</param>
    /// <param name="glassId">The glass for every pane, or null to keep the glass.</param>
    public SetUpNewDesignCommand(Frame frame, string? systemId, string? glassId, DesignInfo info, IProductLibrary library, DesignRules rules)
        : base("Set up the new design", frame)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _rules = rules;
        _systemId = systemId;
        _glassId = glassId;
        _info = info?.Copy() ?? throw new ArgumentNullException(nameof(info));
    }

    protected override void Apply(Frame frame)
    {
        var before = FrameSnapshot.Capture(frame);
        try
        {
            if (_systemId is not null && _systemId != frame.SystemId)
                FrameEditor.SetSystem(frame, _systemId, _library, _rules);
            var panes = frame.GlassPanels.Where(g => g.GlassDefinitionId != _glassId).Select(g => g.Id).ToList();
            if (_glassId is not null && panes.Count > 0)
                FrameEditor.AssignGlass(frame, panes, _glassId, _library, _rules);
            FrameEditor.SetDesignInfo(frame, _info);
        }
        catch
        {
            before.ApplyTo(frame);
            throw;
        }
    }
}
