FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

# Contexto acotado: solo el SDK pin, BuildingBlocks y este servicio. Con `COPY . .`
# cualquier cambio en cualquier archivo del repo invalidaba esta capa y reconstruia
# las 20 imagenes; asi un cambio en otro servicio ya no toca esta.
COPY global.json ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Tasks/ src/Services/Tasks/
RUN dotnet restore "src/Services/Tasks/TaxVision.Tasks.Api/TaxVision.Tasks.Api.csproj"
RUN dotnet publish "src/Services/Tasks/TaxVision.Tasks.Api/TaxVision.Tasks.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Tasks.Api.dll"]
