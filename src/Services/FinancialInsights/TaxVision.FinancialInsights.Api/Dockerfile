FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src
COPY global.json ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/FinancialInsights/ src/Services/FinancialInsights/
RUN dotnet restore "src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj"
RUN dotnet publish "src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
ENV MALLOC_ARENA_MAX=2
RUN apt-get update && apt-get install -y --no-install-recommends poppler-utils tesseract-ocr tesseract-ocr-eng \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.FinancialInsights.Api.dll"]
