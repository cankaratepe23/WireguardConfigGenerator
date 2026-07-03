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
      echo "[gen] applying via wg syncconf on ${VPN_CONTAINER}/${WG_INTERFACE}"
      docker exec "$VPN_CONTAINER" sh -c \
        "wg-quick strip ${WG_CONF_PATH} > /tmp/wg.stripped && wg syncconf ${WG_INTERFACE} /tmp/wg.stripped && rm -f /tmp/wg.stripped" \
        || echo "[gen] reload failed (is ${VPN_CONTAINER} up and the docker socket mounted?)"
    fi
  else
    echo "[gen] generation failed; retrying next cycle"
  fi
  echo "[gen] sleeping ${INTERVAL}s"
  sleep "$INTERVAL"
done
