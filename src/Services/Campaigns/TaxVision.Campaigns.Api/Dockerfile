FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

# Contexto acotado: solo el SDK pin, BuildingBlocks y este servicio.
COPY global.json ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Campaigns/ src/Services/Campaigns/
# Cache mount del NuGet global-packages: hace el restore REANUDABLE ante la red intermitente.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "src/Services/Campaigns/TaxVision.Campaigns.Api/TaxVision.Campaigns.Api.csproj"
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "src/Services/Campaigns/TaxVision.Campaigns.Api/TaxVision.Campaigns.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Campaigns.Api.dll"]
