# Contexto = raíz del repo (convención de docker-compose): las rutas COPY parten de ahí.
FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src
COPY global.json ./
COPY src/Services/ContentScanner/TaxVision.ContentScanner/TaxVision.ContentScanner.csproj TaxVision.ContentScanner/
RUN dotnet restore TaxVision.ContentScanner/TaxVision.ContentScanner.csproj
COPY src/Services/ContentScanner/TaxVision.ContentScanner/ TaxVision.ContentScanner/
RUN dotnet publish TaxVision.ContentScanner/TaxVision.ContentScanner.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Modelo fijado a un commit y verificado por SHA-256: se descarga en el build, nunca en ejecución.
# AdamCodd/vit-base-nsfw-detector (Apache-2.0), ONNX cuantizado, entrada 384x384, clases sfw/nsfw.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS model
ARG MODEL_URL=https://huggingface.co/AdamCodd/vit-base-nsfw-detector/resolve/8587de998f441aac03fdd57a85d2e4cb808c7d64/onnx/model_quantized.onnx
ARG MODEL_SHA256=432763a6899ebc418c55f784b98f90565e5fc694c778d2ffcb0294b12f6a7404
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL "$MODEL_URL" -o /nsfw.onnx \
    && echo "$MODEL_SHA256  /nsfw.onnx" | sha256sum -c -

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
WORKDIR /app
EXPOSE 8080

# ffmpeg decodifica imágenes y muestrea videos; curl es para el healthcheck de docker-compose.
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /tmp/content-scanner && chown app:app /tmp/content-scanner

COPY --from=model /nsfw.onnx /models/nsfw.onnx
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ContentScanner__ModelPath=/models/nsfw.onnx \
    ContentScanner__TempDirectory=/tmp/content-scanner

# Decodifica archivos no confiables: corre sin privilegios.
USER app
ENTRYPOINT ["dotnet", "TaxVision.ContentScanner.dll"]
