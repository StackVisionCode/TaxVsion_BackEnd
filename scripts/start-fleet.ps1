# Staggered launcher (mirror of scripts/start-fleet.ps1, with pauses to avoid the SQL thundering herd).
# Auth first and wait until it listens; then the rest with pauses; Gateway last.
# La raiz del repo es la carpeta padre de scripts/: sirve en cualquier maquina sin editar la ruta.
$root = Split-Path -Parent $PSScriptRoot
$gap  = 3
$fleet = @(
  @{ n = "Auth";           d = "src\Services\Auth\Api";                                p = 5124 },
  @{ n = "Tenant";         d = "src\Services\Tenant\TaxVision.Tenant.Api";             p = 5217 },
  @{ n = "Customer";       d = "src\Services\Customer\TaxVision.Customer.Api";         p = 5263 },
  @{ n = "Wallet";         d = "src\Services\Wallet\TaxVision.Wallet.Api";             p = 5270 },
  @{ n = "Notification";   d = "src\Services\Notification\TaxVision.Notification.Api"; p = 5320 },
  @{ n = "CloudStorage";   d = "src\Services\CloudStorage\TaxVision.CloudStorage.Api"; p = 5330 },
  @{ n = "Signature";      d = "src\Services\Signature\TaxVision.Signature.Api";       p = 5340 },
  @{ n = "Subscription";   d = "src\Services\Subscription\TaxVision.Subscription.Api"; p = 5360 },
  @{ n = "Postmaster";     d = "src\Services\Postmaster\TaxVision.Postmaster.Api";     p = 5370 },
  @{ n = "Scribe";         d = "src\Services\Scribe\TaxVision.Scribe.Api";             p = 5380 },
  @{ n = "Connectors";     d = "src\Services\Connectors\TaxVision.Connectors.Api";     p = 5390 },
  # IMAP IDLE listener (worker, own process). Only reads the DB and publishes to taxvision-events.
  @{ n = "Connectors.MailboxListener"; d = "src\Services\Connectors\TaxVision.Connectors.MailboxListener"; p = 5395;
     e = @{ "Connectors__MailboxListener__Enabled" = "true" } },
  @{ n = "Correspondence"; d = "src\Services\Correspondence\TaxVision.Correspondence.Api"; p = 5400 },
  @{ n = "Growth";         d = "src\Services\Growth\TaxVision.Growth.Api";             p = 5410 },
  @{ n = "PaymentClient";  d = "src\Services\PaymentClient\TaxVision.PaymentClient.Api"; p = 5420 },
  @{ n = "PaymentApp";     d = "src\Services\PaymentApp\TaxVision.PaymentApp.Api";     p = 5430 },
  @{ n = "Notes";          d = "src\Services\Notes\TaxVision.Notes.Api";               p = 5440 },
  @{ n = "Documents";      d = "src\Services\Documents\TaxVision.Documents.Api";       p = 5450 },
  @{ n = "Billing";        d = "src\Services\Billing\TaxVision.Billing.Api";           p = 5460 },
  @{ n = "Sms";            d = "src\Services\Sms\TaxVision.Sms.Api";                   p = 5470 },
  @{ n = "Catalog";        d = "src\Services\Catalog\TaxVision.Catalog.Api";           p = 5480 },
  @{ n = "Inventory";      d = "src\Services\Inventory\TaxVision.Inventory.Api";       p = 5490 },
  @{ n = "Reminder";       d = "src\Services\Reminder\TaxVision.Reminder.Api";         p = 5500 },
  @{ n = "Tasks";          d = "src\Services\Tasks\TaxVision.Tasks.Api";               p = 5510 },
  @{ n = "Calendar";       d = "src\Services\Calendar\TaxVision.Calendar.Api";         p = 5520 },
  @{ n = "Campaigns";      d = "src\Services\Campaigns\TaxVision.Campaigns.Api";       p = 5530 },
  @{ n = "Gateway";        d = "src\Gateway\TaxVision.Gateway";                        p = 5047 }
)

$logs = Join-Path $root "logs"
if (-not (Test-Path $logs)) { New-Item -ItemType Directory $logs | Out-Null }

