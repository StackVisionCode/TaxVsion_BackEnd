# ============================================================
# Wrapper de Docker Compose para TaxVision.
# Fuerza SIEMPRE el .env de la raíz del repositorio, sin importar
# desde qué directorio se ejecute.
#
# Uso (desde cualquier ruta):
#   .\deploy\docker\compose.ps1 up -d --build
#   .\deploy\docker\compose.ps1 ps
#   .\deploy\docker\compose.ps1 logs -f notification-api
#   .\deploy\docker\compose.ps1 down
# ============================================================

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path   # deploy/docker
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")        # raíz del repo
$envFile = Join-Path $repoRoot ".env"
$composeFile = Join-Path $scriptDir "docker-compose.yml"

if (-not (Test-Path $envFile)) {
    Write-Error "No se encontró $envFile. Crea el .env en la raíz del repositorio."
}

docker compose --env-file $envFile -f $composeFile @args
exit $LASTEXITCODE
