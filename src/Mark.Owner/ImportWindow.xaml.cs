using System.Windows;
using System.Windows.Controls;
using Mark.Owner.ViewModels;

namespace Mark.Owner;

/// <summary>Asks where an imported library file goes: added to the catalogue, replacing it, or one company's own items.</summary>
public partial class ImportWindow : Window
{
    /// <summary>A company with a tick box in "Give its systems to".</summary>
    public sealed class CompanyTick
    {
        public CompanyTick(OwnItemsRow company) => Company = company;
        public OwnItemsRow Company { get; }
        public string Name => Company.Name;
        public bool IsChecked { get; set; }
    }

    private readonly bool _hasSystems;
    private readonly List<CompanyTick> _ticks;

    public ImportWindow(string fileName, string contentText, bool hasCatalogue, bool hasSystems, IReadOnlyList<OwnItemsRow> companies)
    {
        InitializeComponent();
        _hasSystems = hasSystems;
        FileText.Text = $"Import {fileName}";
        ContentText.Text = $"In the file: {contentText}.";
        CompanyBox.ItemsSource = companies;
        _ticks = companies.Select(c => new CompanyTick(c)).ToList();
        GiveList.ItemsSource = _ticks;
        CompanyOption.IsEnabled = companies.Count > 0;
        if (!hasCatalogue)
        {
            AddOption.IsEnabled = false;
            ReplaceOption.IsChecked = true;
        }
        else
            AddOption.IsChecked = true;
        Update();
    }

    /// <summary>The answer, once Import is clicked.</summary>
    public ImportChoice? Choice { get; private set; }

    private void Destination_Changed(object sender, RoutedEventArgs e) => Update();

    private void CompanyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CompanyBox.SelectedItem is not null) CompanyOption.IsChecked = true;
        Update();
    }

    private void Update()
    {
        if (GivePanel is null) return;                                          // while loading
        bool forCompany = CompanyOption.IsChecked == true;
        GivePanel.Visibility = !forCompany && _hasSystems && _ticks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Text = "";
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (CompanyOption.IsChecked == true)
        {
            if (CompanyBox.SelectedItem is not OwnItemsRow company)
            {
                ErrorText.Text = "Choose the company the items are for.";
                return;
            }
            Choice = new ImportChoice(ImportDestination.CompanyOwnItems, company.CompanyId);
        }
        else
        {
            var give = _hasSystems ? _ticks.Where(t => t.IsChecked).Select(t => t.Company.CompanyId).ToList() : new List<Guid>();
            Choice = new ImportChoice(ReplaceOption.IsChecked == true ? ImportDestination.ReplaceCatalogue : ImportDestination.AddToCatalogue,
                null, give);
        }
        DialogResult = true;
    }
}
