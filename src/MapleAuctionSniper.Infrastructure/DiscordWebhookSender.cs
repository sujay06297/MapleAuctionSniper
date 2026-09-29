using System.Net;
using System.Net.Http.Json;
using MapleAuctionSniper.Application;

namespace MapleAuctionSniper.Infrastructure;

public sealed class DiscordWebhookSender(HttpClient httpClient) : IDiscordWebhookSender
{
    public async Task SendAsync(string webhookUrl, string message, CancellationToken cancellationToken = default)
    {
        var address = DiscordWebhookAddress.Parse(webhookUrl);
        if (string.IsNullOrWhiteSpace(message) || message.Length > 2000)
            throw new ArgumentException("Discord 訊息需為 1～2000 個字元。");
        var endpoint = new UriBuilder(address) { Query = "wait=true" }.Uri;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { content = message, allowed_mentions = new { parse = Array.Empty<string>() } })
        };
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return;
            var explanation = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.NotFound => "Webhook 已失效或不存在，請確認設定。",
                HttpStatusCode.Forbidden => "Webhook 沒有發送權限。",
                HttpStatusCode.TooManyRequests => "發送過於頻繁，請稍後重試。",
                _ => "請稍後重試或確認 Webhook 設定。"
            };
            throw new InvalidOperationException($"Discord 通知失敗（HTTP {(int)response.StatusCode}）：{explanation}");
        }
        catch (HttpRequestException) { throw new InvalidOperationException("無法連線至 Discord，請檢查網路連線。"); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Discord 通知逾時，請稍後重試。"); }
    }
}
