using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Serilog;
using Serilog.Core;

namespace WireguardConfigGenerator;

internal static class Program
{
    private static readonly HttpClient Client = new();
    private static readonly HashSet<string> IpList = new();

    private static async Task Main(string[] args)
    {
        ConfigureLogger();
        var configuration = Configuration.LoadConfiguration();

        var ipSourceUrls = configuration.IpSourceUrls;
        var ipSourceParseTasks = ipSourceUrls.Select(ParseIpSourceUrlAsync);

        var domainsList = configuration.Domains;
        var domainParseTasks = domainsList.Select(d => ResolveDnsAsync(d, configuration.PadDnsResultsToSlash24));

        var crtShResults = new HashSet<string>();
        var crtShSources = configuration.CrtShSources;
        await Task.WhenAll(crtShSources.Select(async crtShSource =>
        {
            try
            {
                var content = await Client.GetStreamAsync(crtShSource);
                using var document = await JsonDocument.ParseAsync(content);
                var commonNames = document.RootElement.EnumerateArray().Select(element =>
                {
                    element.TryGetProperty("common_name", out var value);
                    return value.GetString();
                }).Where(commonName => !string.IsNullOrWhiteSpace(commonName) &&
                                       !commonName.StartsWith("stage") &&
                                       !commonName.StartsWith("us-") &&
                                       !commonName.StartsWith("newark") &&
                                       !commonName.StartsWith("santa-clara") &&
                                       !commonName.StartsWith("japan") &&
                                       !commonName.StartsWith("brazil") &&
                                       !commonName.StartsWith("santiago") &&
                                       !commonName.StartsWith("sydney") &&
                                       !commonName.StartsWith("russia") &&
                                       !commonName.StartsWith("tel-aviv") &&
                                       !commonName.StartsWith("hongkong") &&
                                       !commonName.StartsWith("atlanta") &&
                                       !commonName.StartsWith("south-korea") &&
                                       !commonName.StartsWith("singapore")
                ).Cast<string>().ToList();

                if (commonNames.Count == 0)
                {
                    Log.Warning(
                        "crt.sh source {CrtShSource} returned no usable names — voice coverage may be incomplete",
                        crtShSource);
                }

                lock (crtShResults)
                {
                    crtShResults.UnionWith(commonNames);
                }
            }
            catch (Exception e) when (e is HttpRequestException or JsonException)
            {
                // crt.sh is frequently flaky (502s, rate limits). Don't let it sink
                // the whole run — the IP sources and domain lookups are still valid.
                Log.Warning(e, e.Message);
                Log.Warning("Skipping crt.sh source {CrtShSource} due to the above error", crtShSource);
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
                throw;
            }
        }));

        var crtShTasks = crtShResults.Select(name => ResolveDnsAsync(name, configuration.PadDnsResultsToSlash24));

        var allTasks = new List<Task>();
        allTasks.AddRange(ipSourceParseTasks);
        allTasks.AddRange(domainParseTasks);
        allTasks.AddRange(crtShTasks);
        await Task.WhenAll(allTasks);

        var wireguardProfilePath =
            Environment.GetEnvironmentVariable("WG_CONF_PATH")
            ?? Environment.ExpandEnvironmentVariables(
                @"%LOCALAPPDATA%\WireSock Foundation\WireSock Secure Connect\Profiles\wg0-ovh.conf");
        if (!File.Exists(wireguardProfilePath))
        {
            throw new FileNotFoundException("Could not find configuration file", wireguardProfilePath);
        }

        var fileContent = await File.ReadAllLinesAsync(wireguardProfilePath);
        var ipListString = "";
        lock (IpList)
        {
            IpList.UnionWith(configuration.AlwaysInclude);
            Log.Information("Generating list of {IpCount} IP addresses...", IpList.Count);
            ipListString = string.Join(',', IpList);
        }
        var newContent = fileContent.Select(line =>
            line.TrimStart().StartsWith("AllowedIPs = ") ? "AllowedIPs = " + ipListString : line);
        await File.WriteAllLinesAsync(wireguardProfilePath, newContent);
    }

    private static void ConfigureLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "config-generator.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
        
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception occurred");
            Log.CloseAndFlush(); // Ensure logs are written
        };
    }

    private static async Task ParseIpSourceUrlAsync(string url)
    {
        try
        {
            var content = await Client.GetStringAsync(url);
            var lines = content.Split("\n").Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'));
            lock (IpList)
            {
                IpList.UnionWith(lines);
            }
        }
        catch (HttpRequestException httpRequestException)
        {
            Log.Warning(httpRequestException, httpRequestException.Message);
            Log.Warning("Skipping IP source {IpSourceUrl} due to the above error", url);
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
            throw;
        }
    }

    private static async Task ResolveDnsAsync(string domain, bool padToSlash24)
    {
        try
        {
            var hostEntry = await Dns.GetHostAddressesAsync(domain);
            var resolved = hostEntry
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => padToSlash24 ? ToSlash24(a) : a.ToString());
            lock (IpList)
            {
                IpList.UnionWith(resolved);
            }
        }
        catch (SocketException se)
        {
            Log.Warning(se, se.Message);
        }
        catch (Exception e)
        {
            Log.Error("Error when resolving: {Domain}", domain);
            Log.Error(e, e.Message);
            throw;
        }
    }

    // Widen an IPv4 address to its containing /24 (zero the final octet).
    private static string ToSlash24(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        bytes[3] = 0;
        return $"{new IPAddress(bytes)}/24";
    }
}