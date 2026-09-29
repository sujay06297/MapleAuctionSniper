using MapleAuctionSniper.Domain;

namespace MapleAuctionSniper.Application;

public sealed class ScanAuction(
    IScreenCapture capture, IAuctionScreenAnalyzer analyzer,
    IAuctionListingRepository repository, IDealDetectionStrategy strategy, INotificationService notifications)
{
    public Task<ScanResult> ExecuteAsync(MonitoringRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return ExecuteForSearchesAsync(new[] { new SearchPreset("目前條件", rule) }, cancellationToken);
    }

    public async Task<ScanResult> ExecuteForSearchesAsync(IReadOnlyList<SearchPreset> searches, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searches);
        var selected = searches.ToArray();
        if (selected.Length == 0 || selected.Any(s => s is null)) throw new ArgumentException("請至少選擇一組搜尋條件。", nameof(searches));
        var frame = await capture.CaptureAsync(cancellationToken);
        var listings = await analyzer.AnalyzeAsync(frame, cancellationToken);
        var evaluated = new List<(AuctionListing Listing, IReadOnlyList<RuleEvaluation> Rules)>();
        // Evaluate the entire batch before saving it: candidates must not become their own reference prices.
        foreach (var listing in listings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prices = await repository.GetHistoricalPricesAsync(listing, cancellationToken);
            evaluated.Add((listing, Array.AsReadOnly(selected.Select(s =>
                new RuleEvaluation(s.Name, strategy.Evaluate(listing, s.Rule, prices))).ToArray())));
        }
        await repository.SaveSnapshotAsync(new(frame.CapturedAt, listings.ToArray()), cancellationToken);
        var results = new List<ListingAnalysis>();
        foreach (var (listing, rules) in evaluated)
        {
            var evaluation = (rules.FirstOrDefault(r => r.Evaluation.IsPotentialDeal) ??
                rules.FirstOrDefault(r => r.Evaluation.MatchesCriteria) ?? rules[0]).Evaluation;
            var notified = false;
            string? notificationError = null;
            if (evaluation.IsPotentialDeal)
            {
                try { notified = await notifications.NotifyAsync(new(listing, evaluation,
                    Array.AsReadOnly(rules.Where(r => r.Evaluation.IsPotentialDeal).Select(r => r.SearchName).ToArray())), cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { notificationError = ex.Message; }
            }
            results.Add(new(listing, evaluation, notified, notificationError, rules));
        }
        return new(results.AsReadOnly());
    }
}
