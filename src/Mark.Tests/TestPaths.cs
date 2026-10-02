using System.IO;

namespace Mark.Tests;

internal static class TestPaths
{
    /// <summary>The folder containing Mark.sln, found by walking up from the test output directory.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Mark.sln")))
                return dir.FullName;
        throw new InvalidOperationException("Mark.sln not found above the test output directory.");
    }
}
