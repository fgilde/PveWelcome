namespace PveWelcome.Models;

/// A self-contained public landing page (HTML/CSS/JS), served on one or more domains.
public class LandingPage
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Hosts { get; set; } = "";
    public string Template { get; set; } = "classic";
    public string Html { get; set; } = "";
    public string Css { get; set; } = "";
    public string Js { get; set; } = "";
    public bool LoginEnabled { get; set; } = true;
    public bool Active { get; set; } = true;
    public bool IsDefault { get; set; }

    public IEnumerable<string> HostList =>
        Hosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Select(h => h.ToLowerInvariant());
}
