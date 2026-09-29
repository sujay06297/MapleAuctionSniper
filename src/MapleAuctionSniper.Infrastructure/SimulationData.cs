using MapleAuctionSniper.Domain;

namespace MapleAuctionSniper.Infrastructure;

public sealed record SimulationData(string Json, IReadOnlyList<AuctionListing> History)
{
    public static SimulationData Create(DateTimeOffset now)
    {
        const string json = """
        [
          { "Id":"demo-1", "Name":"模擬長劍", "Price":75000000, "STR":10, "DEX":5, "INT":0, "LUK":0, "WeaponAttack":100, "MagicAttack":0, "UpgradeSlots":7, "RequiredLevel":100, "Category":"Weapon", "Classification":"模擬分類", "Subclassification":"模擬子分類", "Potential":"特殊", "AdditionalPotential":"稀有" },
          { "Id":"demo-2", "Name":"模擬長劍", "Price":95000000, "STR":10, "DEX":5, "INT":0, "LUK":0, "WeaponAttack":100, "MagicAttack":0, "UpgradeSlots":7, "RequiredLevel":100, "Category":"Weapon", "Classification":"模擬分類", "Subclassification":"模擬子分類", "Potential":"特殊", "AdditionalPotential":"稀有" },
          { "Id":"demo-3", "Name":"模擬長劍", "Price":50000000, "STR":2, "DEX":0, "INT":0, "LUK":0, "WeaponAttack":60, "MagicAttack":0, "UpgradeSlots":3, "RequiredLevel":70, "Category":"Weapon" },
          { "Id":"demo-4", "Name":"模擬法杖", "Price":20000000, "STR":0, "DEX":0, "INT":12, "LUK":5, "WeaponAttack":30, "MagicAttack":110, "UpgradeSlots":7, "RequiredLevel":100, "Category":"Weapon", "Classification":"模擬分類", "Subclassification":"模擬子分類", "Potential":"特殊", "AdditionalPotential":"稀有" }
        ]
        """;
        var equipment = new Equipment("模擬長劍", new(10, 5, 0, 0, 100, 0, 7), 100, searchAttributes: new(ItemCategory.Weapon, "模擬分類", "模擬子分類", "特殊", "稀有"));
        IReadOnlyList<AuctionListing> history = new[] { 90000000m, 100000000m, 110000000m }
            .Select((price, index) => new AuctionListing($"history-{index}", equipment, new(price), now.AddDays(-index - 1))).ToArray();
        return new(json, history);
    }
}
