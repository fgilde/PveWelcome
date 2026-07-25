namespace PveWelcome.Models;

/// A Nginx Proxy Manager proxy host (domain -> internal target).
public record NpmHost(
    int Id,
    string[] DomainNames,
    string ForwardHost,
    int ForwardPort,
    string ForwardScheme,
    bool Enabled,
    bool Online
);
