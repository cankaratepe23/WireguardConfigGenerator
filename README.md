# WireguardConfigGenerator

A small .NET 9 tool that keeps a WireGuard **split tunnel** current. It gathers
the IPs a service needs — from CIDR lists, resolved domains, and `crt.sh`
subdomain lookups — then rewrites **only** the `AllowedIPs =` line of a WireGuard
`.conf`, leaving the keys, endpoint, and addresses untouched.

The shipped default is a **Discord-only** split tunnel for a bot behind a
`linuxserver/wireguard` sidecar: Discord's Cloudflare-fronted API/gateway/CDN and
its Google-hosted `*.discord.media` voice go through the tunnel; everything else
(YouTube/Lavalink, etc.) egresses direct.

Originally built to run against WireSock on Windows; it now also runs as a
container so it can live inside a Docker Compose homelab as a VPN sidecar helper.

## Configuration

What to include is driven by `config.json` (baked into the image; see
[Configuration.cs](Configuration.cs) for the defaults):

| Field                    | Meaning                                                                        |
|--------------------------|--------------------------------------------------------------------------------|
| `IpSourceUrls`           | URLs returning newline-separated CIDR/IP lists (used as-is; never padded)       |
| `Domains`                | Hostnames resolved to A records                                                 |
| `CrtShSources`           | `crt.sh` JSON queries whose common names are resolved                           |
| `AlwaysInclude`          | CIDRs always present in the output; default `["10.66.66.1/32"]` (the tunnel IP) |
| `PadDnsResultsToSlash24` | Widen DNS-resolved A records from `/32` to their `/24`; default `false`          |

All fields are optional — omitting a key uses its default, so a partial config
never errors on a missing key. JSON keys are PascalCase but matched
case-insensitively, and `//` comments and trailing commas are tolerated. A
genuinely malformed config (bad JSON or wrong field types) is logged with a clear
error and the run is skipped, leaving the last-good conf in place.

`AlwaysInclude` guarantees the tunnel server IP is always in `AllowedIPs`, so the
sidecar healthcheck (`ping 10.66.66.1`) keeps passing even if every upstream
source fails. `PadDnsResultsToSlash24` covers sibling voice endpoints
(`*.discord.media`) that resolve to different IPs at connect time — a single
`/32` snapshot misses them, which surfaces as "voice connects, no audio". Padding
applies only to DNS-resolved results, never to `IpSourceUrls` CIDRs.

Two example configs ship in the repo:

- **`config.json`** — minimal, domain-resolved (the baked-in default).
- **`config.example.robust.json`** — tunnels Cloudflare's full published IPv4
  range (`https://www.cloudflare.com/ips-v4`) instead of relying on anycast DNS.
  More robust for a bot whose netns touches nothing else on Cloudflare; the
  `Domains` list becomes largely redundant. Voice still needs `crt.sh`.

### Environment variables

| Variable        | Default                                    | Purpose                                              |
|-----------------|--------------------------------------------|------------------------------------------------------|
| `WG_CONF_PATH`  | Windows WireSock path (container: `/config/wg_confs/wg0.conf`) | Conf whose `AllowedIPs` line is rewritten |
| `CONFIG_PATH`   | `config.json` next to the binary           | Config file location. **If unset, a mounted config is ignored — baked defaults are used** |
| `INTERVAL`      | `21600` (6h)                               | Seconds between runs (container loop)                |
| `VPN_CONTAINER` | `discord-vpn`                              | Container to reload (`wg-quick down/up`) after regenerating |
| `WG_INTERFACE`  | `wg0`                                       | Interface name (retained for compatibility; the reload uses `WG_CONF_PATH`) |
| `RELOAD`        | `true`                                      | Set `false` to only write the file, skip the reload  |

Run natively (Windows/WireSock) with no env vars set and it behaves exactly as
before.

## Running in Docker Compose

The container regenerates the conf on `INTERVAL`, then reloads the tunnel with
`wg-quick down/up` (via `docker exec` — **never** `docker restart`). `wg syncconf`
alone would update WireGuard's cryptokey routing but **not** the kernel routing
table, so freshly-resolved split-tunnel prefixes would get no route to `wg0` and
silently egress untunnelled until the next container start; `down/up` rebuilds
both. The cost is a **sub-second blip** where containers sharing the VPN's network
namespace (e.g. Discord bots via `network_mode: service:...`) briefly lose the
tunnel — negligible on a 6h interval. Reload requires the Docker socket mounted so
the generator can `docker exec` into the WireGuard container.

> **Heads-up on `CONFIG_PATH`:** mounting a `config.json` into the generator does
> nothing on its own — without `CONFIG_PATH` pointing at the mount, the app reads
> the baked-in `/app/config.json` (the minimal default). Set both together (see
> the compose example) to switch to the robust config.

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
