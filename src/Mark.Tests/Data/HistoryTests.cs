using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Models;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.Licensing;
using Xunit;

namespace Mark.Tests.Data;

/// <summary>Milestone 14, who did what: who created and saved each quote, and every quote's history.</summary>
public class HistoryTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private static readonly ProjectUser Ravi = new("shree", "Ravi Shah");
    private static readonly ProjectUser Amit = new("amit", "Amit Kumar");

    private static Project Quote(string name = "Sharma residence")
    {
        var project = new Project { Name = name };
        project.Quote.Client = new ClientInfo { FirstName = "Archit", LastName = "Singh" };
        return project;
    }

    [Fact]
    public void Saves_RecordWhoCreatedAndWhoSavedLast()
    {
        var store = _temp.Open();
        var project = Quote();

        store.Projects.User = Ravi;
        store.Projects.Save(project, new QuoteValue(1000m, "INR"));
        _temp.Tick();
        store.Projects.User = Amit;
        store.Projects.Save(project, new QuoteValue(1000m, "INR"));

        var summary = store.Projects.List().Single();
        Assert.Equal("Ravi Shah", summary.CreatedBy);
        Assert.Equal("Amit Kumar", summary.ModifiedBy);
        var history = store.Projects.History(project.Id);
        Assert.Equal(new[] { ProjectAction.Saved, ProjectAction.Created }, history.Select(h => h.Action));
        Assert.Equal(new[] { "amit", "shree" }, history.Select(h => h.UserId));
        Assert.Equal("Amit Kumar", history[0].Who);
        Assert.Equal(project.Quote.Number, history[0].QuoteNumber);
    }

    [Fact]
    public void Saving_SaysWhatChanged()
    {
        var store = _temp.Open();
        var project = Quote();
        store.Projects.Save(project, new QuoteValue(120000m, "INR"));

        project.Quote.Status = QuoteStatus.Won;
        project.Name = "Sharma villa";
        store.Projects.Save(project, new QuoteValue(135000m, "INR"));
        store.Projects.Save(project, new QuoteValue(135000m, "INR"));

        var history = store.Projects.History(project.Id);
        Assert.Equal("", history[0].Detail);                     // saved again without changes
        Assert.Contains("Status Active → Won", history[1].Detail);
        Assert.Contains("Renamed \"Sharma residence\" → \"Sharma villa\"", history[1].Detail);
        Assert.Contains("Value 120,000.00 → 135,000.00 INR", history[1].Detail);
    }

    [Fact]
    public void Deleting_IsKeptInTheHistory()
    {
        var store = _temp.Open();
        var project = Quote();
        store.Projects.User = Ravi;
        store.Projects.Save(project);
        _temp.Tick();
        store.Projects.User = Amit;

        store.Projects.Delete(project.Id);

        var deleted = store.Projects.History(project.Id)[0];
        Assert.Equal(ProjectAction.Deleted, deleted.Action);
        Assert.Equal("Amit Kumar", deleted.Who);
        Assert.Equal("Sharma residence", deleted.ProjectName);
    }

    [Fact]
    public void RecentHistory_IsNewestFirst_AcrossQuotes()
    {
        var store = _temp.Open();
        var first = Quote("First");
        var second = Quote("Second");
        store.Projects.Save(first);
        _temp.Tick();
        store.Projects.Save(second);
        _temp.Tick();
        store.Projects.Save(first);

        var recent = store.Projects.RecentHistory(2);

        Assert.Equal(new[] { ("First", ProjectAction.Saved), ("Second", ProjectAction.Created) },
            recent.Select(r => (r.ProjectName, r.Action)));
    }

    [Fact]
    public void Designer_RecordsTheSignedInLogin_AndShowsTheHistory()
    {
        var store = _temp.Open();
        var vm = new MainViewModel(store);
        vm.Access.Apply(LicenceEvaluator.Evaluate(new Licence
        {
            CompanyName = "Shree Windows", UserId = "amit", UserName = "Amit Kumar", Role = UserRoles.Staff,
            Permissions = new[] { Features.Quotes, Features.Drawing }, MachineId = "PC-1", IssuedUtc = TestClockStart,
            ValidUntilUtc = TestClockStart.AddYears(1),
            Products = new[] { new ProductGrant(Product.Upvc, TestClockStart.AddYears(1)) },
            Features = FeatureCatalog.CoreIds.Select(f => new FeatureGrant(f, TestClockStart.AddYears(1))).ToList()
        }, TestClockStart));
        vm.Project.Name = "Sharma residence";
        CreateFrameCommand.Create(vm.Project, 0, 0, 1200, 1500, new DesignRules()).Execute();

        Assert.Null(vm.SaveProject());

        Assert.Equal("Amit Kumar", store.Projects.List().Single().CreatedBy);
        Assert.Single(vm.QuoteHistory);
        Assert.Equal("Amit Kumar", vm.QuoteHistory[0].Who);
        Assert.Contains("Created by Amit Kumar", vm.QuoteAuthorsText);
        vm.Dashboard.Reload();
        Assert.Single(vm.Dashboard.RecentActivity);
        vm.Quotes.Reload();
        Assert.Equal("Amit Kumar", vm.Quotes.Quotes.Single().ModifiedBy);
    }

    [Fact]
    public void WithoutALicence_TheWindowsUserIsRecorded()
    {
        var store = _temp.Open();
        var vm = new MainViewModel(store);
        vm.Project.Name = "Sharma residence";

        Assert.Null(vm.SaveProject());

        Assert.Equal(Environment.UserName, store.Projects.List().Single().CreatedBy);
    }

    private static readonly DateTime TestClockStart = DateTime.UtcNow.Date;
}
