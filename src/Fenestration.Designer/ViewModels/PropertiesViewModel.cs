using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Fenestration.Core.Design;
using Fenestration.Core.Models;

namespace Fenestration.Designer.ViewModels;

/// <summary>A single row in the properties panel.</summary>
public record PropertyItem(string Name, string Value, string Unit = "");

/// <summary>
/// Presents the current selection. It is rebuilt from the domain model whenever the selection or the
/// design changes, so it never holds a second copy of the design state. The editable fields are
/// just text the user is typing; Apply hands the parsed values to the host, which runs an undoable
/// command. The model is only changed if the command's validation passes.
/// </summary>
public class PropertiesViewModel : ViewModelBase
{
    public PropertiesViewModel()
    {
        ApplyFrameSizeCommand = new RelayCommand(ApplyFrameSize, () => IsFrameEditable);
        ApplyPositionCommand = new RelayCommand(ApplyPosition, () => IsDivisionEditable);
    }

    /// <summary>Host callback: resize the selected frame. Returns an error message, or null on success.</summary>
    public Func<double, double, string?>? ResizeFrame { get; set; }

    /// <summary>Host callback: move the selected division. Returns an error message, or null on success.</summary>
    public Func<double, string?>? MoveDivision { get; set; }

    private string _header = "NO SELECTION";
    public string Header
    {
        get => _header;
        private set => SetProperty(ref _header, value);
    }

    /// <summary>Read-only information rows.</summary>
    public ObservableCollection<PropertyItem> Items { get; } = new();

    // ── Frame editing ───────────────────────────────────────────────

    private bool _isFrameEditable;
    public bool IsFrameEditable
    {
        get => _isFrameEditable;
        private set => SetProperty(ref _isFrameEditable, value);
    }

    private string _widthText = "";
    public string WidthText
    {
        get => _widthText;
        set => SetProperty(ref _widthText, value);
    }

    private string _heightText = "";
    public string HeightText
    {
        get => _heightText;
        set => SetProperty(ref _heightText, value);
    }

    public ICommand ApplyFrameSizeCommand { get; }

    // ── Division editing ────────────────────────────────────────────

    private bool _isDivisionEditable;
    public bool IsDivisionEditable
    {
        get => _isDivisionEditable;
        private set => SetProperty(ref _isDivisionEditable, value);
    }

    private string _positionText = "";
    public string PositionText
    {
        get => _positionText;
        set => SetProperty(ref _positionText, value);
    }

    private string _positionLabel = "Position";
    /// <summary>"From left" for a mullion, "From top" for a transom.</summary>
    public string PositionLabel
    {
        get => _positionLabel;
        private set => SetProperty(ref _positionLabel, value);
    }

    public ICommand ApplyPositionCommand { get; }

    // ── Messages ────────────────────────────────────────────────────

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    // ── Display ─────────────────────────────────────────────────────

    public void ShowNothing()
    {
        Reset("NO SELECTION");
    }

    public void ShowMultiple(int count)
    {
        Reset($"{count} OBJECTS SELECTED");
    }

    public void ShowFrame(Frame frame)
    {
        Reset("FRAME");
        IsFrameEditable = true;
        WidthText = Format(frame.Width);
        HeightText = Format(frame.Height);

        Items.Add(new PropertyItem("X", Format(frame.X), "mm"));
        Items.Add(new PropertyItem("Y", Format(frame.Y), "mm"));
        if (frame.Profiles.FirstOrDefault(p => p.ProfileType == ProfileType.Frame) is { } outer)
            Items.Add(new PropertyItem("Profile", Format(outer.Thickness), "mm"));
        Items.Add(new PropertyItem("Mullions", frame.Profiles.Count(p => p.ProfileType == ProfileType.Mullion).ToString(CultureInfo.InvariantCulture)));
        Items.Add(new PropertyItem("Transoms", frame.Profiles.Count(p => p.ProfileType == ProfileType.Transom).ToString(CultureInfo.InvariantCulture)));
        Items.Add(new PropertyItem("Glass panels", frame.GlassPanels.Count.ToString(CultureInfo.InvariantCulture)));
    }

    public void ShowProfile(Profile profile)
    {
        bool isDivision = Members.IsDivision(profile);
        Reset(profile.ProfileType.ToString().ToUpperInvariant());

        if (isDivision)
        {
            IsDivisionEditable = true;
            PositionLabel = profile.ProfileType == ProfileType.Mullion ? "From left" : "From top";
            PositionText = Format(Members.DivisionPosition(profile));
        }
        else
        {
            Items.Add(new PropertyItem("Start X", Format(profile.StartPoint.X), "mm"));
            Items.Add(new PropertyItem("Start Y", Format(profile.StartPoint.Y), "mm"));
            Items.Add(new PropertyItem("End X", Format(profile.EndPoint.X), "mm"));
            Items.Add(new PropertyItem("End Y", Format(profile.EndPoint.Y), "mm"));
        }

        Items.Add(new PropertyItem("Length", Format(profile.Length), "mm"));
        Items.Add(new PropertyItem("Thickness", Format(profile.Thickness), "mm"));
        Items.Add(new PropertyItem("Angle", Format(profile.Angle), "°"));
    }

    public void ShowGlass(GlassPanel panel)
    {
        Reset("GLASS");
        Items.Add(new PropertyItem("Width", Format(panel.Boundary.Width), "mm"));
        Items.Add(new PropertyItem("Height", Format(panel.Boundary.Height), "mm"));
        Items.Add(new PropertyItem("Area", (panel.Boundary.Area / 1_000_000.0).ToString("0.###", CultureInfo.InvariantCulture), "m²"));
        Items.Add(new PropertyItem("Thickness", Format(panel.Thickness), "mm"));
    }

    // ── Apply ───────────────────────────────────────────────────────

    private void ApplyFrameSize()
    {
        if (!TryParse(WidthText, out double width) || !TryParse(HeightText, out double height))
        {
            ErrorMessage = "Enter the width and height as numbers in mm.";
            return;
        }
        ErrorMessage = ResizeFrame?.Invoke(width, height);
    }

    private void ApplyPosition()
    {
        if (!TryParse(PositionText, out double position))
        {
            ErrorMessage = "Enter the position as a number in mm.";
            return;
        }
        ErrorMessage = MoveDivision?.Invoke(position);
    }

    /// <summary>Accepts the user's culture ("600,5") and invariant ("600.5") formats.</summary>
    public static bool TryParse(string? text, out double value)
    {
        text = text?.Trim().Replace("mm", "", StringComparison.OrdinalIgnoreCase).Trim();
        return (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
               && double.IsFinite(value);
    }

    private void Reset(string header)
    {
        Header = header;
        Items.Clear();
        IsFrameEditable = false;
        IsDivisionEditable = false;
        ErrorMessage = null;
        ((RelayCommand)ApplyFrameSizeCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ApplyPositionCommand).RaiseCanExecuteChanged();
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
