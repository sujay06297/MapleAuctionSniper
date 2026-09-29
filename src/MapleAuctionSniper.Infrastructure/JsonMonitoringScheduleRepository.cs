using System.Text.Json;
using MapleAuctionSniper.Application;

namespace MapleAuctionSniper.Infrastructure;

public sealed class JsonMonitoringScheduleRepository(string filePath) : IMonitoringScheduleRepository
{
    public async Task<MonitoringScheduleSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(filePath)) return new();
        try
        {
            var data = JsonSerializer.Deserialize<StoredSchedule>(await File.ReadAllTextAsync(filePath, cancellationToken));
            if (data is null || data.Version != 1 || data.SearchNames is null) throw new InvalidDataException("監控排程設定格式或版本無效。");
            return new(data.IntervalMinutes, data.SearchNames);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException("監控排程設定內容無效。"); }
    }
    public async Task SaveAsync(MonitoringScheduleSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await LoadAsync(cancellationToken);
        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var data = new StoredSchedule(1, settings.IntervalMinutes, settings.SearchNames.ToArray());
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
    private sealed record StoredSchedule(int Version, decimal IntervalMinutes, string[] SearchNames);
}
