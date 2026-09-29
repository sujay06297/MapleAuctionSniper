namespace MapleAuctionSniper.Domain;

public enum ItemCategory { Armor, Weapon, Consumable }
public enum DetailMatchMode { And, Or }
public enum EquipmentStat { STR, DEX, INT, LUK, WeaponAttack, MagicAttack, UpgradeSlots }

// Dropdown contents are not visible in the supplied screenshots. Keep their labels extensible.
public sealed record EquipmentSearchAttributes(ItemCategory? Category = null, string? Classification = null,
    string? Subclassification = null, string? Potential = null, string? AdditionalPotential = null);

public sealed record PriceRange
{
    public Price? Minimum { get; }
    public Price? Maximum { get; }
    public PriceRange(Price? minimum = null, Price? maximum = null)
    {
        if (minimum?.Amount > maximum?.Amount) throw new ArgumentException("價格下限不可超過上限。");
        Minimum = minimum; Maximum = maximum;
    }
    public bool Contains(Price price) => (!Minimum.HasValue || price.Amount >= Minimum.Value.Amount) &&
        (!Maximum.HasValue || price.Amount <= Maximum.Value.Amount);
}

public sealed record AuctionSearchFilters(ItemCategory? Category = null, string? Classification = null,
    string? Subclassification = null, string? Potential = null, string? AdditionalPotential = null,
    string? EquipmentName = null, bool ExactName = false, StatRange? Levels = null, PriceRange? Prices = null)
{
    public bool Matches(AuctionListing listing)
    {
        var item = listing.Equipment;
        var attributes = item.SearchAttributes;
        var name = EquipmentName?.Trim();
        return (!Category.HasValue || attributes.Category == Category) &&
            MatchesLabel(Classification, attributes.Classification) && MatchesLabel(Subclassification, attributes.Subclassification) &&
            MatchesLabel(Potential, attributes.Potential) && MatchesLabel(AdditionalPotential, attributes.AdditionalPotential) &&
            (string.IsNullOrEmpty(name) || (ExactName ? string.Equals(name, item.Name, StringComparison.OrdinalIgnoreCase)
                : item.Name.Contains(name, StringComparison.OrdinalIgnoreCase))) &&
            (Levels is null || (Levels.Minimum is null && Levels.Maximum is null) ||
                (item.RequiredLevel.HasValue && Levels.Contains(item.RequiredLevel.Value))) &&
            (Prices?.Contains(listing.Price) ?? true);
    }
    private static bool MatchesLabel(string? filter, string? value) => string.IsNullOrWhiteSpace(filter) ||
        string.Equals(filter.Trim(), value?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public sealed record MinimumStatCondition
{
    public EquipmentStat Stat { get; }
    public int Minimum { get; }
    public MinimumStatCondition(EquipmentStat stat, int minimum)
    {
        if (!Enum.IsDefined(stat)) throw new ArgumentOutOfRangeException(nameof(stat));
        Stat = stat; Minimum = minimum;
    }
    public bool Matches(EquipmentStats stats) => (Stat switch
    {
        EquipmentStat.STR => stats.STR, EquipmentStat.DEX => stats.DEX,
        EquipmentStat.INT => stats.INT, EquipmentStat.LUK => stats.LUK,
        EquipmentStat.WeaponAttack => stats.WeaponAttack, EquipmentStat.MagicAttack => stats.MagicAttack,
        EquipmentStat.UpgradeSlots => stats.UpgradeSlots,
        _ => throw new InvalidOperationException("Unknown stat.")
    }) >= Minimum;
}

/// <summary>At most three selected conditions. Empty OR is unrestricted, not automatically false.</summary>
public sealed record DetailSearch
{
    public DetailMatchMode Mode { get; }
    public IReadOnlyList<MinimumStatCondition> Conditions { get; }
    public DetailSearch(DetailMatchMode mode = DetailMatchMode.And, IEnumerable<MinimumStatCondition>? conditions = null)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var selected = (conditions ?? []).ToArray();
        if (selected.Length > 3) throw new ArgumentException("詳細搜尋最多三個條件。", nameof(conditions));
        if (selected.Any(c => c is null)) throw new ArgumentException("條件不可為 null。", nameof(conditions));
        Mode = mode; Conditions = Array.AsReadOnly(selected);
    }
    public bool Matches(EquipmentStats stats) => Conditions.Count == 0 ||
        (Mode == DetailMatchMode.And ? Conditions.All(c => c.Matches(stats)) : Conditions.Any(c => c.Matches(stats)));
}
