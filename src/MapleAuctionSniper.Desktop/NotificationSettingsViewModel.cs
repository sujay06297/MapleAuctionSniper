using System.ComponentModel;
using System.Runtime.CompilerServices;
using MapleAuctionSniper.Application;

namespace MapleAuctionSniper.Desktop;

public sealed class NotificationSettingsViewModel : INotifyPropertyChanged
{
    private readonly INotificationSettingsRepository repository;
    private readonly IDiscordWebhookSender sender;
    private bool busy = true;
    private bool enabled;
    private string webhookUrl = "";
    private string status = "正在讀取設定…";
    public bool Enabled { get => enabled; set { enabled = value; OnPropertyChanged(); } }
    public string WebhookUrl { get => webhookUrl; set { webhookUrl = value; OnPropertyChanged(); TestCommand.RaiseCanExecuteChanged(); } }
    public bool CanEdit => !busy;
    public string Status { get => status; private set { status = value; OnPropertyChanged(); } }
    public UiCommand SaveCommand { get; }
    public UiCommand TestCommand { get; }

    public NotificationSettingsViewModel(INotificationSettingsRepository repository, IDiscordWebhookSender sender)
    {
        this.repository = repository; this.sender = sender;
        SaveCommand = new(SaveAsync, () => !busy);
        TestCommand = new(TestAsync, () => !busy && !string.IsNullOrWhiteSpace(WebhookUrl));
    }
    public async Task LoadAsync()
    {
        try
        {
            var settings = await repository.LoadAsync();
            Enabled = settings.Enabled; WebhookUrl = settings.WebhookUrl;
            Status = "填入目標文字頻道的 Webhook；儲存後套用至後續掃描。";
        }
        catch (Exception ex) { Status = $"讀取失敗：{ex.Message}"; }
        finally { SetBusy(false); }
    }
    private async Task SaveAsync()
    {
        SetBusy(true);
        try
        {
            await repository.SaveAsync(new(Enabled, WebhookUrl));
            Status = Enabled ? "已儲存並啟用 Discord 通知。" : "已儲存，Discord 通知已停用。";
        }
        catch (Exception ex) { Status = $"儲存失敗：{ex.Message}"; }
        finally { SetBusy(false); }
    }
    private async Task TestAsync()
    {
        SetBusy(true); Status = "正在發送測試通知…";
        try
        {
            // Test the current input without saving it or changing the enabled setting.
            await sender.SendAsync(WebhookUrl, "MapleAuctionSniper｜測試通知\n若看到此訊息，表示 Webhook 可以發送至此頻道。");
            Status = "測試通知已送出。設定尚未自動儲存，請按儲存套用。";
        }
        catch (Exception ex) { Status = $"測試失敗：{ex.Message}"; }
        finally { SetBusy(false); }
    }
    private void SetBusy(bool value)
    {
        busy = value; OnPropertyChanged(nameof(CanEdit));
        SaveCommand.RaiseCanExecuteChanged(); TestCommand.RaiseCanExecuteChanged();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