function Start-One($s) {
  $wd = Join-Path $root $s.d
  # F3 wiring + feedback_fleet_restart_from_release_binaries.md: la flota se arranca desde
  # bin\Release\net10.0 para que los endpoints nuevos (schedule, draft upsert, document, ...)
  # aparezcan sin requerir compilar Debug con el proceso vivo (MSB3027 lock).
  $binDir = Join-Path $wd "bin\Release\net10.0"
  $dll = Get-ChildItem -Path $binDir -Filter "TaxVision.*.dll" -ErrorAction SilentlyContinue |
         Where-Object { $_.BaseName -eq "TaxVision.$($s.n).Api" -or $_.BaseName -eq "TaxVision.$($s.n)" } |
         Select-Object -First 1
  if (-not $dll) { Write-Host "SKIP $($s.n): no dll in $binDir"; return }
  $env:ASPNETCORE_ENVIRONMENT = "Development"
  $env:ASPNETCORE_URLS = "http://localhost:$($s.p)"
  # Extra env per service (e.g. the listener switch); cleared after launch so it does not leak.
  if ($s.e) { foreach ($k in $s.e.Keys) { Set-Item -Path "env:$k" -Value $s.e[$k] } }
  Start-Process -FilePath "dotnet" -ArgumentList $dll.FullName -WorkingDirectory $wd `
    -RedirectStandardOutput (Join-Path $logs "$($s.n).log") `
    -RedirectStandardError (Join-Path $logs "$($s.n).err.log") `
    -WindowStyle Hidden
  if ($s.e) { foreach ($k in $s.e.Keys) { Remove-Item -Path "env:$k" -ErrorAction SilentlyContinue } }
  Write-Host "UP $($s.n) :$($s.p)"
}

function Wait-Port($port, $timeoutSec) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { return $true }
    Start-Sleep -Milliseconds 800
  }
  return $false
}

# 0) content-scanner (T-20) en Docker, en el mismo compose/proyecto que rabbitmq, redis, minio y clamav.
#    `up -d content-scanner` es idempotente y no toca a los demás; la primera vez construye la imagen
#    (descarga y verifica el modelo). Si no sube, CloudStorage falla cerrado: las imágenes quedan en revisión.
$infraCompose = Join-Path $root "deploy\docker\docker-compose.local-infra.yml"
$docker = Get-Command docker -ErrorAction SilentlyContinue
if (-not $docker) { Write-Host "SKIP content-scanner: docker not in PATH." }
else {
  & $docker.Source compose -f $infraCompose --env-file (Join-Path $root ".env") up -d content-scanner *> (Join-Path $logs "content-scanner.compose.log")
  if ($LASTEXITCODE -eq 0) { Write-Host "UP content-scanner (Docker) :5540" }
  else { Write-Host "content-scanner did not start (see logs\content-scanner.compose.log) - continuing" }
}

# 1) Auth alone first: let it apply/validate migrations and warm SQL with no competition.
Start-One $fleet[0]
if (Wait-Port 5124 45) { Write-Host "Auth listening :5124"; Start-Sleep -Seconds 5 }
else { Write-Host "Auth did not listen in 45s (check logs\Auth.err.log) - continuing anyway" }

# 2) The rest, staggered.
foreach ($s in $fleet[1..($fleet.Count-1)]) {
  Start-One $s
  Start-Sleep -Seconds $gap
}

# 3) Node (Communication + transcript worker + recorder), same as start-fleet.ps1.
$nodeFleet = @(
  @{ n = "Communication";                  d = "src\Services\Communication";                  p = 5350 },
  @{ n = "CommunicationTranscriptWorker";  d = "src\Services\CommunicationTranscriptWorker";  p = $null },
  # Grabacion server-side (F7). En local 5365: el 5360 del contenedor lo usa Subscription aqui.
  @{ n = "CommunicationRecorder";          d = "src\Services\CommunicationRecorder";          p = 5365 }
)
$npm = (Get-Command npm.cmd -ErrorAction SilentlyContinue)
if (-not $npm) { Write-Host "SKIP Node: npm.cmd not in PATH." }
else {
  foreach ($s in $nodeFleet) {
    $wd = Join-Path $root $s.d
    if (-not (Test-Path (Join-Path $wd "package.json"))) { Write-Host "SKIP $($s.n): no package.json"; continue }
    if (-not (Test-Path (Join-Path $wd "node_modules"))) {
      $installArgs = if (Test-Path (Join-Path $wd "package-lock.json")) { "ci" } else { "install" }
      & $npm.Source $installArgs --prefix $wd | Out-Null
    }
    if (Test-Path (Join-Path $wd "prisma\schema.prisma")) { & $npm.Source "run" "prisma:generate" --prefix $wd 2>$null | Out-Null }
    Start-Process -FilePath $npm.Source -ArgumentList "run", "dev" -WorkingDirectory $wd `
      -RedirectStandardOutput (Join-Path $logs "$($s.n).log") `
      -RedirectStandardError (Join-Path $logs "$($s.n).err.log") `
      -WindowStyle Hidden
    Write-Host "UP $($s.n)"
    Start-Sleep -Seconds $gap
  }
}
Write-Host "=== staggered start launched ==="
