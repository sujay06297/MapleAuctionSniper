using MapleAuctionSniper.Domain;

namespace MapleAuctionSniper.Domain.Tests;

public sealed class DealDetectionTests
{
    private readonly HistoricalMedianDealStrategy strategy = new();
    private static AuctionListing Listing(decimal price = 75000000m, int attack = 100) =>
        new("candidate", new("長劍", new(STR: 10, WeaponAttack: attack)), new(price), DateTimeOffset.UtcNow);
    private static MonitoringRule Rule(decimal max = 100000000m) =>
        new(new("長劍", new(max), new(STR: new(10, 20), WeaponAttack: new(90))), 0.8m);

    [Theory]
    [InlineData(99999999, true)]
    [InlineData(100000000, true)]
    [InlineData(100000001, false)]
    public void PriceCeilingIsInclusive(decimal price, bool expected) =>
        Assert.Equal(expected, Rule().Criteria.Matches(Listing(price)));

    [Fact]
    public void InsufficientAttackDoesNotMatchEvenWhenCheap()
    {
        var result = strategy.Evaluate(Listing(100, 89), Rule(), [new(100000000)]);
        Assert.False(result.MatchesCriteria);
        Assert.False(result.IsPotentialDeal);
    }

    [Theory]
    [InlineData(80000000, true)]
    [InlineData(80000001, false)]
    public void HistoricalMedianDiscountBoundaryIsInclusive(decimal price, bool expected)
    {
        var result = strategy.Evaluate(Listing(price), Rule(), [new(110000000), new(90000000), new(100000000)]);
        Assert.True(result.MatchesCriteria);
        Assert.Equal(100000000m, result.HistoricalMedianPrice!.Value.Amount);
        Assert.Equal(expected, result.IsPotentialDeal);
    }

    [Fact]
    public void EvenSampleCountUsesAverageOfMiddlePrices()
    {
        var result = strategy.Evaluate(Listing(), Rule(), [new(120000000), new(90000000), new(110000000), new(80000000)]);
        Assert.Equal(100000000m, result.HistoricalMedianPrice!.Value.Amount);
    }

    [Fact]
    public void MissingOrZeroHistoryNeverProducesDeal()
    {
        Assert.False(strategy.Evaluate(Listing(), Rule(), []).IsPotentialDeal);
        Assert.False(strategy.Evaluate(Listing(0), Rule(), [new(0)]).IsPotentialDeal);
    }

    [Fact]
    public void AllStatRangesMustMatchIncludingUpperBounds()
    {
        var criteria = new StatCriteria(new(5, 10), new(2, 4), new(3, 6), new(1, 5), new(80, 100), new(0, 10), new(4, 7));
        var stats = new EquipmentStats(10, 4, 6, 5, 100, 10, 7);
        Assert.True(criteria.Matches(stats));
        Assert.False(criteria.Matches(stats with { STR = 11 }));
        Assert.False(criteria.Matches(stats with { DEX = 1 }));
        Assert.False(criteria.Matches(stats with { INT = 7 }));
        Assert.False(criteria.Matches(stats with { LUK = 6 }));
        Assert.False(criteria.Matches(stats with { WeaponAttack = 101 }));
        Assert.False(criteria.Matches(stats with { MagicAttack = 11 }));
        Assert.False(criteria.Matches(stats with { UpgradeSlots = 3 }));
    }

    [Fact]
    public void DifferentEquipmentNameDoesNotMatch() =>
        Assert.False(new SearchCriteria("法杖", new(100000000)).Matches(Listing()));

    [Fact]
    public void InvalidModelValuesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Price(-1));
        Assert.Throws<ArgumentException>(() => new StatRange(20, 10));
        Assert.Throws<ArgumentException>(() => new Equipment(" ", new()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Equipment("長劍", new(UpgradeSlots: -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonitoringRule(Rule().Criteria, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonitoringRule(Rule().Criteria, 1.01m));
    }
}
