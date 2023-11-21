namespace ProxiedProxy.Models;

internal class ServerConfig
{
    public int Port { get; set; }

    public string[]? IpWhitelist { get; set; }

    public bool BindRemoteProxy { get; set; }

    public string? ProxyFile { get; set; }
}