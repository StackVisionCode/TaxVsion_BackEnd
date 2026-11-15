# CI/CD y secrets

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd`. Etiquetas: **[Código verificado]**, **[Doc. histórica]**, **[Recomendación]**, **[Decisión pendiente]**. Solo nombres de secrets, nunca valores.

## 1. Estado actual

- Un solo workflow: `.github/workflows/deploy.yml` (825 líneas), "Deploy to production", disparado por `push` a `main` y `workflow_dispatch` (3-6), con `concurrency: production-deploy` sin cancelar (8-10). **[Código verificado]**
- **Dos jobs** sobre runner **self-hosted** (la misma VM que corre la flota): `stage` (environment `production`) y `deploy` (environment `production-extra`, `needs: stage`). Se partió porque un job solo ve un environment y ambos llegaron al tope de 100 secrets (13-21). **[Código verificado]**
- **El workflow no corre pruebas** ni compila .NET fuera de Docker: no hay paso `dotnet test` ni `setup-dotnet`. Es decisión del dueño desde el 2026-11-13 (saturaba el runner). El gate se corre **en local** antes de promover. **[Código verificado]** (grep sin `dotnet test`) + **[Doc. histórica]** `memory/project_ci_sin_gate_de_pruebas.md`
- No hay registry: las imágenes se construyen en la VM con `docker compose build` y quedan con tag `taxvision/<svc>:dev`. **[Código verificado]**

## 2. Evidencia

### 2.1 Job `stage` (environment `production`) **[Código verificado]**

| Líneas | Paso |
|---|---|
| 22-35 | `runs-on: self-hosted`, `environment: production`, `timeout-minutes: 30`; env `COMPOSE_FILE`, `TAXVISION_SECRETS_DIR=/opt/taxvision/secrets`, `TAXVISION_STAGING_DIR=/opt/taxvision/deploy-staging` |
| 37-38 | `actions/checkout@v4` |
| 54-59 | Valida `TAXVISION_BASE_DOMAIN` y `TAXVISION_DOMAIN` (sin ellos se corta) |
| 61, 208, 323 | `Write production .env (1/3, 2/3, 3/3)`: heredoc en 3 pasos porque un `run` no puede pasar de 21 000 caracteres (~190 secrets) (44-49) |
| 525-541 | Decodifica de base64 a `TAXVISION_SECRETS_DIR`: `jwt-private.pem`, `jwt-public.pem`, `signature-sealing.pfx`, `firebase-service-account.json` (archivo vacío si el secret falta), `chmod 600` |
| 543-553 | Copia el `.env` al directorio de staging del runner (no artifacts: expondrían secrets en la UI) |
| 555-557 | Si falla, borra el `.env` staged |

### 2.2 Job `deploy` (environment `production-extra`) **[Código verificado]**

| Líneas | Paso |
|---|---|
| 559-568 | `needs: stage`, `timeout-minutes: 45`, `DOTNET_INSTALL_DIR` (no se usa en ningún paso) |
| 577-584 | Carga el `.env` staged (error si no existe) |
| 591-652 | Agrega al `.env` los secrets de `production-extra` (FinancialInsights, Turnstile, Campaign email, Google Drive/OneDrive/Dropbox, `RECORDER_MINIO_SECRET`, `CONTENT_SCANNER_API_KEY`, `CALENDAR_SERVICE_CLIENT_SECRET`, etc.) |
| 654-678 | Escribe `signature-signer-jwt.pem` (falla con error claro si falta `SIGNATURE_SIGNER_JWT_PEM_B64`) |
| 680-710 | Build: lista servicios con `build:` vía `docker compose config --format json` + `python3`, y construye en **tandas de 3** con `--profile tools` (incluye la imagen `migrations` para que no quede vieja; OOM exit 137 con build paralelo) |
| 712-761 | Reconcilia credenciales de RabbitMQ (`rabbitmqctl change_password`/`add_user`, `set_permissions`) porque el volumen guarda las del primer arranque |
| 763-764 | `--profile tools run --rm minio-provision` (cuentas MinIO scoped) |
| 766-767 | `--profile tools run --rm migrations` (EF Core, 25 servicios + FinancialInsights opcional) |
| 769-770 | `run --rm communication-api npx prisma migrate deploy` |
| 778-779 | `up -d --remove-orphans` (no toca volúmenes) |
| 781-784 | `docker image prune -f` y `docker builder prune -f --filter "until=24h"` |
| 786-819 | Healthcheck: 20 intentos cada 5 s a `https://${TAXVISION_DOMAIN}/health/ready`; si falla vuelca `ps`, readiness de auth/tenant/customer/cloudstorage desde la red Docker (vía `caddy` + `wget`) y logs |
| 821-825 | `if: always()`: borra el `.env` staged y el del workspace (en la VM no queda `.env`) |

