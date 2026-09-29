using MapleAuctionSniper.Domain;

namespace MapleAuctionSniper.Application;

public sealed record SearchPreset
{
    public string Name { get; }
    public MonitoringRule Rule { get; }
    public SearchPreset(string name, MonitoringRule rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(rule);
        Name = name.Trim(); Rule = rule;
    }
}

/// <summary>Persistence boundary for named search presets, independent of the desktop and file format.</summary>
public interface ISearchPresetRepository
{
    Task<IReadOnlyList<SearchPreset>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SearchPreset preset, CancellationToken cancellationToken = default);
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
