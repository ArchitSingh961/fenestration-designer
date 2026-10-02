using System.IO;
using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Core.Serialization;
using Mark.Data;
using Mark.Designer.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;
using static Mark.Tests.Library.TestLibrary;

namespace Mark.Tests.Data;

/// <summary>
/// Milestone 10: quotes (client, status, number), design cards, the quote list and the dashboard, from the Core rules
/// through the database to the view models.
/// </summary>
public class QuoteTests : IDisposable
{
    private static readonly DesignRules Rules = new();
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private static QuoteInfo Quote(string first = "Archit", string email = "", string phone = "", QuoteStatus status = QuoteStatus.Active)
        => new()
        {
            Status = status,
            Notes = "  Bedroom and living room, toughened glass  ",
            Client = new ClientInfo { Title = "Mr.", FirstName = first, LastName = "Singh", Email = email, Phone = phone, City = "Jaipur" }
        };

    private static Project ProjectWith(params (double W, double H, int Qty)[] frames)
    {
        var project = new Project { Name = "Sharma residence" };
        foreach (var (w, h, q) in frames)
        {
            var command = CreateFrameCommand.Create(project, project.Frames.Sum(f => f.Width + 500), 0, w, h, Rules);
            command.Execute();
            command.Frame.Design.Quantity = q;
        }
        return project;
    }

    private (MainViewModel Vm, FakeDialogs Dialogs, LocalStore Store) Start()
    {
        var store = _temp.Open();
        if (store.Library.Current.Profiles.Count == 0)
            store.Library.Import(Create());
        var dialogs = new FakeDialogs { PromptAnswer = "Quote" };
        var vm = new MainViewModel(store, null, dialogs);
        vm.Canvas.SetViewportSize(1000, 800);
        return (vm, dialogs, store);
    }

    // ── Core ────────────────────────────────────────────────────────

    [Fact]
    public void SetQuote_TrimsAndStores_KeepsTheNumber()
    {
        var project = new Project { Quote = { Number = "QT-00007" } };

        QuoteEditor.SetQuote(project, "  Villa  ", Quote(email: "a@b.in", phone: "+91 98290-12345"));

        Assert.Equal("Villa", project.Name);
        Assert.Equal("QT-00007", project.Quote.Number);
        Assert.Equal("Mr. Archit Singh", project.Quote.Client.DisplayName);
        Assert.Equal("Bedroom and living room, toughened glass", project.Quote.Notes);
    }

    [Theory]
    [InlineData("", "", "", "project name")]
    [InlineData("Villa", "not-an-email", "", "email")]
    [InlineData("Villa", "", "call me", "phone")]
    public void SetQuote_RejectsBadInput_AndChangesNothing(string name, string email, string phone, string expected)
    {
        var project = new Project { Name = "Before" };

        var result = QuoteEditor.TrySetQuote(project, name, Quote(email: email, phone: phone));

        Assert.False(result.Success);
        Assert.Contains(expected, result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Before", project.Name);
        Assert.Equal("", project.Quote.Client.FirstName);
    }

    [Fact]
    public void SetQuoteInfoCommand_IsUndoable()
    {
        var project = new Project { Name = "Before" };
        var history = new CommandHistory();

        history.Execute(new SetQuoteInfoCommand(project, "After", Quote(status: QuoteStatus.Won)));
        Assert.Equal(("After", QuoteStatus.Won), (project.Name, project.Quote.Status));

        history.Undo();
        Assert.Equal(("Before", QuoteStatus.Active, ""), (project.Name, project.Quote.Status, project.Quote.Client.FirstName));
        history.Redo();
        Assert.Equal("Archit", project.Quote.Client.FirstName);
    }

    [Fact]
    public void Totals_CountEachDesignAsOftenAsItsQuantity()
    {
        var totals = QuoteTotals.Of(ProjectWith((1000, 1000, 2), (2000, 1500, 3)));

        Assert.Equal(2, totals.Designs);
        Assert.Equal(5, totals.Quantity);
        Assert.Equal(2 * 1.0 + 3 * 3.0, totals.AreaM2, 9);
    }

    [Fact]
    public void ACopyOfAProject_IsANewQuote_WithoutANumber()
    {
        var project = new Project { Quote = Quote() };
        project.Quote.Number = "QT-00003";

        var copy = project.Clone();

        Assert.Equal("", copy.Quote.Number);
        Assert.Equal("Archit", copy.Quote.Client.FirstName);
        copy.Quote.Client.FirstName = "Changed";
        Assert.Equal("Archit", project.Quote.Client.FirstName);
    }

    [Fact]
    public void QuoteDetails_RoundTripThroughTheProjectFile_AndOldFilesGetDefaults()
    {
        var project = new Project { Name = "Villa", Quote = Quote(status: QuoteStatus.Lost) };
        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));
        Assert.Equal(QuoteStatus.Lost, loaded.Quote.Status);
        Assert.Equal("Jaipur", loaded.Quote.Client.City);

