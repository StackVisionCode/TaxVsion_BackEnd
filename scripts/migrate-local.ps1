<#
.SYNOPSIS
  Finds pending database migrations (EF Core and Prisma) on this developer machine and, with -Apply, applies them.

.DESCRIPTION
  Each service has its own database. The connection string is resolved the way ASP.NET does in Development:
  user-secrets of the service's Api, then appsettings.Development.json, then appsettings.json.
  Pending migrations are detected by comparing the [Migration] ids in the code with __EFMigrationsHistory
  (no build needed). Prisma (Communication) uses `prisma migrate status` / `migrate deploy` and its .env.

  Only local SQL Servers are touched unless -AllowRemote is passed. Passwords are never printed.

.EXAMPLE
  ./scripts/migrate-local.ps1                      # report only
  ./scripts/migrate-local.ps1 -Apply               # apply what is pending (asks first)
  ./scripts/migrate-local.ps1 -Apply -Yes -Service Calendar,Auth
  ./scripts/migrate-local.ps1 -SkipPrisma
#>
[CmdletBinding()]
param(
  [switch]$Apply,
  [switch]$Yes,
  [string[]]$Service,
  [switch]$SkipEf,
  [switch]$SkipPrisma,
  [switch]$AllowRemote,
  # Aplica aunque la base tenga migraciones que el código no conoce (otra rama).
  [switch]$Force,
  # Detecta con `dotnet ef migrations list` en vez de SQL directo (más lento: compila).
  [switch]$UseEfToDetect
)

