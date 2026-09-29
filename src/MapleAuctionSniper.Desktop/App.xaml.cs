using System.Windows;
using MapleAuctionSniper.Application;
using MapleAuctionSniper.Domain;
using MapleAuctionSniper.Infrastructure;

namespace MapleAuctionSniper.Desktop;

public partial class App : System.Windows.Application
{
    private System.Net.Http.HttpClient? httpClient;
    private MainViewModel? viewModel;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Composition root: constructor injection, no service locator or global mutable state.
        var clock = TimeProvider.System;
        var data = SimulationData.Create(clock.GetUtcNow());
        var settingsDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleAuctionSniper");
        var notificationSettings = new JsonNotificationSettingsRepository(System.IO.Path.Combine(settingsDirectory, "notification-settings.json"));
        httpClient = new(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        var webhookSender = new DiscordWebhookSender(httpClient);
        var workflow = new ScanAuction(new FakeScreenCapture(data.Json, clock), new FakeAuctionScreenAnalyzer(),
            new InMemoryAuctionListingRepository(data.History), new HistoricalMedianDealStrategy(), new DiscordNotificationService(notificationSettings, webhookSender));
        var presetPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MapleAuctionSniper", "search-presets.json");
        viewModel = new MainViewModel(workflow, new JsonSearchPresetRepository(presetPath), new ScheduledAuctionMonitor(workflow, clock),
            new JsonMonitoringScheduleRepository(System.IO.Path.Combine(settingsDirectory, "monitoring-schedule.json")));
        MainWindow = new MainWindow(() => new NotificationSettingsWindow(new(notificationSettings, webhookSender))) { DataContext = viewModel };
        MainWindow.Show();
        await viewModel.LoadPresetsAsync();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        viewModel?.StopMonitoring();
        httpClient?.Dispose();
        base.OnExit(e);
    }
}
