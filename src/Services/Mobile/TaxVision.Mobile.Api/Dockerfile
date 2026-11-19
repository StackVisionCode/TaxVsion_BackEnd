FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src
COPY global.json ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Mobile/ src/Services/Mobile/
RUN dotnet restore "src/Services/Mobile/TaxVision.Mobile.Api/TaxVision.Mobile.Api.csproj"
RUN dotnet publish "src/Services/Mobile/TaxVision.Mobile.Api/TaxVision.Mobile.Api.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
# Contenedor en UTC declarado; la zona de negocio es la de la oficina, no la del proceso.
ENV TZ=Etc/UTC
ENV MALLOC_ARENA_MAX=2
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Mobile.Api.dll"]
