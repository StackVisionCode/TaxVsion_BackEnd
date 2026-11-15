# F9/F10 en PRODUCCION: carga la pagina "demo" en https://api.taxoffix.io.
# Uso (lo corre el duenio, nunca un agente):
#   powershell -ExecutionPolicy Bypass -File scripts\public-demo\seed-public-demo.production.ps1 -HostsFile <ruta>
#   (agrega -Activate para publicarla; sin el, queda cargada pero apagada)
# - Los anfitriones son los reales (D-01): userId del PlatformAdmin, nombre, email de avisos y enlace de
#   Meet/Zoom/Teams. Ver hosts.example.json. El horario semanal solo se toca si el archivo lo trae.
# - Pide el access token de un PlatformAdmin de produccion; no se muestra ni se guarda.
param(
  [Parameter(Mandatory)] [string] $HostsFile,
  [string] $ApiBase = 'https://api.taxoffix.io',
  [switch] $Activate
)

Import-Module (Join-Path $PSScriptRoot 'PublicDemoSeed.psm1') -Force
if (-not (Test-Path $HostsFile)) { throw "Falta $HostsFile." }

# Un enlace de prueba en produccion le llegaria a un prospecto real.
$hosts = @(Read-HostsFile $HostsFile)
foreach ($h in $hosts) {
  if (-not $h.meetingUrl -or $h.meetingUrl -match 'example|test|xxx|aaa-bbbb-ccc') {
    throw "El anfitrion $($h.displayName) no tiene un enlace de reunion real."
  }
}

$answer = Read-Host "Vas a modificar la pagina de demos en $ApiBase. Escribe el host ('$(([Uri]$ApiBase).Host)') para seguir"
if ($answer.Trim() -ne ([Uri]$ApiBase).Host) { throw 'Cancelado.' }

Invoke-PublicDemoSeed -ApiBase $ApiBase -HostsFile $HostsFile -Activate:$Activate