$ErrorActionPreference = 'Stop'
# La raiz del repo es la carpeta padre de scripts/: sirve en cualquier maquina sin editar rutas.
$root = Split-Path -Parent $PSScriptRoot
$onWindows = ($env:OS -eq 'Windows_NT')
$failed = $false
# Con -File la lista llega como un solo texto ("Calendar,Auth").
if ($Service) { $Service = @($Service | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }

function Write-Note($text, $color = 'Gray') { Write-Host $text -ForegroundColor $color }

# Programas externos: en PowerShell 5.1 el stderr con 'Stop' corta el script; se captura como texto.
function Invoke-Native([scriptblock]$command) {
  $previous = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try { $lines = @(& $command 2>&1 | ForEach-Object { "$_" }) }
  finally { $ErrorActionPreference = $previous }
  return [pscustomobject]@{ Lines = $lines; ExitCode = $LASTEXITCODE }
}

# ---------- Lectura de configuracion ----------

# JSON con comentarios (appsettings los admite); se quitan las lineas // y los bloques /* */.
function Read-JsonFile($path) {
  if (-not (Test-Path -LiteralPath $path)) { return $null }
  $raw = [System.IO.File]::ReadAllText($path)
  $raw = [regex]::Replace($raw, '(?s)/\*.*?\*/', '')
  $raw = [regex]::Replace($raw, '(?m)^\s*//.*$', '')
  try { return $raw | ConvertFrom-Json } catch { Write-Note "  WARN cannot parse $path" Yellow; return $null }
}

# Acepta la forma plana de user-secrets ("ConnectionStrings:Default") y la anidada.
function Get-ConnectionValue($json, $name) {
  if (-not $json) { return $null }
  $flat = $json.PSObject.Properties["ConnectionStrings:$name"]
  if ($flat -and $flat.Value) { return [string]$flat.Value }
  $section = $json.PSObject.Properties['ConnectionStrings']
  if ($section -and $section.Value) {
    $entry = $section.Value.PSObject.Properties[$name]
    if ($entry -and $entry.Value) { return [string]$entry.Value }
  }
  return $null
}

function Get-SecretsPath($secretsId) {
  if ($onWindows) { return Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretsId\secrets.json" }
  return Join-Path $HOME ".microsoft/usersecrets/$secretsId/secrets.json"
}

# Mismo orden que ASP.NET en Development (sin variables de entorno: ver aviso al inicio).
function Resolve-Connection($apiProject) {
  $apiDir = Split-Path -Parent $apiProject
  $csproj = [System.IO.File]::ReadAllText($apiProject)
  $match = [regex]::Match($csproj, '<UserSecretsId>([^<]+)</UserSecretsId>')
  if ($match.Success) {
    $value = Get-ConnectionValue (Read-JsonFile (Get-SecretsPath $match.Groups[1].Value)) 'Default'
    if ($value) { return @{ Value = $value; Source = 'user-secrets' } }
  }
  foreach ($file in 'appsettings.Development.json', 'appsettings.json') {
    $value = Get-ConnectionValue (Read-JsonFile (Join-Path $apiDir $file)) 'Default'
    if ($value) { return @{ Value = $value; Source = $file } }
  }
  return $null
}

# ---------- Servidor local o no ----------

function Test-LocalHost($hostName) {
  $h = $hostName.Trim().ToLowerInvariant()
  if ($h.StartsWith('tcp:')) { $h = $h.Substring(4) }
  $h = ($h -split ',')[0]
  $h = ($h -split '\\')[0]
  $machine = [System.Environment]::MachineName.ToLowerInvariant()
  return @('localhost', '127.0.0.1', '::1', '.', '(local)', '(localdb)', 'host.docker.internal', $machine) -contains $h
}

function Get-SqlTarget($connectionString) {
  $builder = New-Object System.Data.Common.DbConnectionStringBuilder
  $builder.set_ConnectionString($connectionString)
  $server = $null; $database = $null
  foreach ($key in 'Server', 'Data Source', 'Address', 'Addr') { if ($builder.ContainsKey($key)) { $server = [string]$builder[$key]; break } }
  foreach ($key in 'Database', 'Initial Catalog') { if ($builder.ContainsKey($key)) { $database = [string]$builder[$key]; break } }
  return @{ Server = $server; Database = $database }
}

# ---------- Deteccion EF ----------

function Get-CodeMigrations($infraDir, $context) {
  $ids = New-Object System.Collections.Generic.List[string]
  $files = Get-ChildItem -LiteralPath $infraDir -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
  foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    if ($text -notmatch '\[Migration\("') { continue }
    # Busca por atributo, no por carpeta: hay migraciones fuera de la carpeta Migrations.
    if ($text -notmatch "\[DbContext\(typeof\(([\w\.]+\.)?$context\)\)\]") { continue }
    foreach ($m in [regex]::Matches($text, '\[Migration\("([^"]+)"\)\]')) { $ids.Add($m.Groups[1].Value) }
  }
  return @($ids | Sort-Object -Unique)
}

$sqlClientReady = $false
function Initialize-SqlClient {
  if ($script:sqlClientReady) { return $true }
  try { Add-Type -AssemblyName System.Data.SqlClient -ErrorAction Stop } catch { }
  $script:sqlClientReady = [bool]('System.Data.SqlClient.SqlConnection' -as [type])
  return $script:sqlClientReady
}

# Exists = false si la base no existe todavía. Se devuelve un objeto: PowerShell pierde los arreglos vacíos.
function Get-AppliedBySql($connectionString) {
  $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $connectionString
  # Con indice: PowerShell trata las propiedades de un diccionario como claves.
  $builder['Connect Timeout'] = 5
  $connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
  $applied = New-Object System.Collections.Generic.List[string]
  try {
    try { $connection.Open() }
    catch {
      # 4060: no se puede abrir la base (no existe todavía). La SqlException llega envuelta.
      $inner = $_.Exception
      while ($inner -and -not ($inner -is [System.Data.SqlClient.SqlException])) { $inner = $inner.InnerException }
      if ($inner -and $inner.Number -eq 4060) { return [pscustomobject]@{ Exists = $false; Ids = $applied } }
      throw
    }
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT OBJECT_ID(N'dbo.__EFMigrationsHistory')"
    if (-not ($command.ExecuteScalar() -is [System.DBNull])) {
      $command.CommandText = 'SELECT MigrationId FROM dbo.__EFMigrationsHistory'
      $reader = $command.ExecuteReader()
      while ($reader.Read()) { $applied.Add($reader.GetString(0)) }
      $reader.Close()
    }
    return [pscustomobject]@{ Exists = $true; Ids = $applied }
  }
  finally { $connection.Dispose() }
}

# La cadena va solo al proceso hijo: las factories de design-time leen ConnectionStrings__Default.
function Invoke-WithConnection($connectionString, [scriptblock]$action) {
  $previous = $env:ConnectionStrings__Default
  $env:ConnectionStrings__Default = $connectionString
  try { & $action }
  finally {
    if ($null -eq $previous) { Remove-Item Env:ConnectionStrings__Default -ErrorAction SilentlyContinue }
    else { $env:ConnectionStrings__Default = $previous }
  }
}

function Get-AppliedByEf($target) {
  $result = Invoke-WithConnection $target.Connection {
    Invoke-Native { dotnet ef migrations list --project $target.InfraProject --startup-project $target.InfraProject --context $target.Context --json --prefix-output }
  }
  if ($result.ExitCode -ne 0) { throw "dotnet ef migrations list failed: $(($result.Lines | Where-Object { $_ -like 'error:*' } | Select-Object -First 1))" }
  $json = ($result.Lines | Where-Object { $_ -like 'data:*' } | ForEach-Object { $_.Substring(5).Trim() }) -join "`n"
  $ids = New-Object System.Collections.Generic.List[string]
  # En 5.1 ConvertFrom-Json no enumera el arreglo: se guarda y se recorre.
  $parsed = $json | ConvertFrom-Json
  foreach ($m in $parsed) { if ($m.applied) { $ids.Add([string]$m.id) } }
  return [pscustomobject]@{ Exists = $true; Ids = $ids }
}

# ---------- Descubrimiento ----------

function Find-EfTargets {
  $targets = @()
  foreach ($serviceDir in Get-ChildItem -LiteralPath (Join-Path $root 'src\Services') -Directory) {
    # Los servicios Node no tienen DbContext y su node_modules hace lenta la busqueda.
    if (Test-Path -LiteralPath (Join-Path $serviceDir.FullName 'package.json')) { continue }
    if ($Service -and ($Service -notcontains $serviceDir.Name)) { continue }

    $snapshots = Get-ChildItem -LiteralPath $serviceDir.FullName -Recurse -Filter *ModelSnapshot.cs -File |
      Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    if (-not $snapshots) { continue }

    $apiProject = Get-ChildItem -LiteralPath $serviceDir.FullName -Recurse -Filter *.csproj -File |
      Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and (Select-String -LiteralPath $_.FullName -Pattern '<UserSecretsId>' -Quiet) } |
      Sort-Object @{ Expression = { if ($_.Name -like '*.Api.csproj') { 0 } else { 1 } } } |
      Select-Object -First 1

    foreach ($snapshot in $snapshots) {
      $context = [regex]::Match([System.IO.File]::ReadAllText($snapshot.FullName), '\[DbContext\(typeof\(([\w\.]+)\)\)\]').Groups[1].Value
      $context = ($context -split '\.')[-1]
      # El proyecto de Infrastructure es el .csproj mas cercano hacia arriba.
      $dir = $snapshot.Directory
      $infraProject = $null
      while ($dir -and -not $infraProject) {
        $infraProject = Get-ChildItem -LiteralPath $dir.FullName -Filter *.csproj -File | Select-Object -First 1
        $dir = $dir.Parent
      }
      $targets += [pscustomobject]@{
        Service      = $serviceDir.Name
        Context      = $context
        InfraProject = $infraProject.FullName
        ApiProject   = if ($apiProject) { $apiProject.FullName } else { $null }
      }
    }
  }
  return $targets
}

