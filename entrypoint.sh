#!/bin/sh
# Regenerate the WireGuard split-tunnel conf on an interval and apply it live.
set -eu

: "${INTERVAL:=21600}"                        # seconds between runs (6h)
: "${VPN_CONTAINER:=discord-vpn}"             # name of the linuxserver/wireguard container
: "${WG_INTERFACE:=wg0}"                      # running interface to sync
: "${WG_CONF_PATH:=/config/wg_confs/wg0.conf}" # conf path (shared with the VPN container)
: "${RELOAD:=true}"                           # set to false to only write the file

export WG_CONF_PATH

while true; do
  echo "[gen] $(date -u '+%Y-%m-%dT%H:%M:%SZ') regenerating ${WG_CONF_PATH}"
  if dotnet WireguardConfigGenerator.dll; then
    if [ "$RELOAD" = "true" ]; then
      # wg-quick down/up (not wg syncconf): syncconf updates cryptokey routing but not the
      # kernel routing table, so newly-resolved split-tunnel prefixes would get no route to
      # wg0. down/up rebuilds both. Sub-second blip; never `docker restart` the container.
      # Use the full conf path — linuxserver/wireguard brings tunnels up by path and has no
      # /etc/wireguard/wg0.conf, so a bare `wg0` name would fail. `;` so up runs even if down
      # reports the interface already down.
      echo "[gen] reloading via wg-quick down/up on ${VPN_CONTAINER} (${WG_CONF_PATH})"
      docker exec "$VPN_CONTAINER" sh -c \
        "wg-quick down ${WG_CONF_PATH}; wg-quick up ${WG_CONF_PATH}" \
        || echo "[gen] reload failed (is ${VPN_CONTAINER} up and the docker socket mounted?)"
    fi
  else
    echo "[gen] generation failed; retrying next cycle"
  fi
  echo "[gen] sleeping ${INTERVAL}s"
  sleep "$INTERVAL"
done
