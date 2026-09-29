using Fenestration.Core.Geometry;
using Xunit;

namespace Fenestration.Tests;

/// <summary>Guards the rule that Core (and so Geometry) never depends on WPF or the UI layers.</summary>
public class ArchitectureTests
{
    private static readonly string[] ForbiddenAssemblies =
    {
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
        "System.Xaml",
        "Fenestration.Designer",
        "Fenestration.App"
    };

    [Fact]
    public void Core_HasNoWpfOrUiReferences()
    {
        var references = typeof(Point2D).Assembly.GetReferencedAssemblies().Select(a => a.Name);
        Assert.Empty(references.Intersect(ForbiddenAssemblies));
    }

    [Fact]
    public void Core_TargetsPlainNet_NotWindows()
    {
        var framework = typeof(Point2D).Assembly
            .GetCustomAttributes(typeof(System.Runtime.Versioning.TargetPlatformAttribute), false);
        Assert.Empty(framework);
    }
}
