using System.Net.Http.Json;

namespace PveWelcome.Services;

/// Pushes new alerts to a generic webhook and/or Telegram (both optional, from ConnectionConfig).
public class NotificationService(IServiceScopeFactory scopeFactory, IHttpClientFactory httpFactory, ILogger<NotificationService> log)
{
    public async Task NotifyAsync(IReadOnlyList<string> newAlerts)
    {
        if (newAlerts.Count == 0) return;
        using var scope = scopeFactory.CreateScope();
        var cfg = scope.ServiceProvider.GetRequiredService<ConnectionConfig>().Current;
        var text = "⚠ PveWelcome:\n" + string.Join("\n", newAlerts);
        var client = httpFactory.CreateClient("notify");

        if (!string.IsNullOrWhiteSpace(cfg.NotifyWebhook))
            try { await client.PostAsJsonAsync(cfg.NotifyWebhook, new { text }); }
            catch (Exception ex) { log.LogWarning(ex, "webhook notify"); }

        if (!string.IsNullOrWhiteSpace(cfg.TelegramToken) && !string.IsNullOrWhiteSpace(cfg.TelegramChatId))
            try { await client.PostAsJsonAsync($"https://api.telegram.org/bot{cfg.TelegramToken}/sendMessage",
                    new { chat_id = cfg.TelegramChatId, text }); }
            catch (Exception ex) { log.LogWarning(ex, "telegram notify"); }
    }
}
