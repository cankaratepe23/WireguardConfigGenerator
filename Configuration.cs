using System.Text.Json;
using Serilog;

namespace WireguardConfigGenerator;

public class Configuration
{
    public required List<string> IpSourceUrls { get; set; }
    public required List<string> Domains { get; set; }
    public required List<string> CrtShSources { get; set; }

    public static Configuration GetDefaultConfiguration()
    {
        return new Configuration()
        {
            IpSourceUrls =
            [
                "https://raw.githubusercontent.com/HybridNetworks/whatsapp-cidr/main/WhatsApp/whatsapp_cidr_ipv4.txt",
                "https://raw.githubusercontent.com/touhidurrr/iplist-youtube/main/lists/ipv4.txt"
            ],
            Domains =
            [
                "discord.com",
                "discordapp.com",
                "discord.gg",
                "discordstatus.com"
            ],
            CrtShSources =
            [
                "https://crt.sh/json?q=discord.media&exclude=expired"
            ]
        };
    }

    public static Configuration LoadConfiguration()
    {
        var configPath = Environment.GetEnvironmentVariable("CONFIG_PATH")
                         ?? Path.Combine(AppContext.BaseDirectory, "config.json");

        if (!File.Exists(configPath))
        {
            Log.Information("Configuration file {configPath} not found", Path.GetFullPath(configPath));
            return GetDefaultConfiguration();
        }

        using var file = File.OpenRead(configPath);
        var loadedConfig = JsonSerializer.Deserialize<Configuration>(file);

        return loadedConfig ?? GetDefaultConfiguration();
    }
}