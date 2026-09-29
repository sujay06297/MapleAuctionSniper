using MapleAuctionSniper.Domain;

namespace MapleAuctionSniper.Application;

/// <summary>Opaque frame payload. Real adapters can provide image bytes and media type; no coordinates here.</summary>
public sealed record CapturedFrame(ReadOnlyMemory<byte> Content, string MediaType, DateTimeOffset CapturedAt);
public sealed record AuctionSnapshot(DateTimeOffset CapturedAt, IReadOnlyList<AuctionListing> Listings);
public sealed record DealNotification(AuctionListing Listing, DealEvaluation Evaluation, IReadOnlyList<string>? SearchNames = null);
public sealed record RuleEvaluation(string SearchName, DealEvaluation Evaluation);
public sealed record ListingAnalysis(AuctionListing Listing, DealEvaluation Evaluation, bool NotificationTriggered,
    string? NotificationError = null, IReadOnlyList<RuleEvaluation>? RuleEvaluations = null)
{
    public string MatchedSearchNames => string.Join("、", (RuleEvaluations ?? []).Where(r => r.Evaluation.MatchesCriteria).Select(r => r.SearchName));
}
public sealed record ScanResult(IReadOnlyList<ListingAnalysis> Listings);

public interface IScreenCapture
{
    Task<CapturedFrame> CaptureAsync(CancellationToken cancellationToken);
}
public interface IAuctionScreenAnalyzer
{
    Task<IReadOnlyList<AuctionListing>> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken);
}
public interface IAuctionListingRepository
{
    /// <summary>Return earlier observations of comparable equipment, excluding the current listing identity.</summary>
    Task<IReadOnlyList<Price>> GetHistoricalPricesAsync(AuctionListing listing, CancellationToken cancellationToken);
    Task SaveSnapshotAsync(AuctionSnapshot snapshot, CancellationToken cancellationToken);
}
public interface INotificationService
{
    /// <returns>True when the adapter accepted the notification.</returns>
    Task<bool> NotifyAsync(DealNotification notification, CancellationToken cancellationToken);
}