### 2.3 Migraciones

- Imagen `deploy/docker/migrations/Dockerfile:1-13`: `sdk:10.0.300`, `COPY . .`, `dotnet tool restore` (dotnet-ef del manifiesto `dotnet-tools.json`), `dotnet build TaxVision.slnx -c Release`, ENTRYPOINT `apply-migrations.sh`. **[Código verificado]**
- `apply-migrations.sh:1-17`: `set -eu`; `dotnet ef database update --project <Infra> --startup-project <Api> --configuration Release --no-build --connection "$<SVC>_DB_CONNECTION"`. 26 llamadas; FinancialInsights solo si su variable tiene valor (171-177). **[Código verificado]**
- Servicio `migrations` del compose con todas las `*_DB_CONNECTION` (`docker-compose.yml:2868-2913`); si falta una con `:?`, el comando no arranca. **[Código verificado]**

### 2.4 Secrets: environments y nombres

- `production`: lo lee el job `stage`. `production-extra`: solo el job `deploy`. **[Código verificado]** `deploy.yml:13-21`, `ProductionExtraSecretsDeployTests.cs:6-11`
- Referencias únicas a `secrets.*`: 223 en `stage` y 35 en `deploy`. **[Código verificado]** (conteo por grep). Ver Problema 2.
- Familias de nombres (sin valores): `<SVC>_DB_CONNECTION`, `SQLSERVER_HOST/PORT`, `RABBITMQ_USER/PASSWORD/CONNECTION`, `MINIO_ROOT_USER/PASSWORD`, `<SVC>_MINIO_SECRET`, `<SVC>_SERVICE_CLIENT_ID/SECRET`, `ENCRYPTION_MASTER_KEY`, `MFA_ENCRYPTION_KEY`, `JWT_PRIVATE_KEY_PEM_B64`, `JWT_PUBLIC_KEY_PEM_B64`, `SIGNATURE_SEALING_PFX_B64`, `SIGNATURE_SIGNER_JWT_PEM_B64`, `FIREBASE_SERVICE_ACCOUNT_JSON_B64`, `TAXVISION_DOMAIN`, `TAXVISION_BASE_DOMAIN`, `CLOUDFLARE_DNS_API_TOKEN`, `MGMT_BIND_IP`, `COMMUNICATION_*`, `STRIPE_*`, SMTP/OAuth de proveedores. **[Código verificado]** (deploy.yml) + **[Doc. histórica]** `Implementaciones/DEVS/Guia_Creacion_Microservicio.md` §10.2 y `CommunicationCorrecciones/F11_Despliegue_Secretos_y_VM.md`.
- Nombres hoy en `production-extra` (job `deploy`): `CALENDAR_PUBLIC_BOOKING_TURNSTILE_SECRET_KEY`, `CALENDAR_PUBLIC_BOOKING_TURNSTILE_SITE_KEY`, `CALENDAR_SERVICE_CLIENT_SECRET`, `CAMPAIGN_EMAIL_*` (14), `CONNECTORS_LISTENER_ENABLED`, `CONTENT_SCANNER_API_KEY`, `DROPBOX_CLIENT_ID/SECRET`, `FINANCIAL_INSIGHTS_DB_CONNECTION`, `FINANCIAL_INSIGHTS_OPENAI_API_KEY`, `FINANCIAL_INSIGHTS_SERVICE_CLIENT_ID/SECRET`, `GOOGLE_DRIVE_CLIENT_ID/SECRET`, `ONEDRIVE_CLIENT_ID/SECRET`, `RECORDER_MINIO_SECRET`, `SIGNATURE_SIGNER_JWT_PEM_B64`, `SMTP2GO_API_KEY`, `TASKS_CLIENT_FORMS_TURNSTILE_SECRET_KEY/SITE_KEY`, y `TAXVISION_DOMAIN` (usado en el healthcheck). **[Código verificado]**
- Hoja de control del dueño con los secrets: `Implementaciones/GITHUBSCT/TaxVision_Secrets.xlsx` (no abierta: contiene secretos). **[Doc. histórica]**

