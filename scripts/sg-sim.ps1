# Seat Slot Gating — simulador local (fase b: inserts directos SQL + hot reload Shadow→Enforce).
# No ejecuta pagos ni invitaciones reales; prepara un tenant Starter para ver el gate funcionando.
#
# Uso:
#   .\scripts\sg-sim.ps1 discover                      # Fase 1: lista tenants candidatos
#   .\scripts\sg-sim.ps1 seed      -TenantId <guid>    # Fase 2: siembra 3 employees + 1 seat Active
#   .\scripts\sg-sim.ps1 suspend   -TenantId <guid>    # Fase 3: suspende el seat
#   .\scripts\sg-sim.ps1 report    -TenantId <guid>    # Lee el projection para ver quién quedó gated
#   .\scripts\sg-sim.ps1 cleanup   -TenantId <guid>    # Deshace lo del seed

param(
  [Parameter(Position=0)][ValidateSet('discover','seed','grant-perms','suspend','add-suspended-seat','recompute','set-cap','report','cleanup')][string]$Command,
  [string]$TenantId,
  [int]$MaxUsers
)

$ErrorActionPreference = 'Stop'

$AuthSecretsId = 'c9f518b8-3374-4abd-a7b6-f0b20cb0877f'
$SubSecretsId  = 'e4b1c9d2-7f3a-4a86-9b2d-subscription1'

function Get-UserSecret($UserSecretsId) {
  $path = Join-Path $env:APPDATA "Microsoft\UserSecrets\$UserSecretsId\secrets.json"
  if (-not (Test-Path $path)) { throw "secrets.json no existe: $path" }
  return Get-Content $path -Raw | ConvertFrom-Json
}

function Get-ConnectionString($UserSecretsId) {
  $secrets = Get-UserSecret $UserSecretsId
  $flat = $secrets.PSObject.Properties | Where-Object { $_.Name -eq 'ConnectionStrings:Default' }
  if ($flat) { return $flat.Value }
  if ($secrets.ConnectionStrings -and $secrets.ConnectionStrings.Default) { return $secrets.ConnectionStrings.Default }
  throw "No encontré ConnectionStrings:Default en user-secrets $UserSecretsId"
}

Add-Type -AssemblyName System.Data

function Invoke-Sql($ConnStr, $Query, [hashtable]$Params = @{}) {
  $conn = New-Object System.Data.SqlClient.SqlConnection $ConnStr
  $conn.Open()
  try {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $Query
    $cmd.CommandTimeout = 30
    foreach ($k in $Params.Keys) { [void]$cmd.Parameters.AddWithValue("@$k", $Params[$k]) }
    $reader = $cmd.ExecuteReader()
    $rows = @()
    while ($reader.Read()) {
      $row = [ordered]@{}
      for ($i = 0; $i -lt $reader.FieldCount; $i++) {
        $row[$reader.GetName($i)] = if ($reader.IsDBNull($i)) { $null } else { $reader.GetValue($i) }
      }
      $rows += [pscustomobject]$row
    }
    return ,$rows
  } finally { $conn.Close() }
}

function Invoke-SqlNonQuery($ConnStr, $Query, [hashtable]$Params = @{}) {
  $conn = New-Object System.Data.SqlClient.SqlConnection $ConnStr
  $conn.Open()
  try {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $Query
    $cmd.CommandTimeout = 30
    foreach ($k in $Params.Keys) { [void]$cmd.Parameters.AddWithValue("@$k", $Params[$k]) }
    return $cmd.ExecuteNonQuery()
  } finally { $conn.Close() }
}

# --- FASE 1: Discovery ---------------------------------------------------------------------------

