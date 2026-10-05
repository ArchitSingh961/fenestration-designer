using Mark.Licensing;

namespace Mark.Designer.ViewModels;

/// <summary>Milestone 18, purchasing and inventory: Inventory › Stock, Purchasing › Purchase orders and Suppliers.</summary>
public partial class MainViewModel
{
    public InventoryViewModel Inventory { get; private set; } = null!;

    private void CreateInventoryFeatures()
    {
        Inventory = new InventoryViewModel(() => Store, () => Library, () => Calculation.Rules, () => Dialogs, Letterhead, () => Access.UserName)
        {
            Blocked = () => Store is null ? "There is no local database."
                : Access.ReadOnlyMessage
                  ?? (Access.Allows(Features.Inventory) || Access.Allows(Features.Purchasing) ? null : Access.Lock(Features.Inventory))
        };
    }
}
