using System.IO;
using System.Text.Json;
using Mark.Calculation;
using Mark.Core.Models;
using Mark.Core.Serialization;
using Mark.Data;
using Mark.Designer.ViewModels;
using Xunit;
using static Mark.Tests.Library.TestLibrary;

namespace Mark.Tests.Data;

/// <summary>
/// M8 through the real view models: design → save → restart → open → recalculate → edit the library → retire/delete.
/// No window is created; dialogs are scripted.
/// </summary>
public class PersistenceWorkflowTests : IDisposable
{
    private static readonly CalculationRules SawRules = new()
    {
        Cutting = new CuttingRules { KerfMm = 3, TrimAllowanceMm = 5, MinUsableOffcutMm = 300 }
    };

    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>Simulates starting the application: open the store (seeding the test library on first run).</summary>
    private (MainViewModel Vm, FakeDialogs Dialogs, LocalStore Store) Start()
    {
        var store = _temp.Open();
        if (store.Library.Current.Profiles.Count == 0)
            store.Library.Import(Create());
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(store, SawRules, dialogs);
        vm.Canvas.SetViewportSize(1000, 800);
        return (vm, dialogs, store);
    }

    private static Frame CreateFrame(MainViewModel vm)
    {
        vm.NewFrameWidthText = "1200";
        vm.NewFrameHeightText = "1500";
        vm.CreateFrameCommand.Execute(null);
        return vm.Project.Frames[^1];
    }

    [Fact]
    public void DefinitionOfDone_Workflow()
    {
        // 1–7: create a project, a frame and a mullion; assign profile and glass; calculate; plan the cuts.
        var (vm, dialogs, _) = Start();
        var frame = CreateFrame(vm);
        vm.AddMullionCommand.Execute(null);
        vm.Select(frame.Id);
        Assert.Null(vm.AssignProfile(Frame50));
        Assert.Null(vm.AssignGlass(Toughened8));
        var result = vm.Calculation.Result;
        var plan = vm.Calculation.CuttingPlan;
        Assert.True(result.IsComplete, string.Join(" | ", result.Issues.Select(i => i.Message)));
        string bom = JsonSerializer.Serialize(result.Bom);
        string cost = vm.CostText;
        string planJson = JsonSerializer.Serialize(plan);
        string design = ProjectSerializer.Serialize(vm.Project);
        Assert.True(vm.IsDirty);

        // 8: save (first save asks for a name).
        dialogs.PromptAnswer = "Kitchen window";
        vm.SaveProjectCommand.Execute(null);
        Assert.Null(vm.DesignMessage);
        Assert.False(vm.IsDirty);
        Guid id = vm.Project.Id;

        // 9–10: close and reopen the application, then open the project.
        var (vm2, dialogs2, store2) = Start();
        dialogs2.ChooseProjectAnswer = list => list.Projects.Single(p => p.Name == "Kitchen window").Id;
        vm2.OpenProjectCommand.Execute(null);

        // 11–16: same geometry, references, BOM, cost and cutting plan.
        Assert.Equal(id, vm2.Project.Id);
        Assert.Equal("Kitchen window", vm2.Project.Name);
        var reloaded = Assert.Single(vm2.Project.Frames);
        Assert.Equal(frame.Id, reloaded.Id);
        // Saving named the project and gave the quote its number; everything else is exactly as designed.
        Assert.Equal("QT-00001", vm2.Project.Quote.Number);
        Assert.Equal(design.Replace("\"name\": \"New quote\"", "\"name\": \"Kitchen window\"")
                .Replace("\"number\": \"\"", "\"number\": \"QT-00001\""),
            ProjectSerializer.Serialize(vm2.Project));
        Assert.All(reloaded.Profiles.Where(p => p.ProfileType == ProfileType.Frame),
            p => Assert.Equal("50mm Frame", store2.Library.Current.FindProfile(p.ProfileDefinitionId)!.Name));
        Assert.All(reloaded.GlassPanels, g => Assert.Equal("8mm Toughened", store2.Library.Current.FindGlass(g.GlassDefinitionId)!.Name));
        Assert.Equal(bom, JsonSerializer.Serialize(vm2.Calculation.Result.Bom));
        Assert.Equal(cost, vm2.CostText);
        Assert.Equal(planJson, JsonSerializer.Serialize(vm2.Calculation.CuttingPlan));
        Assert.False(vm2.CommandHistory.CanUndo);                         // history is session state, not persisted

        // 17–20: open the library manager, search, edit the glass price; the open design is recalculated.
        dialogs2.OnLibraryManager = manager =>
        {
            manager.Kind = LibraryItemKind.Glass;
            manager.SearchText = "tough";
            Assert.Equal(Toughened8, Assert.Single(manager.Items).Id);
            manager.SelectedItem = manager.Items[0];
            Assert.Contains("Kitchen window", manager.UsageText);
            manager.ItemEditor!.CostPerSquareMetre = "2500";                  // was 2000
            manager.SaveCommand.Execute(null);
            Assert.False(manager.MessageIsError, manager.Message);
        };
        decimal glassBefore = vm2.Calculation.Result.Cost.Glass;
        vm2.OpenLibraryManagerCommand.Execute(null);
        Assert.Equal(glassBefore * 1.25m, vm2.Calculation.Result.Cost.Glass);
        Assert.NotEqual(cost, vm2.CostText);

        // 21: deleting a used product is refused; retiring it keeps the design priced.
        dialogs2.OnLibraryManager = manager =>
        {
            manager.Kind = LibraryItemKind.Glass;
            manager.SelectedItem = manager.Items.Single(i => i.Id == Toughened8);
            manager.DeleteCommand.Execute(null);
            Assert.True(manager.MessageIsError);
            Assert.Contains("cannot be deleted", manager.Message);
            manager.ToggleActiveCommand.Execute(null);
            Assert.False(manager.MessageIsError, manager.Message);
        };
        vm2.OpenLibraryManagerCommand.Execute(null);
        Assert.NotNull(store2.Library.Current.FindGlass(Toughened8));
        Assert.False(store2.Library.Current.FindGlass(Toughened8)!.IsActive);
        Assert.True(vm2.Calculation.Result.IsComplete);
        Assert.Contains(vm2.Calculation.Result.Issues, i => i.Message.Contains("retired"));
        // The picker no longer offers the retired glass.
        vm2.Select(reloaded.GlassPanels[0].Id);
        Assert.DoesNotContain(vm2.Properties.GlassPicker!.Options, o => o.Id == Toughened8);
    }

