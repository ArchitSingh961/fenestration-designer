using System.IO;
using Fenestration.Core.Models;
using Fenestration.Data;
using Fenestration.Designer.ViewModels;
using Xunit;
using static Fenestration.Tests.Library.TestLibrary;

namespace Fenestration.Tests.Data;

/// <summary>
/// The database becomes unusable while the application is running (damaged, replaced or deleted). Every operation must
/// report a readable <see cref="DataStoreException"/> — never a raw SQLite error that would crash the UI — and the view
/// models must show it and keep the open design.
/// </summary>
public class DatabaseFailureTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A working store with the test library and one saved project.</summary>
    private (LocalStore Store, Project Saved) Store()
    {
        var store = _temp.Open();
        store.Library.Import(Create());
        var (project, frame) = SingleFrame();
        project.Name = "Saved";
        frame.GlassPanels[0].GlassDefinitionId = Clear6;
        store.Projects.Save(project);
        return (store, project);
    }

    /// <summary>Overwrites the database file with junk, as if it were damaged while the application runs.</summary>
    private void DamageTheFile() => File.WriteAllBytes(_temp.DatabasePath, Enumerable.Repeat((byte)0x5A, 8192).ToArray());

    [Fact]
    public void EveryRepositoryOperation_ReportsADataStoreException()
    {
        var (store, saved) = Store();
        var repository = new SqliteLibraryRepository(store.Database);
        DamageTheFile();

        var operations = new (string Name, Action Run)[]
        {
            ("library load", () => repository.Load()),
            ("library is-empty", () => repository.IsEmpty()),
            ("library save", () => repository.SaveGlass(store.Library.Current.FindGlass(Clear6)!)),
            ("library delete", () => repository.Delete(LibraryItemKind.Glass, Laminated10)),
            ("project list", () => store.Projects.List()),
            ("project exists", () => store.Projects.Exists(saved.Id)),
            ("project load", () => store.Projects.Load(saved.Id)),
            ("project save", () => store.Projects.Save(saved)),
            ("project delete", () => store.Projects.Delete(saved.Id)),
            ("project find-using", () => store.Projects.FindUsing(LibraryItemKind.Glass, Clear6))
        };
        foreach (var (name, run) in operations)
        {
            var ex = Record.Exception(run);
            Assert.True(ex is DataStoreException, $"{name} threw {ex?.GetType().Name ?? "nothing"}");
            Assert.Contains("local database could not", ex!.Message);
        }
    }

    [Fact]
    public void ALibraryChange_IsReported_AndTheInMemoryLibraryIsUnchanged()
    {
        var (store, _) = Store();
        var before = store.Library.Current;
        DamageTheFile();

        Assert.Throws<DataStoreException>(() => store.Library.Update(before.FindGlass(Clear6)! with { CostPerSquareMetre = 1 }));
        Assert.Same(before, store.Library.Current);
    }

    [Fact]
    public void TheDesigner_ReportsTheFailure_AndKeepsTheOpenDesign()
    {
        var (store, _) = Store();
        var dialogs = new FakeDialogs { PromptAnswer = "New name" };
        var vm = new MainViewModel(store, null, dialogs);
        vm.CreateFrameCommand.Execute(null);
        var frame = vm.Project.Frames[0];
        DamageTheFile();

        vm.SaveProjectCommand.Execute(null);                              // checks Exists first, then saves
        Assert.Contains("local database could not", vm.DesignMessage);
        Assert.True(vm.IsDirty);

        vm.DesignMessage = null;
        vm.OpenProjectCommand.Execute(null);                              // lists the saved projects
        Assert.Contains("local database could not", vm.DesignMessage);
        Assert.Same(frame, Assert.Single(vm.Project.Frames));
    }

    [Fact]
    public void TheLibraryManager_ReportsTheFailure_InsteadOfThrowing()
    {
        var (store, _) = Store();
        var manager = new LibraryManagerViewModel(store.Library, store.Projects, () => null, new FakeDialogs());
        DamageTheFile();

        manager.Kind = LibraryItemKind.Glass;
        manager.SelectedItem = manager.Items.First(i => i.Id == Clear6);  // looks up the saved projects using it
        Assert.Contains("could not be checked", manager.UsageText);

        manager.Editor!.CostPerSquareMetre = "1234";
        manager.SaveCommand.Execute(null);
        Assert.True(manager.MessageIsError);
        Assert.Contains("local database could not", manager.Message);
    }

    [Fact]
    public void ADeletedDatabaseFile_IsReported_AndRecreatedCleanlyOnTheNextStart()
    {
        var (store, saved) = Store();
        File.Delete(_temp.DatabasePath);

        Assert.Throws<DataStoreException>(() => store.Projects.Load(saved.Id));

        var restarted = _temp.Open(TempDatabase.ShippedLibraryPath);       // next start: a fresh, seeded database
        Assert.NotEmpty(restarted.Library.Current.Profiles);
        Assert.Empty(restarted.Projects.List());
    }
}
