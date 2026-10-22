# ============================================================
# Wrapper de Docker Compose para TaxVision.
# Fuerza SIEMPRE el .env de la raíz del repositorio, sin importar
# desde qué directorio se ejecute.
#
# Uso (desde cualquier ruta):
#   .\deploy\docker\compose.ps1 up -d --build            # stack completo (producción)
#   .\deploy\docker\compose.ps1 local up -d --build      # sin Caddy, PaymentApp ni Signature
#   .\deploy\docker\compose.ps1 infra up -d              # solo RabbitMQ, Redis, MinIO y ClamAV
#   .\deploy\docker\compose.ps1 logs -f notification-api
#   .\deploy\docker\compose.ps1 down
# ============================================================

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path   # deploy/docker
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")        # raíz del repo
$envFile = Join-Path $repoRoot ".env"

$mode = "full"
$composeArgs = @($args)
if ($composeArgs.Count -gt 0 -and $composeArgs[0] -in @("local", "infra")) {
    $mode = $composeArgs[0]
    $composeArgs = @($composeArgs | Select-Object -Skip 1)
}

if (-not (Test-Path $envFile)) {
    Write-Error "No se encontró $envFile. Crea el .env en la raíz del repositorio."
}

switch ($mode) {
    "infra" {
        $files = @("-f", (Join-Path $scriptDir "docker-compose.local-infra.yml"))
    }
    "local" {
        # Las claves JWT de desarrollo viven en dev-keys (fuera de git); los contenedores las montan desde ahí.
        $devKeys = Join-Path $repoRoot "dev-keys"
        foreach ($key in @("jwt-private.pem", "jwt-public.pem")) {
            if (-not (Test-Path (Join-Path $devKeys $key))) {
                Write-Error "Falta dev-keys\$key. Genera la pareja JWT de desarrollo antes de usar el modo local."
            }
        }
        $env:TAXVISION_SECRETS_DIR = $devKeys
        # Dos tareas a la vez: la compilación inicial no agota la memoria de Docker Desktop.
        $env:COMPOSE_PARALLEL_LIMIT = "2"
        $files = @(
            "-f", (Join-Path $scriptDir "docker-compose.yml"),
            "-f", (Join-Path $scriptDir "docker-compose.local.yml")
        )
    }
    default {
        $files = @("-f", (Join-Path $scriptDir "docker-compose.yml"))
    }
}

docker compose --env-file $envFile @files @composeArgs
exit $LASTEXITCODE
