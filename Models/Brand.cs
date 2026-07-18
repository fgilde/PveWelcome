namespace PveWelcome.Models;

/// Per-domain branding. Selected by the request host.
public class Brand
{
    public string Name { get; set; } = "";
    public string Tagline { get; set; } = "";
    public string Accent { get; set; } = "#6366f1";
    /// External URL the public landing points visitors to.
    public string ProjectsUrl { get; set; } = "https://gilde.org";
}