        var old = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(new Project()).Replace("\"quote\":", "\"unused\":"));
        Assert.Equal(QuoteStatus.Active, old.Quote.Status);
        Assert.Equal("", old.Quote.Client.FirstName);
    }

    [Fact]
    public void DuplicateFrameCommand_CopiesTheDesign_WithANewReferenceAndIds()
    {
        var project = ProjectWith((1500, 1500, 2));
        var original = project.Frames[0];
        FrameEditor.ApplyTemplate(original, DesignTemplates.Find("sld-2")!, null, Rules);

        var command = DuplicateFrameCommand.Create(project, original, Rules);
        command.Execute();

        var copy = project.Frames[1];
        Assert.Equal("W2", copy.Design.Reference);
        Assert.Equal(2, copy.Design.Quantity);
        Assert.True(copy.X >= original.X + original.Width);
        Assert.Equal(original.GlassPanels.Select(g => g.Opening), copy.GlassPanels.Select(g => g.Opening));
        Assert.Empty(copy.GlassPanels.Select(g => g.Id).Intersect(original.GlassPanels.Select(g => g.Id)));
    }

    // ── Database ────────────────────────────────────────────────────

    [Fact]
    public void Saving_NumbersQuotesInOrder_AndStoresTheirSummary()
    {
        var store = _temp.Open();
        var first = ProjectWith((1000, 1000, 2));
        first.Quote = Quote(status: QuoteStatus.Won);
        var second = ProjectWith((1000, 2000, 1));

        store.Projects.Save(first, new QuoteValue(1234.5m, "INR"));
        _temp.Tick();
        store.Projects.Save(second);
        _temp.Tick();
        store.Projects.Save(first, new QuoteValue(2000m, "INR"));   // re-saving keeps the number

        Assert.Equal("QT-00001", first.Quote.Number);
        Assert.Equal("QT-00002", second.Quote.Number);
        var summary = store.Projects.List().First(s => s.Id == first.Id);
        Assert.Equal(("QT-00001", "Mr. Archit Singh", QuoteStatus.Won), (summary.QuoteNumber, summary.ClientName, summary.Status));
        Assert.Equal((1, 2, 2.0), (summary.DesignCount, summary.Quantity, summary.AreaM2));
        Assert.Equal((2000m, "INR"), (summary.Value, summary.Currency));
        Assert.Null(store.Projects.List().First(s => s.Id == second.Id).Value);
        Assert.Equal("QT-00001", store.Projects.Load(first.Id).Quote.Number);
    }

    [Fact]
    public void ADeletedQuotesNumber_IsNotReusedWhileAHigherOneExists()
    {
        var store = _temp.Open();
        var a = new Project();
        var b = new Project();
        store.Projects.Save(a);
        store.Projects.Save(b);
        store.Projects.Delete(a.Id);

        var c = new Project();
        store.Projects.Save(c);

        Assert.Equal("QT-00003", c.Quote.Number);
    }

    [Fact]
    public void AFailedSave_DoesNotKeepTheNumber()
    {
        var store = _temp.Open();
        var project = new Project();
        File.Delete(_temp.DatabasePath);
        Directory.CreateDirectory(_temp.DatabasePath);   // a folder where the file was: every connection fails

        Assert.Throws<DataStoreException>(() => store.Projects.Save(project));

        Assert.Equal("", project.Quote.Number);
    }

    [Fact]
    public void AVersion1Database_IsUpgraded_AndItsProjectsGetNumbersAndSummaries()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_temp.DatabasePath)!);
        var older = ProjectWith((1200, 1500, 3));
        older.Name = "Saved before quotes";
        var newer = ProjectWith((1000, 1000, 1));
        using (var connection = new SqliteConnection($"Data Source={_temp.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = DatabaseSchema.Version1 + $"""
                PRAGMA application_id = {DatabaseSchema.ApplicationId};
                PRAGMA user_version = 1;
                INSERT INTO projects (id, name, format_version, document_json, created_utc, modified_utc)
                VALUES ('{older.Id:D}', 'Saved before quotes', 1, $older, '2025-01-01T00:00:00.0000000Z', '2025-01-01T00:00:00.0000000Z'),
                       ('{newer.Id:D}', 'Later', 1, $newer, '2025-02-01T00:00:00.0000000Z', '2025-02-01T00:00:00.0000000Z');
                """;
            command.Parameters.AddWithValue("$older", ProjectSerializer.Serialize(older).Replace("\"quote\":", "\"unused\":"));
            command.Parameters.AddWithValue("$newer", ProjectSerializer.Serialize(newer).Replace("\"quote\":", "\"unused\":"));
            command.ExecuteNonQuery();
        }

        var store = _temp.Open();

        Assert.Equal(2, store.Database.ReadSchemaVersion());
        var list = store.Projects.List();
        var summary = list.Single(s => s.Id == older.Id);
        Assert.Equal(("QT-00001", 1, 3), (summary.QuoteNumber, summary.DesignCount, summary.Quantity));
        Assert.Equal("QT-00002", list.Single(s => s.Id == newer.Id).QuoteNumber);
        Assert.Null(summary.Value);
        Assert.Equal("QT-00001", store.Projects.Load(older.Id).Quote.Number);
        Assert.Empty(store.StartupMessages.Where(m => m.Contains("quotes")));
    }

    // ── View models ─────────────────────────────────────────────────

    [Fact]
    public void NewQuote_OpensTheClientTab_AndDetailsAreSavedAsOneUndoStep()
    {
        var (vm, _, _) = Start();
        vm.NewQuote();
        Assert.Equal((AppPage.Quote, QuoteSection.Client), (vm.Page, vm.Section));
        Assert.Equal("New quote", vm.Details.ProjectName);

        vm.Details.ProjectName = "Sharma residence";
        vm.Details.FirstName = "Ravi";
        vm.Details.Status = QuoteStatus.Won;
        Assert.True(vm.Details.HasChanges);
        vm.Details.ApplyCommand.Execute(null);

        Assert.False(vm.Details.HasChanges);
        Assert.Equal(("Sharma residence", "Ravi", QuoteStatus.Won), (vm.Project.Name, vm.Project.Quote.Client.FirstName, vm.Project.Quote.Status));
        Assert.Contains("Ravi", vm.QuoteSubHeader);
        vm.UndoCommand.Execute(null);
        Assert.Equal("New quote", vm.Details.ProjectName);   // the form follows the model on undo
    }

    [Fact]
    public void InvalidDetails_ShowWhy_AndKeepWhatWasTyped()
    {
        var (vm, _, _) = Start();
        vm.Details.Email = "nope";
        vm.Details.ApplyCommand.Execute(null);

        Assert.True(vm.Details.MessageIsError);
        Assert.Contains("email", vm.Details.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("nope", vm.Details.Email);
        Assert.True(vm.Details.HasChanges);
    }

    [Fact]
    public void TheDesignsTab_ShowsACardPerDesign_PricedTimesQuantity()
    {
        var (vm, _, _) = Start();
        vm.CreateFrame();
        vm.Project.Frames[0].Design.Quantity = 3;
        vm.OnDesignChanged();

        vm.Section = QuoteSection.Designs;

        var card = Assert.Single(vm.Designs.Cards);
        Assert.Equal("W1", card.Reference);
        Assert.Equal(3, card.Quantity);
        decimal unit = vm.Calculation.Result.FindFrame(vm.Project.Frames[0].Id)!.Cost.Total;
        Assert.Equal((unit * 3).ToString("N2", System.Globalization.CultureInfo.InvariantCulture), card.TotalPriceText);
        Assert.Contains("1 design · 3 pcs", vm.Designs.TotalsText);
        Assert.Equal(unit * 3, vm.QuoteValueOf().Amount);
    }

    [Fact]
    public void DesignCards_Duplicate_Delete_AndEdit()
    {
        var (vm, dialogs, _) = Start();
        vm.CreateFrame();
        vm.Section = QuoteSection.Designs;

        vm.Designs.Cards[0].DuplicateCommand.Execute(null);
        Assert.Equal(new[] { "W1", "W2" }, vm.Designs.Cards.Select(c => c.Reference));

        dialogs.ConfirmAnswer = false;
        vm.Designs.Cards[1].DeleteCommand.Execute(null);
        Assert.Equal(2, vm.Project.Frames.Count);
        dialogs.ConfirmAnswer = true;
        vm.Designs.Cards[1].DeleteCommand.Execute(null);
        Assert.Single(vm.Designs.Cards);

        vm.Designs.Cards[0].EditCommand.Execute(null);
        Assert.Equal(QuoteSection.Drawing, vm.Section);
        Assert.True(vm.IsSelected(vm.Project.Frames[0].Id));
    }

    [Fact]
    public void TheQuoteList_FiltersBySatusAndSearch_AndOpensAQuote()
    {
        var (vm, _, store) = Start();
        var won = ProjectWith((1000, 1000, 1));
        won.Name = "Gupta villa";
        won.Quote = Quote("Neha", status: QuoteStatus.Won);
        store.Projects.Save(won);
        _temp.Tick();
        store.Projects.Save(new Project { Name = "Office block" });

        vm.Page = AppPage.Quotes;
        Assert.Equal("Active (1)", vm.Quotes.ActiveHeader);
        Assert.Equal("Won (1)", vm.Quotes.WonHeader);
        Assert.Equal("Office block", Assert.Single(vm.Quotes.Quotes).Name);

        vm.Quotes.Filter = QuoteFilter.All;
        vm.Quotes.SearchText = "neha";
        var row = Assert.Single(vm.Quotes.Quotes);
        Assert.Equal(("QT-00001", "Mr. Neha Singh"), (row.Number, row.Client));

        vm.Quotes.OpenCommand.Execute(row);
        Assert.Equal((AppPage.Quote, QuoteSection.Designs), (vm.Page, vm.Section));
        Assert.Equal(won.Id, vm.Project.Id);
        Assert.Single(vm.Designs.Cards);
    }

    [Fact]
    public void OpeningAnotherQuote_AsksAboutUnsavedChanges()
    {
        var (vm, dialogs, store) = Start();
        var saved = new Project { Name = "Saved" };
        store.Projects.Save(saved);
        vm.CreateFrame();   // unsaved change
        dialogs.ConfirmAnswer = false;

        vm.Quotes.Reload();
        vm.Quotes.OpenCommand.Execute(vm.Quotes.Quotes.Single());

        Assert.NotEqual(saved.Id, vm.Project.Id);
        Assert.Single(dialogs.Confirms);
    }

    [Fact]
    public void SavingFromTheDesigner_StoresTheQuoteValueForTheList()
    {
        var (vm, _, store) = Start();
        vm.CreateFrame();
        vm.Project.Frames[0].Design.Quantity = 2;
        vm.OnDesignChanged();

        vm.SaveProjectCommand.Execute(null);

        var summary = store.Projects.List().Single();
        Assert.Equal(vm.QuoteValueOf().Amount, summary.Value);
        Assert.True(summary.Value > 0);
        Assert.Equal("QT-00001", vm.Project.Quote.Number);
        Assert.StartsWith("QT-00001", vm.QuoteHeader);
        Assert.Equal("QT-00001", vm.Details.Number);
    }

    [Fact]
    public void TheDashboard_CountsQuotesByStatus_WithValuesAndWinRate()
    {
        var (vm, _, store) = Start();
        foreach (var (status, value) in new[] { (QuoteStatus.Won, 100m), (QuoteStatus.Won, 50m), (QuoteStatus.Lost, 70m), (QuoteStatus.Active, 30m) })
        {
            var p = ProjectWith((1000, 1000, 2));
            p.Quote.Status = status;
            store.Projects.Save(p, new QuoteValue(value, "INR"));
        }

        vm.Page = AppPage.Dashboard;

        var tiles = vm.Dashboard.Tiles.ToDictionary(t => t.Title);
        Assert.Equal("2", tiles["Won"].Count);
        Assert.StartsWith("150.00 INR", tiles["Won"].Detail);
        Assert.Equal("1", tiles["Lost"].Count);
        Assert.Equal("1", tiles["Active quotes"].Count);
        Assert.Contains("67 %", vm.Dashboard.WinRateText);
        Assert.Equal(1.0, vm.Dashboard.ValueByStatus.Single(b => b.Label == "Won").Fraction, 9);
        Assert.Equal(4, vm.Dashboard.RecentQuotes.Count);
    }
}

/// <summary>The rename to MARK moved the database folder; the earlier version's data comes along once, as a copy.</summary>
public class LegacyDatabaseTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void TheOldDatabase_IsCopiedOnce_AndKeptAsABackup()
    {
        string legacy = System.IO.Path.Combine(_temp.Folder, "old", "fenestration.db");
        var saved = new Project { Name = "From the old version" };
        LocalStore.Open(legacy).Projects.Save(saved);

        Assert.Contains("copied", LocalStore.AdoptLegacyDatabase(_temp.DatabasePath, legacy));
        Assert.Null(LocalStore.AdoptLegacyDatabase(_temp.DatabasePath, legacy));   // never overwrites

        Assert.True(File.Exists(legacy));
        Assert.Equal("From the old version", _temp.Open().Projects.Load(saved.Id).Name);
    }

    [Fact]
    public void WithoutAnOldDatabase_NothingHappens()
        => Assert.Null(LocalStore.AdoptLegacyDatabase(_temp.DatabasePath, System.IO.Path.Combine(_temp.Folder, "missing.db")));
}
