FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Services/Payment/TaxVision.Payment.Api/TaxVision.Payment.Api.csproj", "src/Services/Payment/TaxVision.Payment.Api/"]
COPY ["src/Services/Payment/TaxVision.Payment.Application/TaxVision.Payment.Application.csproj", "src/Services/Payment/TaxVision.Payment.Application/"]
COPY ["src/Services/Payment/TaxVision.Payment.Domain/TaxVision.Payment.Domain.csproj", "src/Services/Payment/TaxVision.Payment.Domain/"]
COPY ["src/Services/Payment/TaxVision.Payment.Infrastructure/TaxVision.Payment.Infrastructure.csproj", "src/Services/Payment/TaxVision.Payment.Infrastructure/"]
COPY ["src/BuildingBlocks/BuildingBlocks.csproj", "src/BuildingBlocks/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Infrastructure/BuildingBlocks.Infrastructure.csproj", "src/BuildingBlocks/BuildingBlocks.Infrastructure/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Web/BuildingBlocks.Web.csproj", "src/BuildingBlocks/BuildingBlocks.Web/"]
RUN dotnet restore "src/Services/Payment/TaxVision.Payment.Api/TaxVision.Payment.Api.csproj"
COPY . .
WORKDIR "/src/src/Services/Payment/TaxVision.Payment.Api"
RUN dotnet build "TaxVision.Payment.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TaxVision.Payment.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Payment.Api.dll"]
