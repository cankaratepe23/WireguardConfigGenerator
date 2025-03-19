using System.Text.Json;

namespace WireguardConfigGenerator;

public class Configuration
{
    public required List<string> IpSourceUrls { get; set; }
    public required List<string> Domains { get; set; }

    public static Configuration GetDefaultConfiguration()
    {
        return new Configuration()
        {
            IpSourceUrls =
            [
                "https://raw.githubusercontent.com/HybridNetworks/whatsapp-cidr/main/WhatsApp/whatsapp_cidr_ipv4.txt",
                "https://raw.githubusercontent.com/touhidurrr/iplist-youtube/main/ipv4_list.txt"
            ],
            Domains =
            [
                "discord.com",
                "discordapp.com",
                "discord.gg",
                "discordstatus.com"
            ]
        };
    }

    public static Configuration LoadConfiguration()
    {
        const string configPath = @"config.json";

        if (!File.Exists(configPath))
        {
            return GetDefaultConfiguration();
        }

        using var file = File.OpenRead(configPath);
        var loadedConfig = JsonSerializer.Deserialize<Configuration>(file);

        return loadedConfig ?? GetDefaultConfiguration();
    }
}