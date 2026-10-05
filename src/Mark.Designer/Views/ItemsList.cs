using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Mark.Designer.Views;

/// <summary>
/// An <see cref="ItemsControl"/> whose items show to screen readers and UI automation as what they contain (their texts,
/// buttons and boxes), not as one "data item" named after the row's type. A plain ItemsControl wraps each row in an item
/// that hides the buttons inside it (e.g. the stage buttons of an order).
/// </summary>
public class ItemsList : ItemsControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