### 2.5 Pruebas que protegen el workflow **[Código verificado]**

- `ProductionExtraSecretsDeployTests` (14-37+): cada secret de la lista se escribe en el job `deploy`, no en `stage` (incidente Google Drive: 503 "not configured" con el secret cargado).
- `ServiceClientSecretsDeployWiringTests` (14): todo `*_CLIENT_SECRET` del compose está en `deploy.yml` (incidente Calendar).
- `PlatformDomainSingleSourceDeployTests`, y por servicio `PaymentAppDeployConfigWiringTests` (lee `deploy.yml`, 35), `PaymentClientDeployConfigWiringTests`, `AuthOnboardingDeployConfigWiringTests`, `AccountSurfaceDeployWiringTests`, `ConnectorsDeployConfigWiringTests`, `CampaignEmailDeployWiringTests`, `SignerKeyDeployWiringTests`, `CloudProviderDeployWiringTests`, `TrialPlanDeployWiringTests`.
- Estas pruebas solo protegen si el gate se corre en local antes del push (el CI ya no lo corre).

### 2.6 Calidad antes de promover (estado real)

1. Gate local exacto: `dotnet test TaxVision.slnx --nologo --filter "FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence.&FullyQualifiedName!~ReminderRetentionJobTests"`. **[Doc. histórica]** `AGENTS.md`, `DomainDrivenDesign/01-Convenciones-Pruebas.md:19`
2. Vitest de Communication, Recorder y TranscriptWorker en local. **[Doc. histórica]** `memory/project_ci_sin_gate_de_pruebas.md`
3. CSharpier (`printWidth 120`) antes del gate: el formato puede romper fitness de tamaño de método. **[Doc. histórica]** `memory/feedback_csharpier_antes_del_gate.md`
4. Pensar en Linux: el runner/VM es Linux; `Path.GetInvalidFileNameChars()` y fábricas con user-secrets fuera de `.Integration.` fallaron allí. **[Doc. histórica]** `memory/feedback_ci_linux_difiere_de_windows.md`
5. Commit y push los hace el dueño; el push a `main` despliega. **[Doc. histórica]** `AGENTS.md`

## 3. Problemas

1. **Sin gate en CI**: un push a `main` con pruebas rojas despliega igual. Es decisión del dueño (no reabrir), pero la guía debe dejar explícito que la calidad depende del gate local. **[Doc. histórica]**
2. **223 nombres de secret referenciados en el job `stage`** frente al tope de 100 por environment. Solo cuadra si muchos son secrets de repositorio (u organización) o si algunos no existen y salen vacíos. **[Decisión pendiente]**: confirmar en GitHub qué nivel tiene cada uno.
3. **`TAXVISION_DOMAIN` se usa en el job `deploy`** (healthcheck, 788), que solo ve `production-extra` y los de repo; si fuera solo de `production` el healthcheck consultaría `https:///health/ready`. Funciona hoy, así que debe estar en repo o duplicado. **[Decisión pendiente]**: confirmar.
4. **El healthcheck final solo prueba Caddy → Gateway**: `/health/ready` del gateway no tiene checks `ready` (`src/Gateway/TaxVision.Gateway/Program.cs:81-92,180`). Un servicio caído no hace fallar el deploy. **[Código verificado]**
5. **Guía DEVS desfasada**: describe un "job único" con gate de tests .NET y Node (`DEVS/Guia_Creacion_Microservicio.md:1062-1083`). Hoy son dos jobs sin pruebas. **[Doc. histórica]** vs **[Código verificado]**
6. **`DOTNET_INSTALL_DIR`** declarado y sin uso (568). **[Código verificado]**
7. **Sin rollback automatizado** ni etiquetado de imágenes por commit (todo `:dev`). **[Código verificado]** **[Decisión pendiente]**

## 4. Buenas prácticas ya vigentes

