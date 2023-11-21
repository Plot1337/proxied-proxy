using NLog;
using ProxiedProxy.Models;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ProxiedProxy;

internal class ProxyServer
{
    private readonly TcpListener _listener;
    private readonly ServerConfig _config;
    private readonly Dictionary<string, int> _proxies = new();
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public ProxyServer(ServerConfig config, Dictionary<string, int>? proxies)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        proxies = proxies ?? throw new ArgumentNullException(nameof(proxies));

        if (proxies.Count == 0)
            throw new Exception("Failed to load proxies!");

        foreach (var entry in proxies)
            _proxies.TryAdd(entry.Key, entry.Value);

        if (_proxies.Count == 0)
            throw new Exception("No proxies were loaded!");

        int port = config.Port;

        if (port < 1 || port > 65535)
            throw new ArgumentOutOfRangeException(
                nameof(port), "Port number must be between 1 and 65535."
                );

        if (IsPortInUse(port))
            throw new Exception($"Port {port} is already in use!");

        _listener = new(IPAddress.Any, port);
    }

    public async Task StartAsync()
    {
        _listener.Start();

        while (true)
        {
            var client = await _listener.AcceptTcpClientAsync();

            try { _ = Task.Run(() => HandleClientAsync(client)); }
            catch (Exception ex) { _logger.Error(ex.Message); }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            string clientIp = GetClientIpAddress(client);

            if (!IpIsWhitelisted(clientIp))
            {
                client.Close();
                return;
            }

            using TcpClient remoteProxy = new();
            (string remoteHost, int remotePort) = GetOneProxy();

            try { await remoteProxy.ConnectAsync(remoteHost, remotePort); }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                return;
            }

            using var clientStream = client.GetStream();
            using var remoteStream = remoteProxy.GetStream();

            await Task.WhenAny(
                clientStream.CopyToAsync(remoteStream),
                remoteStream.CopyToAsync(clientStream)
                );
        }
    }

    private bool IpIsWhitelisted(string clientIp)
    {
        var whitelist = _config.IpWhitelist;

        if (whitelist is null || whitelist.Length == 0)
            return true;

        return whitelist.Any(ip => clientIp == ip);
    }

    private static string GetClientIpAddress(TcpClient client)
    {
        var endPoint = (IPEndPoint?)client.Client.RemoteEndPoint;
        var ipAddress = endPoint?.Address;

        return ipAddress is null
            ? throw new Exception("Failed to get client IP address!")
            : ipAddress.ToString();
    }

    private (string, int) GetOneProxy()
    {
        int len = _proxies.Count;

        if (len == 1)
        {
            var proxy = _proxies.First();
            return (proxy.Key, proxy.Value);
        }

        lock (_proxyLock)
        {
            var randomProxy = _proxies.ElementAt(
                _rnd.Next(0, len)
                );

            return (randomProxy.Key, randomProxy.Value);
        }
    }

    private readonly Random _rnd = new();
    private readonly object _proxyLock = new();

    private static bool IsPortInUse(int port)
    {
        IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
        IPEndPoint[] activeEndPoints = ipGlobalProperties.GetActiveTcpListeners();

        foreach (IPEndPoint endPoint in activeEndPoints)
            if (endPoint.Port == port)
                return true;

        return false;
    }
}
