# Carga la pagina publica "Book a live demo" por la API de plataforma de Calendar (F9 del MASTER).
# Idempotente: crea lo que falta y actualiza lo que existe. El token de PlatformAdmin se pide en
# pantalla, no se muestra ni se guarda. Solo ASCII en este archivo (PowerShell 5.1); los textos con
# acentos viven en public-demo.content.json.

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

function Read-PlatformToken {
  $secure = Read-Host -Prompt 'PlatformAdmin access token (no se muestra)' -AsSecureString
  $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
  try { $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
  finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
  $token = $token.Trim()
  if ($token.StartsWith('Bearer ', [StringComparison]::OrdinalIgnoreCase)) { $token = $token.Substring(7).Trim() }
  if (-not $token) { throw 'Token vacio.' }
  return $token
}

# PowerShell 5.1 devuelve un arreglo JSON como UN objeto (un "[]" no es "nada"): se emite elemento por
# elemento, asi @(...) da la lista real y una lista vacia da cero elementos.
function ConvertFrom-JsonItems([string] $Text) {
  if (-not $Text.Trim()) { return }
  $parsed = ConvertFrom-Json -InputObject $Text
  if ($parsed -is [array]) {
    foreach ($item in $parsed) { $item }
    return
  }
  $parsed
}

# Anfitriones del archivo JSON, uno por elemento.
function Read-HostsFile([string] $Path) {
  ConvertFrom-JsonItems (Get-Content -Path $Path -Raw -Encoding UTF8)
}

function Invoke-Api {
  param(
    [Parameter(Mandatory)] [string] $Method,
    [Parameter(Mandatory)] [string] $Url,
    [string] $Token,
    $Body
  )
  $headers = @{ Accept = 'application/json' }
  if ($Token) { $headers['Authorization'] = "Bearer $Token" }
  $params = @{ Method = $Method; Uri = $Url; Headers = $headers; UseBasicParsing = $true }
  if ($null -ne $Body) {
    $json = $Body | ConvertTo-Json -Depth 10 -Compress
    $params['Body'] = [Text.Encoding]::UTF8.GetBytes($json)
    $params['ContentType'] = 'application/json; charset=utf-8'
  }
  try {
    $response = Invoke-WebRequest @params
    if (-not $response.Content) { return $null }
    $text = [Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray())
    return ConvertFrom-JsonItems $text
  }
  catch {
    $status = $null
    $detail = ''
    if ($_.Exception.Response) {
      $status = [int]$_.Exception.Response.StatusCode
      try {
        $reader = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())
        $detail = $reader.ReadToEnd()
      } catch { $detail = '' }
    }
    # PowerShell 7 deja el cuerpo del error aqui.
    if (-not $detail -and $_.ErrorDetails) { $detail = $_.ErrorDetails.Message }
    throw "HTTP $status en $Method $Url $detail"
  }
}

function Expand-Brand([string] $Text, [string] $Brand) {
  if ($null -eq $Text) { return $null }
  return $Text.Replace('{brand}', $Brand)
}

function Write-Step([string] $Text) { Write-Host "  - $Text" }

