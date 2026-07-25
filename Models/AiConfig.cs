namespace PveWelcome.Models;

/// AI integration config (single row).
public class AiSettings
{
    public int Id { get; set; } = 1;
    public string Provider { get; set; } = "hermes";

    public string HermesHost { get; set; } = "";
    public int HermesPort { get; set; } = 22;
    public string HermesUser { get; set; } = "hermes";
    public string HermesAuth { get; set; } = "";
    public bool HermesAuthIsKey { get; set; }
    public string HermesCommand { get; set; } = "hermes";
    public string HermesModel { get; set; } = "";
    public bool HermesYolo { get; set; } = true;

    public string ClaudeApiKey { get; set; } = "";
    public string ClaudeModel { get; set; } = "claude-sonnet-5";

    public bool Configured => Provider == "hermes"
        ? !string.IsNullOrWhiteSpace(HermesHost)
        : !string.IsNullOrWhiteSpace(ClaudeApiKey);
}

/// Audit entry: one AI request + its result.
public class AiRun
{
    public int Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Provider { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Result { get; set; } = "";
    public bool Ok { get; set; }
}
