using MapleAuctionSniper.Application;
using MapleAuctionSniper.Domain;
using MapleAuctionSniper.Infrastructure;

namespace MapleAuctionSniper.Application.Tests;

public sealed class ScheduledMonitoringTests
{
    private static SearchPreset Sword(string name = "長劍") => new(name,
        new(new SearchCriteria("模擬長劍", new(100000000), new(STR: new(10), WeaponAttack: new(90)))));

    [Fact]
    public async Task MultipleItemsShareOneCaptureAnalysisAndSnapshot()
    {
        var clock = new ManualClock();
        var data = SimulationData.Create(clock.GetUtcNow());
        var capture = new CountingCapture(new FakeScreenCapture(data.Json, clock));
        var analyzer = new CountingAnalyzer();
        var staff = new Equipment("模擬法杖", new(0, 0, 12, 5, 30, 110, 7), 100,
            searchAttributes: new(ItemCategory.Weapon, "模擬分類", "模擬子分類", "特殊", "稀有"));
        var repository = new CountingRepository(new InMemoryAuctionListingRepository(data.History.Concat(
            [new AuctionListing("old-staff", staff, new(40000000), clock.GetUtcNow().AddDays(-1))])));
        var notifier = new RecordingNotificationService();
        var scan = new ScanAuction(capture, analyzer, repository, new HistoricalMedianDealStrategy(), notifier);
        var result = await scan.ExecuteForSearchesAsync([Sword(), new("法杖", new(new SearchCriteria("模擬法杖", new(30000000))))]);
        Assert.Equal(4, result.Listings.Count);
        Assert.Equal(2, result.Listings.Count(r => r.Evaluation.IsPotentialDeal));
        Assert.Equal("長劍", result.Listings[0].MatchedSearchNames);
        Assert.Equal("法杖", result.Listings[3].MatchedSearchNames);
        Assert.Equal(2, notifier.Sent.Count);
        Assert.Equal(1, capture.Calls);
        Assert.Equal(1, analyzer.Calls);
        Assert.Equal(4, repository.Reads);
        Assert.Equal(1, repository.Saves);
    }

    [Fact]
    public async Task OverlappingRulesSendOnlyOneNotificationForEachDeal()
    {
        var clock = new ManualClock();
        var data = SimulationData.Create(clock.GetUtcNow());
        var notifier = new RecordingNotificationService();
        var scan = new ScanAuction(new FakeScreenCapture(data.Json, clock), new FakeAuctionScreenAnalyzer(),
            new InMemoryAuctionListingRepository(data.History), new HistoricalMedianDealStrategy(), notifier);
        var result = await scan.ExecuteForSearchesAsync([Sword("物攻條件"), Sword("素質條件")]);
        Assert.Equal(4, result.Listings.Count);
        var notification = Assert.Single(notifier.Sent);
        Assert.Equal(new[] { "物攻條件", "素質條件" }, notification.SearchNames);
        Assert.Equal("物攻條件、素質條件", result.Listings[0].MatchedSearchNames);
        Assert.Equal(2, result.Listings[0].RuleEvaluations!.Count);
    }

    [Fact]
    public async Task EmptySelectionIsRejectedBeforeCapture()
    {
        var clock = new ManualClock();
        var capture = new CountingCapture(new FakeScreenCapture("[]", clock));
        var scan = CreateScan(capture);
        await Assert.ThrowsAsync<ArgumentException>(() => scan.ExecuteForSearchesAsync([]));
        Assert.Equal(0, capture.Calls);
    }

