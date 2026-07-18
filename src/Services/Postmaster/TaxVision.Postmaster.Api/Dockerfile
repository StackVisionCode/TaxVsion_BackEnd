FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/Services/Postmaster/TaxVision.Postmaster.Api/TaxVision.Postmaster.Api.csproj"
RUN dotnet publish "src/Services/Postmaster/TaxVision.Postmaster.Api/TaxVision.Postmaster.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Postmaster.Api.dll"]
