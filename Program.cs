using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace WireguardConfigGenerator;

internal abstract class Program
{
    private static async Task Main(string[] args)
    {
        var configuration = Configuration.LoadConfiguration();
        
        var ipSourceUrls = configuration.IpSourceUrls;

        var ipList = new List<string>();
        using var client = new HttpClient();

        await Task.WhenAll(ipSourceUrls.Select(async url =>
        {
            var content = await client.GetStringAsync(url);
            var lines = content.Split("\n").Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'));
            lock (ipList)
            {
                ipList.AddRange(lines);
            }
        }));

        var domainsList = configuration.Domains;

        await Task.WhenAll(domainsList.Select(async domain =>
        {
            var hostEntry = await Dns.GetHostAddressesAsync(domain);
            lock (ipList)
            {
                ipList.AddRange(hostEntry.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()));
            }
        }));
        
        var configFilePath = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\WireSock Foundation\WireSock Secure Connect\Profiles\wg0-ovh.conf");
        if (!File.Exists(configFilePath))
        {
            throw new FileNotFoundException("Could not find configuration file", configFilePath);
        }
        
        var fileContent = await File.ReadAllLinesAsync(configFilePath);
        var newContent = fileContent.Select(line =>
            line.TrimStart().StartsWith("AllowedIPs = ") ? "AllowedIPs = " + string.Join(',', ipList) : line);
        await File.WriteAllLinesAsync(configFilePath, newContent);
    }
}