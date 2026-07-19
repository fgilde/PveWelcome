namespace PveWelcome.Models;

/// Editable per-domain branding, stored in the DB (seeded from appsettings).
public class BrandConfig
{
    public int Id { get; set; }
    public string Host { get; set; } = "";   // "default", "nksoft.de", ...
    public string Name { get; set; } = "";
    public string Tagline { get; set; } = "";
    public string Accent { get; set; } = "#8b5cf6";
    public string ProjectsUrl { get; set; } = "https://www.gilde.org";

    public Brand ToBrand() => new()
    {
        Name = Name, Tagline = Tagline, Accent = Accent, ProjectsUrl = ProjectsUrl
    };
}
