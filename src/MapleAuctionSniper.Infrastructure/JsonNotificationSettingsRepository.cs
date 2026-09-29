using System.Text.Json;
using MapleAuctionSniper.Application;

namespace MapleAuctionSniper.Infrastructure;

public sealed class JsonNotificationSettingsRepository(string filePath) : INotificationSettingsRepository
{
    public async Task<DiscordNotificationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(filePath)) return new();
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        try
        {
            var data = JsonSerializer.Deserialize<StoredSettings>(json);
            if (data is null || data.Version != 1 || data.WebhookUrl is null) throw new InvalidDataException("通知設定檔格式或版本無效。");
            var settings = new DiscordNotificationSettings(data.Enabled, data.WebhookUrl);
            Validate(settings);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new InvalidDataException("通知設定檔內容無效，請檢查本機設定檔。");
        }
    }

    public async Task SaveAsync(DiscordNotificationSettings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        // Refuse to overwrite a corrupt or newer-version file.
        await LoadAsync(cancellationToken);
        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(new StoredSettings(1, settings.Enabled, settings.WebhookUrl),
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
    private static void Validate(DiscordNotificationSettings settings)
    {
        if (settings.Enabled || settings.WebhookUrl.Length != 0) DiscordWebhookAddress.Parse(settings.WebhookUrl);
    }
    private sealed record StoredSettings(int Version, bool Enabled, string WebhookUrl);
}
