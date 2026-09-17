using System.Windows;
using System.Windows.Controls;

namespace AsphaltPlantManager.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && sender is PasswordBox passwordBox)
            viewModel.Settings.NewPassword = passwordBox.Password;
    }
}
