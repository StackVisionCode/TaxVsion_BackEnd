# TaxVision.Sms — Data Model

- **Servicio:** SMS (`TaxVision.Sms`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

BD propia del bounded context (sin FK cruzando contexts; referencias a Campaign/Recipient/Wallet por **IDs opacos**, ver `02_Context_Map.md`). EF Core + Npgsql, esquema `sms`. Multi-tenant fail-closed: `TenantEntity` + query filter global por `TenantId`; accesos administrativos usan `.IgnoreQueryFilters()` + tenant explícito (ver `Guia_IgnoreQueryFilters`). Dinero en `bigint` (USD minor units), nunca `numeric`/`decimal` de coma flotante (corrige `SmsSendLog.EstimatedCost decimal`, `SmsSendLog.cs:54`).

## 1. `sms.sms_dispatch`

| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid | query filter |
| `campaign_id` | uuid null | opaco; null en envío individual |
| `campaign_run_id` | uuid null | opaco |
| `recipient_key` | text | hash estable `(campaign_id, recipient_id, attempt)` o `(client_ref)` individual |
| `attempt` | int | ≥1 |
| `idempotency_key` | text | ≤200 |
| `to_phone_e164` | text | normalizado |
| `sender_id_ref` | text | referencia a un sender de la config |
| `message_class` | smallint | 0=Transactional,1=Marketing |
| `encoding` | smallint | 0=Gsm7,1=Ucs2 |
| `segments` | int | ≥1 |
| `cost_quote_cents` | bigint | USD minor units |
| `actual_cost_cents` | bigint null | del DLR/proveedor |
| `currency` | char(3) | siempre `USD` |
| `reservation_id` | uuid null | opaco (Wallet) |
| `status` | smallint | ver `State_Machines.md` |
| `provider` | smallint | proveedor efectivo |
| `provider_message_id` | text null | conciliación de webhooks |
| `failure_code` | text null | |
| `created_at_utc` / `accepted_at_utc` / `delivered_at_utc` / `failed_at_utc` | timestamptz | |
| `row_version` | bytea/xmin | optimistic concurrency |

**Índices/constraints:**
- `UNIQUE (tenant_id, campaign_id, recipient_key, attempt)` — idempotencia por destinatario (el corazón del fix del anti-patrón 3).
- `UNIQUE (tenant_id, idempotency_key)` — envíos individuales.
- `INDEX (tenant_id, provider, provider_message_id)` — resolución de webhook DLR.
- `INDEX (tenant_id, campaign_run_id, status)` — agregación de stats por run.

## 2. `sms.opt_in_registry`

| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid | |
| `phone_e164` | text | |
| `opt_in_state` | smallint | Pending/Subscribed/StoppedByUser/Unsubscribed/Blocked |
| `accepts_marketing` | bool | |
| `accepts_transactional` | bool | |
| `opt_in_at_utc` / `opt_out_at_utc` | timestamptz null | |
| `consent_source` | text | webhook-stop / api / import-prior-consent / double-optin |
| `consent_proof_ref` | text null | referencia a evidencia (auditoría) |
| `language` | text | `en`/`es` |
| `row_version` | bytea/xmin | |

`UNIQUE (tenant_id, phone_e164)`. Consolida `SmsCell` (`SmsCell.cs`) sin arrastrar sus 20+ columnas de negocio (label/tags/employees) que pertenecen a Customer, no a SMS.

## 3. `sms.provider_config`

| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid | `UNIQUE (tenant_id, provider)` |
| `provider` | smallint | Twilio/Sns/… |
| `encrypted_credentials` | bytea | **cifrado** (envelope/DPAPI/KMS — ver `Security.md`); nunca plaintext (corrige `SmsProviderCredential.cs:20,25`) |
| `encrypted_webhook_secret` | bytea | HMAC de verificación de webhooks |
| `key_version` | int | rotación de clave |
| `is_active` | bool | |
| `default_message_class` | smallint | |
| `default_sender_id_ref` | text | |
| `created_at_utc` / `updated_at_utc` | timestamptz | |

## 4. `sms.provider_sender_id`

| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid | |
| `provider_config_id` | uuid | FK **dentro** del context |
| `sender_type` | smallint | LongCode/TollFree/ShortCode/Alphanumeric |
| `value` | text | DID/short code/alfanumérico |
| `country` | char(2) | capacidad por país |
| `is_default` | bool | |

## 5. `sms.processed_business_message`
Copia local del patrón `ProcessedBusinessMessage` (`Growth/.../Idempotency/ProcessedBusinessMessage.cs:9-124`): `operation`, `scope_id`, `idempotency_key`, `request_fingerprint` (SHA-256 hex 64), `status`, `response_*`, `failure_code`, `created_at_utc`, `expires_at_utc`, `row_version`. `UNIQUE (tenant_id, operation, scope_id, idempotency_key)`. Ver `Idempotency_Spec.md`.

## 6. Wolverine durable messaging
Tablas de outbox/inbox de Wolverine en esquema propio (`sms_wolverine`) para at-least-once durable. No se comparten con otros servicios.

## 7. Qué NO se guarda (deltas vs legado)
- **No** se persiste JWT ni token de usuario (el legado guardaba `Campaign.BackgroundAuthToken`; `SmsProviderCredential.UserApiToken` en claro). M2M client-credentials en su lugar.
- **No** se copian contactos/segmentos (Customer es la fuente).
- **No** `decimal` para dinero.
- **No** `Dictionary<string,string>` sin esquema para config de canal (el legado usaba `ChannelConfiguration.GetValueOrDefault`, `SmsCampaignSender.cs:86-87`); config tipada.

## 8. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado money en `decimal` | `SmsSendLog.cs:54` | VERIFIED | 98% |
| Legado secretos en claro | `SmsProviderCredential.cs:20,25` | VERIFIED | 98% |
| Legado config de canal como dict sin esquema | `SmsCampaignSender.cs:86-87` | VERIFIED | 96% |
| `ProcessedBusinessMessage` reusable como copia | `Growth/.../ProcessedBusinessMessage.cs:9-124` | VERIFIED | 97% |
| Esquema de tablas SMS propuesto | este documento | NEW | — |
