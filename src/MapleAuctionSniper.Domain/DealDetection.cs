namespace MapleAuctionSniper.Domain;

public sealed record DealEvaluation(bool MatchesCriteria, bool IsPotentialDeal, Price? HistoricalMedianPrice, string Reason);

/// <summary>Domain policy boundary: replace without changing the workflow or persistence.</summary>
public interface IDealDetectionStrategy
{
    DealEvaluation Evaluate(AuctionListing listing, MonitoringRule rule, IReadOnlyList<Price> historicalPrices);
}

public sealed class HistoricalMedianDealStrategy : IDealDetectionStrategy
{
    public DealEvaluation Evaluate(AuctionListing listing, MonitoringRule rule, IReadOnlyList<Price> historicalPrices)
    {
        var sorted = historicalPrices.Select(p => p.Amount).Order().ToArray();
        Price? median = sorted.Length == 0 ? null : new Price(sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : sorted[sorted.Length / 2 - 1] / 2m + sorted[sorted.Length / 2] / 2m);
        if (!rule.Criteria.Matches(listing)) return new(false, false, median, "不符合進階搜尋條件");
        if (median is null || median.Value.Amount <= 0) return new(true, false, median, "缺少有效歷史價格，無法判定低價");
        var deal = listing.Price.Amount <= median.Value.Amount * rule.DiscountThreshold;
        return new(true, deal, median, deal ? "符合條件且低於歷史折扣門檻" : "符合條件，但未達歷史折扣門檻");
    }
}