function Invoke-Discover {
  Write-Host "`n== Fase 1: Discovery ==" -ForegroundColor Cyan
  $sub  = Get-ConnectionString $SubSecretsId
  $auth = Get-ConnectionString $AuthSecretsId
  Write-Host "Subscription DB -> conectado" -ForegroundColor Green
  Write-Host "Auth DB         -> conectado" -ForegroundColor Green

  $qTenants = @'
SELECT TOP 20 ts.TenantId, ts.PlanCode, ts.Status, ts.CurrentPeriodEndUtc, ts.OnboardingId
FROM TenantSubscriptions ts
WHERE ts.PlanCode = 'starter' AND ts.Status IN ('Active','Trialing')
ORDER BY ts.UpdatedAtUtc DESC
'@
  $tenants = Invoke-Sql $sub $qTenants
  if (-not $tenants -or $tenants.Count -eq 0) {
    Write-Host "No hay tenants Starter Active/Trialing." -ForegroundColor Yellow
    return
  }

  Write-Host "`nTenants Starter candidatos:" -ForegroundColor Cyan
  $qUsers = "SELECT COUNT(*) AS N FROM Users WHERE TenantId=@tid AND IsActive=1 AND Status='Active'"
  $qSeats = "SELECT COUNT(*) AS N FROM SubscriptionSeats WHERE TenantId=@tid AND Status IN ('Active','PastDue')"
  foreach ($t in $tenants) {
    $uc = (Invoke-Sql $auth $qUsers @{ tid = $t.TenantId })[0].N
    $sc = (Invoke-Sql $sub  $qSeats @{ tid = $t.TenantId })[0].N
    '{0}  plan={1}  status={2}  users={3}  activeSeats={4}' -f $t.TenantId, $t.PlanCode, $t.Status, $uc, $sc | Write-Host
  }
  Write-Host "`nElegí uno y seguí con:"
  Write-Host "  .\scripts\sg-sim.ps1 seed -TenantId <guid>" -ForegroundColor Yellow
}

# --- FASE 2: Seed --------------------------------------------------------------------------------

$qFindOwner = @'
SELECT TOP 1 u.Id AS OwnerId, u.Email, u.PasswordHash
FROM Users u
WHERE u.TenantId=@tid AND u.ActorType='TenantAdmin' AND u.IsActive=1
ORDER BY u.CreatedAtUtc ASC
'@

$qExistingEmp = 'SELECT Id FROM Users WHERE Email=@e AND TenantId=@tid'

# Users real schema: Name/LastName (no FullName_*), IsActive+Status (no IsDeleted),
# EmailVerified (no EmailConfirmed), Roles JSON. No UpdatedAtUtc/CreatedBy/UpdatedBy.
$qInsertUser = @'
INSERT INTO Users
  (Id, TenantId, Name, LastName, Email, PasswordHash, ActorType,
   IsActive, Status, SlotPriorityAt,
   EmailVerified, PhoneVerified, MfaEnabled, FailedLoginCount, PermissionsVersion,
   CreatedAtUtc, Roles)
VALUES
  (@id, @tid, @first, @last, @email, @pwhash, 'TenantEmployee',
   1, 'Active', @slot,
   1, 0, 0, 0, 1,
   @now, @roles)
'@

$qInsertSeat = @'
INSERT INTO SubscriptionSeats
  (Id, TenantId, Type, Status, SourceType, SourceReferenceId,
   PurchasedAtUtc, CurrentPeriodStartUtc, CurrentPeriodEndUtc, NextRenewalAtUtc,
   AutoRenew, BillingCycle,
   UnitPriceAmount, UnitPriceCurrency,
   CreatedAtUtc, UpdatedAtUtc, CreatedBy, UpdatedBy)
VALUES
  (@id, @tid, 'Standard', 'Active', 'Manual', NULL,
   @now, @now, @pe, @pe,
   1, 'Monthly',
   10.0000, 'USD',
   @now, @now, @owner, @owner)
'@

