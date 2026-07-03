# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY WireguardConfigGenerator.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish WireguardConfigGenerator.csproj -c Release -o /app

# ---- docker cli (static) ----
# We only need the client, to `docker exec` a `wg syncconf` into the VPN container.
FROM alpine:3.20 AS dockercli
ARG DOCKER_VERSION=27.3.1
# Set to aarch64 for arm64 hosts (e.g. Raspberry Pi).
ARG DOCKER_ARCH=x86_64
RUN apk add --no-cache curl \
 && curl -fsSL "https://download.docker.com/linux/static/stable/${DOCKER_ARCH}/docker-${DOCKER_VERSION}.tgz" \
    | tar -xz -C /tmp \
 && cp /tmp/docker/docker /usr/local/bin/docker \
 && chmod +x /usr/local/bin/docker

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app ./
COPY --from=dockercli /usr/local/bin/docker /usr/local/bin/docker
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

ENV WG_CONF_PATH=/config/wg_confs/wg0.conf \
    INTERVAL=21600 \
    VPN_CONTAINER=discord-vpn \
    WG_INTERFACE=wg0 \
    RELOAD=true

ENTRYPOINT ["/entrypoint.sh"]
