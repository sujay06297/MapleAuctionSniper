namespace MapleAuctionSniper.Application;

/// <summary>Immediate first round, then a delay after completion. Rounds never overlap.</summary>
public sealed class ScheduledAuctionMonitor(ScanAuction scan, TimeProvider clock)
{
    public async Task RunAsync(IReadOnlyList<SearchPreset> searches, TimeSpan interval,
        Func<ScanResult, Task> onResult, Func<Exception, Task> onError, Action<DateTimeOffset?> onNextScan,
        CancellationToken cancellationToken)
    {
        if (interval < TimeSpan.FromMilliseconds(1) || interval > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(interval), "間隔須大於零且不超過一天。");
        var selected = searches.ToArray();
        if (selected.Length == 0) throw new ArgumentException("請至少選擇一組搜尋條件。", nameof(searches));
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                onNextScan(null);
                try { await onResult(await scan.ExecuteForSearchesAsync(selected, cancellationToken)); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { await onError(ex); }
                cancellationToken.ThrowIfCancellationRequested();
                onNextScan(clock.GetUtcNow() + interval);
                await Task.Delay(interval, clock, cancellationToken);
            }
        }
        finally { onNextScan(null); }
    }
}