function Invoke-Seed([Guid]$tid) {
  Write-Host "`n== Fase 2: Seed para tenant $tid ==" -ForegroundColor Cyan
  $auth = Get-ConnectionString $AuthSecretsId
  $sub  = Get-ConnectionString $SubSecretsId

  $owner = Invoke-Sql $auth $qFindOwner @{ tid = $tid }
  if (-not $owner) { throw "No encontré TenantAdmin para el tenant $tid" }
  $ownerId = $owner[0].OwnerId
  $ownerPwHash = $owner[0].PasswordHash  # Shared con los employees sim -> podés loguear con la misma pass
  Write-Host ("Owner: {0} ({1})" -f $owner[0].Email, $ownerId) -ForegroundColor Green

  $now = (Get-Date).ToUniversalTime()
  $employees = @(
    @{ Email='sg-sim-emp1@example.com'; First='SgSim'; Last='Employee 1'; Offset=-30 },
    @{ Email='sg-sim-emp2@example.com'; First='SgSim'; Last='Employee 2'; Offset=-20 },
    @{ Email='sg-sim-emp3@example.com'; First='SgSim'; Last='Employee 3'; Offset=-10 }
  )

  foreach ($emp in $employees) {
    $existing = Invoke-Sql $auth $qExistingEmp @{ e=$emp.Email; tid=$tid }
    if ($existing -and $existing.Count -gt 0) {
      Write-Host ('[skip] ya existe: {0}' -f $emp.Email) -ForegroundColor DarkGray
      continue
    }
    $empId = [guid]::NewGuid()
    $slot  = $now.AddDays($emp.Offset)
    # PasswordHash NOT NULL — hash dummy (no permite login real). Roles: JSON del rol inicial.
    $pwhash = 'DISABLED$sgsim$cannot-login'
    $roles = '["TenantEmployee"]'
    try {
      Invoke-SqlNonQuery $auth $qInsertUser @{
        id=$empId; tid=$tid; email=$emp.Email; first=$emp.First; last=$emp.Last;
        slot=$slot; now=$now; pwhash=$pwhash; roles=$roles
      } | Out-Null
      Write-Host ('[+] {0}  slotPriorityAt={1:O}' -f $emp.Email, $slot) -ForegroundColor Green
    } catch {
      Write-Host ('[!] Falló insert de {0}' -f $emp.Email) -ForegroundColor Red
      Write-Host $_.Exception.Message -ForegroundColor DarkYellow
      Write-Host 'La tabla Users tiene columnas obligatorias que no mapeé. Mandame el schema real:' -ForegroundColor DarkYellow
      Write-Host "  Invoke-Sqlcmd -ConnectionString `"<auth-cs>`" -Query `"SELECT TOP 1 * FROM Users WHERE TenantId='$tid'`"" -ForegroundColor DarkYellow
      throw
    }
  }

  $seatId = [guid]::NewGuid()
  $pe = $now.AddDays(30)
  Invoke-SqlNonQuery $sub $qInsertSeat @{
    id=$seatId; tid=$tid; now=$now; pe=$pe; owner=$ownerId
  } | Out-Null
  Write-Host "[+] Seat Active insertado: $seatId" -ForegroundColor Green

  Write-Host "`nAhora dispará recalculate-entitlements (sube el cap efectivo a +1 seat):" -ForegroundColor Yellow
  Write-Host "  curl -X POST http://localhost:5360/subscription/admin/entitlements/$tid/recalculate -H `"Authorization: Bearer <platform-admin-jwt>`"" -ForegroundColor Yellow
  Write-Host "`nO corré:  .\scripts\sg-sim.ps1 report -TenantId $tid" -ForegroundColor Yellow
}

# --- Grant perms: asigna rol system "Employee" a los sim users --------------------------------
#
# Sin esto los users creados por SQL no tienen entradas en UserRoles → ningún [HasPermission]
# los deja pasar → 403 por permisos (no por el seat gate). Esto les asigna el rol system
# "Employee" (el default que da un alta vía invitación real) e incrementa PermissionsVersion
# para que el fast-path refresque.

$qFindEmployeeRole = @'
SELECT TOP 1 Id FROM Roles WHERE TenantId=@tid AND Name='Employee' AND IsSystem=1
'@

