using System.Windows;

namespace Mark.Designer.Views;

/// <summary>Asks for one line of text (e.g. a project name). UI glue only.</summary>
public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string label, string initialText)
    {
        InitializeComponent();
        Title = title;
        Label.Text = label;
        Input.Text = initialText;
        Input.SelectAll();
    }

    public string Text => Input.Text;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
