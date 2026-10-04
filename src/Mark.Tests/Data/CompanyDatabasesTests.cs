using System.IO;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Data;
using Xunit;

namespace Mark.Tests.Data;

/// <summary>
/// Each company signed in on a computer has its own local database; the work of each login in the database the companies
/// used to share is brought over to its own company only.
/// </summary>
public class CompanyDatabasesTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }

    private static readonly Guid Shree = Guid.NewGuid(), Om = Guid.NewGuid();
    private static readonly ProjectUser Archit = new("archit", "Archit"), Bengali = new("bengali", "Bengali");

    private string Root => Path.Combine(_temp.Folder, "MARK");

    private LocalStore OpenCompany(Guid company) => LocalStore.Open(CompanyDatabases.PathFor(company, Root));

    private static Project Quote(string name) => new() { Name = name };

    /// <summary>The shared database as it was: an earlier quote nobody is recorded for, one quote each, an enquiry each.</summary>
    private LocalStore Shared()
    {
        var shared = _temp.Open();
        shared.Projects.Save(Quote("Before names"), new QuoteValue(100m, "INR"));
        shared.Projects.User = Archit;
        shared.Projects.Save(Quote("Archit's quote"), new QuoteValue(200m, "INR"));
        var deleted = Quote("Archit's deleted quote");
        shared.Projects.Save(deleted, new QuoteValue(50m, "INR"));
        shared.Projects.Delete(deleted.Id);
        shared.Projects.User = Bengali;
        shared.Projects.Save(Quote("Bengali's quote"), new QuoteValue(300m, "INR"));
        shared.Enquiries.User = Archit;
        shared.Enquiries.Save(new Enquiry { Client = new ClientInfo { FirstName = "Neha" } });
        shared.Enquiries.User = Bengali;
        shared.Enquiries.Save(new Enquiry { Client = new ClientInfo { FirstName = "Rohit" } });
        return shared;
    }

    [Fact]
    public void EachCompany_GetsOnlyItsOwnWork_AndTheEarlierWorkGoesToTheFirstCompany()
    {
        var shared = Shared();

        var shree = OpenCompany(Shree);
        Assert.Contains("2 quotes and 1 enquiry", CompanyDatabases.AdoptSharedWork(shree, shared.Database.FilePath, Shree, "archit", "Archit"));
        var om = OpenCompany(Om);
        Assert.Contains("1 quote and 1 enquiry", CompanyDatabases.AdoptSharedWork(om, shared.Database.FilePath, Om, "bengali", "Bengali"));

        Assert.Equal(new[] { "Archit's quote", "Before names" }, shree.Projects.List().Select(p => p.Name).Order());
        Assert.Equal(new[] { "Bengali's quote" }, om.Projects.List().Select(p => p.Name));
        Assert.Equal(new[] { "Neha" }, shree.Enquiries.List().Select(e => e.ClientName));
        Assert.Equal(new[] { "Rohit" }, om.Enquiries.List().Select(e => e.ClientName));

        // Recent activity: only the company's own (with what Archit deleted), never the other company's.
        Assert.DoesNotContain(shree.Projects.RecentHistory(50), h => h.UserId == "bengali");
        Assert.Contains(shree.Projects.RecentHistory(50), h => h.Action == ProjectAction.Deleted);
        Assert.All(om.Projects.RecentHistory(50), h => Assert.Equal("bengali", h.UserId));
        Assert.NotEmpty(om.Projects.RecentHistory(50));

        // The shared file is kept as it was (a backup).
        Assert.Equal(3, shared.Projects.List().Count);
    }

    [Fact]
    public void A_LoginsWork_IsBroughtOverOnce()
    {
        var shared = Shared();
        var shree = OpenCompany(Shree);
        CompanyDatabases.AdoptSharedWork(shree, shared.Database.FilePath, Shree, "archit", "Archit");
        var copied = shree.Projects.List().Single(p => p.Name == "Archit's quote");
        shree.Projects.Delete(copied.Id);

        Assert.Null(CompanyDatabases.AdoptSharedWork(shree, shared.Database.FilePath, Shree, "archit", "Archit"));

        Assert.DoesNotContain(shree.Projects.List(), p => p.Name == "Archit's quote");                  // stays deleted
    }

    [Fact]
    public void A_LaterLoginOfTheSameCompany_GetsItsOwnWork_ButNotTheEarlierWorkAgain()
    {
        var shared = Shared();
        var om = OpenCompany(Om);
        CompanyDatabases.AdoptSharedWork(om, shared.Database.FilePath, Om, "bengali", "Bengali");     // Om signs in first
        var shree = OpenCompany(Shree);

        CompanyDatabases.AdoptSharedWork(shree, shared.Database.FilePath, Shree, "archit", "Archit");

        Assert.Equal(new[] { "Before names", "Bengali's quote" }, om.Projects.List().Select(p => p.Name).Order());
        Assert.Equal(new[] { "Archit's quote" }, shree.Projects.List().Select(p => p.Name));
    }

    [Fact]
    public void NoSharedDatabase_NothingToDo()
    {
        var shree = OpenCompany(Shree);

        Assert.Null(CompanyDatabases.AdoptSharedWork(shree, Path.Combine(_temp.Folder, "none.db"), Shree, "archit", "Archit"));
        Assert.Empty(shree.Projects.List());
    }

    [Fact]
    public void EachCompany_HasItsOwnFile()
    {
        Assert.NotEqual(CompanyDatabases.PathFor(Shree), CompanyDatabases.PathFor(Om));
        Assert.EndsWith(Path.Combine("MARK", "Companies", Shree.ToString("N"), "mark.db"), CompanyDatabases.PathFor(Shree));
    }
}
