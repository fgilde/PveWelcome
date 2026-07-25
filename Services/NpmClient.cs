using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PveWelcome.Models;

namespace PveWelcome.Services;

public class NpmOptions
{
    /// e.g. http://192.168.178.100:81
    public string BaseUrl { get; set; } = "";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
}

public class NpmClient(HttpClient http, ConnectionConfig config, ILogger<NpmClient> log)
{
    private string BaseUrl => config.Current.NpmBaseUrl;
    private string? _token;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private async Task<string?> GetTokenAsync()
    {
        if (_token is not null && DateTime.UtcNow < _tokenExpiry) return _token;
        await _lock.WaitAsync();
        try
        {
            if (_token is not null && DateTime.UtcNow < _tokenExpiry) return _token;
            using var res = await http.PostAsJsonAsync($"{BaseUrl}/api/tokens",
                new { identity = config.Current.NpmUser, secret = config.Current.NpmPassword });
            if (!res.IsSuccessStatusCode) { log.LogWarning("npm login {Code}", res.StatusCode); return null; }
            var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
            _token = doc.RootElement.GetProperty("token").GetString();
            _tokenExpiry = DateTime.UtcNow.AddMinutes(30);
            return _token;
        }
        catch (Exception ex) { log.LogWarning(ex, "npm login"); return null; }
        finally { _lock.Release(); }
    }

    public async Task<List<NpmHost>> GetHostsAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)) return [];
        var token = await GetTokenAsync();
        if (token is null) return [];
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/nginx/proxy-hosts");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await http.SendAsync(req);
            res.EnsureSuccessStatusCode();
            var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
            var list = new List<NpmHost>();
            foreach (var h in doc.RootElement.EnumerateArray())
            {
                var domains = h.GetProperty("domain_names").EnumerateArray().Select(d => d.GetString()!).ToArray();
                var online = h.TryGetProperty("meta", out var m) && m.TryGetProperty("nginx_online", out var no) && no.ValueKind == JsonValueKind.True;
                list.Add(new NpmHost(
                    h.GetProperty("id").GetInt32(),
                    domains,
                    h.GetProperty("forward_host").GetString() ?? "",
                    h.GetProperty("forward_port").GetInt32(),
                    h.GetProperty("forward_scheme").GetString() ?? "http",
                    h.GetProperty("enabled").ValueKind == JsonValueKind.True || (h.GetProperty("enabled").ValueKind == JsonValueKind.Number && h.GetProperty("enabled").GetInt32() == 1),
                    online));
            }
            return list;
        }
        catch (Exception ex) { log.LogWarning(ex, "npm hosts"); return []; }
    }

    private object BuildPayload(string[] domains, string scheme, string host, int port, bool ws, string advanced) => new
    {
        domain_names = domains,
        forward_scheme = scheme,
        forward_host = host,
        forward_port = port,
        block_exploits = true,
        allow_websocket_upgrade = ws,
        access_list_id = 0,
        certificate_id = 0,
        ssl_forced = false,
        caching_enabled = false,
        http2_support = false,
        hsts_enabled = false,
        advanced_config = advanced,
        locations = Array.Empty<object>(),
        meta = new { }
    };

    private async Task<bool> SendAsync(HttpMethod method, string path, object? body)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)) return false;
        var token = await GetTokenAsync();
        if (token is null) return false;
        try
        {
            using var req = new HttpRequestMessage(method, $"{BaseUrl}{path}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) req.Content = JsonContent.Create(body);
            using var res = await http.SendAsync(req);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) { log.LogWarning(ex, "npm {Method} {Path}", method, path); return false; }
    }

    public Task<bool> CreateHostAsync(string[] domains, string scheme, string host, int port, bool ws, string advanced = "")
        => SendAsync(HttpMethod.Post, "/api/nginx/proxy-hosts", BuildPayload(domains, scheme, host, port, ws, advanced));

    public Task<bool> UpdateHostAsync(int id, string[] domains, string scheme, string host, int port, bool ws, string advanced = "")
        => SendAsync(HttpMethod.Put, $"/api/nginx/proxy-hosts/{id}", BuildPayload(domains, scheme, host, port, ws, advanced));

    public Task<bool> SetEnabledAsync(int id, bool enabled)
        => SendAsync(HttpMethod.Post, $"/api/nginx/proxy-hosts/{id}/{(enabled ? "enable" : "disable")}", null);

    public Task<bool> DeleteHostAsync(int id)
        => SendAsync(HttpMethod.Delete, $"/api/nginx/proxy-hosts/{id}", null);
}
