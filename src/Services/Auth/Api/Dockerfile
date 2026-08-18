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

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
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
ENTRYPOINT ["dotnet", "TaxVision.Auth.Api.dll"]
