using System.Text.Json;
using Mark.Calculation;
using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Models;
using Mark.Core.Serialization;
using Mark.Data;
using Microsoft.Data.Sqlite;
using Xunit;
using static Mark.Tests.Library.TestLibrary;

namespace Mark.Tests.Data;

/// <summary>Projects saved to and loaded from SQLite: round trip, Ids, references, listing, and M6/M7 reproducibility.</summary>
public class ProjectPersistenceTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private LocalStore Store()
    {
        var store = _temp.Open();
        if (store.Library.Current.Profiles.Count == 0)
            store.Library.Import(Create());
        return store;
    }

    /// <summary>A designed window: frame, mullion, transom, assigned profiles and glass (through the real commands).</summary>
    private static Project DesignedProject(Core.Library.ProductLibrary library)
    {
        var (project, frame) = SingleFrame(1800, 1500);
        project.Name = "Living room";
        var history = new CommandHistory();
        history.Execute(AddDivisionCommand.Mullion(frame, Rules, x: 900));
        history.Execute(AddDivisionCommand.Transom(frame, Rules, frame.GlassPanels[0].Id, y: 600));
        history.Execute(AssignProfileCommand.ForOuterFrame(frame, Frame50, library, Rules));
        history.Execute(new AssignProfileCommand(frame, frame.Profiles.Where(Members.IsDivision).Select(p => p.Id).ToList(),
            Mullion80, library, Rules));
        history.Execute(new AssignGlassCommand(frame, new[] { frame.GlassPanels[0].Id }, Toughened8, library, Rules));
        history.Execute(new AssignGlassCommand(frame, frame.GlassPanels.Skip(1).Select(g => g.Id).ToList(), Laminated10, library, Rules));
        return project;
    }

    private static readonly CalculationRules SawRules = new()
    {
        Cutting = new CuttingRules { KerfMm = 3, TrimAllowanceMm = 5, MinUsableOffcutMm = 300 }
    };

    [Fact]
    public void SaveAndLoad_RoundTripsTheWholeDesign_WithEveryId()
    {
        var store = Store();
        var project = DesignedProject(store.Library.Current);

        store.Projects.Save(project);
        var loaded = _temp.Open().Projects.Load(project.Id);

        Assert.Equal(ProjectSerializer.Serialize(project), ProjectSerializer.Serialize(loaded));
        Assert.Equal(project.Frames.SelectMany(f => f.Profiles).Select(p => p.Id), loaded.Frames.SelectMany(f => f.Profiles).Select(p => p.Id));
        Assert.Equal(project.Frames[0].GlassPanels.Select(g => g.Id), loaded.Frames[0].GlassPanels.Select(g => g.Id));
    }

    [Fact]
    public void ProfileAndGlassReferences_ResolveThroughTheLibrary_AfterReload()
    {
        var store = Store();
        store.Projects.Save(DesignedProject(store.Library.Current));

        var reopened = _temp.Open();
        var summary = Assert.Single(reopened.Projects.List());
        var frame = reopened.Projects.Load(summary.Id).Frames[0];
        var library = reopened.Library.Current;

        // Frame → Profile.ProfileDefinitionId → library → ProfileDefinition
        var outer = frame.Profiles.First(p => p.ProfileType == ProfileType.Frame);
        Assert.Equal("50mm Frame", library.FindProfile(outer.ProfileDefinitionId)!.Name);
        Assert.Equal(50, outer.Thickness);
        // Frame → GlassPanel.GlassDefinitionId → library → GlassDefinition
        Assert.Equal(new[] { "8mm Toughened", "10mm Laminated", "10mm Laminated" },
            frame.GlassPanels.Select(g => library.FindGlass(g.GlassDefinitionId)!.Name));
    }

    [Fact]
    public void Save_RecordsTheExplicitReferences()
    {
        var store = Store();
        var project = DesignedProject(store.Library.Current);
        store.Projects.Save(project);

        Assert.Equal(new[] { (LibraryItemKind.Profile, Frame50), (LibraryItemKind.Profile, Mullion80),
                (LibraryItemKind.Glass, Laminated10), (LibraryItemKind.Glass, Toughened8) },
            SqliteProjectRepository.References(project));
        Assert.Equal(project.Id, Assert.Single(store.Projects.FindUsing(LibraryItemKind.Glass, Laminated10)).Id);
        Assert.Empty(store.Projects.FindUsing(LibraryItemKind.Glass, Clear6));

        // Saving again after changing the glass rewrites the references.
        foreach (var g in project.Frames[0].GlassPanels) g.GlassDefinitionId = Clear6;
        store.Projects.Save(project);
        Assert.Empty(store.Projects.FindUsing(LibraryItemKind.Glass, Laminated10));
        Assert.Single(store.Projects.FindUsing(LibraryItemKind.Glass, Clear6));
    }

    [Fact]
    public void Calculation_AndCuttingPlan_AreIdenticalAfterReload()
    {
        var store = Store();
        var project = DesignedProject(store.Library.Current);
        var engine = new CalculationEngine();
        var optimizer = new CuttingOptimizer();
        var before = engine.Calculate(project, store.Library.Current, SawRules);
        var planBefore = optimizer.Optimize(before, store.Library.Current, SawRules);
        Assert.True(before.IsComplete, string.Join(" | ", before.Issues.Select(i => i.Message)));
        store.Projects.Save(project);

        var reopened = _temp.Open();
        var loaded = reopened.Projects.Load(project.Id);
        var after = engine.Calculate(loaded, reopened.Library.Current, SawRules);
        var planAfter = optimizer.Optimize(after, reopened.Library.Current, SawRules);

        Assert.Equal(before.Bom, after.Bom);
        Assert.Equal(before.Cost, after.Cost);
        Assert.Equal(before.CutList, after.CutList);
        Assert.Equal(before.Profiles.Select(p => (p.ProfileId, p.CutLengthMm, p.Cost)), after.Profiles.Select(p => (p.ProfileId, p.CutLengthMm, p.Cost)));
        Assert.Equal(before.Glass.Select(g => (g.GlassPanelId, g.WidthMm, g.HeightMm, g.Cost)), after.Glass.Select(g => (g.GlassPanelId, g.WidthMm, g.HeightMm, g.Cost)));
        Assert.Equal(JsonSerializer.Serialize(planBefore), JsonSerializer.Serialize(planAfter));
        Assert.Equal((planBefore.Utilization, planBefore.TotalWasteMm, planBefore.StockCost),
            (planAfter.Utilization, planAfter.TotalWasteMm, planAfter.StockCost));
    }

    [Fact]
    public void EditingALibraryProduct_IsUsedByTheNextCalculationOfASavedProject()
    {
        var store = Store();
        var project = DesignedProject(store.Library.Current);
        store.Projects.Save(project);
        decimal before = new CalculationEngine().Calculate(project, store.Library.Current, SawRules).Cost.Glass;

        store.Library.Update(store.Library.Current.FindGlass(Laminated10)! with { CostPerSquareMetre = 6000m });   // was 3000

        var reopened = _temp.Open();
        var after = new CalculationEngine().Calculate(reopened.Projects.Load(project.Id), reopened.Library.Current, SawRules);
        Assert.True(after.Cost.Glass > before);
        Assert.All(after.Glass.Where(g => g.DefinitionId == Laminated10), g => Assert.Equal(6000m, g.CostPerSquareMetre));
    }

    [Fact]
    public void ARetiredProduct_StillPricesTheSavedProject_WithAWarning()
    {
        var store = Store();
        var project = DesignedProject(store.Library.Current);
        store.Projects.Save(project);
        store.Library.SetActive(LibraryItemKind.Glass, Toughened8, false);

        var reopened = _temp.Open();
        var result = new CalculationEngine().Calculate(reopened.Projects.Load(project.Id), reopened.Library.Current, SawRules);

        Assert.True(result.IsComplete);
        Assert.True(result.Glass.First(g => g.DefinitionId == Toughened8).Cost > 0);
        Assert.Contains(result.Issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("retired"));
    }

    [Fact]
    public void List_IsOrderedByModifiedThenName_AndSaveKeepsTheCreationTime()
    {
        var store = Store();
        var a = new Project { Name = "Alpha" };
        var b = new Project { Name = "Beta" };
        store.Projects.Save(a);
        _temp.Tick();
        store.Projects.Save(b);
        Assert.Equal(new[] { "Beta", "Alpha" }, store.Projects.List().Select(p => p.Name));

        _temp.Tick();
        store.Projects.Save(a);                                              // Alpha modified last
        var list = store.Projects.List();
        Assert.Equal(new[] { "Alpha", "Beta" }, list.Select(p => p.Name));
        Assert.Equal(TempDatabase.Start, list[0].CreatedUtc);
        Assert.Equal(TempDatabase.Start.AddMinutes(2), list[0].ModifiedUtc);
    }

    [Fact]
    public void Delete_RemovesTheProjectAndItsReferences()
    {
        var store = Store();
        var project = DesignedProject(store.Library.Current);
        store.Projects.Save(project);

        store.Projects.Delete(project.Id);

        Assert.False(store.Projects.Exists(project.Id));
        Assert.Empty(store.Projects.FindUsing(LibraryItemKind.Profile, Frame50));
        Assert.Throws<DataStoreException>(() => store.Projects.Delete(project.Id));
    }

    [Fact]
    public void Loading_AMissingProject_IsAClearError()
    {
        var store = Store();
        var ex = Assert.Throws<DataStoreException>(() => store.Projects.Load(Guid.NewGuid()));
        Assert.Contains("not in the database", ex.Message);
    }

    [Fact]
    public void Loading_ADamagedProject_IsAClearError_NotACrash()
    {
        var store = Store();
        var project = new Project { Name = "Broken" };
        store.Projects.Save(project);
        using (var connection = store.Database.Connect())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE projects SET document_json = '{ \"version\": 1, \"project\": { \"frames\": [ { \"width\": -5 } ] } }'";
            command.ExecuteNonQuery();
        }

        var ex = Assert.Throws<DataStoreException>(() => store.Projects.Load(project.Id));
        Assert.Contains("damaged", ex.Message);
    }

    [Fact]
    public void AFailedSave_LeavesTheSavedVersionUnchanged()
    {
        var store = Store();
        var project = new Project { Name = "First" };
        store.Projects.Save(project);
        using (var connection = store.Database.Connect())
        using (var command = connection.CreateCommand())
        {
            // Make the reference insert fail halfway through the save transaction.
            command.CommandText = "CREATE TRIGGER fail_refs BEFORE INSERT ON project_references BEGIN SELECT RAISE(ABORT, 'boom'); END";
            command.ExecuteNonQuery();
        }

        project.Name = "Second";
        project.Frames.Add(SingleFrame().Frame);
        project.Frames[0].GlassPanels[0].GlassDefinitionId = Clear6;
        Assert.Throws<DataStoreException>(() => store.Projects.Save(project));

        Assert.Equal("First", Assert.Single(store.Projects.List()).Name);
        Assert.Empty(store.Projects.Load(project.Id).Frames);
    }
}
