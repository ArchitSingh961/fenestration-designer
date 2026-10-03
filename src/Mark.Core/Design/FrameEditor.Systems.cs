using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Core.Design;

/// <summary>Milestone 13: the product system a frame is made in.</summary>
public static partial class FrameEditor
{
    /// <summary>Puts the frame in a product system (or none, <paramref name="systemId"/> null).</summary>
    public static void SetSystem(Frame frame, string? systemId, IProductLibrary library, DesignRules rules)
        => TrySetSystem(frame, systemId, library, rules).ThrowIfFailed();

    /// <summary>
    /// Sets <see cref="Frame.SystemId"/> and makes the frame follow the system: every member drops its own profile and
    /// takes the system's (or, without a system, the library default) with that profile's face width, outer members
    /// keeping their outside faces; glass the system does not take falls back to the system's glass. The outer size
    /// never changes. Rejected (nothing changes) if the system is unknown or the result would be invalid.
    /// </summary>
    public static EditResult TrySetSystem(Frame frame, string? systemId, IProductLibrary library, DesignRules rules)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(library);
        ProductSystem? system = null;
        if (systemId is not null && (system = library.FindSystem(systemId)) is null)
            return EditResult.Fail($"The system '{systemId}' is not in the library.");

        ProfileDefinition? ProfileFor(ProfileType role)
            => library.FindProfile(system?.ProfileIdFor(role)) ?? library.DefaultProfileFor(role);

        return TryCommit(frame, rules, "Cannot change the system: ", scratch =>
        {
            scratch.SystemId = systemId;
            if (ProfileFor(ProfileType.Frame) is { } outerDefinition && FrameMembers.Find(scratch) is { } outer)
            {
                double t = outerDefinition.FaceWidthMm;
                foreach (var (member, position) in new (Profile, double)[]
                         {
                             (outer.Left, t / 2.0), (outer.Top, t / 2.0), (outer.Right, scratch.Width - t / 2.0),
                             (outer.Bottom, scratch.Height - t / 2.0)
                         })
                {
                    if (MoveMember(scratch, member, position) is { } error)
                        return error;
                    member.Thickness = t;
                }
            }

            foreach (var profile in scratch.Profiles)
            {
                profile.ProfileDefinitionId = null;
                if (Members.IsDivision(profile) && ProfileFor(profile.ProfileType) is { } definition)
                    profile.Thickness = definition.FaceWidthMm;
            }

            var defaultGlass = library.FindGlass(system?.GlassId) ?? library.DefaultGlass;
            foreach (var panel in scratch.GlassPanels)
            {
                var glass = library.FindGlass(panel.GlassDefinitionId);
                bool fits = glass is not null && (system is null || (system.AcceptsGlass(glass.ThicknessMm)
                                                                     && (glass.UsedWith?.FitsSystem(system.Id) ?? true)));
                if (!fits)
                    panel.GlassDefinitionId = null;
                var used = fits ? glass : defaultGlass;
                if (used is not null)
                    panel.Thickness = used.ThicknessMm;
            }
            return null;
        });
    }
}
