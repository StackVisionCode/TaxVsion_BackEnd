FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

# Contexto acotado: solo el SDK pin, BuildingBlocks y este servicio. Con `COPY . .`
# cualquier cambio en cualquier archivo del repo invalidaba esta capa y reconstruia
# las 20 imagenes; asi un cambio en otro servicio ya no toca esta.
COPY global.json ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Calendar/ src/Services/Calendar/
RUN dotnet restore "src/Services/Calendar/TaxVision.Calendar.Api/TaxVision.Calendar.Api.csproj"
RUN dotnet publish "src/Services/Calendar/TaxVision.Calendar.Api/TaxVision.Calendar.Api.csproj"     -c Release     -o /app/publish     --no-restore     /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Calendar.Api.dll"]
