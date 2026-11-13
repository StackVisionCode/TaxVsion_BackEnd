# F9 en local: carga y ACTIVA la pagina "demo" contra el Gateway local.
# Uso (desde la raiz del repo, con la flota arriba):
#   powershell -ExecutionPolicy Bypass -File scripts\public-demo\seed-public-demo.local.ps1
# Pide el access token de un PlatformAdmin (inicia sesion en la app de plataforma y copialo).
# Los anfitriones salen de hosts.local.json (no se versiona); ver hosts.example.json.
param(
  [string] $ApiBase = 'http://localhost:5047',
  [string] $HostsFile = (Join-Path $PSScriptRoot 'hosts.local.json')
)

Import-Module (Join-Path $PSScriptRoot 'PublicDemoSeed.psm1') -Force
if (-not (Test-Path $HostsFile)) { throw "Falta $HostsFile (copia hosts.example.json y completalo)." }
Invoke-PublicDemoSeed -ApiBase $ApiBase -HostsFile $HostsFile -Activate