function Invoke-PublicDemoSeed {
  param(
    [Parameter(Mandatory)] [string] $ApiBase,
    [Parameter(Mandatory)] [string] $HostsFile,
    [string] $ContentFile = (Join-Path $PSScriptRoot 'public-demo.content.json'),
    [switch] $Activate
  )
  $api = $ApiBase.TrimEnd('/')
  $content = Get-Content -Path $ContentFile -Raw -Encoding UTF8 | ConvertFrom-Json
  $hosts = @(Read-HostsFile $HostsFile)
  if ($hosts.Count -eq 0) { throw "No hay anfitriones en $HostsFile." }

  $settings = Invoke-Api -Method GET -Url "$api/platform/settings/public"
  $brand = if ($settings.brandName) { $settings.brandName } else { 'TaxOffix' }
  Write-Host "API $api (marca: $brand)"

  $token = Read-PlatformToken

  # 1. Tipo de cita: no virtual, bloquea por conflicto. De aqui salen la duracion y el tope diario.
  $typeBody = @{
    name             = $content.appointmentType.name
    defaultDuration  = [TimeSpan]::FromMinutes([int]$content.appointmentType.durationMinutes).ToString('c')
    colorHex         = $content.appointmentType.colorHex
    isVirtual        = $false
    blocksOnConflict = $true
    dailyCap         = $content.appointmentType.dailyCap
  }
  $types = @(Invoke-Api -Method GET -Url "$api/calendar/types?onlyActive=false" -Token $token)
  $type = $types | Where-Object { $_.name -eq $content.appointmentType.name } | Select-Object -First 1
  if ($type) {
    $type = Invoke-Api -Method PUT -Url "$api/calendar/types/$($type.id)" -Token $token -Body $typeBody
    Write-Step "Tipo '$($type.name)' actualizado ($($type.id))"
  } else {
    $type = Invoke-Api -Method POST -Url "$api/calendar/types" -Token $token -Body $typeBody
    Write-Step "Tipo '$($type.name)' creado ($($type.id))"
  }

  # 2. Pagina por slug.
  $pageBody = @{
    slug              = $content.page.slug
    titleEn           = Expand-Brand $content.page.titleEn $brand
    titleEs           = Expand-Brand $content.page.titleEs $brand
    descriptionEn     = Expand-Brand $content.page.descriptionEn $brand
    descriptionEs     = Expand-Brand $content.page.descriptionEs $brand
    appointmentTypeId = $type.id
  }
  $pages = @(Invoke-Api -Method GET -Url "$api/calendar/platform/booking-pages" -Token $token)
  $page = $pages | Where-Object { $_.slug -eq $content.page.slug } | Select-Object -First 1
  if ($page) {
    $page = Invoke-Api -Method PUT -Url "$api/calendar/platform/booking-pages/$($page.id)" -Token $token -Body $pageBody
    Write-Step "Pagina '$($page.slug)' actualizada ($($page.id))"
  } else {
    $page = Invoke-Api -Method POST -Url "$api/calendar/platform/booking-pages" -Token $token -Body $pageBody
    Write-Step "Pagina '$($page.slug)' creada ($($page.id))"
  }
  $pageUrl = "$api/calendar/platform/booking-pages/$($page.id)"

  # 3. Reglas, campos, consentimiento y opciones.
  $r = $content.rules
  [void](Invoke-Api -Method PUT -Url "$pageUrl/rules" -Token $token -Body @{
      minNoticeMinutes         = $r.minNoticeMinutes
      maxDaysAhead             = $r.maxDaysAhead
      bufferMinutes            = $r.bufferMinutes
      slotStepMinutes          = $r.slotStepMinutes
      requireEmailVerification = $r.requireEmailVerification
      reminderHoursBefore      = $r.reminderHoursBefore
      icsAlarmMinutes          = @($r.icsAlarmMinutes)
    })
  Write-Step 'Reglas'
  [void](Invoke-Api -Method PUT -Url "$pageUrl/fields" -Token $token -Body $content.fields)
  Write-Step 'Campos'
  $c = $content.consent
  [void](Invoke-Api -Method PUT -Url "$pageUrl/consent" -Token $token -Body @{
      privacyNoticeEn    = $c.privacyNoticeEn
      privacyNoticeEs    = $c.privacyNoticeEs
      marketingConsentEn = $c.marketingConsentEn
      marketingConsentEs = $c.marketingConsentEs
      consentVersion     = $c.consentVersion
    })
  Write-Step "Consentimiento ($($c.consentVersion))"
  $order = 0
  $options = foreach ($o in $content.heardAboutOptions) {
    $order += 10
    @{
      code         = $o.code
      labelEn      = Expand-Brand $o.labelEn $brand
      labelEs      = Expand-Brand $o.labelEs $brand
      sortOrder    = $order
      isActive     = $true
      allowsDetail = [bool]$o.allowsDetail
    }
  }
  [void](Invoke-Api -Method PUT -Url "$pageUrl/heard-about-options" -Token $token -Body @{ options = @($options) })
  Write-Step "Opciones de 'How did you hear about us?' ($(@($options).Count))"

  # 4. Anfitriones (Calendar los verifica en Auth antes de guardar) y su horario semanal.
  $order = 0
  $hostInputs = foreach ($h in $hosts) {
    $order += 10
    @{
      userId            = $h.userId
      displayName       = $h.displayName
      notificationEmail = $h.notificationEmail
      meetingUrl        = $h.meetingUrl
      sortOrder         = $order
      isActive          = $true
    }
  }
  [void](Invoke-Api -Method PUT -Url "$pageUrl/hosts" -Token $token -Body @{ hosts = @($hostInputs) })
  Write-Step "Anfitriones ($(@($hostInputs).Count))"
  foreach ($h in $hosts) {
    if ($h.PSObject.Properties.Name -contains 'availability' -and $h.availability) {
      $rules = foreach ($a in @($h.availability)) {
        @{ daysMask = $a.daysMask; startTime = $a.startTime; endTime = $a.endTime; timeZoneId = $a.timeZoneId }
      }
      [void](Invoke-Api -Method PUT -Url "$api/calendar/availability/rules?userId=$($h.userId)" -Token $token -Body @{ rules = @($rules) })
      Write-Step "Horario de $($h.displayName)"
    } else {
      Write-Step "Horario de $($h.displayName): sin cambios (no viene en el archivo)"
    }
  }

  # 5. Activar solo si se pide.
  if ($Activate) {
    [void](Invoke-Api -Method POST -Url "$pageUrl/activate" -Token $token)
    Write-Step 'Pagina activada'
  } else {
    Write-Step 'Pagina sin activar (usa -Activate para publicarla)'
  }

  # 6. Lo que ve el landing, sin token.
  try {
    $public = Invoke-Api -Method GET -Url "$api/calendar/public/booking-pages/$($content.page.slug)?lang=en"
    # Una sola hora: dos lecturas separadas dejaban el rango apenas por encima de los 14 dias que acepta la API.
    $now = [DateTime]::UtcNow
    $from = $now.ToString('o')
    $to = $now.AddDays(14).ToString('o')
    $slots = Invoke-Api -Method GET -Url "$api/calendar/public/booking-pages/$($content.page.slug)/slots?from=$([Uri]::EscapeDataString($from))&to=$([Uri]::EscapeDataString($to))"
    Write-Host "Publico: bookingAvailable=$($public.bookingAvailable), horarios en 14 dias=$(@($slots.slots).Count)"
  } catch {
    Write-Host "Publico: la pagina todavia no responde ($($_.Exception.Message))"
  }
  $token = $null
}

Export-ModuleMember -Function Invoke-PublicDemoSeed, Read-HostsFile
