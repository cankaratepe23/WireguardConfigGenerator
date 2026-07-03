using System.Text.Json;
using Serilog;

namespace WireguardConfigGenerator;

public class Configuration
{
    // All fields are optional. Omitting a key keeps the initializer below, so a partial
    // config never throws on a missing property. JSON keys are PascalCase but matched
    // case-insensitively (see SerializerOptions).
    public List<string> IpSourceUrls { get; set; } = [];
    public List<string> Domains { get; set; } = [];
    public List<string> CrtShSources { get; set; } = [];

    // Prefixes always present in the output, regardless of upstream source results.
    // Defaults to the tunnel server IP so the sidecar healthcheck (ping 10.66.66.1)
    // keeps passing and the client can reach the server after a regeneration.
    public List<string> AlwaysInclude { get; set; } = ["10.66.66.1/32"];

    // Widen DNS-resolved A records (Domains + crt.sh names) from /32 to their containing
    // /24. Catches sibling voice endpoints (*.discord.media) that resolve differently at
    // connect time. Never applies to IpSourceUrls, which are already correct CIDRs.
    public bool PadDnsResultsToSlash24 { get; set; } = false;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static Configuration GetDefaultConfiguration()
    {
        // Minimal Discord-only split tunnel: Cloudflare-fronted API/gateway/CDN via DNS
        // resolution, Google-hosted voice via crt.sh. Non-Discord traffic egresses direct.
        return new Configuration()
        {
            IpSourceUrls = [],
            Domains =
            [
                "discord.com",
                "gateway.discord.gg",
                "cdn.discordapp.com"
            ],
            CrtShSources =
            [
                "https://crt.sh/?q=discord.media&output=json&exclude=expired"
            ],
            AlwaysInclude = ["10.66.66.1/32"],
            PadDnsResultsToSlash24 = true
        };
    }

    public static Configuration LoadConfiguration()
    {
        var envConfigPath = Environment.GetEnvironmentVariable("CONFIG_PATH");
        var configPath = envConfigPath ?? Path.Combine(AppContext.BaseDirectory, "config.json");

        if (!File.Exists(configPath))
        {
            if (!string.IsNullOrEmpty(envConfigPath))
            {
                // CONFIG_PATH was set explicitly but points at no file. A common Compose
                // cause: the bind-mount source didn't exist at first `up`, so Docker created
                // a directory there. Make the otherwise-silent fallback loud.
                Log.Warning(
                    "CONFIG_PATH is set to {ConfigPath} but no file exists there (a missing bind-mount source makes Docker create a directory) — falling back to baked-in defaults",
                    Path.GetFullPath(configPath));
            }
            else
            {
                Log.Information("Configuration file {ConfigPath} not found, using baked-in defaults",
                    Path.GetFullPath(configPath));
            }

            return GetDefaultConfiguration();
        }

        try
        {
            using var file = File.OpenRead(configPath);
            var loadedConfig = JsonSerializer.Deserialize<Configuration>(file, SerializerOptions);
            return loadedConfig ?? GetDefaultConfiguration();
        }
        catch (JsonException ex)
        {
            Log.Error(ex,
                "Config at {ConfigPath} is malformed or has bad field types — fix it; keys are " +
                "PascalCase (case-insensitive) and all fields are optional. Aborting this run",
                Path.GetFullPath(configPath));
            Log.CloseAndFlush();
            Environment.Exit(1); // a clean non-zero exit, not an unhandled throw
            throw; // unreachable (Exit terminates); satisfies the compiler's return analysis
        }
    }
}
