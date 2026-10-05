using System.Globalization;
using System.Windows.Input;
using Mark.Designer.ViewModels;
using Mark.Licensing.Api;

namespace Mark.Owner.ViewModels;

/// <summary>
/// MARK Owner › Updates (Milestone 20): the latest MARK release — version, download link (e.g. the MARK Setup file on
/// your website or drive) and what is new. Every MARK asks for it and offers the download when it is newer than itself.
/// </summary>
public sealed class UpdatesViewModel : OwnerPage
{
    public UpdatesViewModel(OwnerApiClient api, IOwnerDialogs dialogs, Action sessionEnded) : base(api, dialogs, sessionEnded)
    {
        PublishCommand = new AsyncCommand(PublishAsync);
        WithdrawCommand = new AsyncCommand(WithdrawAsync);
    }

    private string _version = "";
    public string Version { get => _version; set => SetProperty(ref _version, value); }

    private string _downloadUrl = "";
    public string DownloadUrl { get => _downloadUrl; set => SetProperty(ref _downloadUrl, value); }

    private string _notes = "";
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }

    private string _currentText = "";
    /// <summary>What MARK is offered now.</summary>
    public string CurrentText { get => _currentText; private set => SetProperty(ref _currentText, value); }

    /// <summary>The version of this MARK Owner (MARK is released with the same number).</summary>
    public string OwnerVersionText => $"This MARK Owner is version {typeof(UpdatesViewModel).Assembly.GetName().Version?.ToString(3) ?? "?"}.";

    public ICommand PublishCommand { get; }
    public ICommand WithdrawCommand { get; }

    public override Task LoadAsync() => RunAsync(async () => Show(await Api.LatestAsync()));

    private void Show(UpdateInfo latest)
    {
        CurrentText = latest.Version.Length == 0
            ? "No release published: MARK does not offer an update."
            : $"MARK {latest.Version} published {latest.PublishedUtc.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)}: every MARK older than it offers {latest.DownloadUrl}.";
        Version = latest.Version;
        DownloadUrl = latest.DownloadUrl;
        Notes = latest.Notes;
    }

    public Task PublishAsync() => RunAsync(async () => Show(await Api.SetLatestAsync(new UpdateInfo(Version, DownloadUrl, Notes, default))),
        "Published: each MARK sees it when it starts or checks in.");

    public Task WithdrawAsync()
    {
        if (!Dialogs.Confirm("Withdraw release", "Stop offering this release to MARK?")) return Task.CompletedTask;
        return RunAsync(async () => Show(await Api.SetLatestAsync(UpdateInfo.None)), "MARK no longer offers an update.");
    }
}
