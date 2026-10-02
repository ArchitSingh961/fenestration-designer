using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Fenestration.Calculation;
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
        var src = Path.Combine(TestPaths.RepositoryRoot, "src");
        var headless = Directory.GetFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Where(p =>
            {
                var name = Path.GetFileNameWithoutExtension(p);
                return name.EndsWith(".Core") || name.Contains("Calculation") || name.Contains(".Data");
            })
            .ToList();
        Assert.Contains(headless, p => p.EndsWith("Fenestration.Core.csproj"));
        Assert.Contains(headless, p => p.EndsWith("Fenestration.Calculation.csproj"));
        Assert.Contains(headless, p => p.EndsWith("Fenestration.Data.csproj"));

        foreach (var project in headless)
        {
            var xml = XDocument.Load(project);
            var useWpf = xml.Descendants("UseWPF").Select(e => e.Value.Trim());
            var frameworks = xml.Descendants("TargetFramework").Concat(xml.Descendants("TargetFrameworks")).Select(e => e.Value);
            Assert.DoesNotContain("true", useWpf, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(frameworks, f => f.Contains("-windows", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>The calculation engine depends on Core only: never on WPF, the designer or the app.</summary>
    [Fact]
    public void Calculation_ReferencesOnlyCore()
    {
        var references = typeof(CalculationEngine).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.Equal(new[] { "Fenestration.Core" }, references.Where(r => r!.StartsWith("Fenestration.")));
        Assert.Empty(references.Intersect(ForbiddenInCore));
    }

    [Fact]
    public void Core_DoesNotReferenceCalculation()
    {
        var references = typeof(Point2D).Assembly.GetReferencedAssemblies().Select(a => a.Name);
        Assert.DoesNotContain("Fenestration.Calculation", references);
    }

    /// <summary>Database code stays in Fenestration.Data: Core and Calculation never see SQLite or the data layer.</summary>
    [Fact]
    public void CoreAndCalculation_DoNotReferenceTheDatabase()
    {
        foreach (var assembly in new[] { typeof(Point2D).Assembly, typeof(CalculationEngine).Assembly })
        {
            var references = assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
            Assert.DoesNotContain("Fenestration.Data", references);
            Assert.DoesNotContain(references, r => r.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
                                                   || r.StartsWith("SQLitePCL", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>The data layer depends on Core only (plus SQLite): never on WPF, the designer, the app or Calculation.</summary>
    [Fact]
    public void Data_ReferencesOnlyCore()
    {
        var references = typeof(Fenestration.Data.LocalStore).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.Equal(new[] { "Fenestration.Core" }, references.Where(r => r!.StartsWith("Fenestration.")));
        Assert.Empty(references.Intersect(ForbiddenInCore));
    }
}
