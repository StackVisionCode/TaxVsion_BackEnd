FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Services/Subscription/TaxVision.Subscription.Api/TaxVision.Subscription.Api.csproj", "src/Services/Subscription/TaxVision.Subscription.Api/"]
COPY ["src/Services/Subscription/TaxVision.Subscription.Application/TaxVision.Subscription.Application.csproj", "src/Services/Subscription/TaxVision.Subscription.Application/"]
COPY ["src/Services/Subscription/TaxVision.Subscription.Domain/TaxVision.Subscription.Domain.csproj", "src/Services/Subscription/TaxVision.Subscription.Domain/"]
COPY ["src/Services/Subscription/TaxVision.Subscription.Infrastructure/TaxVision.Subscription.Infrastructure.csproj", "src/Services/Subscription/TaxVision.Subscription.Infrastructure/"]
COPY ["src/BuildingBlocks/BuildingBlocks.csproj", "src/BuildingBlocks/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Infrastructure/BuildingBlocks.Infrastructure.csproj", "src/BuildingBlocks/BuildingBlocks.Infrastructure/"]
COPY ["src/BuildingBlocks/BuildingBlocks.Web/BuildingBlocks.Web.csproj", "src/BuildingBlocks/BuildingBlocks.Web/"]
RUN dotnet restore "src/Services/Subscription/TaxVision.Subscription.Api/TaxVision.Subscription.Api.csproj"
COPY . .
WORKDIR "/src/src/Services/Subscription/TaxVision.Subscription.Api"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Subscription.Api.dll"]
