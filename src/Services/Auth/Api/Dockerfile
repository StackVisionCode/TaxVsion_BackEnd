FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

# Contexto acotado: solo el SDK pin, BuildingBlocks y este servicio. Con `COPY . .`
# cualquier cambio en cualquier archivo del repo invalidaba esta capa y reconstruia
# las 19 imagenes; asi un cambio en otro servicio ya no toca esta.
COPY global.json ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Auth/ src/Services/Auth/
RUN dotnet restore "src/Services/Auth/Api/TaxVision.Auth.Api.csproj"
RUN dotnet publish "src/Services/Auth/Api/TaxVision.Auth.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Ubicación aproximada de las sesiones: DB-IP City Lite (CC BY 4.0, https://db-ip.com). Etapa aparte para
# que un cambio de código no la descargue otra vez. Si DB-IP no responde, la imagen sale sin archivo.
FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS geoip
ARG GEOIP_MONTH=
RUN mkdir -p /geoip \
    && for month in ${GEOIP_MONTH:-$(date -u +%Y-%m)} $(date -u -d "$(date -u +%Y-%m-01) -1 month" +%Y-%m); do \
         curl -fsSL --max-time 600 -o /tmp/dbip.mmdb.gz "https://download.db-ip.com/free/dbip-city-lite-${month}.mmdb.gz" \
         && gunzip -c /tmp/dbip.mmdb.gz > /geoip/dbip-city-lite.mmdb.tmp \
         && mv /geoip/dbip-city-lite.mmdb.tmp /geoip/dbip-city-lite.mmdb \
         && break; \
       done; \
    rm -f /tmp/dbip.mmdb.gz /geoip/*.tmp; true

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
# Contenedor en UTC declarado; la zona de negocio es la de la oficina, no la del proceso.
ENV TZ=Etc/UTC
# glibc: 2 arenas de malloc en vez de 8 por nucleo; .NET retenia memoria nativa ya liberada.
ENV MALLOC_ARENA_MAX=2
WORKDIR /app
EXPOSE 8080

# curl es necesario para el HEALTHCHECK de docker-compose (GET /health/ready) — la imagen
# base aspnet no lo trae instalado (Debian slim). Sin esto el healthcheck siempre falla con
# "curl: not found" y el contenedor queda "unhealthy" para siempre, bloqueando cualquier
# depends_on: condition: service_healthy que apunte a este servicio (auth-api es el único
# servicio del que dependen scribe-api/customer-api/communication-transcript-worker al arrancar).
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY --from=geoip /geoip/ ./geoip/
ENTRYPOINT ["dotnet", "TaxVision.Auth.Api.dll"]
