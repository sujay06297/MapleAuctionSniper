using MapleAuctionSniper.Application;
using MapleAuctionSniper.Domain;
using MapleAuctionSniper.Infrastructure;

namespace MapleAuctionSniper.Application.Tests;

public sealed class SearchPresetPersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MapleAuctionSniper.Tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "search-presets.json");
    private static SearchPreset Preset(string name = "長劍搜尋", decimal max = 80000000) => new(name,
        new(new SearchCriteria(new AuctionSearchFilters(ItemCategory.Weapon, "分類", "子分類", "特殊", "稀有", "長劍", false,
            new(80, 120), new(new Price(70000000), new Price(max))),
            new(DetailMatchMode.Or, [new(EquipmentStat.STR, 10), new(EquipmentStat.WeaponAttack, 90), new(EquipmentStat.DEX, 5)]),
            new(MagicAttack: new(0, 20))), 0.75m));

    [Fact]
    public async Task MissingFileLoadsEmptyWithoutCreatingFile()
    {
        Assert.Empty(await new JsonSearchPresetRepository(FilePath).LoadAsync());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task FreshRepositoryRestoresCompleteRuleAndMatchingBehavior()
    {
        var expected = Preset();
        await new JsonSearchPresetRepository(FilePath).SaveAsync(expected);
        var actual = Assert.Single(await new JsonSearchPresetRepository(FilePath).LoadAsync());
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Rule.DiscountThreshold, actual.Rule.DiscountThreshold);
        Assert.Equal(expected.Rule.Criteria.Filters, actual.Rule.Criteria.Filters);
        Assert.Equal(expected.Rule.Criteria.Stats, actual.Rule.Criteria.Stats);
        Assert.Equal(DetailMatchMode.Or, actual.Rule.Criteria.Details.Mode);
        Assert.Equal(expected.Rule.Criteria.Details.Conditions, actual.Rule.Criteria.Details.Conditions);
        var item = new AuctionListing("listing", new("模擬長劍", new(STR: 10), 100,
            searchAttributes: new(ItemCategory.Weapon, "分類", "子分類", "特殊", "稀有")), new(75000000), DateTimeOffset.UtcNow);
        Assert.True(actual.Rule.Criteria.Matches(item));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task SameNameUpdatesWhileOtherNamesRemainAndDeletionPersists()
    {
        var repository = new JsonSearchPresetRepository(FilePath);
        await repository.SaveAsync(Preset("Sword"));
        await repository.SaveAsync(Preset("法杖"));
        await repository.SaveAsync(Preset("sword", 90000000));
        var loaded = await repository.LoadAsync();
        Assert.Equal(2, loaded.Count);
        Assert.Equal(90000000m, Assert.Single(loaded, p => p.Name == "sword").Rule.Criteria.Filters.Prices!.Maximum!.Value.Amount);
        await repository.DeleteAsync("SWORD");
        Assert.Equal("法杖", Assert.Single(await new JsonSearchPresetRepository(FilePath).LoadAsync()).Name);
    }

    [Fact]
    public async Task UnboundedAndEmptyOrConditionsRoundTrip()
    {
        var repository = new JsonSearchPresetRepository(FilePath);
        await repository.SaveAsync(new("全體搜尋", new(new SearchCriteria(new AuctionSearchFilters(), new(DetailMatchMode.Or)))));
        var actual = Assert.Single(await repository.LoadAsync());
        Assert.Null(actual.Rule.Criteria.Filters.Category);
        Assert.Null(actual.Rule.Criteria.Filters.Prices!.Minimum);
        Assert.Null(actual.Rule.Criteria.Filters.Prices.Maximum);
        Assert.Empty(actual.Rule.Criteria.Details.Conditions);
        Assert.True(actual.Rule.Criteria.Matches(new("id", new("任何裝備", new()), new(0), DateTimeOffset.UtcNow)));
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{\"Version\":99,\"Presets\":[]}")]
    [InlineData("{\"Version\":1,\"Presets\":[null]}")]
    public async Task InvalidFileIsReportedAndNeverOverwritten(string invalid)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(FilePath, invalid);
        var repository = new JsonSearchPresetRepository(FilePath);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.SaveAsync(Preset()));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.DeleteAsync("長劍搜尋"));
        Assert.Equal(invalid, await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task CancelledSavePreservesExistingFile()
    {
        var repository = new JsonSearchPresetRepository(FilePath);
        await repository.SaveAsync(Preset());
        var original = await File.ReadAllTextAsync(FilePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.SaveAsync(Preset("另一組"), cancellation.Token));
        Assert.Equal(original, await File.ReadAllTextAsync(FilePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
