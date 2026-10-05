using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Mark.Tests.Design;

/// <summary>
/// Every <c>{StaticResource Key}</c> in the windows is defined in the window itself, its application's resources or the
/// shared theme. A missing one only shows when the window is opened (MARK would not start), so it is checked here.
/// </summary>
public class XamlResourceTests
{
    private static string Src()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mark.App", "MainWindow.xaml"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The source folder was not found.");
    }

    private static readonly Regex Used = new(@"\{StaticResource\s+([A-Za-z_][A-Za-z0-9_]*)\s*\}", RegexOptions.Compiled);
    private static readonly Regex Defined = new(@"x:Key=""([A-Za-z_][A-Za-z0-9_]*)""", RegexOptions.Compiled);

    private static HashSet<string> Keys(params string[] files)
        => files.SelectMany(f => Defined.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).ToHashSet();

    [Theory]
    [InlineData("Mark.App", "MainWindow.xaml")]
    [InlineData("Mark.App", "Dialogs/SignInWindow.xaml")]
    [InlineData("Mark.App", "Dialogs/OpenProjectWindow.xaml")]
    [InlineData("Mark.Owner", "MainWindow.xaml")]
    [InlineData("Mark.Owner", "SignInWindow.xaml")]
    [InlineData("Mark.Owner", "ImportWindow.xaml")]
    [InlineData("Mark.Designer", "Views/LibraryManagerWindow.xaml")]
    [InlineData("Mark.Designer", "Views/TextPromptWindow.xaml")]
    public void EveryStaticResource_IsDefined(string project, string file)
    {
        string src = Src();
        string path = Path.Combine(src, project, file);
        var known = Keys(path, Path.Combine(src, "Mark.Designer", "Themes", "Theme.xaml"));
        if (project != "Mark.Designer") known.UnionWith(Keys(Path.Combine(src, project, "App.xaml")));

        var missing = Used.Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value).Distinct().Where(k => !known.Contains(k)).ToList();

        Assert.True(missing.Count == 0, $"{project}/{file} uses resources that are not defined: {string.Join(", ", missing)}");
    }
}
