# Levanta la flota .NET completa en local. Cada servicio arranca desde su propia carpeta
# porque `dotnet <dll>` toma el cwd del shell como ContentRootPath.
$root = "C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd"
$fleet = @(
  @{ n = "Auth";           d = "src\Services\Auth\Api";                                p = 5124 },
  @{ n = "Tenant";         d = "src\Services\Tenant\TaxVision.Tenant.Api";             p = 5217 },
  @{ n = "Customer";       d = "src\Services\Customer\TaxVision.Customer.Api";         p = 5263 },
  @{ n = "Notification";   d = "src\Services\Notification\TaxVision.Notification.Api"; p = 5320 },
  @{ n = "CloudStorage";   d = "src\Services\CloudStorage\TaxVision.CloudStorage.Api"; p = 5330 },
  @{ n = "Signature";      d = "src\Services\Signature\TaxVision.Signature.Api";       p = 5340 },
  @{ n = "Subscription";   d = "src\Services\Subscription\TaxVision.Subscription.Api"; p = 5360 },
  @{ n = "Postmaster";     d = "src\Services\Postmaster\TaxVision.Postmaster.Api";     p = 5370 },
  @{ n = "Scribe";         d = "src\Services\Scribe\TaxVision.Scribe.Api";             p = 5380 },
  @{ n = "Connectors";     d = "src\Services\Connectors\TaxVision.Connectors.Api";     p = 5390 },
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
  @{ n = "Gateway";        d = "src\Gateway\TaxVision.Gateway";                        p = 5047 }
)

$logs = Join-Path $root "logs"
if (-not (Test-Path $logs)) { New-Item -ItemType Directory $logs | Out-Null }

foreach ($s in $fleet) {
  $wd = Join-Path $root $s.d
  $binDir = Join-Path $wd "bin\Debug\net10.0"
  $dll = Get-ChildItem -Path $binDir -Filter "TaxVision.*.dll" -ErrorAction SilentlyContinue |
         Where-Object { $_.BaseName -eq "TaxVision.$($s.n).Api" -or $_.BaseName -eq "TaxVision.$($s.n)" } |
         Select-Object -First 1
  if (-not $dll) { Write-Host "SKIP $($s.n): no se encontro el dll en $binDir"; continue }

  # Sin Development no se cargan appsettings.Development.json ni los user-secrets, y los
  # servicios mueren al arrancar por falta de RabbitMq:Uri.
  $env:ASPNETCORE_ENVIRONMENT = "Development"
  $env:ASPNETCORE_URLS = "http://localhost:$($s.p)"
  Start-Process -FilePath "dotnet" -ArgumentList $dll.FullName -WorkingDirectory $wd `
    -RedirectStandardOutput (Join-Path $logs "$($s.n).log") `
    -RedirectStandardError (Join-Path $logs "$($s.n).err.log") `
    -WindowStyle Hidden
  Write-Host "UP $($s.n) :$($s.p)"
}
