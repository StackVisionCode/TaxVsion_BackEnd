# Regenera el codigo pregenerado de Wolverine (Internal/Generated de cada Api).
# Produccion corre en modo Static: un handler nuevo o cambiado sin regenerar falla en el gate del CI
# (PreGeneratedHandlerCodeTests). La prueba de frescura, con TAXVISION_CODEGEN_WRITE=1, reescribe la carpeta.
#
# Uso:
#   powershell -ExecutionPolicy Bypass -File scripts\codegen\regenerate-wolverine-code.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\codegen\regenerate-wolverine-code.ps1 -Service Tasks,Auth
param([string[]]$Service)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$all = @(
  "Auth", "Billing", "Calendar", "Campaigns", "Catalog", "CloudStorage", "Connectors", "Correspondence",
  "Customer", "Documents", "FinancialInsights", "Growth", "Inventory", "Notes", "Notification", "PaymentApp", "PaymentClient",
  "Postmaster", "Reminder", "Scribe", "Signature", "Sms", "Subscription", "Tasks", "Tenant", "Wallet"
)
# Con -File la lista llega como un solo texto "Tasks,Auth".
$targets = if ($Service) { $Service | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ } } else { $all }

$failed = @()
$env:TAXVISION_CODEGEN_WRITE = "1"
# En Static Wolverine solo descubre los handlers del GeneratedHandlerRegistry existente: un handler nuevo
# quedaria fuera sin aviso. Se regenera en Dynamic para que entren todos.
$env:Wolverine__CodeGeneration__TypeLoadMode = "Dynamic"
try {
  foreach ($s in $targets) {
    $project = Join-Path $root "deploy\tests\TaxVision.$s.Tests\TaxVision.$s.Tests.csproj"
    Write-Host "== $s"
    & dotnet test $project --nologo --filter "FullyQualifiedName~PreGeneratedHandlerCodeTests.Pre_generated_code_matches_the_current_handlers" | Out-Host
    if ($LASTEXITCODE -ne 0) { $failed += $s }
  }
}
finally {
  Remove-Item env:TAXVISION_CODEGEN_WRITE -ErrorAction SilentlyContinue
  Remove-Item env:Wolverine__CodeGeneration__TypeLoadMode -ErrorAction SilentlyContinue
}

if ($failed.Count -gt 0) {
  Write-Host "FAILED: $($failed -join ', ')"
  exit 1
}
Write-Host "Regenerated: $($targets -join ', ')"
