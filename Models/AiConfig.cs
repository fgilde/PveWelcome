namespace PveWelcome.Models;

/// AI integration config (single row). Keys/creds live here (per instance), NOT in any shared vault.
public class AiSettings
{
    public int Id { get; set; } = 1;
    /// "hermes" | "claude"
    public string Provider { get; set; } = "hermes";

    // --- Hermes (own agent, reached over SSH; it already has SSH + tools = full power) ---
    public string HermesHost { get; set; } = "";
    public int HermesPort { get; set; } = 22;
    public string HermesUser { get; set; } = "hermes";
    /// password OR a private-key PEM (see HermesAuthIsKey)
    public string HermesAuth { get; set; } = "";
    public bool HermesAuthIsKey { get; set; }
    /// base command (default "hermes"); model + flags appended
    public string HermesCommand { get; set; } = "hermes";
    public string HermesModel { get; set; } = "";
    /// --yolo = act without per-step confirmation (full power)
    public bool HermesYolo { get; set; } = true;

    // --- Claude (raw LLM via Messages API; text only in this version) ---
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
