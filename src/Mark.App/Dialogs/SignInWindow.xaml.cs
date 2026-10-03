using System.Windows;
using System.Windows.Controls;
using Mark.Designer.ViewModels;

namespace Mark.App.Dialogs;

/// <summary>The sign-in page shown before MARK opens. Closes with DialogResult true once signed in.</summary>
public partial class SignInWindow : Window
{
    public SignInWindow(SignInViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.SignedIn += () => DialogResult = true;
        if (!string.IsNullOrEmpty(viewModel.UserId))
            Loaded += (_, _) => PasswordBox.Focus();
    }

    /// <summary>A PasswordBox cannot be bound; the password goes to the view model as it is typed.</summary>
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        => ((SignInViewModel)DataContext).Password = ((PasswordBox)sender).Password;
}
