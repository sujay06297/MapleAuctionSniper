namespace MapleAuctionSniper.Application;

public sealed class MonitoringScheduleSettings
{
    public decimal IntervalMinutes { get; }
    public IReadOnlyList<string> SearchNames { get; }
    public MonitoringScheduleSettings(decimal intervalMinutes = 5, IEnumerable<string>? searchNames = null)
    {
        if (intervalMinutes <= 0 || intervalMinutes > 1440 || TimeSpan.FromMinutes((double)intervalMinutes) < TimeSpan.FromMilliseconds(1))
            throw new ArgumentOutOfRangeException(nameof(intervalMinutes), "請填入大於零、最多 1440 分鐘的間隔。");
        var names = (searchNames ?? []).ToArray();
        if (names.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("搜尋條件名稱不可空白。");
        IntervalMinutes = intervalMinutes;
        SearchNames = Array.AsReadOnly(names.Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

public interface IMonitoringScheduleRepository
{
    Task<MonitoringScheduleSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MonitoringScheduleSettings settings, CancellationToken cancellationToken = default);
}
