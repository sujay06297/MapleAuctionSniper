using System.Text;
using System.Text.Json;
using MapleAuctionSniper.Application;
using MapleAuctionSniper.Domain;

namespace MapleAuctionSniper.Infrastructure;

/// <summary>Deterministic data only; does not access the screen or the game.</summary>
public sealed class FakeScreenCapture(string json, TimeProvider timeProvider) : IScreenCapture
{
    public Task<CapturedFrame> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CapturedFrame(Encoding.UTF8.GetBytes(json), "application/json", timeProvider.GetUtcNow()));
    }
}

public sealed class FakeAuctionScreenAnalyzer : IAuctionScreenAnalyzer
{
    public Task<IReadOnlyList<AuctionListing>> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (frame.MediaType != "application/json") throw new ArgumentException("Fake analyzer expects JSON.", nameof(frame));
        var options = new JsonSerializerOptions();
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter<ItemCategory>());
        var rows = JsonSerializer.Deserialize<SimulatedListing[]>(frame.Content.Span, options)
            ?? throw new JsonException("Expected listing array.");
        IReadOnlyList<AuctionListing> listings = rows.Select(r => new AuctionListing(r.Id,
            new Equipment(r.Name, new(r.STR, r.DEX, r.INT, r.LUK, r.WeaponAttack, r.MagicAttack, r.UpgradeSlots),
                r.RequiredLevel, searchAttributes: new(r.Category, r.Classification, r.Subclassification, r.Potential, r.AdditionalPotential)),
            new Price(r.Price), frame.CapturedAt)).ToArray();
        return Task.FromResult(listings);
    }
    private sealed record SimulatedListing(string Id, string Name, decimal Price, int STR, int DEX,
        int INT, int LUK, int WeaponAttack, int MagicAttack, int UpgradeSlots,
        int? RequiredLevel = null, ItemCategory? Category = null, string? Classification = null,
        string? Subclassification = null, string? Potential = null, string? AdditionalPotential = null);
}

/// <summary>Single-user simulation store. Comparable means equal name, stats, level and rarity.</summary>
public sealed class InMemoryAuctionListingRepository(IEnumerable<AuctionListing>? seed = null) : IAuctionListingRepository
{
    private readonly Dictionary<string, AuctionListing> observations = (seed ?? []).ToDictionary(l => l.Id);
    private readonly List<AuctionSnapshot> snapshots = [];
    public IReadOnlyList<AuctionSnapshot> Snapshots => snapshots.AsReadOnly();

    public Task<IReadOnlyList<Price>> GetHistoricalPricesAsync(AuctionListing listing, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<Price> prices = observations.Values.Where(h => h.Id != listing.Id &&
            h.ObservedAt < listing.ObservedAt && h.Equipment == listing.Equipment).Select(h => h.Price).ToArray();
        return Task.FromResult(prices);
    }
    public Task SaveSnapshotAsync(AuctionSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        snapshots.Add(new(snapshot.CapturedAt, snapshot.Listings.ToArray()));
        foreach (var listing in snapshot.Listings) observations[listing.Id] = listing;
        return Task.CompletedTask;
    }
}

/// <summary>Records accepted notifications in memory, visible through the desktop scan results.</summary>
public sealed class RecordingNotificationService : INotificationService
{
    private readonly List<DealNotification> sent = [];
    public IReadOnlyList<DealNotification> Sent => sent.AsReadOnly();
    public Task<bool> NotifyAsync(DealNotification notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sent.Add(notification);
        return Task.FromResult(true);
    }
}
