using System.Windows;

namespace Mark.Designer.Views;

/// <summary>"Save changes to …?" with Save, Don't save and Cancel. UI glue only.</summary>
public partial class SaveChangesWindow : Window
{
    public SaveChangesWindow(string title, string question, string detail)
    {
        InitializeComponent();
        Title = title;
        Question.Text = question;
        Detail.Text = detail;
        Detail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>True: save; false: don't save; null: cancel (also when the window is closed).</summary>
    public bool? Choice { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Choice = true;
        DialogResult = true;
    }

    private void DontSave_Click(object sender, RoutedEventArgs e)
    {
        Choice = false;
        DialogResult = true;
    }
}