$qInsertUserRole = @'
INSERT INTO UserRoles (UserId, RoleId, AssignedAtUtc)
SELECT @uid, @rid, @now
WHERE NOT EXISTS (SELECT 1 FROM UserRoles WHERE UserId=@uid AND RoleId=@rid)
'@

$qBumpPermsVersion = @'
UPDATE Users SET PermissionsVersion = PermissionsVersion + 1
WHERE TenantId=@tid AND Email LIKE 'sg-sim-emp%@example.com'
'@

$qFindSimUsers = @'
SELECT Id, Email FROM Users
WHERE TenantId=@tid AND Email LIKE 'sg-sim-emp%@example.com'
'@

function Invoke-GrantPerms([Guid]$tid) {
  Write-Host "`n== Grant perms: asignar rol Employee a los sim ==" -ForegroundColor Cyan
  $auth = Get-ConnectionString $AuthSecretsId

  $role = Invoke-Sql $auth $qFindEmployeeRole @{ tid=$tid }
  if (-not $role) { throw "No encontré rol 'Employee' system para el tenant" }
  $rid = $role[0].Id
  Write-Host ("Role Employee: {0}" -f $rid) -ForegroundColor Green

  $users = Invoke-Sql $auth $qFindSimUsers @{ tid=$tid }
  $now = (Get-Date).ToUniversalTime()
  foreach ($u in $users) {
    $n = Invoke-SqlNonQuery $auth $qInsertUserRole @{ uid=$u.Id; rid=$rid; now=$now }
    $mark = if ($n -gt 0) { '+' } else { '=' }
    Write-Host ("  [{0}] {1}" -f $mark, $u.Email) -ForegroundColor Green
  }

  $bumped = Invoke-SqlNonQuery $auth $qBumpPermsVersion @{ tid=$tid }
  Write-Host "PermissionsVersion bumped para $bumped users." -ForegroundColor Yellow
  Write-Host "Los emp sim deberían poder loguear y navegar; el gate SG decide por slot." -ForegroundColor Yellow
}

# --- FASE 3: Suspend el seat ---------------------------------------------------------------------

$qFindManualSeat = @'
SELECT TOP 1 Id FROM SubscriptionSeats
WHERE TenantId=@tid AND SourceType='Manual' AND Status='Active'
ORDER BY CreatedAtUtc DESC
'@

$qSuspendSeat = @'
UPDATE SubscriptionSeats
   SET Status='Suspended', SuspendedAtUtc=@now, SuspensionReason='SgSim: manual',
       UpdatedAtUtc=@now
 WHERE Id=@sid
'@

