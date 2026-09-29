using MapleAuctionSniper.Application;
using MapleAuctionSniper.Domain;
using MapleAuctionSniper.Infrastructure;

namespace MapleAuctionSniper.Application.Tests;

public sealed class ScanAuctionTests
{
    private static MonitoringRule Rule() => new(new("模擬長劍", new(100000000),
        new(STR: new(10), WeaponAttack: new(90))));

    [Fact]
    public async Task AdvancedFiltersUseRecognizedMetadataAndStillDetectDeal()
    {
        var data = SimulationData.Create(DateTimeOffset.UtcNow);
        var workflow = new ScanAuction(new FakeScreenCapture(data.Json, TimeProvider.System),
            new FakeAuctionScreenAnalyzer(), new InMemoryAuctionListingRepository(data.History),
            new HistoricalMedianDealStrategy(), new RecordingNotificationService());
        var filters = new AuctionSearchFilters(ItemCategory.Weapon, "模擬分類", "模擬子分類", "特殊", "稀有",
            "模擬長劍", true, new(100, 100), new(new Price(70000000), new Price(80000000)));
        var rule = new MonitoringRule(new SearchCriteria(filters,
            new(DetailMatchMode.Or, [new(EquipmentStat.WeaponAttack, 101), new(EquipmentStat.STR, 10)])));
        var result = await workflow.ExecuteAsync(rule);
        var match = Assert.Single(result.Listings, r => r.Evaluation.MatchesCriteria);
        Assert.Equal("demo-1", match.Listing.Id);
        Assert.Equal(100, match.Listing.Equipment.RequiredLevel);
        Assert.Equal(ItemCategory.Weapon, match.Listing.Equipment.SearchAttributes.Category);
        Assert.True(match.NotificationTriggered);
    }

    [Fact]
    public async Task JsonSimulationSavesSnapshotAndNotifiesOnlyForOneDeal()
    {
        var data = SimulationData.Create(DateTimeOffset.UtcNow);
        var repository = new InMemoryAuctionListingRepository(data.History);
        var notifier = new RecordingNotificationService();
        var workflow = new ScanAuction(new FakeScreenCapture(data.Json, TimeProvider.System),
            new FakeAuctionScreenAnalyzer(), repository, new HistoricalMedianDealStrategy(), notifier);

        var result = await workflow.ExecuteAsync(Rule());

        Assert.Equal(4, result.Listings.Count);
        Assert.Equal(2, result.Listings.Count(r => r.Evaluation.MatchesCriteria));
        var deal = Assert.Single(result.Listings, r => r.Evaluation.IsPotentialDeal);
        Assert.Equal("demo-1", deal.Listing.Id);
        Assert.True(deal.NotificationTriggered);
        Assert.Equal("demo-1", Assert.Single(notifier.Sent).Listing.Id);
        Assert.Equal(4, Assert.Single(repository.Snapshots).Listings.Count);
        Assert.Equal(100000000m, result.Listings[1].Evaluation.HistoricalMedianPrice!.Value.Amount);
    }

    [Fact]
    public async Task AlternateAnalyzerRepositoryAndStrategyWorkWithoutWorkflowChanges()
    {
        var now = DateTimeOffset.UtcNow;
        var listing = new AuctionListing("alternate", new("任意裝備", new()), new(99), now);
        var frame = new CapturedFrame(new byte[] { 1 }, "image/png", now);
        var analyzer = new StubAnalyzer([listing]);
        var repository = new StubRepository([new(200)]);
        var notifier = new RecordingNotificationService();
        var strategy = new AlwaysDealStrategy();
        var workflow = new ScanAuction(new StubCapture(frame), analyzer, repository, strategy, notifier);

        var result = await workflow.ExecuteAsync(Rule());

        Assert.Same(frame, analyzer.ReceivedFrame);
        Assert.Same(listing, Assert.Single(result.Listings).Listing);
        Assert.True(result.Listings[0].NotificationTriggered);
        Assert.Single(repository.Saved!.Listings);
        Assert.Equal(200m, Assert.Single(strategy.ReceivedPrices!).Amount);
    }

    [Fact]
    public async Task WholeBatchIsEvaluatedBeforeSave()
    {
        var data = SimulationData.Create(DateTimeOffset.UtcNow);
        var repository = new StubRepository([new(100000000)]);
        var workflow = new ScanAuction(new FakeScreenCapture(data.Json, TimeProvider.System),
            new FakeAuctionScreenAnalyzer(), repository, new HistoricalMedianDealStrategy(), new RecordingNotificationService());
        await workflow.ExecuteAsync(Rule());
        Assert.Equal(new[] { "history", "history", "history", "history", "save" }, repository.Calls);
    }

    [Fact]
    public async Task NoHistorySavesButDoesNotNotify()
    {
        var data = SimulationData.Create(DateTimeOffset.UtcNow);
        var notifier = new RecordingNotificationService();
        var repository = new InMemoryAuctionListingRepository();
        var workflow = new ScanAuction(new FakeScreenCapture(data.Json, TimeProvider.System),
            new FakeAuctionScreenAnalyzer(), repository, new HistoricalMedianDealStrategy(), notifier);
        var result = await workflow.ExecuteAsync(Rule());
        Assert.All(result.Listings, r => Assert.False(r.Evaluation.IsPotentialDeal));
        Assert.Empty(notifier.Sent);
        Assert.Single(repository.Snapshots);
    }

