using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Fenestration.Core.Commands;
using Fenestration.Core.Geometry;
using Fenestration.Designer.ViewModels;
using Xunit;

namespace Fenestration.Tests;

/// <summary>Guards the layering: Core (and anything that will consume it headless) never depends on WPF or the UI.</summary>
public class ArchitectureTests
{
    private static readonly string[] WpfAssemblies =
    {
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
        "System.Xaml"
    };

    private static readonly string[] ForbiddenInCore = WpfAssemblies.Concat(new[] { "Fenestration.Designer", "Fenestration.App" }).ToArray();

    [Fact]
    public void Core_HasNoWpfOrUiReferences()
    {
        var references = typeof(Point2D).Assembly.GetReferencedAssemblies().Select(a => a.Name);
        Assert.Empty(references.Intersect(ForbiddenInCore));
    }

    [Fact]
    public void Core_TargetsPlainNet_NotWindows()
    {
        var framework = typeof(Point2D).Assembly
            .GetCustomAttributes(typeof(System.Runtime.Versioning.TargetPlatformAttribute), false);
        Assert.Empty(framework);
    }

    /// <summary>Undo history must hold domain data only, so commands never keep WPF objects alive.</summary>
    [Fact]
    public void Commands_DoNotStoreWpfObjects()
    {
        var commandTypes = new[] { typeof(IUndoableCommand).Assembly, typeof(MainViewModel).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IUndoableCommand).IsAssignableFrom(t) && !t.IsInterface)
            .ToList();
        Assert.NotEmpty(commandTypes);

        foreach (var type in commandTypes)
        {
            for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
            {
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    var assemblies = new[] { field.FieldType }.Concat(field.FieldType.GetGenericArguments())
                        .Select(ft => ft.Assembly.GetName().Name);
                    Assert.False(assemblies.Any(a => WpfAssemblies.Contains(a)),
                        $"{type.Name}.{field.Name} holds a WPF type ({field.FieldType.Name}).");
                }
            }
        }
    }

    /// <summary>
    /// Core, and the future Calculation / Data projects, must stay free of WPF and Windows-only frameworks so they
    /// can run in a server or cloud process. Checked on the project files, so it applies as soon as those
    /// projects are added.
    /// </summary>
    [Fact]
    public void HeadlessProjects_DoNotUseWpf()
    {
        var src = Path.Combine(FindRepositoryRoot(), "src");
        var headless = Directory.GetFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Where(p =>
            {
                var name = Path.GetFileNameWithoutExtension(p);
                return name.EndsWith(".Core") || name.Contains("Calculation") || name.Contains(".Data");
            })
            .ToList();
        Assert.Contains(headless, p => p.EndsWith("Fenestration.Core.csproj"));

        foreach (var project in headless)
        {
            var xml = XDocument.Load(project);
            var useWpf = xml.Descendants("UseWPF").Select(e => e.Value.Trim());
            var frameworks = xml.Descendants("TargetFramework").Concat(xml.Descendants("TargetFrameworks")).Select(e => e.Value);
            Assert.DoesNotContain("true", useWpf, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(frameworks, f => f.Contains("-windows", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Fenestration.sln")))
                return dir.FullName;
        throw new InvalidOperationException("Fenestration.sln not found above the test output directory.");
    }
}
