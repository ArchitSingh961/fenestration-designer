using System.Globalization;
using System.Windows.Input;
using Mark.Core.Models;

namespace Mark.Designer.ViewModels;

/// <summary>
/// Edits a frame's design information (reference, quantity, name, location, floor, note, floor distance, colours, mesh
/// type and extra cost) in the
/// properties panel. The fields are text; Apply validates and changes the model through one undoable command.
/// </summary>
public sealed class DesignInfoEditorViewModel : ViewModelBase
{
    private readonly Func<DesignInfo, string?> _apply;

    /// <param name="apply">Stores the new information; returns an error message or null.</param>
    public DesignInfoEditorViewModel(DesignInfo current, Func<DesignInfo, string?> apply)
    {
        ArgumentNullException.ThrowIfNull(current);
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _reference = current.Reference;
        _quantityText = current.Quantity.ToString(CultureInfo.InvariantCulture);
        _name = current.Name;
        _location = current.Location;
        _floor = current.Floor;
        _note = current.Note;
        _floorDistanceText = current.FloorDistanceMm is { } d ? d.ToString("0.#", CultureInfo.InvariantCulture) : "";
        _profileColour = current.ProfileColour;
        _handleColour = current.HandleColour;
        _meshType = current.MeshType;
        _extraCostText = current.ExtraCost == 0 ? "" : current.ExtraCost.ToString("0.##", CultureInfo.InvariantCulture);
        ApplyCommand = new RelayCommand(Apply);
    }

    private string _reference;
    public string Reference { get => _reference; set => SetProperty(ref _reference, value); }

    private string _quantityText;
    public string QuantityText { get => _quantityText; set => SetProperty(ref _quantityText, value); }

    private string _name;
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string _location;
    public string Location { get => _location; set => SetProperty(ref _location, value); }

    private string _floor;
    public string Floor { get => _floor; set => SetProperty(ref _floor, value); }

    private string _note;
    public string Note { get => _note; set => SetProperty(ref _note, value); }

    private string _floorDistanceText;
    /// <summary>Sill height above the finished floor in mm; empty = no floor line.</summary>
    public string FloorDistanceText { get => _floorDistanceText; set => SetProperty(ref _floorDistanceText, value); }

    private string _profileColour;
    public string ProfileColour { get => _profileColour; set => SetProperty(ref _profileColour, value); }

    private string _handleColour;
    public string HandleColour { get => _handleColour; set => SetProperty(ref _handleColour, value); }

    private string _meshType;
    public string MeshType { get => _meshType; set => SetProperty(ref _meshType, value); }

    private string _extraCostText;
    /// <summary>Extra cost per window for this design (the cost sheet's "Extra cost" line); empty = none.</summary>
    public string ExtraCostText { get => _extraCostText; set => SetProperty(ref _extraCostText, value); }

    /// <summary>Usual profile colours, offered in the drop-down (any other can be typed).</summary>
    public static IReadOnlyList<string> Colours { get; } = new[]
    {
        "White", "Black", "Grey", "Anthracite Grey", "Silver", "Champagne", "Bronze", "Brown", "Golden Oak", "Walnut",
        "Mahogany", "Rosewood", "White / Golden Oak", "White / Walnut"
    };

    /// <summary>Usual insect meshes.</summary>
    public static IReadOnlyList<string> MeshTypes { get; } = new[]
    {
        "SS Flymesh", "Fibreglass Mesh", "Pleated Mesh", "Aluminium Mesh", "Pet Mesh"
    };

    public ICommand ApplyCommand { get; }

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

    private void Apply()
    {
        if (!int.TryParse(QuantityText?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int quantity))
        {
            ErrorMessage = "Enter the quantity as a whole number.";
            return;
        }

        double? floorDistance = null;
        if (!string.IsNullOrWhiteSpace(FloorDistanceText))
        {
            if (!PropertiesViewModel.TryParse(FloorDistanceText, out double d))
            {
                ErrorMessage = "Enter the floor distance in mm, or leave it empty.";
                return;
            }
            floorDistance = d;
        }

        decimal extraCost = 0;
        if (!string.IsNullOrWhiteSpace(ExtraCostText) && !PricingViewModel.TryParseDecimal(ExtraCostText, out extraCost))
        {
            ErrorMessage = "Enter the extra cost as an amount, or leave it empty.";
            return;
        }

        ErrorMessage = _apply(new DesignInfo
        {
            Reference = Reference ?? "",
            Quantity = quantity,
            Name = Name ?? "",
            Location = Location ?? "",
            Floor = Floor ?? "",
            Note = Note ?? "",
            FloorDistanceMm = floorDistance,
            ProfileColour = ProfileColour ?? "",
            HandleColour = HandleColour ?? "",
            MeshType = MeshType ?? "",
            ExtraCost = extraCost
        });
    }
}