    [Fact]
    public void Save_Twice_UpdatesTheSameProject_AndOnlyTheFirstSaveAsksForAName()
    {
        var (vm, dialogs, store) = Start();
        CreateFrame(vm);
        dialogs.PromptAnswer = "Office";
        vm.SaveProjectCommand.Execute(null);
        CreateFrame(vm);
        vm.SaveProjectCommand.Execute(null);

        Assert.Single(dialogs.Prompts);
        var saved = Assert.Single(store.Projects.List());
        Assert.Equal(2, store.Projects.Load(saved.Id).Frames.Count);
    }

    [Fact]
    public void CancellingTheNamePrompt_SavesNothing()
    {
        var (vm, dialogs, store) = Start();
        CreateFrame(vm);
        dialogs.PromptAnswer = null;

        vm.SaveProjectCommand.Execute(null);

        Assert.Empty(store.Projects.List());
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void SaveACopy_GivesNewIds_AndLeavesTheOriginal()
    {
        var (vm, dialogs, store) = Start();
        var frame = CreateFrame(vm);
        dialogs.PromptAnswer = "Original";
        vm.SaveProjectCommand.Execute(null);
        Guid original = vm.Project.Id;

        dialogs.PromptAnswer = "Variant";
        vm.SaveProjectAsCommand.Execute(null);

        Assert.NotEqual(original, vm.Project.Id);
        Assert.NotEqual(frame.Id, vm.Project.Frames[0].Id);
        Assert.Equal(new[] { "Original", "Variant" }, store.Projects.List().Select(p => p.Name).Order());
        Assert.Equal(frame.Id, store.Projects.Load(original).Frames[0].Id);
    }

    [Fact]
    public void Opening_AskBeforeDiscardingUnsavedChanges()
    {
        var (vm, dialogs, store) = Start();
        store.Projects.Save(new Project { Name = "Other" });
        CreateFrame(vm);
        dialogs.ConfirmAnswer = false;

        vm.OpenProjectCommand.Execute(null);

        Assert.Single(dialogs.Confirms);
        Assert.Single(vm.Project.Frames);                                // kept
        dialogs.ConfirmAnswer = true;
        vm.OpenProjectCommand.Execute(null);
        Assert.Equal("Other", vm.Project.Name);
    }

    [Fact]
    public void ProjectFile_ExportThenImport_KeepsTheDesign_AndAnImportOfASavedIdBecomesACopy()
    {
        var (vm, dialogs, _) = Start();
        CreateFrame(vm);
        Directory.CreateDirectory(_temp.Folder);
        string file = Path.Combine(_temp.Folder, "window.json");
        dialogs.SaveFileAnswer = file;
        vm.ExportProjectFileCommand.Execute(null);
        string exported = ProjectSerializer.Serialize(vm.Project);
        Guid id = vm.Project.Id;

        dialogs.PromptAnswer = "Saved";
        vm.SaveProjectCommand.Execute(null);
        dialogs.OpenFileAnswer = file;
        vm.ImportProjectFileCommand.Execute(null);

        Assert.NotEqual(id, vm.Project.Id);                              // same Id already saved → a copy
        Assert.EndsWith("(imported)", vm.Project.Name);
        Assert.True(vm.IsDirty);
        Assert.Equal(ProjectSerializer.Deserialize(exported).Frames[0].Width, vm.Project.Frames[0].Width);
    }

    [Fact]
    public void ImportingAnInvalidProjectFile_ReportsIt_AndKeepsTheOpenProject()
    {
        var (vm, dialogs, _) = Start();
        var frame = CreateFrame(vm);
        Directory.CreateDirectory(_temp.Folder);
        string file = Path.Combine(_temp.Folder, "broken.json");
        File.WriteAllText(file, "{ not json");
        dialogs.ConfirmAnswer = true;
        dialogs.OpenFileAnswer = file;

        vm.ImportProjectFileCommand.Execute(null);

        Assert.Contains("could not be read", vm.DesignMessage);
        Assert.Same(frame, vm.Project.Frames[0]);
    }

    [Fact]
    public void LibraryManager_AddsAProduct_ThatThePickersOfferStraightAway()
    {
        var (vm, dialogs, _) = Start();
        var frame = CreateFrame(vm);
        dialogs.OnLibraryManager = manager =>
        {
            manager.Kind = LibraryItemKind.Glass;
            manager.NewCommand.Execute(null);
            var editor = manager.ItemEditor!;
            editor.Id = "GLS-LOWE-6";
            editor.Name = "6mm Low-E";
            editor.Group = "Low-E";
            editor.Thickness = "6";
            editor.CostPerSquareMetre = "1800";
            editor.ForCasement = editor.ForSliding = true;                    // for every kind of window
            manager.SaveCommand.Execute(null);
            Assert.False(manager.MessageIsError, manager.Message);
            Assert.Equal("GLS-LOWE-6", manager.SelectedItem!.Id);
        };
        vm.OpenLibraryManagerCommand.Execute(null);

        vm.Select(frame.GlassPanels[0].Id);
        Assert.Contains(vm.Properties.GlassPicker!.Options, o => o.Id == "GLS-LOWE-6");
        Assert.Null(vm.AssignGlass("GLS-LOWE-6"));
        Assert.Equal(1800m, vm.Calculation.Result.Glass[0].CostPerSquareMetre);
    }

    [Fact]
    public void LibraryManager_ReportsDuplicateIdsAndInvalidFields_WithoutChangingTheLibrary()
    {
        var (vm, dialogs, store) = Start();
        string before = store.Library.Export();
        dialogs.OnLibraryManager = manager =>
        {
            manager.NewCommand.Execute(null);                            // profile
            manager.ItemEditor!.Id = Frame60;                                // already used
            manager.ItemEditor!.Name = "Dup";
            manager.ItemEditor!.RoleFrame = true;
            manager.ItemEditor!.FaceWidth = "60";
            manager.ItemEditor!.ForCasement = true;
            manager.SaveCommand.Execute(null);
            Assert.True(manager.MessageIsError);
            Assert.Contains("already used", manager.Message);

            manager.ItemEditor!.Id = "PRF-NEW";
            manager.ItemEditor!.FaceWidth = "sixty";
            manager.SaveCommand.Execute(null);
            Assert.Contains("Face width must be a number", manager.Message);

            manager.ItemEditor!.FaceWidth = "60";
            manager.ItemEditor!.RoleFrame = false;                            // no role: library validation refuses it
            manager.SaveCommand.Execute(null);
            Assert.Contains("at least one role", manager.Message);
        };
        vm.OpenLibraryManagerCommand.Execute(null);

        Assert.Equal(before, store.Library.Export());
    }

    [Fact]
    public void LibraryManager_FiltersByManufacturerGroupAndActiveState()
    {
        var (vm, dialogs, store) = Start();
        store.Library.Add(new Core.Library.ProfileDefinition
        {
            Id = "ACME-70", Name = "Acme 70", Manufacturer = "Acme", Series = "Acme 70", Roles = new[] { ProfileType.Frame },
            FaceWidthMm = 70, IsActive = false
        });
        dialogs.OnLibraryManager = manager =>
        {
            Assert.DoesNotContain(manager.Items, i => i.Id == "ACME-70");    // retired: hidden by default
            manager.ShowInactive = true;
            manager.ManufacturerFilter = "Acme";
            Assert.Equal("ACME-70", Assert.Single(manager.Items).Id);
            Assert.Equal("retired", manager.Items[0].Status);
            manager.ManufacturerFilter = "(all)";
            manager.GroupFilter = "Series 60";
            Assert.Equal(new[] { Frame60, Mullion60, Mullion80 }, manager.Items.Select(i => i.Id));
            manager.Kind = LibraryItemKind.Material;
            manager.GroupFilter = "Hardware";
            Assert.Equal(Cleat, Assert.Single(manager.Items).Id);
        };
        vm.OpenLibraryManagerCommand.Execute(null);
    }

    [Fact]
    public void ProjectList_SearchesAndDeletes_ButNotTheOpenProject()
    {
        var (vm, dialogs, store) = Start();
        store.Projects.Save(new Project { Name = "Alpha house" });
        _temp.Tick();
        store.Projects.Save(new Project { Name = "Beta flat" });
        var list = new ProjectListViewModel(store.Projects, vm.Project.Id, dialogs);
        Assert.Equal(new[] { "Beta flat", "Alpha house" }, list.Projects.Select(p => p.Name));

        list.SearchText = "alpha";
        Assert.Equal("Alpha house", Assert.Single(list.Projects).Name);
        list.DeleteCommand.Execute(null);
        Assert.Equal(new[] { "Beta flat" }, store.Projects.List().Select(p => p.Name));

        vm.Project.Name = "Open";
        store.Projects.Save(vm.Project);
        var withOpen = new ProjectListViewModel(store.Projects, vm.Project.Id, dialogs) { SearchText = "Open" };
        withOpen.DeleteCommand.Execute(null);
        Assert.Contains("open in the designer", withOpen.Message);
        Assert.True(store.Projects.Exists(vm.Project.Id));
    }

    [Fact]
    public void WithoutAStore_TheDesignerStillWorks_ButCannotSave()
    {
        var vm = new MainViewModel(Create());
        CreateFrame(vm);

        Assert.False(vm.HasStore);
        Assert.False(vm.SaveProjectCommand.CanExecute(null));
        Assert.Contains("no local database", vm.SaveProject());
    }
}
