# Genera el par de claves RSA para JWT RS256 en entornos de desarrollo.
# Ejecutar UNA sola vez desde la raiz del repositorio (PowerShell):
#   .\scripts\generate-dev-keys.ps1
#
# Requisito: openssl instalado.
#   - Git for Windows lo incluye en C:\Program Files\Git\usr\bin\openssl.exe
#   - O instalar con: winget install ShiningLight.OpenSSL

$ErrorActionPreference = "Stop"

$KeysDir = Join-Path $PSScriptRoot "..\dev-keys"
if (-not (Test-Path $KeysDir)) {
    New-Item -ItemType Directory -Path $KeysDir | Out-Null
}

$PrivateKey = Join-Path $KeysDir "jwt-private.pem"
$PublicKey  = Join-Path $KeysDir "jwt-public.pem"

if (Test-Path $PrivateKey) {
    Write-Warning "AVISO: $PrivateKey ya existe. Borralo manualmente si quieres regenerarlo."
    exit 0
}

Write-Host "Generando clave privada RSA 2048-bit..."
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out $PrivateKey

Write-Host "Extrayendo clave publica..."
openssl pkey -in $PrivateKey -pubout -out $PublicKey

Write-Host ""
Write-Host "Claves generadas:"
Write-Host "  Privada (NUNCA subir a git): $PrivateKey"
Write-Host "  Publica (segura para github): $PublicKey"
Write-Host ""
Write-Host "El archivo dev-keys/.gitignore ya protege jwt-private.pem."
