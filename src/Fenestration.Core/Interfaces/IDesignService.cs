using Fenestration.Core.Models;

namespace Fenestration.Core.Interfaces;

/// <summary>
/// Integration contract for external modules (calculation engine, quotation, etc.)
/// to consume the current design without any WPF dependency.
/// </summary>
public interface IDesignService
{
    /// <summary>Returns the live project model. Mutations must go through the designer's commands.</summary>
    Project GetCurrentProject();

    /// <summary>
    /// Returns an isolated deep copy of the project with all Ids preserved.
    /// Preferred input for the calculation engine: it cannot corrupt the live design,
    /// and results can still be mapped back to design objects by Id.
    /// </summary>
    Project GetProjectSnapshot();

    /// <summary>Returns the currently selected frame, or null.</summary>
    Frame? GetSelectedFrame();

    /// <summary>Returns all profiles across all frames.</summary>
    IReadOnlyList<Profile> GetAllProfiles();

    /// <summary>Returns all glass panels across all frames.</summary>
    IReadOnlyList<GlassPanel> GetAllGlassPanels();

    /// <summary>Returns all frames in the project.</summary>
    IReadOnlyList<Frame> GetFrames();
}
