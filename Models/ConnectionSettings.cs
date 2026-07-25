namespace PveWelcome.Models;

/// Runtime-editable connection config (single row).
public class ConnectionSettings
{
    public int Id { get; set; } = 1;
    public string PveBaseUrl { get; set; } = "";
    public string PveApiToken { get; set; } = "";
    public string NpmBaseUrl { get; set; } = "";
    public string NpmUser { get; set; } = "";
    public string NpmPassword { get; set; } = "";
    public string BackupStorage { get; set; } = "";
    public string NotifyWebhook { get; set; } = "";
    public string TelegramToken { get; set; } = "";
    public string TelegramChatId { get; set; } = "";
    public string Language { get; set; } = "de";
    public string McpUrl { get; set; } = "";
    public string McpToken { get; set; } = "";
}