function Invoke-Suspend([Guid]$tid) {
  Write-Host "`n== Fase 3: Suspender seat sim ==" -ForegroundColor Cyan
  $sub = Get-ConnectionString $SubSecretsId
  $now = (Get-Date).ToUniversalTime()

  $seats = Invoke-Sql $sub $qFindManualSeat @{ tid=$tid }
  if (-not $seats -or $seats.Count -eq 0) {
    Write-Host "No encontré seat Active con SourceType=Manual para $tid." -ForegroundColor Yellow
    return
  }
  $sid = $seats[0].Id
  Invoke-SqlNonQuery $sub $qSuspendSeat @{ sid=$sid; now=$now } | Out-Null
  Write-Host "[~] Seat $sid -> Suspended" -ForegroundColor Green

  Write-Host "`nDispará recalculate para que baje el cap y re-pinte el projection:" -ForegroundColor Yellow
  Write-Host "  curl -X POST http://localhost:5360/subscription/admin/entitlements/$tid/recalculate -H `"Authorization: Bearer <platform-admin-jwt>`"" -ForegroundColor Yellow
}

# --- Recompute del projection (emula TenantSeatSlotCalculator por SQL) --------------------------
#
# Reemplaza el llamado al endpoint `/admin/subscription/tenants/{tid}/recalculate-entitlements` que
# requeriría JWT PlatformAdmin. El projection es derivado: lo re-escribimos aquí con la misma
# lógica que TenantSeatSlotCalculator (owner primero, luego SlotPriorityAt asc, luego UserId).

$qUsersForCalc = @'
SELECT u.Id, u.ActorType, u.SlotPriorityAt
FROM Users u
WHERE u.TenantId=@tid AND u.IsActive=1
  AND u.ActorType IN ('TenantAdmin','TenantEmployee')
'@

$qActiveSeatsCount = @'
SELECT COUNT(*) AS N
FROM SubscriptionSeats
WHERE TenantId=@tid AND Status IN ('Active','PastDue')
'@

$qMaxRevision = 'SELECT ISNULL(MAX(RevisionNumber), 0) AS N FROM TenantSeatSlotProjections WHERE TenantId=@tid'

$qDeleteProjection = 'DELETE FROM TenantSeatSlotProjections WHERE TenantId=@tid'

$qInsertProjection = @'
INSERT INTO TenantSeatSlotProjections
  (TenantId, UserId, SlotIndex, EffectiveCap, IsGated, ComputedAtUtc, RevisionNumber)
VALUES (@tid, @uid, @idx, @cap, @gated, @now, @rev)
'@

function Invoke-Recompute([Guid]$tid) {
  Write-Host "`n== Recompute del projection (SQL emulando TenantSeatSlotCalculator) ==" -ForegroundColor Cyan
  $auth = Get-ConnectionString $AuthSecretsId
  $sub  = Get-ConnectionString $SubSecretsId

  $users = Invoke-Sql $auth $qUsersForCalc @{ tid=$tid }
  if (-not $users -or $users.Count -eq 0) { throw "El tenant no tiene users Active" }

  $seatCount = (Invoke-Sql $sub $qActiveSeatsCount @{ tid=$tid })[0].N
  # Cap efectivo: 1 slot base del owner (siempre entra) + 1 slot por cada seat Active/PastDue.
  # Starter base = 1 slot (owner). El resto son add-ons de seats.
  $effectiveCap = 1 + [int]$seatCount
  Write-Host ("ActiveSeats={0}  EffectiveCap={1}" -f $seatCount, $effectiveCap) -ForegroundColor Green

  # Orden: IsOwner desc, SlotPriorityAt asc (DateTime.MaxValue si null), UserId tie-break
  $ordered = $users | Sort-Object -Property `
    @{ Expression={ if ($_.ActorType -eq 'TenantAdmin') { 0 } else { 1 } }; Ascending=$true },
    @{ Expression={ if ($_.SlotPriorityAt -is [DateTime]) { $_.SlotPriorityAt } else { [DateTime]::MaxValue } }; Ascending=$true },
    @{ Expression='Id'; Ascending=$true }

  $maxRev = (Invoke-Sql $auth $qMaxRevision @{ tid=$tid })[0].N
  $rev = [int64]$maxRev + 1
  $now = (Get-Date).ToUniversalTime()

  Invoke-SqlNonQuery $auth $qDeleteProjection @{ tid=$tid } | Out-Null

  $i = 1
  foreach ($u in $ordered) {
    $isOwner = $u.ActorType -eq 'TenantAdmin'
    $gated = (-not $isOwner) -and ($i -gt $effectiveCap)
    Invoke-SqlNonQuery $auth $qInsertProjection @{
      tid=$tid; uid=$u.Id; idx=$i; cap=$effectiveCap; gated=[int]$gated; now=$now; rev=$rev
    } | Out-Null
    $mark = if ($gated) { 'GATED' } else { 'OK' }
    Write-Host ('  slot {0}  {1}  {2}' -f $i, $u.Id, $mark) -ForegroundColor Green
    $i++
  }
  Write-Host "Projection repintado con revisión $rev." -ForegroundColor Yellow
}

# --- Add suspended seat: inserta un seat Standard ya Suspended --------------------------------
#
# Útil para apilar "Pay now" en la UI del owner cuando queremos simular que varios seats
# comprados cayeron por renovación fallida. Cada seat Suspended da un Pay now distinto.

$qFindOwnerId = @'
SELECT TOP 1 Id FROM Users WHERE TenantId=@tid AND ActorType='TenantAdmin' AND IsActive=1
ORDER BY CreatedAtUtc ASC
'@

$qInsertSuspendedSeat = @'
INSERT INTO SubscriptionSeats
  (Id, TenantId, Type, Status, SourceType, SourceReferenceId,
   PurchasedAtUtc, CurrentPeriodStartUtc, CurrentPeriodEndUtc, NextRenewalAtUtc,
   AutoRenew, BillingCycle,
   UnitPriceAmount, UnitPriceCurrency,
   SuspendedAtUtc, SuspensionReason,
   CreatedAtUtc, UpdatedAtUtc, CreatedBy, UpdatedBy)
VALUES
  (@id, @tid, 'Standard', 'Suspended', 'Manual', NULL,
   @past, @past, @pe, @pe,
   1, 'Monthly',
   10.0000, 'USD',
   @now, 'SgSim: additional suspended seat',
   @past, @now, @owner, @owner)
'@

function Invoke-AddSuspendedSeat([Guid]$tid) {
  Write-Host "`n== Add suspended seat ==" -ForegroundColor Cyan
  $auth = Get-ConnectionString $AuthSecretsId
  $sub  = Get-ConnectionString $SubSecretsId

  $owner = Invoke-Sql $auth $qFindOwnerId @{ tid=$tid }
  if (-not $owner) { throw "No encontré TenantAdmin para el tenant $tid" }

  $now = (Get-Date).ToUniversalTime()
  $past = $now.AddDays(-45)
  $pe = $now.AddDays(30)
  $seatId = [guid]::NewGuid()
  Invoke-SqlNonQuery $sub $qInsertSuspendedSeat @{
    id=$seatId; tid=$tid; past=$past; pe=$pe; now=$now; owner=$owner[0].Id
  } | Out-Null
  Write-Host "[+] Suspended seat insertado: $seatId" -ForegroundColor Green
  Write-Host "Refrescá /account/seats en el landing — ahora debería aparecer otro Pay now." -ForegroundColor Yellow
}

