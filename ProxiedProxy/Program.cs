using NLog;
using ProxiedProxy.Models;
using Tomlyn;

namespace ProxiedProxy;

internal class Program
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private static readonly List<Task> _servers = new();

    private static readonly Dictionary<string, Dictionary<string, int>?> _proxyFileCache = new();

    private static async Task Main(string[] args)
    {
        try { await MainAsync(); }
        catch (Exception ex)
        {
            _logger.Error(ex.Message, ex);
            Console.WriteLine(ex);
        }

        Exit();
    }

    private static async Task MainAsync()
    {
        if (!File.Exists(CONFIG_FILE))
        {
            await GenerateDefaultConfigAsync();
            return;
        }

        var servers = await TryParseConfigAsync();

        if (servers is null || servers.Length == 0)
        {
            _logger.Warn("Failed to load config!");
            Exit();
        }

        HashSet<int> ports = new();

        foreach (var serverConfig in servers!)
        {
            if (!serverConfig.BindRemoteProxy)
            {
                ports.Add(RunServer(serverConfig));
                continue;
            }

            var resp = await RunLinkedServersAsync(serverConfig);

            if (resp is not null)
                ports.UnionWith(resp);
        }

        // TODO: Make some sure for "ports"

        await Task.WhenAll(_servers);
    }

    private static async Task<HashSet<int>?> RunLinkedServersAsync(ServerConfig config)
    {
        var proxies = await LoadProxiesAsync(config);

        if (proxies is null || proxies.Count == 0)
        {
            _logger.Error("Failed to load proxies!");
            return null;
        }

        HashSet<int> ports = new();

        foreach (var proxy in proxies)
        {
            ports.Add(
                RunServer(
                    config, new() { { proxy.Key, proxy.Value } }
                    )
                );

            config.Port++;
        }

        return ports;
    }

    private static int RunServer(
        ServerConfig config,
        Dictionary<string, int>? proxies = null
        )
    {
        _servers.Add(StartServerAsync(config, proxies));
        return config.Port;
    }

    private static async Task StartServerAsync(
        ServerConfig config,
        Dictionary<string, int>? proxies = null
        )
    {
        proxies ??= await LoadProxiesAsync(config);

        try
        {
            ProxyServer server = new(config, proxies);

            _logger.Info(
                "Started server using port: {0}",
                config.Port
                );

            await server.StartAsync();
        }
        catch (Exception ex) { _logger.Error(ex.Message, ex); }
    }

    private static async Task<Dictionary<string, int>?> LoadProxiesAsync(
        ServerConfig config
        )
    {
        string? path = config.ProxyFile;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        if (_proxyFileCache.ContainsKey(path))
            return _proxyFileCache[path];

        var lines = await File.ReadAllLinesAsync(path);
        char[] splitter = { ':', ';', '|' };

        Dictionary<string, int> res = new();

        foreach (var i in lines)
        {
            var data = i.Split(
                splitter,
                StringSplitOptions.RemoveEmptyEntries
                );

            res.TryAdd(data[0], int.Parse(data[1]));
        }

        _proxyFileCache.Add(path, res);
        return res;
    }

    private static async Task<ServerConfig[]?> TryParseConfigAsync()
    {
        var model = Toml.ToModel<ConfigModel>(
            await File.ReadAllTextAsync(CONFIG_FILE)
            );

        if (model is null)
            return null;

        var servers = model.Servers;

        if (servers is null || servers.Count == 0)
            return null;

        var result = new ServerConfig[servers.Count];

        for (int i = 0; i < servers.Count; i++)
        {
            var server = servers[i];
            var ipFile = server.IpFile;

            // TODO: Add cache for this
            string[]? ipWhitelist = null;

            if (ipFile is not null)
            {
                ipWhitelist = await File.ReadAllLinesAsync(
                    ipFile
                    );

                if (ipWhitelist.Length == 0)
                    ipWhitelist = null;
            }

            if (ipWhitelist is null)
                _logger.Warn("There is no IP whitelist in place, requests from any source will be handled.");

            result[i] = new()
            {
                Port = server.Port,
                IpWhitelist = ipWhitelist,
                BindRemoteProxy = server.BindRemoteProxy,
                ProxyFile = server.ProxyFile
            };
        }

        return result;
    }

    private static async Task GenerateDefaultConfigAsync()
    {
        _logger.Warn("Config file not found!");

        ConfigModel config = new()
        {
            Servers = new()
            {
                new()
                {
                    Port = 8800,
                    IpFile = ".allowed.txt",
                    BindRemoteProxy = false,
                    ProxyFile = ".proxy.txt"
                }
            }
        };

        await File.WriteAllTextAsync(
            CONFIG_FILE,
            Toml.FromModel(config)
            );

        _logger.Info("Generated config!.");
        Exit();
    }

    private static void Exit()
    {
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey(true);
        Environment.Exit(0);
    }

    private const string CONFIG_FILE = ".config.toml";
}