    [Fact]
    public async Task ImmediateRoundThenConfiguredIntervalAndStopDuringWait()
    {
        var clock = new ManualClock();
        var capture = new CountingCapture(new FakeScreenCapture("[]", clock));
        var monitor = new ScheduledAuctionMonitor(CreateScan(capture), clock);
        using var cancellation = new CancellationTokenSource();
        var firstWait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rounds = 0;
        DateTimeOffset? next = null;
        var startedAt = clock.GetUtcNow();
        var task = monitor.RunAsync([Sword()], TimeSpan.FromMinutes(3),
            _ => { if (Interlocked.Increment(ref rounds) == 2) secondRound.SetResult(); return Task.CompletedTask; },
            _ => throw new InvalidOperationException("Unexpected failure."),
            time => { next = time; if (time.HasValue) firstWait.TrySetResult(); }, cancellation.Token);
        try
        {
            await firstWait.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, capture.Calls);
            Assert.Equal(startedAt.AddMinutes(3), next);
            clock.Advance(TimeSpan.FromMinutes(2));
            Assert.Equal(1, capture.Calls);
            clock.Advance(TimeSpan.FromMinutes(1));
            await secondRound.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, capture.Calls);
        }
        finally { cancellation.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Null(next);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, capture.Calls);
    }

    [Fact]
    public async Task SlowScanDoesNotOverlapAndStoppingCancelsCapture()
    {
        var clock = new ManualClock();
        var capture = new BlockingCapture();
        var monitor = new ScheduledAuctionMonitor(CreateScan(capture), clock);
        using var cancellation = new CancellationTokenSource();
        var task = monitor.RunAsync([Sword()], TimeSpan.FromMinutes(1), _ => Task.CompletedTask,
            _ => Task.CompletedTask, _ => { }, cancellation.Token);
        try
        {
            await capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromHours(1));
            Assert.Equal(1, capture.Calls);
        }
        finally { cancellation.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, capture.Calls);
    }

    [Fact]
    public async Task FailureIsReportedAndNextRoundStillRuns()
    {
        var clock = new ManualClock();
        var capture = new CountingCapture(new FakeScreenCapture("[]", clock)) { FailFirst = true };
        var monitor = new ScheduledAuctionMonitor(CreateScan(capture), clock);
        using var cancellation = new CancellationTokenSource();
        var failures = 0;
        var successful = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = monitor.RunAsync([Sword()], TimeSpan.FromMinutes(2),
            _ => { successful.TrySetResult(); return Task.CompletedTask; },
            _ => { failures++; return Task.CompletedTask; }, _ => { }, cancellation.Token);
        try
        {
            Assert.Equal(1, failures);
            clock.Advance(TimeSpan.FromMinutes(2));
            await successful.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, capture.Calls);
        }
        finally { cancellation.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    public async Task InvalidIntervalsNeverCapture(int minutes)
    {
        var clock = new ManualClock();
        var capture = new CountingCapture(new FakeScreenCapture("[]", clock));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new ScheduledAuctionMonitor(CreateScan(capture), clock)
            .RunAsync([Sword()], TimeSpan.FromMinutes(minutes), _ => Task.CompletedTask, _ => Task.CompletedTask, _ => { }, default));
        Assert.Equal(0, capture.Calls);
    }

    [Fact]
    public async Task ScheduleIntervalAndSelectionsPersistLocally()
    {
        var path = Path.Combine(Path.GetTempPath(), "MapleAuctionSniper.Tests", Guid.NewGuid().ToString("N"), "schedule.json");
        try
        {
            var repository = new JsonMonitoringScheduleRepository(path);
            Assert.Equal(5m, (await repository.LoadAsync()).IntervalMinutes);
            await repository.SaveAsync(new(2.5m, ["長劍", "法杖"]));
            var loaded = await new JsonMonitoringScheduleRepository(path).LoadAsync();
            Assert.Equal(2.5m, loaded.IntervalMinutes);
            Assert.Equal(new[] { "長劍", "法杖" }, loaded.SearchNames);
            await File.WriteAllTextAsync(path, "invalid");
            await Assert.ThrowsAsync<InvalidDataException>(() => repository.SaveAsync(new()));
            Assert.Equal("invalid", await File.ReadAllTextAsync(path));
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    private static ScanAuction CreateScan(IScreenCapture capture) => new(capture, new FakeAuctionScreenAnalyzer(),
        new InMemoryAuctionListingRepository(), new HistoricalMedianDealStrategy(), new RecordingNotificationService());
    private sealed class CountingCapture(IScreenCapture inner) : IScreenCapture
    {
        public int Calls { get; private set; }
        public bool FailFirst { get; init; }
        public Task<CapturedFrame> CaptureAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (FailFirst && Calls == 1) throw new InvalidOperationException("Simulated capture failure.");
            return inner.CaptureAsync(cancellationToken);
        }
    }
    private sealed class CountingAnalyzer : IAuctionScreenAnalyzer
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<AuctionListing>> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken)
        { Calls++; return new FakeAuctionScreenAnalyzer().AnalyzeAsync(frame, cancellationToken); }
    }
    private sealed class CountingRepository(InMemoryAuctionListingRepository inner) : IAuctionListingRepository
    {
        public int Reads { get; private set; }
        public int Saves { get; private set; }
        public Task<IReadOnlyList<Price>> GetHistoricalPricesAsync(AuctionListing listing, CancellationToken cancellationToken)
        { Reads++; return inner.GetHistoricalPricesAsync(listing, cancellationToken); }
        public Task SaveSnapshotAsync(AuctionSnapshot snapshot, CancellationToken cancellationToken)
        { Saves++; return inner.SaveSnapshotAsync(snapshot, cancellationToken); }
    }
    private sealed class BlockingCapture : IScreenCapture
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public async Task<CapturedFrame> CaptureAsync(CancellationToken cancellationToken)
        {
            Calls++; Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    /// <summary>Deterministic single-shot timers for Task.Delay; tests never wait real minutes.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);
        private readonly List<ManualTimer> timers = [];
        private readonly object gate = new();
        public override DateTimeOffset GetUtcNow() { lock (gate) return now; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (gate) timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
        public void Advance(TimeSpan duration)
        {
            ManualTimer[] due;
            lock (gate)
            {
                now += duration;
                due = timers.Where(t => t.Due <= now).ToArray();
                foreach (var timer in due) timer.Due = DateTimeOffset.MaxValue;
            }
            foreach (var timer in due) timer.Callback(timer.State);
        }
        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset Due { get; set; } = DateTimeOffset.MaxValue;
            public TimerCallback Callback { get; } = callback;
            public object? State { get; } = state;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (period != Timeout.InfiniteTimeSpan) throw new NotSupportedException("Only single-shot timers are needed.");
                lock (clock.gate) Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : clock.now + dueTime;
                return true;
            }
            public void Dispose() { lock (clock.gate) { Due = DateTimeOffset.MaxValue; clock.timers.Remove(this); } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
