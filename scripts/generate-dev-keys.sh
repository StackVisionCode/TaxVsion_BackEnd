#!/usr/bin/env bash
# Genera el par de claves RSA para JWT RS256 en entornos de desarrollo.
# Ejecutar UNA sola vez desde la raiz del repositorio:
#   bash scripts/generate-dev-keys.sh
#
# Requisito: openssl instalado (viene en macOS y la mayoria de distros Linux).

set -euo pipefail

KEYS_DIR="$(dirname "$0")/../dev-keys"
mkdir -p "$KEYS_DIR"

PRIVATE_KEY="$KEYS_DIR/jwt-private.pem"
PUBLIC_KEY="$KEYS_DIR/jwt-public.pem"

if [ -f "$PRIVATE_KEY" ]; then
  echo "AVISO: $PRIVATE_KEY ya existe. Borralo manualmente si quieres regenerarlo."
  exit 0
fi

echo "Generando clave privada RSA 2048-bit..."
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$PRIVATE_KEY"

echo "Extrayendo clave publica..."
openssl pkey -in "$PRIVATE_KEY" -pubout -out "$PUBLIC_KEY"

echo ""
echo "Claves generadas:"
echo "  Privada (NUNCA subir a git): $PRIVATE_KEY"
echo "  Publica (segura para github): $PUBLIC_KEY"
echo ""
echo "El archivo dev-keys/.gitignore ya protege jwt-private.pem."
