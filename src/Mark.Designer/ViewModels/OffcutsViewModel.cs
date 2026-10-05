using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Core.Production;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>An offcut in stock: "60mm Frame · 1450 mm · from OR-00012 · 5 Oct 2026".</summary>
public sealed record OffcutRow(long Id, string Profile, string LengthText, string Source, string Added);

/// <summary>A profile's offcuts together: "60mm Frame · 4 offcuts · 5.2 m".</summary>
public sealed record OffcutGroup(string Profile, string Summary, IReadOnlyList<OffcutRow> Offcuts);

/// <summary>
/// Production › Offcuts (Milestone 16): the reusable leftover bars kept in the workshop, by profile. Cutting lists cut
/// from them first; after cutting, a production order takes the ones it used out and puts its new ones in. Offcuts can
/// also be added and removed by hand (e.g. after a stock count).
/// </summary>
public sealed class OffcutsViewModel : ViewModelBase
{
    private readonly Func<LocalStore?> _store;
    private readonly Func<IProductLibrary> _library;

    public OffcutsViewModel(Func<LocalStore?> store, Func<IProductLibrary> library)
    {
        _store = store;
        _library = library;
        AddCommand = new RelayCommand(Add, () => CanChange);
        RemoveCommand = new RelayCommand(p => Remove(p as OffcutRow), _ => CanChange);
    }

    /// <summary>Why changes are refused, or null.</summary>
    public Func<string?> Blocked { get; set; } = () => null;

    public bool CanChange => Blocked() is null;

    public ObservableCollection<OffcutGroup> Groups { get; } = new();

    public bool HasOffcuts => Groups.Count > 0;

    public string SummaryText { get; private set; } = "";

    /// <summary>The profiles an offcut can be of (steel included).</summary>
    public ObservableCollection<LibraryChoice> Profiles { get; } = new();

    private LibraryChoice? _profile;
    public LibraryChoice? Profile { get => _profile; set => SetProperty(ref _profile, value); }

    private string _lengthText = "";
    public string LengthText { get => _lengthText; set => SetProperty(ref _lengthText, value); }

    private string _countText = "1";
    public string CountText { get => _countText; set => SetProperty(ref _countText, value); }

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }

    private string? _message;
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    private bool _messageIsError;
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    public void Reload()
    {
        var library = _library();
        string? keep = _profile?.Id;
        Profiles.Clear();
        foreach (var p in library.Profiles.Where(p => p.IsActive).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            Profiles.Add(new LibraryChoice(p.Id, $"{p.Name}  ({p.Id})"));
        Profile = Profiles.FirstOrDefault(p => p.Id == keep) ?? Profiles.FirstOrDefault();

        Groups.Clear();
        if (_store() is not { } store) return;
        try
        {
            var offcuts = store.Production.Offcuts();
            foreach (var group in offcuts.GroupBy(o => o.DefinitionId))
            {
                string name = library.FindProfile(group.Key)?.Name ?? group.Key;
                var rows = group.Select(o => new OffcutRow(o.Id, name, $"{Mm(o.LengthMm)} mm",
                    o.Source.Length > 0 ? $"from {o.Source}" : "added by hand",
                    o.AddedUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture))).ToList();
                double metres = group.Sum(o => o.LengthMm) / 1000.0;
                Groups.Add(new OffcutGroup(name,
                    $"{rows.Count} offcut{(rows.Count == 1 ? "" : "s")} · {metres.ToString("0.##", CultureInfo.InvariantCulture)} m", rows));
            }
            SummaryText = offcuts.Count == 0 ? "No offcuts in stock."
                : $"{offcuts.Count} offcut{(offcuts.Count == 1 ? "" : "s")} of {Groups.Count} profile{(Groups.Count == 1 ? "" : "s")} in stock.";
        }
        catch (DataStoreException ex)
        {
            Show(ex.Message, true);
        }
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasOffcuts));
    }

    private void Add()
    {
        if (Blocked() is { } blocked)
        {
            Show(blocked, true);
            return;
        }
        if (_store() is not { } store) return;
        if (Profile?.Id is not { } id)
        {
            Show("Choose the profile of the offcut.", true);
            return;
        }
        if (!PropertiesViewModel.TryParse(LengthText, out double length) || length <= 0)
        {
            Show("Enter the length of the offcut in mm.", true);
            return;
        }
        if (!int.TryParse(CountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count < 1 || count > 500)
        {
            Show("Enter how many offcuts (1–500).", true);
            return;
        }
        try
        {
            store.Production.AddOffcuts(id, length, count, "");
            LengthText = "";
            CountText = "1";
            Reload();
            Show($"Added {count} offcut{(count == 1 ? "" : "s")} of {Mm(length)} mm.", false);
        }
        catch (DataStoreException ex)
        {
            Show(ex.Message, true);
        }
    }

    private void Remove(OffcutRow? row)
    {
        if (row is null || _store() is not { } store) return;
        if (Blocked() is { } blocked)
        {
            Show(blocked, true);
            return;
        }
        try
        {
            store.Production.RemoveOffcut(row.Id);
            Reload();
            Show($"Removed an offcut of {row.LengthText} ({row.Profile}).", false);
        }
        catch (DataStoreException ex)
        {
            Show(ex.Message, true);
        }
    }

    private static string Mm(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private void Show(string message, bool isError)
    {
        MessageIsError = isError;
        Message = message;
    }
}