# --- Set cap (TenantPlanLimits.MaxUsers): la fuente del effectiveCap del gate --------------------
#
# El GetTenantSeatSlotsHandler lee `TenantPlanLimits.MaxUsers` del tenant como effectiveCap.
# Bajarlo acá fuerza al calculator a marcar más users como gated. Al próximo request el
# consumer real recomputa y el projection se alinea; el cambio queda hasta que algo que
# toque el entitlement snapshot sobreescriba el límite (recalculate-entitlements vuelve a
# llenarlo según el plan + seats).

$qSetCap = @'
UPDATE TenantPlanLimits SET MaxUsers=@max WHERE Id=@tid
'@

$qGetCap = 'SELECT MaxUsers FROM TenantPlanLimits WHERE Id=@tid'

function Invoke-SetCap([Guid]$tid, [int]$max) {
  Write-Host "`n== Set cap (TenantPlanLimits.MaxUsers = $max) ==" -ForegroundColor Cyan
  $auth = Get-ConnectionString $AuthSecretsId
  $n = Invoke-SqlNonQuery $auth $qSetCap @{ tid=$tid; max=$max }
  if ($n -eq 0) { Write-Host "No existe TenantPlanLimits para ese tenant" -ForegroundColor Red; return }
  $after = (Invoke-Sql $auth $qGetCap @{ tid=$tid })[0].MaxUsers
  Write-Host "MaxUsers del tenant -> $after" -ForegroundColor Green
  Write-Host "Al próximo request el projection se recomputa; corré 'recompute' para forzarlo ya." -ForegroundColor Yellow
}

# --- Report --------------------------------------------------------------------------------------

$qReportSeats = @'
SELECT Id, Status, SourceType, CurrentPeriodEndUtc, SuspendedAtUtc
FROM SubscriptionSeats WHERE TenantId=@tid
ORDER BY CreatedAtUtc
'@

