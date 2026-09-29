using System.Text.RegularExpressions;

namespace MapleAuctionSniper.Infrastructure;

internal static partial class DiscordWebhookAddress
{
    public static Uri Parse(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            (uri.Host != "discord.com" && uri.Host != "discordapp.com") || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || !WebhookPath().IsMatch(uri.AbsolutePath))
            throw new ArgumentException("請填入完整的 Discord HTTPS Webhook URL（不是頻道連結）。");
        return uri;
    }

    [GeneratedRegex(@"^/api/(?:v[0-9]+/)?webhooks/[0-9]+/[A-Za-z0-9_-]+$")]
    private static partial Regex WebhookPath();
}
