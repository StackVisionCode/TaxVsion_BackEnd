FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/Services/Scribe/TaxVision.Scribe.Api/TaxVision.Scribe.Api.csproj"
RUN dotnet publish "src/Services/Scribe/TaxVision.Scribe.Api/TaxVision.Scribe.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Scribe.Api.dll"]
