using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PveWelcome.Models;

namespace PveWelcome.Services;

public class NpmOptions
{
    /// e.g. http://192.168.178.100:81
    public string BaseUrl { get; set; } = "";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
}

public class NpmClient(HttpClient http, IOptions<NpmOptions> options, ILogger<NpmClient> log)
{
    private readonly NpmOptions opt = options.Value;
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
            using var res = await http.PostAsJsonAsync($"{opt.BaseUrl}/api/tokens",
                new { identity = opt.User, secret = opt.Password });
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
        if (string.IsNullOrWhiteSpace(opt.BaseUrl)) return [];
        var token = await GetTokenAsync();
        if (token is null) return [];
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{opt.BaseUrl}/api/nginx/proxy-hosts");
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
}
