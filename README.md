# WireguardConfigGenerator

A small .NET 9 tool that keeps a WireGuard **split tunnel** current. It gathers
IPs for a set of services (Discord/WhatsApp/YouTube CIDR lists, resolved
domains, and `crt.sh` subdomain lookups), then rewrites **only** the
`AllowedIPs =` line of a WireGuard `.conf` — leaving the keys, endpoint, and
addresses untouched.

Originally built to run against WireSock on Windows; it now also runs as a
container so it can live inside a Docker Compose homelab as a VPN sidecar helper.

## Configuration

What to include is driven by `config.json` (baked into the image; see
[Configuration.cs](Configuration.cs) for the defaults):

| Field          | Meaning                                                        |
|----------------|----------------------------------------------------------------|
| `IpSourceUrls` | URLs returning newline-separated CIDR/IP lists                 |
| `Domains`      | Hostnames resolved to A records                                |
| `CrtShSources` | `crt.sh` JSON queries whose common names are resolved          |

### Environment variables

| Variable        | Default                                    | Purpose                                              |
|-----------------|--------------------------------------------|------------------------------------------------------|
| `WG_CONF_PATH`  | Windows WireSock path (container: `/config/wg_confs/wg0.conf`) | Conf whose `AllowedIPs` line is rewritten |
| `CONFIG_PATH`   | `config.json` next to the binary           | Override the config file location                    |
| `INTERVAL`      | `21600` (6h)                               | Seconds between runs (container loop)                |
| `VPN_CONTAINER` | `discord-vpn`                              | Container to `wg syncconf` after regenerating        |
| `WG_INTERFACE`  | `wg0`                                       | Interface synced live                                |
| `RELOAD`        | `true`                                      | Set `false` to only write the file, skip the reload  |

Run natively (Windows/WireSock) with no env vars set and it behaves exactly as
before.

## Running in Docker Compose

The container regenerates the conf on `INTERVAL`, then applies the new IPs
**live** with `wg syncconf` — no interface restart, so containers sharing the
VPN's network namespace (e.g. Discord bots via `network_mode: service:...`) are
never disconnected. Live reload requires the Docker socket mounted so the
generator can `docker exec` into the WireGuard container.

See [docker-compose.example.yml](docker-compose.example.yml) for a full stack.

> **Note:** current `linuxserver/wireguard` reads client confs from
> `/config/wg_confs/wg0.conf`. Place your base `wg0.conf` (with its keys) there
> before first start. WireGuard `.conf` files contain private keys and are
> `.gitignore`d — never commit them.

## CI/CD

[`.github/workflows/docker-publish.yml`](.github/workflows/docker-publish.yml)
builds and pushes to GHCR
(`ghcr.io/cankaratepe23/wireguardconfiggenerator`) on pushes to `master` and on
`v*` tags.
