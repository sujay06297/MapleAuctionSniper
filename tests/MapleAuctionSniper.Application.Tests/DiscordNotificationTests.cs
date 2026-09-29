using System.Net;
using System.Text.Json;
using MapleAuctionSniper.Application;
using MapleAuctionSniper.Domain;
using MapleAuctionSniper.Infrastructure;

namespace MapleAuctionSniper.Application.Tests;

public sealed class DiscordNotificationTests : IDisposable
{
    private const string FakeUrl = "https://discord.com/api/webhooks/123456789012345678/fake_token_not_a_real_webhook";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MapleAuctionSniper.Tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(directory, "notification-settings.json");

    [Fact]
    public async Task MissingSettingsAreDisabledAndSettingsRoundTrip()
    {
        var repository = new JsonNotificationSettingsRepository(SettingsPath);
        Assert.False((await repository.LoadAsync()).Enabled);
        Assert.False(File.Exists(SettingsPath));
        await repository.SaveAsync(new(true, " " + FakeUrl + " "));
        var loaded = await new JsonNotificationSettingsRepository(SettingsPath).LoadAsync();
        Assert.True(loaded.Enabled);
        Assert.Equal(FakeUrl, loaded.WebhookUrl);
        await repository.SaveAsync(new(false, FakeUrl));
        Assert.False((await repository.LoadAsync()).Enabled);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        Assert.DoesNotContain("fake_token", loaded.ToString()!);
    }

    [Fact]
    public async Task InvalidUrlOrCancelledSaveDoesNotChangeSavedSettings()
    {
        var repository = new JsonNotificationSettingsRepository(SettingsPath);
        await repository.SaveAsync(new(true, FakeUrl));
        var original = await File.ReadAllTextAsync(SettingsPath);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(new(true)));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(new(true, "https://example.com/webhook")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.SaveAsync(new(false), cancellation.Token));
        Assert.Equal(original, await File.ReadAllTextAsync(SettingsPath));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("{\"Version\":99,\"Enabled\":false,\"WebhookUrl\":\"\"}")]
    [InlineData("{\"Version\":1,\"Enabled\":true,\"WebhookUrl\":\"bad_secret\"}")]
    public async Task CorruptSettingsCannotBeSilentlyOverwritten(string content)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(SettingsPath, content);
        var repository = new JsonNotificationSettingsRepository(SettingsPath);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync());
        Assert.DoesNotContain("bad_secret", error.Message);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.SaveAsync(new()));
        Assert.Equal(content, await File.ReadAllTextAsync(SettingsPath));
    }

    [Theory]
    [InlineData("http://discord.com/api/webhooks/123/token")]
    [InlineData("https://example.com/api/webhooks/123/token")]
    [InlineData("https://discord.com/channels/123/456")]
    [InlineData("https://discord.com/api/webhooks/123/token?anything=1")]
    [InlineData("https://discord.com/api/webhooks/123/token#fragment")]
    [InlineData("https://discord.com.evil.example/api/webhooks/123/token")]
    public async Task InvalidDestinationsAreRejectedBeforeHttp(string url)
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => new DiscordWebhookSender(http).SendAsync(url, "test"));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task HttpPayloadRequestsConfirmationAndDisablesMentions()
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        await new DiscordWebhookSender(http).SendAsync(FakeUrl, "中文測試 @everyone");
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("?wait=true", handler.Address!.Query);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("中文測試 @everyone", json.RootElement.GetProperty("content").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HttpFailuresNeverExposeWebhookOrResponseBody(HttpStatusCode status)
    {
        using var handler = new RecordingHandler { Status = status };
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new DiscordWebhookSender(http).SendAsync(FakeUrl, "test"));
        Assert.Contains(((int)status).ToString(), error.Message);
        Assert.DoesNotContain("fake_token", error.ToString());
        Assert.DoesNotContain("response_secret", error.ToString());
    }

    [Fact]
    public async Task NetworkErrorsAreSanitizedAndCancellationRemainsCancellation()
    {
        using var handler = new RecordingHandler { Failure = new HttpRequestException("network error at " + FakeUrl) };
        using var http = new HttpClient(handler);
        var sender = new DiscordWebhookSender(http);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(FakeUrl, "test"));
        Assert.DoesNotContain("fake_token", error.ToString());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync(FakeUrl, "test", cancellation.Token));
    }

    [Fact]
    public async Task SettingsChangesApplyToLaterNotificationsWithoutRestart()
    {
        var settings = new StubSettings();
        var sender = new StubSender();
        var service = new DiscordNotificationService(settings, sender);
        var notification = new DealNotification(new("id", new("長劍", new(STR: 10, WeaponAttack: 100)), new(75000000), DateTimeOffset.UtcNow),
            new(true, true, new(100000000), "符合折扣"));
        Assert.False(await service.NotifyAsync(notification, default));
        Assert.Empty(sender.Messages);
        settings.Current = new(true, FakeUrl);
        Assert.True(await service.NotifyAsync(notification, default));
        var message = Assert.Single(sender.Messages);
        Assert.Contains("長劍", message);
        Assert.Contains("STR 10", message);
        Assert.Contains("物攻 100", message);
        Assert.Contains("符合折扣", message);
        settings.Current = new(false, FakeUrl);
        Assert.False(await service.NotifyAsync(notification, default));
        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task FailedNotificationKeepsAnalysisAndSnapshot()
    {
        var data = SimulationData.Create(DateTimeOffset.UtcNow);
        var settings = new StubSettings { Current = new(true, FakeUrl) };
        var repository = new InMemoryAuctionListingRepository(data.History);
        using var handler = new RecordingHandler { Status = HttpStatusCode.TooManyRequests };
        using var http = new HttpClient(handler);
        var workflow = new ScanAuction(new FakeScreenCapture(data.Json, TimeProvider.System), new FakeAuctionScreenAnalyzer(),
            repository, new HistoricalMedianDealStrategy(), new DiscordNotificationService(settings, new DiscordWebhookSender(http)));
        var result = await workflow.ExecuteAsync(new(new SearchCriteria("模擬長劍", new(100000000), new(STR: new(10), WeaponAttack: new(90)))));
        Assert.Equal(4, result.Listings.Count);
        var deal = Assert.Single(result.Listings, r => r.Evaluation.IsPotentialDeal);
        Assert.False(deal.NotificationTriggered);
        Assert.Contains("429", deal.NotificationError!);
        Assert.Single(repository.Snapshots);
        Assert.Equal(1, handler.Calls);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? Address { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Body { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public Exception? Failure { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++; Address = request.RequestUri; Method = request.Method;
            if (Failure is not null) throw Failure;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(Status) { Content = new StringContent("response_secret") };
        }
    }
    private sealed class StubSettings : INotificationSettingsRepository
    {
        public DiscordNotificationSettings Current { get; set; } = new();
        public Task<DiscordNotificationSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(DiscordNotificationSettings settings, CancellationToken cancellationToken = default)
        { Current = settings; return Task.CompletedTask; }
    }
    private sealed class StubSender : IDiscordWebhookSender
    {
        public List<string> Messages { get; } = [];
        public Task SendAsync(string webhookUrl, string message, CancellationToken cancellationToken = default)
        { Messages.Add(message); return Task.CompletedTask; }
    }
    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
