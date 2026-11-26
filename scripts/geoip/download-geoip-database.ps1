<#
.SYNOPSIS
  Descarga la base DB-IP "IP to City Lite" (MMDB) que Auth usa para la ubicación aproximada de las sesiones.

.DESCRIPTION
  Prueba el mes actual y, si aún no está publicado, el anterior. Descarga a un temporal, descomprime y
  renombra al final: nunca deja un archivo a medias. El archivo NO va en git (pesa más de 100 MB).
  Licencia CC BY 4.0: las pantallas que muestran el dato llevan "IP Geolocation by DB-IP" (https://db-ip.com).

.EXAMPLE
  ./scripts/geoip/download-geoip-database.ps1
  ./scripts/geoip/download-geoip-database.ps1 -Destination D:\geoip
#>
[CmdletBinding()]
param(
  [string]$Destination = (Join-Path $env:LOCALAPPDATA 'TaxVision\geoip')
)

$ErrorActionPreference = 'Stop'
$fileName = 'dbip-city-lite.mmdb'
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

$now = Get-Date
$months = @($now.ToString('yyyy-MM'), $now.AddMonths(-1).ToString('yyyy-MM'))
$gz = Join-Path $Destination "download-$PID.mmdb.gz"
$tmp = Join-Path $Destination "download-$PID.mmdb"
$downloaded = $null

try {
  foreach ($month in $months) {
    $url = "https://download.db-ip.com/free/dbip-city-lite-$month.mmdb.gz"
    try {
      Invoke-WebRequest -Uri $url -OutFile $gz -UseBasicParsing -TimeoutSec 600
      $downloaded = $month
      break
    } catch {
      # El mes actual puede no estar publicado todavía: se prueba el anterior.
      Remove-Item $gz -Force -ErrorAction SilentlyContinue
    }
  }
  if (-not $downloaded) { throw "DB-IP City Lite is not available for $($months -join ' or ')." }

  $in = [System.IO.File]::OpenRead($gz)
  try {
    $unzip = New-Object System.IO.Compression.GZipStream($in, [System.IO.Compression.CompressionMode]::Decompress)
    $out = [System.IO.File]::Create($tmp)
    try { $unzip.CopyTo($out) } finally { $out.Dispose(); $unzip.Dispose() }
  } finally { $in.Dispose() }

  Move-Item -Force $tmp (Join-Path $Destination $fileName)
  $size = [math]::Round((Get-Item (Join-Path $Destination $fileName)).Length / 1MB, 1)
  "DB-IP City Lite $downloaded - $size MB"
} finally {
  Remove-Item $gz, $tmp -Force -ErrorAction SilentlyContinue
}