$qReportUsers = @'
SELECT Email, ActorType, SlotPriorityAt, Status, IsActive
FROM Users WHERE TenantId=@tid AND IsActive=1
ORDER BY ActorType DESC, SlotPriorityAt
'@

$qReportProjection = @'
SELECT p.UserId, u.Email, u.ActorType, p.SlotIndex, p.EffectiveCap, p.IsGated, p.ComputedAtUtc
FROM TenantSeatSlotProjections p
JOIN Users u ON u.Id=p.UserId
WHERE p.TenantId=@tid
ORDER BY p.SlotIndex
'@

function Invoke-Report([Guid]$tid) {
  Write-Host "`n== Report del projection ==" -ForegroundColor Cyan
  $auth = Get-ConnectionString $AuthSecretsId
  $sub  = Get-ConnectionString $SubSecretsId

  Write-Host "`nSeats del tenant:" -ForegroundColor Cyan
  Invoke-Sql $sub  $qReportSeats     @{ tid=$tid } | Format-Table
  Write-Host "Users del tenant:" -ForegroundColor Cyan
  Invoke-Sql $auth $qReportUsers     @{ tid=$tid } | Format-Table
  Write-Host "TenantSeatSlotProjection (el gate):" -ForegroundColor Cyan
  Invoke-Sql $auth $qReportProjection @{ tid=$tid } | Format-Table
}

# --- Cleanup -------------------------------------------------------------------------------------

function Invoke-Cleanup([Guid]$tid) {
  Write-Host "`n== Cleanup ==" -ForegroundColor Cyan
  $auth = Get-ConnectionString $AuthSecretsId
  $sub  = Get-ConnectionString $SubSecretsId

  $n1 = Invoke-SqlNonQuery $auth "DELETE FROM Users WHERE TenantId=@tid AND Email LIKE 'sg-sim-emp%@example.com'" @{ tid=$tid }
  $n2 = Invoke-SqlNonQuery $sub  "DELETE FROM SubscriptionSeats WHERE TenantId=@tid AND SourceType='Manual' AND (SuspensionReason LIKE 'SgSim:%' OR SuspensionReason IS NULL)" @{ tid=$tid }
  Write-Host "[x] Users borrados: $n1" -ForegroundColor Yellow
  Write-Host "[x] Seats borrados: $n2" -ForegroundColor Yellow
}

# --- Dispatcher ----------------------------------------------------------------------------------

if (-not $Command) {
  Write-Host "Comandos: discover | seed -TenantId | suspend -TenantId | report -TenantId | cleanup -TenantId"
  exit 0
}

switch ($Command) {
  'discover'  { Invoke-Discover }
  'seed'        { if (-not $TenantId) { throw "-TenantId requerido" } Invoke-Seed       ([Guid]$TenantId) }
  'grant-perms' { if (-not $TenantId) { throw "-TenantId requerido" } Invoke-GrantPerms ([Guid]$TenantId) }
  'suspend'     { if (-not $TenantId) { throw "-TenantId requerido" } Invoke-Suspend    ([Guid]$TenantId) }
  'add-suspended-seat' { if (-not $TenantId) { throw "-TenantId requerido" } Invoke-AddSuspendedSeat ([Guid]$TenantId) }
  'recompute'   { if (-not $TenantId) { throw "-TenantId requerido" } Invoke-Recompute ([Guid]$TenantId) }
  'set-cap'     { if (-not $TenantId) { throw "-TenantId requerido" } if (-not $PSBoundParameters.ContainsKey('MaxUsers')) { throw "-MaxUsers requerido (p.ej. -MaxUsers 2)" } Invoke-SetCap ([Guid]$TenantId) $MaxUsers }
  'report'    { if (-not $TenantId) { throw "-TenantId requerido" } Invoke-Report    ([Guid]$TenantId) }
  'cleanup'   { if (-not $TenantId) { throw "-TenantId requerido" } Invoke-Cleanup   ([Guid]$TenantId) }
}
