using PveWelcome.Models;

namespace PveWelcome.Services;

/// Resolves per-domain branding from the request host.
public class BrandResolver(IConfiguration config)
{
    private readonly Dictionary<string, Brand> _brands =
        config.GetSection("Brands").Get<Dictionary<string, Brand>>() ?? new();

    private Brand Default => _brands.TryGetValue("default", out var d) ? d
        : new Brand { Name = "Home", Tagline = "Self-hosted." };

    public Brand Resolve(string? host)
    {
        if (string.IsNullOrEmpty(host)) return Default;
        host = host.Split(':')[0].ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];
        return _brands.TryGetValue(host, out var b) ? b : Default;
    }
}
