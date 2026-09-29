namespace MapleAuctionSniper.Application;

public sealed class DiscordNotificationSettings(bool enabled = false, string webhookUrl = "")
{
    public bool Enabled { get; } = enabled;
    public string WebhookUrl { get; } = webhookUrl.Trim();
    // Deliberately avoid a record-generated ToString that would reveal the webhook token.
}

public interface INotificationSettingsRepository
{
    Task<DiscordNotificationSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(DiscordNotificationSettings settings, CancellationToken cancellationToken = default);
}

public interface IDiscordWebhookSender
{
    Task SendAsync(string webhookUrl, string message, CancellationToken cancellationToken = default);
}

public sealed class DiscordNotificationService(INotificationSettingsRepository settingsRepository,
    IDiscordWebhookSender sender) : INotificationService
{
    public async Task<bool> NotifyAsync(DealNotification notification, CancellationToken cancellationToken)
    {
        var settings = await settingsRepository.LoadAsync(cancellationToken);
        if (!settings.Enabled) return false;
        var listing = notification.Listing;
        var equipment = listing.Equipment;
        var stats = equipment.Stats;
        var name = equipment.Name.Length > 200 ? equipment.Name[..200] : equipment.Name;
        var reason = notification.Evaluation.Reason.Length > 300 ? notification.Evaluation.Reason[..300] : notification.Evaluation.Reason;
        var message = $"""
            MapleAuctionSniper｜發現潛在低價裝備
            裝備：{name}
            搜尋條件：{string.Join("、", (notification.SearchNames ?? []).Select(n => n.Length > 80 ? n[..80] : n).Take(10))}
            價格：{listing.Price.Amount:N0}
            歷史中位數：{notification.Evaluation.HistoricalMedianPrice?.Amount.ToString("N0") ?? "無"}
            STR {stats.STR} / DEX {stats.DEX} / INT {stats.INT} / LUK {stats.LUK}
            物攻 {stats.WeaponAttack} / 魔攻 {stats.MagicAttack} / 剩餘升級次數 {stats.UpgradeSlots}
            判定：{reason}
            觀測時間：{listing.ObservedAt:yyyy-MM-dd HH:mm:ss zzz}
            請自行確認裝備與價格後購買。
            """;
        await sender.SendAsync(settings.WebhookUrl, message, cancellationToken);
        return true;
    }
}