    [Fact]
    public async Task RepositoryExcludesCurrentIdentityFutureAndNonComparableStats()
    {
        var now = DateTimeOffset.UtcNow;
        var equipment = new Equipment("長劍", new(WeaponAttack: 100));
        var candidate = new AuctionListing("current", equipment, new(50), now);
        var history = new[]
        {
            new AuctionListing("current", equipment, new(20), now.AddDays(-1)),
            new AuctionListing("future", equipment, new(20), now.AddDays(1)),
            new AuctionListing("other", new("長劍", new(WeaponAttack: 80)), new(20), now.AddDays(-1)),
            new AuctionListing("match", equipment, new(100), now.AddDays(-1))
        };
        var repository = new InMemoryAuctionListingRepository(history);
        Assert.Equal(100m, Assert.Single(await repository.GetHistoricalPricesAsync(candidate, default)).Amount);
    }

    [Fact]
    public async Task RepeatedSnapshotsDoNotMultiplyHistoricalObservations()
    {
        var now = DateTimeOffset.UtcNow;
        var listing = new AuctionListing("same-id", new("長劍", new()), new(100), now);
        var repository = new InMemoryAuctionListingRepository();
        await repository.SaveSnapshotAsync(new(now, [listing]), default);
        await repository.SaveSnapshotAsync(new(now, [listing]), default);
        var future = new AuctionListing("next", listing.Equipment, new(50), now.AddDays(1));
        Assert.Single(await repository.GetHistoricalPricesAsync(future, default));
        Assert.Equal(2, repository.Snapshots.Count);
    }

    [Fact]
    public async Task CancelledScanDoesNotPersistOrNotify()
    {
        var data = SimulationData.Create(DateTimeOffset.UtcNow);
        var repository = new InMemoryAuctionListingRepository();
        var notifier = new RecordingNotificationService();
        var workflow = new ScanAuction(new FakeScreenCapture(data.Json, TimeProvider.System),
            new FakeAuctionScreenAnalyzer(), repository, new HistoricalMedianDealStrategy(), notifier);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.ExecuteAsync(Rule(), cancellation.Token));
        Assert.Empty(repository.Snapshots);
        Assert.Empty(notifier.Sent);
    }

    [Fact]
    public async Task SaveFailureDoesNotSendNotifications()
    {
        var data = SimulationData.Create(DateTimeOffset.UtcNow);
        var notifier = new RecordingNotificationService();
        var repository = new StubRepository([new(100000000)]) { FailSave = true };
        var workflow = new ScanAuction(new FakeScreenCapture(data.Json, TimeProvider.System),
            new FakeAuctionScreenAnalyzer(), repository, new HistoricalMedianDealStrategy(), notifier);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ExecuteAsync(Rule()));
        Assert.Empty(notifier.Sent);
    }

    [Fact]
    public async Task NotificationAdapterMayDeclineWithoutChangingDealEvaluation()
    {
        var data = SimulationData.Create(DateTimeOffset.UtcNow);
        var workflow = new ScanAuction(new FakeScreenCapture(data.Json, TimeProvider.System),
            new FakeAuctionScreenAnalyzer(), new InMemoryAuctionListingRepository(data.History),
            new HistoricalMedianDealStrategy(), new DecliningNotifier());
        var result = await workflow.ExecuteAsync(Rule());
        var deal = Assert.Single(result.Listings, r => r.Evaluation.IsPotentialDeal);
        Assert.False(deal.NotificationTriggered);
    }

    [Fact]
    public async Task InvalidJsonFailsBeforeSaving()
    {
        var repository = new InMemoryAuctionListingRepository();
        var workflow = new ScanAuction(new FakeScreenCapture("invalid", TimeProvider.System),
            new FakeAuctionScreenAnalyzer(), repository, new HistoricalMedianDealStrategy(), new RecordingNotificationService());
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => workflow.ExecuteAsync(Rule()));
        Assert.Empty(repository.Snapshots);
    }

    private sealed class StubCapture(CapturedFrame frame) : IScreenCapture
    {
        public Task<CapturedFrame> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult(frame);
    }
    private sealed class DecliningNotifier : INotificationService
    {
        public Task<bool> NotifyAsync(DealNotification notification, CancellationToken cancellationToken) => Task.FromResult(false);
    }
    private sealed class StubAnalyzer(IReadOnlyList<AuctionListing> listings) : IAuctionScreenAnalyzer
    {
        public CapturedFrame? ReceivedFrame { get; private set; }
        public Task<IReadOnlyList<AuctionListing>> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken)
        {
            ReceivedFrame = frame;
            return Task.FromResult(listings);
        }
    }
    private sealed class StubRepository(IReadOnlyList<Price> prices) : IAuctionListingRepository
    {
        public List<string> Calls { get; } = [];
        public AuctionSnapshot? Saved { get; private set; }
        public bool FailSave { get; init; }
        public Task<IReadOnlyList<Price>> GetHistoricalPricesAsync(AuctionListing listing, CancellationToken cancellationToken)
        {
            Calls.Add("history");
            return Task.FromResult(prices);
        }
        public Task SaveSnapshotAsync(AuctionSnapshot snapshot, CancellationToken cancellationToken)
        {
            Calls.Add("save");
            if (FailSave) throw new InvalidOperationException("Simulated persistence failure.");
            Saved = snapshot;
            return Task.CompletedTask;
        }
    }
    private sealed class AlwaysDealStrategy : IDealDetectionStrategy
    {
        public IReadOnlyList<Price>? ReceivedPrices { get; private set; }
        public DealEvaluation Evaluate(AuctionListing listing, MonitoringRule rule, IReadOnlyList<Price> historicalPrices)
        {
            ReceivedPrices = historicalPrices;
            return new(true, true, new(200), "替代策略");
        }
    }
}
