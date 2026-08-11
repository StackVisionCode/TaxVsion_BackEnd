FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

# Contexto acotado: solo el SDK pin, BuildingBlocks y este servicio.
COPY global.json ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Sms/ src/Services/Sms/
RUN dotnet restore "src/Services/Sms/TaxVision.Sms.Api/TaxVision.Sms.Api.csproj"
RUN dotnet publish "src/Services/Sms/TaxVision.Sms.Api/TaxVision.Sms.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Sms.Api.dll"]
