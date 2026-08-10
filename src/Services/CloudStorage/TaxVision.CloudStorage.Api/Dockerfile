FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

# Contexto acotado: solo el SDK pin, BuildingBlocks y este servicio. Con `COPY . .`
# cualquier cambio en cualquier archivo del repo invalidaba esta capa y reconstruia
# las 19 imagenes; asi un cambio en otro servicio ya no toca esta.
COPY global.json ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/CloudStorage/ src/Services/CloudStorage/
RUN dotnet restore "src/Services/CloudStorage/TaxVision.CloudStorage.Api/TaxVision.CloudStorage.Api.csproj"
RUN dotnet publish "src/Services/CloudStorage/TaxVision.CloudStorage.Api/TaxVision.CloudStorage.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
WORKDIR /app
EXPOSE 8080

# curl es necesario para el HEALTHCHECK de docker-compose (GET /health/ready) — ver el mismo
# comentario en src/Services/Auth/Api/Dockerfile. scribe-api/customer-api dependen de este
# servicio estando "service_healthy" antes de arrancar.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.CloudStorage.Api.dll"]
