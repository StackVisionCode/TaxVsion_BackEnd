FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

# Mismo contexto acotado que connectors-api: solo el SDK pin, BuildingBlocks y Connectors.
COPY global.json ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Connectors/ src/Services/Connectors/
RUN dotnet restore "src/Services/Connectors/TaxVision.Connectors.MailboxListener/TaxVision.Connectors.MailboxListener.csproj"
RUN dotnet publish "src/Services/Connectors/TaxVision.Connectors.MailboxListener/TaxVision.Connectors.MailboxListener.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
WORKDIR /app
EXPOSE 8080

# curl para el HEALTHCHECK de docker-compose (GET /health/ready). Sin puertos publicados: nadie le habla.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Connectors.MailboxListener.dll"]