- `.env` y certificados se escriben en cada run y nunca se versionan; el `.env` se borra con `always()`.
- Staging entre jobs por directorio del runner, no artifacts.
- `:?` en el compose: falta de secret = deploy que no arranca, no servicio a medias.
- Migraciones antes del `up -d`, con la imagen de migraciones reconstruida en cada deploy.
- Build por tandas para no agotar RAM.
- Reconciliación de RabbitMQ idempotente.

## 5. Recomendación

- **[Recomendación]** En la guía: "un secret nuevo va a `production-extra`, se escribe en el paso *Append production-extra secrets to .env* del job `deploy` y se agrega su nombre al `[InlineData]` de `ProductionExtraSecretsDeployTests`".
- **[Recomendación]** Definition of Done previa a pedir push: gate local exacto verde, CSharpier sobre lo tocado, `.Integration.` del servicio corridas contra base local si se tocó persistencia o handlers, migración aplicada en local.
- **[Decisión pendiente]** Etiquetar imágenes por SHA y documentar rollback manual.

## 6. Riesgos

- Secret en el job equivocado → variable vacía en el contenedor (pasó). Lo cubre la prueba si se agrega el nombre.
- `*_CLIENT_SECRET` no escrito → M2M falla en silencio. Lo cubre `ServiceClientSecretsDeployWiringTests`.
- `*_DB_CONNECTION` faltante en el servicio `migrations` → `set -eu` corta **todas** las migraciones (`docker-compose.yml:2893-2897`, comentario del incidente).
- Runner = VM de producción: un build pesado compite por RAM con la flota.

## 7. Pruebas

Ver 2.5. Ninguna corre en CI hoy; todas en el gate local.

## 8. Dependencias

- Compose (`Docker_Deploy.md`), migraciones EF (`apply-migrations.sh`), Prisma de Communication.
- Cloudflare y Caddy para el healthcheck público (`Cloudflare.md`).

## 9. Material para la guía

### 9.1 Checklist CI/CD de un servicio nuevo

1. Proyectos en `TaxVision.slnx` (entran a la imagen `migrations`, que compila la solución).
2. `build:` en su bloque de compose (el workflow lo descubre solo, no hay lista).
3. Secrets nuevos en GitHub → environment **`production-extra`** (el `production` está lleno).
4. En `deploy.yml`, paso `Append production-extra secrets to .env` (591-652):
   ```bash
   <SVC>_DB_CONNECTION=${{ secrets.<SVC>_DB_CONNECTION }}
   <SVC>_SERVICE_CLIENT_ID=${{ secrets.<SVC>_SERVICE_CLIENT_ID }}
   <SVC>_SERVICE_CLIENT_SECRET=${{ secrets.<SVC>_SERVICE_CLIENT_SECRET }}
   ```
5. Nombres al `[InlineData]` de `ProductionExtraSecretsDeployTests`.
6. `*_DB_CONNECTION` en el servicio `migrations` del compose y bloque en `apply-migrations.sh`:
   ```sh
   apply_migration \
     "<Svc>" \
     "src/Services/<Svc>/TaxVision.<Svc>.Infrastructure/TaxVision.<Svc>.Infrastructure.csproj" \
     "src/Services/<Svc>/TaxVision.<Svc>.Api/TaxVision.<Svc>.Api.csproj" \
     "$<SVC>_DB_CONNECTION"
   ```
7. Cliente M2M en `auth-api` (`ServiceAuth__Clients__<N>__ClientId/Secret/Permissions__*`, ej. `docker-compose.yml:713-715`).
8. Gate local completo verde; el dueño hace commit y push a `main`.
9. Tras el deploy: revisar logs del servicio en la VM (ver `Observabilidad.md`) y que diga "code generation mode is Static".

### 9.2 Comandos de diagnóstico en la VM (documentados)

```bash
sudo docker ps
sudo docker logs --tail 200 taxvision-<svc>-api
sudo docker exec -it taxvision-communication-api node dist/jobs/<job>.js --dry-run
```
Fuente: `CommunicationCorrecciones/F11_Despliegue_Secretos_y_VM.md:45-60`. En la VM no queda `.env`: los jobs a mano corren dentro del contenedor, que tiene sus variables.
