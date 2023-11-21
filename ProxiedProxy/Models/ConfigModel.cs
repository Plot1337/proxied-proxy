using System.Runtime.Serialization;
using Tomlyn.Model;

namespace ProxiedProxy.Models;

internal class ConfigModel
{
    [DataMember(Name = "server")]
    public List<ServerConfigToml>? Servers { get; set; }
}

internal class ServerConfigToml : ITomlMetadataProvider
{
    [DataMember(Name = "port")]
    public int Port { get; set; }

    [DataMember(Name = "ip-file")]
    public string? IpFile { get; set; }

    [DataMember(Name = "bind-remote-proxy")]
    public bool BindRemoteProxy { get; set; }

    [DataMember(Name = "proxy-file")]
    public string? ProxyFile { get; set; }

    TomlPropertiesMetadata? ITomlMetadataProvider.PropertiesMetadata { get; set; }
}