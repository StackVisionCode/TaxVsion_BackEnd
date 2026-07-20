FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/Services/Growth/TaxVision.Growth.Api/TaxVision.Growth.Api.csproj"
RUN dotnet publish "src/Services/Growth/TaxVision.Growth.Api/TaxVision.Growth.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Growth.Api.dll"]
