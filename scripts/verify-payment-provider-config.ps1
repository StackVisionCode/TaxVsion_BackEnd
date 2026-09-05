param(
  [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
  [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Read-DotEnv {
  param([string]$Path)

  $values = @{}
  if (-not (Test-Path -LiteralPath $Path)) {
    return $values
  }

  foreach ($line in Get-Content -LiteralPath $Path) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line.TrimStart().StartsWith("#")) { continue }

    if ($line -match "^\s*([^#=]+?)\s*=\s*(.*)\s*$") {
      $key = $Matches[1].Trim()
      $value = $Matches[2].Trim()

      if (
        ($value.StartsWith('"') -and $value.EndsWith('"')) -or
        ($value.StartsWith("'") -and $value.EndsWith("'"))
      ) {
        $value = $value.Substring(1, $value.Length - 2)
      }

      $values[$key] = $value
    }
  }

  return $values
}

function Add-FlattenedJson {
  param(
    [Parameter(Mandatory = $true)]$Node,
    [string]$Prefix = "",
    [Parameter(Mandatory = $true)][hashtable]$Output
  )

  foreach ($property in $Node.PSObject.Properties) {
    $key = if ([string]::IsNullOrWhiteSpace($Prefix)) { $property.Name } else { "${Prefix}:$($property.Name)" }
    if ($property.Value -is [pscustomobject]) {
      Add-FlattenedJson -Node $property.Value -Prefix $key -Output $Output
    }
    else {
      $Output[$key] = [string]$property.Value
    }
  }
}

function Read-UserSecrets {
  param([string]$ProjectPath)

  $values = @{}
  if (-not (Test-Path -LiteralPath $ProjectPath)) {
    return $values
  }

  [xml]$project = Get-Content -LiteralPath $ProjectPath
  $secretId = ($project.Project.PropertyGroup | Where-Object { $_.UserSecretsId } | Select-Object -First 1).UserSecretsId
  if ([string]::IsNullOrWhiteSpace($secretId)) {
    return $values
  }

  $secretRoots = @()
  if (-not [string]::IsNullOrWhiteSpace($env:APPDATA)) {
    $secretRoots += Join-Path $env:APPDATA "Microsoft\UserSecrets"
  }
  if (-not [string]::IsNullOrWhiteSpace($env:HOME)) {
    $secretRoots += Join-Path $env:HOME ".microsoft/usersecrets"
  }

  $secretPath = $null
  foreach ($root in $secretRoots) {
    $candidate = Join-Path (Join-Path $root $secretId) "secrets.json"
    if (Test-Path -LiteralPath $candidate) {
      $secretPath = $candidate
      break
    }
  }

  if ([string]::IsNullOrWhiteSpace($secretPath)) {
    return $values
  }

  if (-not (Test-Path -LiteralPath $secretPath)) {
    return $values
  }

  $json = Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json
  Add-FlattenedJson -Node $json -Output $values

  return $values
}

function Get-ConfigValue {
  param(
    [hashtable]$Values,
    [string]$Key
  )

  if ($Values.ContainsKey($Key)) {
    return $Values[$Key]
  }

  return $null
}

function Get-Status {
  param([AllowNull()][string]$Value)

  if ($null -eq $Value) { return "missing" }
  if ([string]::IsNullOrWhiteSpace($Value)) { return "empty" }
  if ($Value -match "(?i)replace_me|change_me|placeholder|dummy") { return "placeholder" }

  return "present"
}

function Is-Present {
  param([string]$Status)

  return $Status -eq "present"
}

function Get-Flag {
  param(
    [hashtable]$Values,
    [string]$Key,
    [bool]$Default
  )

  $raw = Get-ConfigValue -Values $Values -Key $Key
  if ([string]::IsNullOrWhiteSpace($raw)) {
    return $Default
  }

  return $raw -match "^(?i:true|1|yes|y|on)$"
}

$dotenv = Read-DotEnv -Path (Join-Path $RepoRoot ".env")
$paymentAppProject = Join-Path $RepoRoot "src\Services\PaymentApp\TaxVision.PaymentApp.Api\TaxVision.PaymentApp.Api.csproj"
$paymentAppSecrets = Read-UserSecrets -ProjectPath $paymentAppProject
$paymentClientProject = Join-Path $RepoRoot "src\Services\PaymentClient\TaxVision.PaymentClient.Api\TaxVision.PaymentClient.Api.csproj"
$paymentClientSecrets = Read-UserSecrets -ProjectPath $paymentClientProject

$checks = @(
  @{ Scope = ".env PaymentApp onboarding"; Key = "STRIPE_SECRET_KEY"; RequiredWhen = "Stripe onboarding enabled"; Values = $dotenv },
  @{ Scope = ".env PaymentApp onboarding"; Key = "STRIPE_WEBHOOK_SECRET"; RequiredWhen = "Stripe onboarding enabled + dashboard webhook"; Values = $dotenv },
  @{ Scope = ".env PaymentApp onboarding"; Key = "STRIPE_ONBOARDING_ENABLED"; RequiredWhen = "Optional kill switch"; Values = $dotenv },
  @{ Scope = ".env PaymentApp onboarding"; Key = "STRIPE_ONBOARDING_DISABLED_REASON"; RequiredWhen = "Optional kill switch reason"; Values = $dotenv },
  @{ Scope = ".env PaymentApp onboarding"; Key = "PAYPAL_BASE_URL"; RequiredWhen = "PayPal onboarding enabled"; Values = $dotenv },
  @{ Scope = ".env PaymentApp onboarding"; Key = "PAYPAL_CLIENT_ID"; RequiredWhen = "PayPal onboarding enabled"; Values = $dotenv },
  @{ Scope = ".env PaymentApp onboarding"; Key = "PAYPAL_CLIENT_SECRET"; RequiredWhen = "PayPal onboarding enabled"; Values = $dotenv },
  @{ Scope = ".env PaymentApp onboarding"; Key = "PAYPAL_WEBHOOK_ID"; RequiredWhen = "PayPal onboarding enabled + dashboard webhook"; Values = $dotenv },
  @{ Scope = ".env PaymentApp onboarding"; Key = "PAYPAL_ONBOARDING_ENABLED"; RequiredWhen = "Optional kill switch"; Values = $dotenv },
  @{ Scope = ".env PaymentApp onboarding"; Key = "PAYPAL_ONBOARDING_DISABLED_REASON"; RequiredWhen = "Optional kill switch reason"; Values = $dotenv },
  @{ Scope = ".env PaymentClient Stripe Connect"; Key = "STRIPE_CONNECT_PLATFORM_SECRET_KEY"; RequiredWhen = "Tenant Connect payments, not SaaS onboarding"; Values = $dotenv },
  @{ Scope = ".env PaymentClient Stripe Connect"; Key = "STRIPE_CONNECT_WEBHOOK_SECRET"; RequiredWhen = "Stripe Connect webhooks, not SaaS onboarding"; Values = $dotenv },
  @{ Scope = "user-secrets PaymentApp onboarding"; Key = "Stripe:SecretKey"; RequiredWhen = "Local Stripe onboarding enabled"; Values = $paymentAppSecrets },
  @{ Scope = "user-secrets PaymentApp onboarding"; Key = "Stripe:WebhookSecret"; RequiredWhen = "Local Stripe webhook tests/manual"; Values = $paymentAppSecrets },
  @{ Scope = "user-secrets PaymentApp onboarding"; Key = "PayPal:BaseUrl"; RequiredWhen = "Optional, sandbox default exists"; Values = $paymentAppSecrets },
  @{ Scope = "user-secrets PaymentApp onboarding"; Key = "PayPal:ClientId"; RequiredWhen = "Local PayPal onboarding enabled"; Values = $paymentAppSecrets },
  @{ Scope = "user-secrets PaymentApp onboarding"; Key = "PayPal:ClientSecret"; RequiredWhen = "Local PayPal onboarding enabled"; Values = $paymentAppSecrets },
  @{ Scope = "user-secrets PaymentApp onboarding"; Key = "PayPal:WebhookId"; RequiredWhen = "Local PayPal webhook tests/manual"; Values = $paymentAppSecrets },
  @{ Scope = "user-secrets PaymentClient Stripe Connect"; Key = "Stripe:PlatformSecretKey"; RequiredWhen = "Local tenant Connect payments, not SaaS onboarding"; Values = $paymentClientSecrets },
  @{ Scope = "user-secrets PaymentClient Stripe Connect"; Key = "Stripe:ConnectWebhookSecret"; RequiredWhen = "Local Stripe Connect webhook tests/manual"; Values = $paymentClientSecrets }
)

$rows = foreach ($check in $checks) {
  $value = Get-ConfigValue -Values $check.Values -Key $check.Key
  [pscustomobject]@{
    Scope = $check.Scope
    Key = $check.Key
    Status = Get-Status -Value $value
    RequiredWhen = $check.RequiredWhen
  }
}

Write-Host ""
Write-Host "Payment provider config check (values redacted)"
Write-Host "Repo: $RepoRoot"
Write-Host ""
$rows | Format-Table -AutoSize

$issues = New-Object System.Collections.Generic.List[string]
$stripeEnabled = Get-Flag -Values $dotenv -Key "STRIPE_ONBOARDING_ENABLED" -Default $true
$payPalEnabled = Get-Flag -Values $dotenv -Key "PAYPAL_ONBOARDING_ENABLED" -Default $false

if ($stripeEnabled) {
  foreach ($key in @("STRIPE_SECRET_KEY", "STRIPE_WEBHOOK_SECRET")) {
    $status = Get-Status -Value (Get-ConfigValue -Values $dotenv -Key $key)
    if (-not (Is-Present -Status $status)) {
      $issues.Add(".env $key is $status while Stripe onboarding is enabled.")
    }
  }
}

if ($payPalEnabled) {
  foreach ($key in @("PAYPAL_CLIENT_ID", "PAYPAL_CLIENT_SECRET", "PAYPAL_WEBHOOK_ID")) {
    $status = Get-Status -Value (Get-ConfigValue -Values $dotenv -Key $key)
    if (-not (Is-Present -Status $status)) {
      $issues.Add(".env $key is $status while PayPal onboarding is enabled.")
    }
  }
}

if ($issues.Count -gt 0) {
  Write-Host ""
  Write-Warning "Configuration is not ready:"
  foreach ($issue in $issues) {
    Write-Warning " - $issue"
  }

  if ($Strict) {
    exit 1
  }
}
else {
  Write-Host ""
  Write-Host "Configuration looks ready for the providers currently enabled in .env."
}

Write-Host ""
Write-Host "Dashboard endpoints to configure:"
Write-Host "  PaymentApp Stripe onboarding: https://<api-domain>/payments-app/webhooks/stripe"
Write-Host "  PaymentApp PayPal onboarding: https://<api-domain>/payments-app/webhooks/paypal"
Write-Host "  PaymentClient Stripe Connect: https://<api-domain>/payments-client/webhooks/stripe-connect"
