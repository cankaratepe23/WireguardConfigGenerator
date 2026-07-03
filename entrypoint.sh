#!/bin/sh
# Regenerate the WireGuard split-tunnel conf on an interval and apply it live.
set -eu

: "${INTERVAL:=21600}"                        # seconds between runs (6h)
: "${VPN_CONTAINER:=discord-vpn}"             # name of the linuxserver/wireguard container
: "${WG_INTERFACE:=wg0}"                      # retained for compatibility; reload keys off WG_CONF_PATH
: "${WG_CONF_PATH:=/config/wg_confs/wg0.conf}" # conf path (shared with the VPN container)
: "${RELOAD:=true}"                           # set to false to only write the file

export WG_CONF_PATH

# Snapshot of the last conf that successfully came up, so a bad regen can roll back
# instead of leaving the tunnel down until the next cycle. Kept in the generator's own
# /tmp, not /config/wg_confs (whose *.conf files linuxserver would try to start).
LAST_GOOD_CONF="/tmp/wg-last-good.conf"

# wg-quick down/up (not wg syncconf): syncconf updates cryptokey routing but not the
# kernel routing table, so newly-resolved split-tunnel prefixes would get no route to
# wg0. down/up rebuilds both. Sub-second blip; never `docker restart` the container.
# Full conf path — linuxserver/wireguard brings tunnels up by path and has no
# /etc/wireguard/wg0.conf, so a bare `wg0` name would fail. `;` so up runs even if down
# reports the interface already down.
reload_tunnel() {
  docker exec "$VPN_CONTAINER" sh -c "wg-quick down ${WG_CONF_PATH}; wg-quick up ${WG_CONF_PATH}"
}

# Seed last-good from the conf the sidecar is already running (the generator waits for it
# to be healthy via depends_on), so even the first cycle has something to roll back to.
if [ "$RELOAD" = "true" ] && [ -f "$WG_CONF_PATH" ]; then
  cp -f "$WG_CONF_PATH" "$LAST_GOOD_CONF" 2>/dev/null || true
fi

while true; do
  echo "[gen] $(date -u '+%Y-%m-%dT%H:%M:%SZ') regenerating ${WG_CONF_PATH}"
  if dotnet WireguardConfigGenerator.dll; then
    if [ "$RELOAD" = "true" ]; then
      echo "[gen] reloading via wg-quick down/up on ${VPN_CONTAINER} (${WG_CONF_PATH})"
      if reload_tunnel; then
        cp -f "$WG_CONF_PATH" "$LAST_GOOD_CONF" 2>/dev/null || true   # new conf is up; it becomes last-good
      else
        # wg-quick up rejected the new conf (e.g. a poisoned source slipped a bad prefix
        # through). down already ran, so restore the last-good conf and bring it back up
        # rather than leave the tunnel down until the next cycle.
        echo "[gen] wg-quick up rejected the new conf; rolling back to last-good to keep the tunnel up"
        if [ -f "$LAST_GOOD_CONF" ] && cp -f "$LAST_GOOD_CONF" "$WG_CONF_PATH"; then
          reload_tunnel || echo "[gen] rollback reload also failed — ${VPN_CONTAINER} needs manual intervention (is it up and is the docker socket mounted?)"
        else
          echo "[gen] no last-good conf to roll back to — ${VPN_CONTAINER} needs manual intervention (is it up and is the docker socket mounted?)"
        fi
      fi
    fi
  else
    echo "[gen] generation failed; retrying next cycle"
  fi
  echo "[gen] sleeping ${INTERVAL}s"
  sleep "$INTERVAL"
done
