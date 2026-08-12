FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY global.json ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Inventory/ src/Services/Inventory/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "src/Services/Inventory/TaxVision.Inventory.Api/TaxVision.Inventory.Api.csproj"
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "src/Services/Inventory/TaxVision.Inventory.Api/TaxVision.Inventory.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Inventory.Api.dll"]
