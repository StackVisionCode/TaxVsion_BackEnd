# Stop de la flota local que levanta start-fleet.ps1 (.NET `dotnet <dll>` + Node `npm run dev`).
# Cubre los 3 caminos: por puerto conocido, y por línea de comando bajo el repo (para cazar el
# CommunicationTranscriptWorker, que no expone puerto). Mata el árbol de procesos (taskkill /T) por
# si algún Node corre bajo nodemon y respawnea al hijo.
$root = "C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd"

# Mismos puertos que start-fleet.ps1 (25 .NET + Gateway 5047 + Communication 5350).
$ports = @(
  5124, 5217, 5263, 5320, 5330, 5340, 5360, 5370, 5380, 5390, 5400, 5410, 5420, 5430,
  5440, 5450, 5460, 5470, 5480, 5490, 5500, 5510, 5520, 5530, 5047, 5350
)

$pids = [System.Collections.Generic.HashSet[int]]::new()

# 1) PIDs escuchando en los puertos de la flota.
foreach ($p in $ports) {
  Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { [void]$pids.Add([int]$_.OwningProcess) }
}

# 2) Procesos por línea de comando. Acotado para NO tocar un `dotnet build/test` en curso:
#    - .NET: los lanzados como `dotnet ...\bin\Debug\net10.0\TaxVision.*.dll`
#    - Node: cualquier proceso bajo src\Services\Communication (cubre Communication y el TranscriptWorker)
$procs = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
  Where-Object {
    $_.CommandLine -and (
      $_.CommandLine -like "*bin\Debug\net10.0\TaxVision.*.dll*" -or
      $_.CommandLine -like "*\src\Services\Communication*"
    ) -and $_.CommandLine -like "*$root*"
  }
foreach ($proc in $procs) { [void]$pids.Add([int]$proc.ProcessId) }

if ($pids.Count -eq 0) {
  Write-Host "La flota ya está detenida (nada escuchando ni corriendo bajo el repo)."
  return
}

# 3) Matar cada PID con su árbol de hijos.
foreach ($procId in $pids) {
  $name = (Get-Process -Id $procId -ErrorAction SilentlyContinue).ProcessName
  taskkill /PID $procId /T /F 2>$null | Out-Null
  if ($LASTEXITCODE -eq 0) { Write-Host "DOWN pid $procId ($name)" }
}

Start-Sleep -Seconds 2

# 4) Verificación: reportar puertos que sigan escuchando.
$still = @()
foreach ($p in $ports) {
  if (Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue) { $still += $p }
}
if ($still.Count -gt 0) {
  Write-Host "OJO: siguen escuchando estos puertos: $($still -join ', ')"
} else {
  Write-Host "=== flota detenida (todos los puertos libres) ==="
}
