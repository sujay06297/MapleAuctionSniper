using System.Windows;

namespace MapleAuctionSniper.Desktop;

public partial class MainWindow : Window
{
    private readonly Func<NotificationSettingsWindow>? createNotificationSettings;
    public MainWindow(Func<NotificationSettingsWindow>? createNotificationSettings = null)
    {
        InitializeComponent();
        this.createNotificationSettings = createNotificationSettings;
        NotificationSettingsButton.IsEnabled = createNotificationSettings is not null;
    }
    private void OpenNotificationSettings(object sender, RoutedEventArgs e)
    {
        if (createNotificationSettings is null) return;
        var dialog = createNotificationSettings();
        dialog.Owner = this;
        dialog.ShowDialog();
    }
}
