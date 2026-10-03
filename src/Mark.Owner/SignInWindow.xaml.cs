using System.Windows;
using System.Windows.Controls;
using Mark.Owner.ViewModels;

namespace Mark.Owner;

/// <summary>MARK Owner's sign-in (or first admin setup). Connects to the server as soon as it opens.</summary>
public partial class SignInWindow : Window
{
    public SignInWindow(OwnerSignInViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            UserIdBox.Focus();
            await viewModel.ConnectAsync();
        };
    }

    private OwnerSignInViewModel ViewModel => (OwnerSignInViewModel)DataContext;

    private void Password_Changed(object sender, RoutedEventArgs e) => ViewModel.Password = ((PasswordBox)sender).Password;

    private void ConfirmPassword_Changed(object sender, RoutedEventArgs e) => ViewModel.ConfirmPassword = ((PasswordBox)sender).Password;
}
