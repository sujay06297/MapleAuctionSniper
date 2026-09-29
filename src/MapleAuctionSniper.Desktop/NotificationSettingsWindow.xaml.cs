using System.Windows;

namespace MapleAuctionSniper.Desktop;

public partial class NotificationSettingsWindow : Window
{
    public NotificationSettingsWindow(NotificationSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
