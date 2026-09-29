namespace MapleAuctionSniper.Domain;

/// <summary>Non-negative amount in the single auction currency; decimals avoid floating point rounding.</summary>
public readonly record struct Price
{
    public decimal Amount { get; }
    public Price(decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Amount = amount;
    }
    public override string ToString() => Amount.ToString("N0");
}

public sealed record EquipmentStats(int STR = 0, int DEX = 0, int INT = 0, int LUK = 0,
    int WeaponAttack = 0, int MagicAttack = 0, int UpgradeSlots = 0);

public sealed record Equipment
{
    public string Name { get; }
    public EquipmentStats Stats { get; }
    // Explicit optional fields for future recognized price-affecting information.
    public int? RequiredLevel { get; }
    public string? Rarity { get; }
    public EquipmentSearchAttributes SearchAttributes { get; }
    public Equipment(string name, EquipmentStats stats, int? requiredLevel = null, string? rarity = null,
        EquipmentSearchAttributes? searchAttributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stats);
        if (stats.UpgradeSlots < 0 || requiredLevel < 0) throw new ArgumentOutOfRangeException(nameof(stats));
        Name = name.Trim(); Stats = stats; RequiredLevel = requiredLevel; Rarity = rarity;
        SearchAttributes = searchAttributes ?? new();
    }
}

public sealed record AuctionListing
{
    public string Id { get; }
    public Equipment Equipment { get; }
    public Price Price { get; }
    public DateTimeOffset ObservedAt { get; }
    public AuctionListing(string id, Equipment equipment, Price price, DateTimeOffset observedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(equipment);
        Id = id; Equipment = equipment; Price = price; ObservedAt = observedAt;
    }
}

public sealed record StatRange
{
    public int? Minimum { get; }
    public int? Maximum { get; }
    public StatRange(int? minimum = null, int? maximum = null)
    {
        if (minimum.HasValue && maximum.HasValue && minimum > maximum)
            throw new ArgumentException("Minimum cannot exceed maximum.");
        Minimum = minimum; Maximum = maximum;
    }
    public bool Contains(int value) => (!Minimum.HasValue || value >= Minimum) && (!Maximum.HasValue || value <= Maximum);
}

public sealed record StatCriteria(
    StatRange? STR = null, StatRange? DEX = null, StatRange? INT = null, StatRange? LUK = null,
    StatRange? WeaponAttack = null, StatRange? MagicAttack = null, StatRange? UpgradeSlots = null)
{
    public bool Matches(EquipmentStats s) =>
        (STR?.Contains(s.STR) ?? true) && (DEX?.Contains(s.DEX) ?? true) &&
        (INT?.Contains(s.INT) ?? true) && (LUK?.Contains(s.LUK) ?? true) &&
        (WeaponAttack?.Contains(s.WeaponAttack) ?? true) && (MagicAttack?.Contains(s.MagicAttack) ?? true) &&
        (UpgradeSlots?.Contains(s.UpgradeSlots) ?? true);
}

public sealed record SearchCriteria
{
    public AuctionSearchFilters Filters { get; }
    public StatCriteria Stats { get; }
    public DetailSearch Details { get; }
    public SearchCriteria(AuctionSearchFilters filters, DetailSearch? details = null, StatCriteria? stats = null)
    {
        ArgumentNullException.ThrowIfNull(filters);
        Filters = filters; Details = details ?? new(); Stats = stats ?? new();
    }
    // Convenience constructor for the original exact-name / max-price use case.
    public SearchCriteria(string equipmentName, Price maxPrice, StatCriteria? stats = null)
        : this(new AuctionSearchFilters(EquipmentName: equipmentName, ExactName: true, Prices: new(maximum: maxPrice)), stats: stats) { }
    public bool Matches(AuctionListing listing) =>
        Filters.Matches(listing) && Stats.Matches(listing.Equipment.Stats) && Details.Matches(listing.Equipment.Stats);
}

public sealed record MonitoringRule
{
    public SearchCriteria Criteria { get; }
    public decimal DiscountThreshold { get; }
    public MonitoringRule(SearchCriteria criteria, decimal discountThreshold = 0.8m)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (discountThreshold <= 0 || discountThreshold > 1) throw new ArgumentOutOfRangeException(nameof(discountThreshold));
        Criteria = criteria; DiscountThreshold = discountThreshold;
    }
}
