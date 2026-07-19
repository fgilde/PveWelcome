namespace PveWelcome.Models;

/// Runtime-editable connection config (single row). Seeded from env/appsettings on first run,
/// then editable in the admin Settings UI so others can point the tool at their own PVE/NPM.
public class ConnectionSettings
{
    public int Id { get; set; } = 1;
    public string PveBaseUrl { get; set; } = "";
    public string PveApiToken { get; set; } = "";
    public string NpmBaseUrl { get; set; } = "";
    public string NpmUser { get; set; } = "";
    public string NpmPassword { get; set; } = "";
}
