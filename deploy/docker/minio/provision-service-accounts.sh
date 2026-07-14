#!/usr/bin/env sh
# Fase D0/D-Customer — crea las 4 cuentas de servicio scoped que reemplazan el uso
# de las credenciales root de MinIO (MINIO_ROOT_USER/PASSWORD) en Signature/
# Notification/CommunicationTranscriptWorker/Customer. Cada una solo puede
# s3:PutObject bajo su propio prefijo en taxvision-temp/* (ver policies/*.json) —
# nunca leer, listar ni tocar taxvision-storage/taxvision-quarantine.
#
# Los access keys y los nombres de variable *_MINIO_SECRET de abajo son EXACTAMENTE
# los que consume deploy/docker/docker-compose.yml (Signature__Minio__AccessKey,
# Notification__Minio__AccessKey, TRANSCRIPT_WORKER_MINIO_ACCESS_KEY,
# Customer__Minio__AccessKey) — si los cambias aca, cambialos tambien alla o los
# servicios dejan de poder autenticarse.
#
# Se ejecuta automaticamente como parte del deploy (ver el servicio "minio-provision"
# en docker-compose.yml, profile "tools", y el workflow deploy.yml) DESPUES de que
# minio este healthy. Tambien se puede correr a mano contra cualquier entorno.
# Requiere el cliente `mc` (https://min.io/docs/minio/linux/reference/minio-mc.html).
#
# Uso manual:
#   MINIO_ALIAS=local MINIO_ENDPOINT=http://localhost:9000 \
#   MINIO_ROOT_USER=... MINIO_ROOT_PASSWORD=... \
#   SIGNATURE_MINIO_SECRET=... NOTIFICATION_MINIO_SECRET=... TRANSCRIPT_WORKER_MINIO_SECRET=... \
#   CUSTOMER_MINIO_SECRET=... \
#   ./provision-service-accounts.sh
#
# Los *_MINIO_SECRET son las contrasenas que despues van en cada servicio como
# Minio__AccessKey/Minio__SecretKey (appsettings/user-secrets), NUNCA en este repo.

set -eu

MINIO_ALIAS="${MINIO_ALIAS:?Set MINIO_ALIAS (ej. local, prod)}"
MINIO_ENDPOINT="${MINIO_ENDPOINT:?Set MINIO_ENDPOINT (ej. http://localhost:9000)}"
MINIO_ROOT_USER="${MINIO_ROOT_USER:?Set MINIO_ROOT_USER}"
MINIO_ROOT_PASSWORD="${MINIO_ROOT_PASSWORD:?Set MINIO_ROOT_PASSWORD}"

mc alias set "$MINIO_ALIAS" "$MINIO_ENDPOINT" "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD"

provision() {
  service_name="$1"
  access_key="$2"
  secret_key="$3"
  policy_file="$4"

  mc admin policy create "$MINIO_ALIAS" "${service_name}-source" "$policy_file"
  # idempotente: si el user ya existe, mc admin user add actualiza la password.
  mc admin user add "$MINIO_ALIAS" "$access_key" "$secret_key"
  mc admin policy attach "$MINIO_ALIAS" "${service_name}-source" --user "$access_key"
}

provision "signature" "signature-worker" "${SIGNATURE_MINIO_SECRET:?Set SIGNATURE_MINIO_SECRET}" \
  "$(dirname "$0")/policies/signature-source.json"
provision "notification" "notification-worker" "${NOTIFICATION_MINIO_SECRET:?Set NOTIFICATION_MINIO_SECRET}" \
  "$(dirname "$0")/policies/notification-source.json"
provision "transcript" "communication-transcript-worker" "${TRANSCRIPT_WORKER_MINIO_SECRET:?Set TRANSCRIPT_WORKER_MINIO_SECRET}" \
  "$(dirname "$0")/policies/transcript-source.json"
provision "customer" "customer-worker" "${CUSTOMER_MINIO_SECRET:?Set CUSTOMER_MINIO_SECRET}" \
  "$(dirname "$0")/policies/customer-source.json"

echo "Done. Set Minio__AccessKey/Minio__SecretKey in each service to the access-key/secret used above."
