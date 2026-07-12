FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/Services/Signature/TaxVision.Signature.Api/TaxVision.Signature.Api.csproj"
RUN dotnet publish "src/Services/Signature/TaxVision.Signature.Api/TaxVision.Signature.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
WORKDIR /app
EXPOSE 8080

# Liberation Sans tiene metricas de glyph identicas a Arial — el PdfSharp
# font resolver (SealingFontResolver.cs) ya busca en
# /usr/share/fonts/truetype/liberation, solo falta instalar el paquete.
# Sin esto, cada XFont creation en el sellado PAdES-B falla con
# "No appropriate font found" en Linux (no pasa en Windows dev porque ahi
# Arial esta instalado de fabrica).
RUN apt-get update && apt-get install -y --no-install-recommends fonts-liberation \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.Signature.Api.dll"]
