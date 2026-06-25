FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/Services/Tenant/TaxVision.Tenant.Api/TaxVision.Tenant.Api.csproj"
RUN dotnet publish "src/Services/Tenant/TaxVision.Tenant.Api/TaxVision.Tenant.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Tenant.Api.dll"]
