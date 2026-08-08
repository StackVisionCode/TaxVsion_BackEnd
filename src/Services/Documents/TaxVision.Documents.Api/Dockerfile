FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

# Contexto acotado: solo el SDK pin, BuildingBlocks y este servicio. Con `COPY . .`
# cualquier cambio en cualquier archivo del repo invalidaba esta capa y reconstruia
# las 19 imagenes; asi un cambio en otro servicio ya no toca esta.
COPY global.json ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Documents/ src/Services/Documents/
RUN dotnet restore "src/Services/Documents/TaxVision.Documents.Api/TaxVision.Documents.Api.csproj"
RUN dotnet publish "src/Services/Documents/TaxVision.Documents.Api/TaxVision.Documents.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Chromium headless (Playwright) para HTML->PDF. La imagen mcr .../playwright/dotnet trae Chromium +
# dependencias del sistema ya instaladas para Linux (fuentes incluidas), evitando el error
# "No appropriate font found" y el peso de instalar navegadores en runtime.
FROM mcr.microsoft.com/playwright/dotnet:v1.49.0-noble AS final
WORKDIR /app
EXPOSE 8080

# La base de Playwright v1.49 trae .NET 8 + Chromium + fuentes; el app es net10.0 → agregar el
# runtime ASP.NET 10 junto al 8 (dotnet elige el compatible). Mantiene Chromium/fuentes de la base.
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget ca-certificates \
    && wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh \
    && apt-get purge -y wget \
    && apt-get autoremove -y \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Documents.Api.dll"]
