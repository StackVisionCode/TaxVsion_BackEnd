# Email (SMTP2GO) — Data Model

- Servicio: **TaxVision.Campaigns.Email**
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- BD: PostgreSQL propio del servicio (schema `campaigns_email`), NO comparte BD con Postmaster/Notification/Campaigns.

## 1. Principios
- **Sin FK cross-context**: `campaign_id`, `campaign_run_id`, `recipient_id`, `tenant_id` son `uuid` opacos (no FK a Campaigns).
- **Multi-tenant fail-closed**: toda tabla con datos de tenant lleva `tenant_id` + **query filter global**; repos tenant-scoped; `.IgnoreQueryFilters()`+tenant explícito solo en handlers de bus (ver `Guia_IgnoreQueryFilters...`).
- **Dinero: ninguno acá.** Este servicio no persiste montos ni saldo (eso es Wallet). Corrige el `EstimatedCost/Currency` que el legado embebía en `EmailSendLog` (`Smtp2GoService.cs:322-324`).
- **Secretos cifrados** siempre (envelope encryption), nunca texto plano.
- Optimistic concurrency vía `xmin`/`RowVersion` en aggregates mutables.

## 2. Tablas

### 2.1 `email_dispatch` (aggregate root)
| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid NOT NULL | query filter |
| `campaign_id` | uuid NOT NULL | opaco |
| `campaign_run_id` | uuid NOT NULL | opaco |
| `recipient_id` | uuid NOT NULL | opaco |
| `attempt` | int NOT NULL | |
| `idempotency_key` | text NOT NULL | canónico `(run,recipient,attempt)` |
| `to_address` | citext NOT NULL | normalizada |
| `provider_scope` | smallint NOT NULL | System/Tenant |
| `status` | smallint NOT NULL | ver State_Machines |
| `provider_message_id` | text NULL | `email_id` SMTP2GO (correlación webhook) |
| `body_hash` | bytea NULL | hash del HTML final (no el HTML) |
| `failure_reason` | text NULL | |
| `created_at_utc` | timestamptz NOT NULL | |
| `sent_at_utc` `delivered_at_utc` `terminal_at_utc` | timestamptz NULL | |
| `row_version` | xmin | concurrencia |

Índices/constraints:
- **UNIQUE `(tenant_id, campaign_run_id, recipient_id, attempt)`** — una fila por intento (idempotencia de dispatch; corrige #3/#8).
- INDEX `(tenant_id, provider_message_id)` — lookup por webhook.
- INDEX `(tenant_id, campaign_run_id, status)` — reconciliación/stats por run.
- El **HTML completo NO se almacena** (privacidad + tamaño); solo `body_hash`.

### 2.2 `suppression_entry`
| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid NOT NULL | |
| `address` | citext NOT NULL | normalizada |
| `reason` | smallint NOT NULL | HardBounce/SpamComplaint/Unsubscribe/Manual/ProviderSuppressed |
| `source_message_id` | text NULL | |
| `created_at_utc` | timestamptz NOT NULL | |
- **UNIQUE `(tenant_id, address)`** — consulta O(1) fail-closed antes de enviar.
- `HardBounce`/`SpamComplaint` no son borrables por API (solo `Manual`/`Unsubscribe`).

### 2.3 `provider_credential`
| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid NULL | NULL ⇒ scope System (plataforma) |
| `scope` | smallint NOT NULL | System/Tenant |
| `encrypted_api_key` | bytea NOT NULL | **envelope-encrypted**, nunca texto plano |
| `key_version` | int NOT NULL | rotación |
| `base_url` | text NOT NULL | default `https://api.smtp2go.com/v3` |
| `from_email` `from_name` | text | FromEmail verificado |
| `from_domain_verified` | bool NOT NULL | |
| `provider_rate_per_second` | int NOT NULL | rate del plan |
| `webhook_secret_enc` | bytea NOT NULL | HMAC secret del webhook, cifrado |
| `is_active` | bool NOT NULL | |
- UNIQUE `(coalesce(tenant_id,'00..0'), scope)`.
- Reemplaza `SmtpProviderConfig` legado que tenía `ApiKey` en claro (`SmtpProviderConfig.cs:7`).

### 2.4 `inbound_webhook_event` (crudo + dedupe)
| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid NULL | resuelto por provider_message_id |
| `provider_event_id` | text NOT NULL | del payload SMTP2GO |
| `provider_message_id` | text NULL | correlación al dispatch |
| `event_type` | text NOT NULL | delivered/bounce/spam/unsubscribe |
| `raw_payload` | jsonb NOT NULL | crudo, auditable |
| `signature_valid` | bool NOT NULL | |
| `received_at_utc` | timestamptz NOT NULL | |
| `processed_at_utc` | timestamptz NULL | |
- **UNIQUE `(provider_event_id)`** — dedupe de reintento del proveedor.

### 2.5 `processed_business_message` (business-inbox)
Copia local del patrón `Growth/.../Idempotency/ProcessedBusinessMessage.cs`: `(handler, message_key)` unique, para dedupe de **efecto de negocio** (no solo de transporte). Usado por `ProcessEmailDispatch` y `ApplyProviderWebhook`.

### 2.6 (opcional) `email_tracking_event`
Solo si se hostea open/click propio. `(tenant_id, dispatch_id, kind)` con dedupe de open (una vez por dispatch). Corrige el double-count de `CampaignTrackingEvent` legado (que no deduplicaba, `TrackingController.cs:53,98`).

## 3. Retención / PII
- `to_address` es PII: retención por política del tenant; purga tras N días de terminal. No se guarda el cuerpo.
- `raw_payload` del webhook puede contener PII: mismo régimen de retención.
- Suppression list se conserva (obligación anti-spam) aunque se purguen dispatches.

## 4. Evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado guardaba costo/moneda en el log de email (mezcla de concerns) | `Smtp2GoService.cs:322-324` | VERIFIED | 90% |
| Legado: ApiKey en claro en tabla | `SmtpProviderConfig.cs:7` | VERIFIED | 98% |
| Legado `EmailSendLog` guardaba tracking URLs con cid/rid en claro | `Smtp2GoService.cs:317-319` | VERIFIED | 92% |
| Modelo nuevo por-dispatch + suppression + credencial cifrada | este diseño | NEW | n/a |
