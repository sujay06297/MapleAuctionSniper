using MapleAuctionSniper.Domain;

namespace MapleAuctionSniper.Domain.Tests;

public sealed class AuctionSearchTests
{
    private static AuctionListing Listing(int? level = 100, EquipmentSearchAttributes? attributes = null) =>
        new("item", new("模擬長劍", new(STR: 10, DEX: 5, WeaponAttack: 100), level,
            searchAttributes: attributes ?? new(ItemCategory.Weapon, "分類", "子分類", "潛能", "附加潛能")),
            new(75000000), DateTimeOffset.UtcNow);

    [Fact]
    public void AllBasicFiltersMatchTogether()
    {
        var filters = new AuctionSearchFilters(ItemCategory.Weapon, "分類", "子分類", "潛能", "附加潛能",
            "長劍", Levels: new(100, 100), Prices: new(new Price(75000000), new Price(75000000)));
        Assert.True(new SearchCriteria(filters).Matches(Listing()));
        Assert.False(new SearchCriteria(filters with { Category = ItemCategory.Armor }).Matches(Listing()));
        Assert.False(new SearchCriteria(filters with { Classification = "另一分類" }).Matches(Listing()));
        Assert.False(new SearchCriteria(filters with { Subclassification = "另一子分類" }).Matches(Listing()));
        Assert.False(new SearchCriteria(filters with { Potential = "另一潛能" }).Matches(Listing()));
        Assert.False(new SearchCriteria(filters with { AdditionalPotential = "另一附加潛能" }).Matches(Listing()));
        Assert.False(new SearchCriteria(filters with { Levels = new(101) }).Matches(Listing()));
        Assert.False(new SearchCriteria(filters with { Prices = new(new Price(75000001)) }).Matches(Listing()));
        Assert.False(new SearchCriteria(filters with { Prices = new(maximum: new Price(74999999)) }).Matches(Listing()));
    }

    [Fact]
    public void EmptyFiltersAndUnselectedOrAreUnrestricted() =>
        Assert.True(new SearchCriteria(new AuctionSearchFilters(), new(DetailMatchMode.Or)).Matches(Listing(level: null)));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NameSupportsContainsOrExactMatch(bool exact, bool expected) =>
        Assert.Equal(expected, new SearchCriteria(new AuctionSearchFilters(EquipmentName: "長劍", ExactName: exact)).Matches(Listing()));

    [Fact]
    public void MissingRecognizedValuesCannotSatisfySpecifiedFilters()
    {
        var missing = Listing(level: null, attributes: new());
        Assert.False(new SearchCriteria(new AuctionSearchFilters(Levels: new(1))).Matches(missing));
        Assert.False(new SearchCriteria(new AuctionSearchFilters(Category: ItemCategory.Weapon)).Matches(missing));
        Assert.False(new SearchCriteria(new AuctionSearchFilters(Potential: "潛能")).Matches(missing));
        Assert.True(new SearchCriteria(new AuctionSearchFilters(Levels: new())).Matches(missing));
    }

    [Theory]
    [InlineData(DetailMatchMode.And, false)]
    [InlineData(DetailMatchMode.Or, true)]
    public void DetailModeChangesOnlyCombinationOfSelectedConditions(DetailMatchMode mode, bool expected)
    {
        var details = new DetailSearch(mode, [new(EquipmentStat.STR, 10), new(EquipmentStat.WeaponAttack, 101)]);
        Assert.Equal(expected, new SearchCriteria(new AuctionSearchFilters(), details).Matches(Listing()));
        Assert.False(new SearchCriteria(new AuctionSearchFilters(Category: ItemCategory.Armor), details).Matches(Listing()));
    }

    [Fact]
    public void ThreeDetailConditionsUseInclusiveMinimums()
    {
        var details = new DetailSearch(conditions: [new(EquipmentStat.STR, 10), new(EquipmentStat.DEX, 5), new(EquipmentStat.WeaponAttack, 100)]);
        Assert.True(details.Matches(Listing().Equipment.Stats));
    }

    [Fact]
    public void TooManyDetailsOrReversedPriceRangeAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new DetailSearch(conditions: Enumerable.Repeat(new MinimumStatCondition(EquipmentStat.STR, 1), 4)));
        Assert.Throws<ArgumentException>(() => new PriceRange(new Price(20), new Price(10)));
    }

    [Theory]
    [InlineData(EquipmentStat.STR, 10)]
    [InlineData(EquipmentStat.DEX, 5)]
    [InlineData(EquipmentStat.INT, 8)]
    [InlineData(EquipmentStat.LUK, 6)]
    [InlineData(EquipmentStat.WeaponAttack, 100)]
    [InlineData(EquipmentStat.MagicAttack, 120)]
    [InlineData(EquipmentStat.UpgradeSlots, 7)]
    public void EachDetailStatReadsItsOwnField(EquipmentStat stat, int value)
    {
        var stats = new EquipmentStats(10, 5, 8, 6, 100, 120, 7);
        Assert.True(new MinimumStatCondition(stat, value).Matches(stats));
        Assert.False(new MinimumStatCondition(stat, value + 1).Matches(stats));
    }
}
