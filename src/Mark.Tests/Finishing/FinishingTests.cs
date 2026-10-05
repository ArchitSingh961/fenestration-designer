using System.IO;
using System.IO.Compression;
using Mark.Core.Commands;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.LicenceServer;
using Mark.Licensing.Api;
using Mark.Setup;
using Mark.Tests.Data;
using Mark.Tests.Licensing;
using Xunit;

namespace Mark.Tests.Finishing;

/// <summary>
/// Milestone 20, finishing: backups (by hand, automatic, restore), update checks (published in MARK Owner, offered by
/// MARK), the installer, and the keyboard shortcuts for the areas.
/// </summary>
public class FinishingTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Path_(string name) => Path.Combine(_temp.Folder, name);

    // ── Backups ─────────────────────────────────────────────────────

    [Fact]
    public void ABackup_IsRestored_AndTheDataBeforeIsKept()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var dialogs = new FakeDialogs { PromptAnswer = "Before" };
        var vm = new MainViewModel(store, null, dialogs);
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 0, 0, 1200, 1500, vm.Rules));
        vm.Project.Name = "Before";
        Assert.Null(vm.SaveProject());
        string backup = Path_("one.markbackup");
        Assert.Null(vm.Backup(backup));
        var info = DatabaseBackup.Inspect(backup);
        Assert.Equal(SqliteDatabase.CurrentSchemaVersion, info.SchemaVersion);
        Assert.Equal("by hand", info.Note);

        // Something is added after the backup …
        vm.NewProjectCommand.Execute(null);
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 0, 0, 900, 900, vm.Rules));
        vm.Project.Name = "After";
        Assert.Null(vm.SaveProject());
        Assert.Equal(2, store.Projects.List().Count);

        // … and the restore brings back the state of the backup, keeping the newer data as a backup too.
        bool restarted = false;
        vm.RestartRequested = () => restarted = true;
        Assert.Null(vm.Restore(backup));
        Assert.True(restarted);
        Assert.Contains("Replace everything", dialogs.Confirms.Last());
        var reopened = LocalStore.Open(store.Database.FilePath);
        Assert.Equal(new[] { "Before" }, reopened.Projects.List().Select(p => p.Name));
        var kept = DatabaseBackup.List(store.Database.FilePath).Single(b => b.Info.Note == "before restore");
        DatabaseBackup.Restore(kept.File, store.Database.FilePath);                       // and back again
        Assert.Equal(2, LocalStore.Open(store.Database.FilePath).Projects.List().Count);
    }

    [Fact]
    public void AutomaticBackups_AreDaily_AndTheNewestAreKept()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        string db = store.Database.FilePath;
        var day = new DateTime(2026, 10, 1, 9, 0, 0);
        Assert.NotNull(DatabaseBackup.AutoBackup(db, keep: 3, now: day));
        Assert.Null(DatabaseBackup.AutoBackup(db, keep: 3, now: day.AddHours(5)));      // not again the same day
        foreach (var file in Directory.GetFiles(DatabaseBackup.FolderFor(db))) File.SetLastWriteTime(file, day);
        for (int d = 1; d <= 4; d++)
        {
            Assert.NotNull(DatabaseBackup.AutoBackup(db, keep: 3, now: day.AddDays(d)));
            foreach (var file in Directory.GetFiles(DatabaseBackup.FolderFor(db))) File.SetLastWriteTime(file, day.AddDays(d));
        }
        var autos = Directory.GetFiles(DatabaseBackup.FolderFor(db), "auto-*.markbackup").Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(3, autos.Count);
        Assert.StartsWith("auto-20261003", autos[0]);
    }

    [Fact]
    public void NotABackup_IsRefused_AndNothingChanges()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        string junk = Path_("junk.markbackup");
        File.WriteAllText(junk, "not a zip");
        Assert.Throws<DataStoreException>(() => DatabaseBackup.Inspect(junk));
        var vm = new MainViewModel(store, null, new FakeDialogs());
        Assert.Contains("not a MARK backup", vm.Restore(junk));

        // A zip without a MARK database in it.
        string zip = Path_("other.markbackup");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
            writer.Write("{\"App\":\"MARK\",\"CreatedUtc\":\"2026-10-06T00:00:00Z\",\"SchemaVersion\":1,\"Source\":\"x\",\"Note\":\"\"}");
        Assert.Contains("no database", Assert.Throws<DataStoreException>(() => DatabaseBackup.Inspect(zip)).Message);
    }

    // ── Updates ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.1", "1.0.0", true)]
    [InlineData("1.0.1", "1.0.0.0", true)]
    [InlineData("1.0", "1.0.0", false)]
    [InlineData("0.9.9", "1.0.0", false)]
    [InlineData("", "1.0.0", false)]
    public void ARelease_IsNewer_OnlyWhenItsNumberIs(string latest, string installed, bool newer)
        => Assert.Equal(newer, new UpdateInfo(latest, "https://x", "", default).IsNewerThan(installed));

    [Fact]
    public void TheOwnersRelease_IsOfferedByMark()
    {
        using var server = new TestServer();
        Assert.Equal("", server.Service.Latest().Version);
        Assert.Throws<ApiException>(() => server.Service.SetLatest(new UpdateInfo("one", "https://example.com/setup.exe", "", default)));
        Assert.Throws<ApiException>(() => server.Service.SetLatest(new UpdateInfo("1.2", "ftp://example.com/setup.exe", "", default)));
        var published = server.Service.SetLatest(new UpdateInfo(" 9.1.0 ", "https://example.com/MARK-Setup-9.1.0.exe", "Accounts", default));
        Assert.Equal("9.1.0", published.Version);
        Assert.Equal(server.Clock.Now, published.PublishedUtc);

        var api = new DirectApi(server.Service);
        var vm = new MainViewModel(_temp.Open(TempDatabase.ShippedLibraryPath), null, new FakeDialogs()) { FetchLatest = () => api.LatestAsync()! };
        vm.CheckForUpdateAsync().GetAwaiter().GetResult();
        Assert.True(vm.HasUpdate);
        Assert.Equal("MARK 9.1.0 available", vm.UpdateText);
        Assert.Contains("Accounts", vm.UpdateTip);
        string? opened = null;
        vm.OpenDocument = p => opened = p;
        vm.DownloadUpdateCommand.Execute(null);
        Assert.Equal("https://example.com/MARK-Setup-9.1.0.exe", opened);

        Assert.Equal("", server.Service.SetLatest(UpdateInfo.None).Version);       // withdrawn
        vm.CheckForUpdateAsync().GetAwaiter().GetResult();
        Assert.False(vm.HasUpdate);
    }

    // ── Installer ───────────────────────────────────────────────────

    private static MemoryStream Payload(params (string Name, string Text)[] files)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in files)
                using (var writer = new StreamWriter(zip.CreateEntry(name).Open()))
                    writer.Write(text);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void TheInstaller_InstallsUpdatesAndRemoves_LeavingTheDataAlone()
    {
        string target = Path_("Programs\\MARK");
        var options = new InstallOptions(target, Shortcuts: false, DesktopShortcut: false, Register: false);

        using (var first = Payload(("MARK.exe", "v1"), ("old.dll", "x"), ("Library\\library.json", "{}")))
            Assert.Null(Installer.Install(first, options, "1.0.0"));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(target, "MARK.exe")));

        // An update replaces the files and removes what the new version no longer has.
        using (var second = Payload(("MARK.exe", "v2"), ("Library\\library.json", "{}")))
            Assert.Null(Installer.Install(second, options, "1.1.0"));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(target, "MARK.exe")));
        Assert.False(File.Exists(Path.Combine(target, "old.dll")));
        Assert.False(Directory.Exists(target + ".new"));

        using (var broken = Payload(("readme.txt", "no program")))
            Assert.Contains("MARK.exe is missing", Installer.Install(broken, options, "1.2.0"));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(target, "MARK.exe")));        // untouched

        Assert.Empty(Installer.RunningFrom(target));
        Assert.Null(Installer.Uninstall(target, removeData: false, unregister: false));
        Assert.False(Directory.Exists(target));
    }

    // ── Keyboard ────────────────────────────────────────────────────

    [Fact]
    public void CtrlAndANumber_OpenTheAreas()
    {
        var vm = new MainViewModel(_temp.Open(TempDatabase.ShippedLibraryPath), null, new FakeDialogs());
        vm.AreaByNumberCommand.Execute("5");
        Assert.Equal(AppArea.Production, vm.Area);
        vm.AreaByNumberCommand.Execute("9");
        Assert.Equal(AppArea.Accounts, vm.Area);
        Assert.Equal(AppPage.Invoices, vm.Page);
        vm.AreaByNumberCommand.Execute("0");                              // nothing
        Assert.Equal(AppArea.Accounts, vm.Area);
        Assert.Contains("Ctrl+1", MainViewModel.ShortcutsText);
        Assert.Matches(@"^MARK \d+\.\d+\.\d+", vm.AboutText);
    }
}
