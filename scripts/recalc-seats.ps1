# Obtiene un token PlatformAdmin y dispara POST /admin/subscription/recalculate-entitlements/all.
# La password se pide en runtime (no se guarda en el archivo).
$base = 'http://localhost:5047'
$email = 'jturbi@syschar.com'
$platformTenant = '8f58a521-4c25-4d91-9f4e-7ad5df14c001'

$secure = Read-Host "Password de $email" -AsSecureString
$pass = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
  [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

function Post([string]$uri, $obj) {
  try {
    return Invoke-RestMethod -Uri $uri -Method Post -ContentType 'application/json' -Body ($obj | ConvertTo-Json)
  } catch {
    $r = $_.Exception.Response
    if ($r) {
      $sr = New-Object System.IO.StreamReader($r.GetResponseStream())
      Write-Host ("  ERR {0}: {1}" -f [int]$r.StatusCode, $sr.ReadToEnd()) -ForegroundColor DarkYellow
    } else {
      Write-Host ("  ERR: {0}" -f $_.Exception.Message) -ForegroundColor DarkYellow
    }
    return $null
  }
}

function TryLogin($tenantId) {
  $label = if ($tenantId) { $tenantId } else { 'null' }
  Write-Host ("Login tenantId=$label ...") -ForegroundColor Cyan
  $body = @{ email = $email; password = $pass; tenantId = $tenantId }
  return Post "$base/auth/login" $body
}

function IsUsable($resp) {
  return $resp -and ($resp.tokens -or $resp.takeoverRequired -or $resp.mfaRequired)
}

# 1) Login con el tenant de plataforma (tenantId es obligatorio).
$login = TryLogin $platformTenant
if (-not (IsUsable $login)) {
  Write-Host "El login no autentico (mira el error de arriba). Revisa email/password/tenant." -ForegroundColor Red
  return
}
$login | ConvertTo-Json -Depth 6

# 2) Resolver el access token (tokens directos / MFA / takeover).
$token = $null
if ($login.tokens) {
  $token = $login.tokens.accessToken
} elseif ($login.mfaRequired) {
  $code = Read-Host 'Codigo MFA (6 digitos)'
  $mfa = Post "$base/auth/mfa/verify" @{ loginTicket = $login.loginTicket; code = $code }
  if ($mfa.tokens) {
    $token = $mfa.tokens.accessToken
  } elseif ($mfa.takeoverRequired) {
    $to = Post "$base/auth/session/takeover" @{ ticket = $mfa.takeoverTicket }
    if ($to) { $token = $to.tokens.accessToken }
  }
} elseif ($login.takeoverRequired) {
  $to = Post "$base/auth/session/takeover" @{ ticket = $login.takeoverTicket }
  if ($to) { $token = $to.tokens.accessToken }
}

if (-not $token) {
  Write-Host "No se obtuvo access token (mira el error de arriba)." -ForegroundColor Red
  return
}
Write-Host ("Token OK: {0}..." -f $token.Substring(0, 20)) -ForegroundColor Green

# 3) Recalc-all (PlatformAdmin) — con el Bearer token.
try {
  $res = Invoke-RestMethod -Uri "$base/admin/subscription/recalculate-entitlements/all" `
    -Method Post -Headers @{ Authorization = "Bearer $token" }
  Write-Host ("OK - Recalc encolado para {0} plan(es)." -f $res) -ForegroundColor Green
} catch {
  $r = $_.Exception.Response
  if ($r) {
    $sr = New-Object System.IO.StreamReader($r.GetResponseStream())
    Write-Host ("Recalc ERR {0}: {1}" -f [int]$r.StatusCode, $sr.ReadToEnd()) -ForegroundColor Red
  } else {
    Write-Host ("Recalc ERR: {0}" -f $_.Exception.Message) -ForegroundColor Red
  }
}