# ---------- Inicio ----------

Push-Location $root
try {
  Write-Note "TaxVision migrations — $(if ($Apply) { 'apply mode' } else { 'report only (use -Apply to apply)' })" Cyan
  if ($env:ConnectionStrings__Default) {
    Write-Note "WARN ConnectionStrings__Default is set in your environment. It is ignored here (it would send every service to the same database)." Yellow
  }

  $rows = @()
  $toApply = @()

  if (-not $SkipEf) {
    $useEf = [bool]$UseEfToDetect
    if (-not $useEf -and -not (Initialize-SqlClient)) {
      Write-Note 'WARN System.Data.SqlClient is not available in this PowerShell; detecting with dotnet ef (slower, builds each project).' Yellow
      $useEf = $true
    }
    if ($useEf -or $Apply) {
      $restore = Invoke-Native { dotnet tool restore }
      if ($restore.ExitCode -ne 0) { throw 'dotnet tool restore failed: dotnet-ef is needed (see dotnet-tools.json).' }
    }

    foreach ($t in Find-EfTargets) {
      $row = [ordered]@{ Service = $t.Service; Kind = "EF $($t.Context)"; Database = '?'; Source = '-'; Applied = '-'; Pending = '-'; Status = '' }
      $resolved = if ($t.ApiProject) { Resolve-Connection $t.ApiProject } else { $null }
      if (-not $resolved) {
        $row.Status = 'NO CONNECTION STRING (set user-secrets ConnectionStrings:Default on the Api)'
        $rows += [pscustomobject]$row; $failed = $true; continue
      }
      $sql = Get-SqlTarget $resolved.Value
      $row.Database = "$($sql.Server)/$($sql.Database)"
      $row.Source = $resolved.Source
      if (-not $AllowRemote -and -not (Test-LocalHost ([string]$sql.Server))) {
        $row.Status = 'SKIPPED remote server (use -AllowRemote)'
        $rows += [pscustomobject]$row; continue
      }

      $code = Get-CodeMigrations (Split-Path -Parent $t.InfraProject) $t.Context
      $t | Add-Member Connection $resolved.Value
      try {
        $state = if ($useEf) { Get-AppliedByEf $t } else { Get-AppliedBySql $resolved.Value }
      }
      catch {
        $row.Status = "ERROR $($_.Exception.Message.Split("`n")[0])"
        $rows += [pscustomobject]$row; $failed = $true; continue
      }

      $isNew = -not $state.Exists
      $applied = @($state.Ids)
      $pending = @($code | Where-Object { $applied -notcontains $_ })
      $unknown = @($applied | Where-Object { $code -notcontains $_ })
      $row.Applied = $applied.Count
      $row.Pending = $pending.Count
      if ($unknown.Count -gt 0) {
        $row.Status = "DRIFT: DB has $($unknown.Count) migration(s) the code doesn't (other branch?) e.g. $($unknown[-1])"
        if ($pending.Count -gt 0 -and $Force) { $toApply += $t }
      }
      elseif ($isNew) { $row.Status = 'NEW DATABASE (created on apply)'; $toApply += $t }
      elseif ($pending.Count -gt 0) { $row.Status = "PENDING: $($pending[-1])$(if ($pending.Count -gt 1) { " (+$($pending.Count - 1) more)" })"; $toApply += $t }
      else { $row.Status = 'up to date' }
      $rows += [pscustomobject]$row
    }
  }

  # ---------- Prisma (Communication) ----------
  $prisma = $null
  $communication = Join-Path $root 'src\Services\Communication'
  if (-not $SkipPrisma -and (Test-Path -LiteralPath (Join-Path $communication 'prisma\schema.prisma')) -and (-not $Service -or $Service -contains 'Communication')) {
    $row = [ordered]@{ Service = 'Communication'; Kind = 'Prisma'; Database = '?'; Source = '-'; Applied = '-'; Pending = '-'; Status = '' }
    $url = $env:COMMUNICATION_DB_CONNECTION
    $row.Source = 'environment'
    if (-not $url) {
      $envFile = Join-Path $communication '.env'
      if (Test-Path -LiteralPath $envFile) {
        $line = Get-Content -LiteralPath $envFile | Where-Object { $_ -match '^\s*COMMUNICATION_DB_CONNECTION\s*=' } | Select-Object -First 1
        if ($line) { $url = ($line -replace '^\s*COMMUNICATION_DB_CONNECTION\s*=\s*', '').Trim().Trim('"').Trim("'") }
      }
      $row.Source = '.env'
    }
    $hostMatch = [regex]::Match([string]$url, '^sqlserver://([^;:/]+)(:\d+)?')
    $dbMatch = [regex]::Match([string]$url, '(?i);\s*(database|initial catalog)\s*=\s*([^;]+)')
    if (-not $url) { $row.Status = 'NO COMMUNICATION_DB_CONNECTION (copy .env.example to .env)'; $failed = $true }
    elseif (-not (Test-Path -LiteralPath (Join-Path $communication 'node_modules\.bin'))) { $row.Status = 'node_modules missing: run npm ci in src/Services/Communication'; $failed = $true }
    else {
      $row.Database = "$($hostMatch.Groups[1].Value)$($hostMatch.Groups[2].Value)/$($dbMatch.Groups[2].Value)"
      if (-not $AllowRemote -and -not (Test-LocalHost $hostMatch.Groups[1].Value)) { $row.Status = 'SKIPPED remote server (use -AllowRemote)' }
      else {
        Push-Location $communication
        try { $status = Invoke-Native { npx --no-install prisma migrate status }; $output = $status.Lines; $exit = $status.ExitCode }
        finally { Pop-Location }
        $names = @($output | Where-Object { $_ -match '^\s*\d{14}_\S+\s*$' } | ForEach-Object { $_.Trim() })
        if ($exit -eq 0) { $row.Pending = 0; $row.Status = 'up to date' }
        elseif ($output -match 'P3009|failed') { $row.Status = 'FAILED migration in DB: fix it, then `npx prisma migrate resolve --rolled-back <name>`'; $failed = $true }
        elseif ($output -match 'not yet been applied') { $row.Pending = $names.Count; $row.Status = "PENDING: $($names[-1])"; $prisma = $communication }
        else { $row.Status = "ERROR $((@($output) | Select-Object -Last 1))"; $failed = $true }
      }
    }
    $rows += [pscustomobject]$row
  }

  if (-not $rows) { Write-Note "No service matched $(if ($Service) { "-Service $($Service -join ',')" } else { 'the search' })." Yellow; exit 1 }
  $rows | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host

  $count = $toApply.Count + $(if ($prisma) { 1 } else { 0 })
  if ($count -eq 0) { Write-Note 'Nothing to apply.' Green }
  elseif (-not $Apply) { Write-Note "$count database(s) with pending migrations. Run again with -Apply to apply them." Yellow }
  else {
    if (-not $Yes) {
      $answer = Read-Host "Apply pending migrations to $count database(s)? (y/N)"
      if ($answer -notmatch '^(y|yes|s|si)$') { Write-Note 'Cancelled.' Yellow; exit 0 }
    }
    foreach ($t in $toApply) {
      Write-Note "-> $($t.Service) ($($t.Context))" Cyan
      $update = Invoke-WithConnection $t.Connection {
        Invoke-Native { dotnet ef database update --project $t.InfraProject --startup-project $t.InfraProject --context $t.Context }
      }
      $update.Lines | Where-Object { $_ -match 'Applying migration|Done\.|error|No migrations were applied' } | ForEach-Object { Write-Note "   $_" }
      if ($update.ExitCode -ne 0) { Write-Note "   FAILED $($t.Service)" Red; $failed = $true } else { Write-Note "   OK $($t.Service)" Green }
    }
    if ($prisma) {
      Write-Note '-> Communication (Prisma)' Cyan
      Push-Location $prisma
      try { $deploy = Invoke-Native { npx --no-install prisma migrate deploy }; $exit = $deploy.ExitCode } finally { Pop-Location }
      $deploy.Lines | Where-Object { $_ -match 'Applying migration|applied|error|Error' } | ForEach-Object { Write-Note "   $_" }
      if ($exit -ne 0) { Write-Note '   FAILED Communication' Red; $failed = $true } else { Write-Note '   OK Communication' Green }
    }
    Write-Note 'Restart the services you migrated if the fleet is running.' Gray
  }
}
finally { Pop-Location }

if ($failed) { exit 1 }
exit 0
