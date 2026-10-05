using System.Globalization;
using System.IO;
using System.Windows.Input;
using Mark.Data;
using Mark.Licensing.Api;

namespace Mark.Designer.ViewModels;

/// <summary>
/// Milestone 20, finishing: backup and restore of the local database, the update notice (the latest MARK the owner
/// published), Help (about, keyboard shortcuts) and Ctrl+1 … Ctrl+9 for the areas.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _backupCommand, _restoreCommand, _openBackupsCommand, _aboutCommand, _shortcutsCommand, _areaByNumberCommand,
        _checkUpdatesCommand, _downloadUpdateCommand;

    /// <summary>"1.0.0".</summary>
    public static string AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Restarts MARK (set by the app; after a restore).</summary>
    public Action? RestartRequested { get; set; }

    /// <summary>Asks the licence server for the latest release (set by the app; null without sign-in).</summary>
    public Func<Task<UpdateInfo?>>? FetchLatest { get; set; }

    public ICommand BackupCommand => _backupCommand ??= new RelayCommand(() => Report(Backup(null)), () => HasStore);
    public ICommand RestoreCommand => _restoreCommand ??= new RelayCommand(() => Report(Restore(null)), () => HasStore);
    public ICommand OpenBackupsCommand => _openBackupsCommand ??= new RelayCommand(OpenBackups, () => HasStore);
    public ICommand AboutCommand => _aboutCommand ??= new RelayCommand(() => Dialogs?.Inform("About MARK", AboutText));
    public ICommand ShortcutsCommand => _shortcutsCommand ??= new RelayCommand(() => Dialogs?.Inform("Keyboard shortcuts", ShortcutsText));
    public ICommand CheckUpdatesCommand => _checkUpdatesCommand ??= new RelayCommand(async () => await CheckForUpdateAsync(manual: true));
    public ICommand DownloadUpdateCommand => _downloadUpdateCommand ??= new RelayCommand(() =>
    {
        if (_update is { } update) OpenDocument?.Invoke(update.DownloadUrl);
    });

    /// <summary>Ctrl+1 … Ctrl+9: the areas in the order of the area bar (hidden ones are skipped).</summary>
    public ICommand AreaByNumberCommand => _areaByNumberCommand ??= new RelayCommand(p =>
    {
        if (p is string text && int.TryParse(text, out int n) && n >= 1 && n <= AreaCatalog.All.Count)
        {
            var area = AreaCatalog.All[n - 1].Area;
            if (AreaState(area) != AccessState.Hidden) ShowArea(area);
        }
    });

    public string AboutText => string.Join("\n", new[]
    {
        $"MARK {AppVersion}",
        "Windows and doors: design, quotes, pricing, production, orders, purchasing, stock and accounts.",
        "",
        Access.IsLicensed ? $"Licensed to {Access.CompanyName} · signed in as {Access.UserName}" : "Not signed in.",
        Store is null ? "No local database." : $"Data: {Store.Database.FilePath}",
        Store is null ? "" : $"Backups: {DatabaseBackup.FolderFor(Store.Database.FilePath)} (one a day, the newest 10 kept)"
    }.Where(l => l is not null));

    public static string ShortcutsText => string.Join("\n", new[]
    {
        "Ctrl+N  new quote",
        "Ctrl+O  open a quote",
        "Ctrl+S  save",
        "Ctrl+Z / Ctrl+Y  undo / redo",
        "Ctrl+1 … Ctrl+9  Sales, Design, Pricing, Library, Production, Orders, Purchasing, Inventory, Accounts",
        "F1  these shortcuts",
        "",
        "In the drawing: click selects, Shift+click adds, Ctrl+click toggles, drag on empty space box-selects;",
        "Esc cancels, Del deletes, Ctrl+A selects all. In a drop-down list, type to search; ↓ and Enter choose."
    });

    // ── Backup and restore ──────────────────────────────────────────

    /// <summary>Writes a backup of the local database (to <paramref name="path"/>, or where the user chooses).</summary>
    public string? Backup(string? path)
    {
        if (Store is null) return "There is no local database.";
        path ??= Dialogs?.ChooseSaveFile("Back up MARK", $"MARK backup (*{DatabaseBackup.Extension})|*{DatabaseBackup.Extension}",
            $"MARK backup {DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture)}{DatabaseBackup.Extension}");
        if (path is null) return null;
        try
        {
            DatabaseBackup.Create(Store.Database.FilePath, path, "by hand");
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        Hint = $"Backed up everything in MARK to {Path.GetFileName(path)}. Keep a copy away from this computer (a pen drive, e-mail, cloud drive).";
        HintIsError = false;
        return null;
    }

    /// <summary>
    /// Replaces the local database with a backup (after checking it and asking), keeping the current data as a backup,
    /// then restarts MARK. Returns an error, or null.
    /// </summary>
    public string? Restore(string? path)
    {
        if (Store is null) return "There is no local database.";
        if (Access.ReadOnlyMessage is { } readOnly) return readOnly;
        path ??= Dialogs?.ChooseOpenFile("Restore MARK from a backup", $"MARK backup (*{DatabaseBackup.Extension})|*{DatabaseBackup.Extension}");
        if (path is null) return null;
        BackupInfo info;
        try
        {
            info = DatabaseBackup.Inspect(path);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        if (Dialogs is not null && !Dialogs.Confirm("Restore from backup",
                $"Replace everything in MARK on this computer with the backup of {info.Text}?\n\nWhat is in MARK now is kept as a backup first. MARK starts again afterwards."))
            return null;
        if (!ConfirmDiscardChanges()) return null;
        try
        {
            DatabaseBackup.Restore(path, Store.Database.FilePath);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        ForgetChanges();
        Hint = "Restored the backup. MARK starts again.";
        HintIsError = false;
        RestartRequested?.Invoke();
        return null;
    }

    private void OpenBackups()
    {
        if (Store is null) return;
        string folder = DatabaseBackup.FolderFor(Store.Database.FilePath);
        try
        {
            Directory.CreateDirectory(folder);
            OpenDocument?.Invoke(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(ex.Message);
        }
    }

    // ── Updates ─────────────────────────────────────────────────────

    private UpdateInfo? _update;

    /// <summary>A newer MARK the owner published, or null.</summary>
    public UpdateInfo? Update
    {
        get => _update;
        private set
        {
            if (SetProperty(ref _update, value))
            {
                OnPropertyChanged(nameof(HasUpdate));
                OnPropertyChanged(nameof(UpdateText));
                OnPropertyChanged(nameof(UpdateTip));
            }
        }
    }

    public bool HasUpdate => _update is not null;
    public string UpdateText => _update is null ? "" : $"MARK {_update.Version} available";
    public string UpdateTip => _update is null ? ""
        : $"You have MARK {AppVersion}. Click to download MARK {_update.Version}, then run it to update (your data stays)."
          + (_update.Notes.Length > 0 ? $"\n\nWhat is new:\n{_update.Notes}" : "");

    /// <summary>Asks for the latest release and shows the notice when it is newer than this MARK.</summary>
    public async Task CheckForUpdateAsync(bool manual = false)
    {
        UpdateInfo? latest = FetchLatest is null ? null : await FetchLatest();
        Update = latest is not null && latest.IsNewerThan(AppVersion) ? latest : null;
        if (!manual) return;
        if (FetchLatest is null || latest is null)
            Dialogs?.Inform("Check for updates", "The licence server could not be asked (not signed in, or offline). Try again later.");
        else if (Update is { } update)
            Dialogs?.Inform("Check for updates", $"MARK {update.Version} is available (you have {AppVersion}). Use \"{UpdateText}\" at the top to download it.");
        else
            Dialogs?.Inform("Check for updates", $"MARK {AppVersion} is the latest.");
    }

}
