# Campaigns — Data Model

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- **Persistencia:** PostgreSQL (EF Core), esquema propio `campaigns`. Multi-tenant fail-closed (query filter global por `TenantId` + repos tenant-scoped).

Cada aggregate tiene su tabla raíz con `TenantId` y `RowVersion` (concurrencia optimista). Sin FK cross-context: `CampaignId`, `WalletAccountId`, `AudienceRef`, `ScribeTemplateKey` son ids/strings opacos.

---

## 1. Tablas

### 1.1 `campaigns.campaign` (aggregate Campaign)

| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid | query filter global, índice |
| `name` | text | |
| `created_by_user_id` | uuid | |
| `channel` | smallint | Email/Sms/WhatsApp/Push/InApp |
| `channel_config` | jsonb | **tipado + versionado** (`schema_version`), no `Dictionary<string,string>` suelto |
| `channel_config_schema_version` | int | |
| `audience_kind` | smallint | Segment/ContactList/Manual |
| `audience_ref` | text null | id opaco Customer (Segment/ContactList) |
| `manual_contacts` | jsonb null | solo modo Manual |
| `template_key` | text | Scribe key |
| `subject` | text null | email |
| `objective` | smallint | Engagement/Conversion/Transactional/Retention |
| `schedule_mode` | smallint | Immediate/Scheduled/Recurring |
| `recurrence_rule` | jsonb null | (owner lógico: Scheduler) |
| `status` | smallint | Draft/Ready/Scheduled/Archived |
| `created_at_utc` / `updated_at_utc` | timestamptz | |
| `row_version` | bytea/xmin | concurrencia optimista |

Índices: `(tenant_id, status)`, `(tenant_id, created_at_utc desc)`.

### 1.2 `campaigns.campaign_run` (aggregate CampaignRun, inmutable)

| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid | |
| `campaign_id` | uuid | id opaco (no FK a otro context, sí índice) |
| `occurrence_key` | text | idempotencia de creación (ver abajo) |
| `trigger_kind` | smallint | Manual/Scheduled/Recurring |
| `triggered_at_utc` | timestamptz | |
| `channel_snapshot` | jsonb | **congelado** al disparar |
| `audience_snapshot_ref` | text | referencia a la materialización |
| `template_snapshot` | jsonb | |
| `unit_price_minor` | bigint | USD cents por mensaje, **congelado** |
| `currency` | char(3) | "USD" |
| `recipient_count` | int | |
| `cost_estimate_minor` | bigint | `recipient_count × unit_price_minor` |
| `wallet_account_id` | uuid | opaco |
| `wallet_reservation_id` | uuid null | tras RESERVE (set-once) |
| `wallet_reserved_minor` | bigint null | |
| `cost_actual_minor` | bigint null | tras reconcile (set-once) |
| `counter_dispatched` … `counter_clicked` | int | RunCounters (denormalizado, ver §3) |
| `run_status` | smallint | Created…Completed/Rejected |
| `row_version` | bytea/xmin | |

**Unique constraint clave:** `UNIQUE (tenant_id, campaign_id, occurrence_key)` → dos entregas del mismo `RunDue`/trigger crean **un** run (corrige doble-scheduler, ADR-CAMP-000 #6).

Índices: `(tenant_id, campaign_id, triggered_at_utc desc)`, `(tenant_id, run_status)` (para el sweeper de reconciliación).

### 1.3 `campaigns.campaign_recipient` (entidad, hijo de CampaignRun)

| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid | |
| `run_id` | uuid FK → campaign_run(id) | **misma frontera de context** (sí FK intra-context) |
| `contact_ref` | text | id opaco Customer |
| `email` | text null | destino resuelto (según canal) |
| `phone_e164` | text null | |
| `push_token_ref` | text null | referencia, no token crudo |
| `dispatch_state` | smallint | Pending/Dispatched/Delivered/Failed/Suppressed/Bounced |
| `attempt_no` | int | |
| `dispatch_idempotency_key` | text | `f(run_id,recipient_id,attempt_no)` |
| `provider_message_id` | text null | opaco del ejecutor |
| `failure_code` | text null | |
| `delivered_at_utc` | timestamptz null | set-once |
| `first_open_at_utc` | timestamptz null | set-once |
| `first_click_at_utc` | timestamptz null | set-once |
| `open_count` / `click_count` | int | dedupe por provider_event_id |
| `row_version` | bytea/xmin | |

**Unique constraint clave:** `UNIQUE (run_id, dispatch_idempotency_key)` → un dispatch por (recipient, attempt). Índice `(run_id, dispatch_state)` para el cierre por conteo.

### 1.4 `campaigns.processed_business_message` (idempotencia de efecto de negocio)

Copia local del patrón `ProcessedBusinessMessage` (`Growth/.../Idempotency/ProcessedBusinessMessage.cs:9-23`): `operation`, `scope_id`, `idempotency_key`, `request_fingerprint` (SHA-256 hex), `status` (Processing/Completed/Failed), respuesta cacheada, `expires_at_utc`, `row_version`. Ver `Idempotency_Spec.md`.

`UNIQUE (tenant_id, operation, scope_id, idempotency_key)`.

### 1.5 `campaigns.tracking_event_dedupe` (opcional, o vía processed_business_message)

Dedupe de open/click/bounce por `(recipient_id, provider_event_id)`. Puede colapsarse dentro de `processed_business_message` con `operation='tracking'`.

---

## 2. Diagrama de relaciones

```
campaign (1) ──opaco── (N) campaign_run          [distintos aggregates; sin FK física]
                              │ FK (intra-context)
                              ▼
                      campaign_recipient (N)

campaign_run.wallet_account_id / wallet_reservation_id ──opaco──► Wallet (otro service)
campaign.audience_ref ──opaco──► Customer (otro service)
campaign.template_key ──opaco──► Scribe (otro service)
```

Solo hay FK física **dentro** del context (`campaign_recipient.run_id → campaign_run.id`). Todo cruce a otro bounded context es id opaco (mismo principio que Growth Codes↔Referrals, `../02_Context_Map.md`).

---

## 3. Contadores: denormalización controlada

`RunCounters` se guardan denormalizados en `campaign_run` (columnas `counter_*`) para lectura O(1) de dashboards, pero la **fuente de verdad** son las filas `campaign_recipient`. Cada transición de `dispatch_state` incrementa el contador correspondiente **una sola vez** (guard de estado: solo incrementa si la transición es válida y nueva). Un reconciliador puede recomputar `counter_*` desde los recipients como auto-corrección (batch), pero el camino normal es incremental idempotente. Corrige el doble-conteo del legado (`CampaignStatistics` actualizado sin dedupe).

---

## 4. Dinero

- Todo monto: `bigint` minor units (USD cents) + `char(3)` currency. **Nunca** `decimal` de dólares ni `float`. Corrige el legado (`Campaign.WalletAmountPaid` era `decimal?`, `Campaign.cs:55`).
- `unit_price_minor` se **congela** en el run; no se re-lee del catálogo al reconciliar.

---

## 5. Retención / PII

- `email`/`phone_e164`/`push_token_ref` en `campaign_recipient` son PII mínima por run. Política de retención: purga/anonimización tras N días de `run_status=Completed` (ver `Security.md`). Los contadores agregados sobreviven a la purga (no son PII).
- **Nunca** se persiste JWT de usuario (corrige `Campaign.BackgroundAuthToken`, `Campaign.cs:87`). Los tokens de tracking son HMAC opacos derivados, no almacenados en claro con PII.

---

## 6. Migraciones

- EF Core migrations en `TaxVision.Campaigns.Infrastructure`. Esquema `campaigns`.
- Query filter global `HasQueryFilter(e => e.TenantId == _tenant.Current)` en todas las entidades; escrituras cross-tenant solo vía `.IgnoreQueryFilters()` + tenant explícito auditado (guía `Guia_IgnoreQueryFilters`).

---

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado usa `Dictionary<string,string>` para config de canal | `Campaign.cs:39` | VERIFIED | 98% |
| Legado guarda dinero como `decimal?` de dólares | `Campaign.cs:55,70,81` | VERIFIED | 97% |
| Legado persiste JWT (`BackgroundAuthToken`) | `Campaign.cs:87`; usado en `CampaignSendService.cs:112-127` | VERIFIED | 97% |
| `ProcessedBusinessMessage` shape a copiar | `Growth/.../ProcessedBusinessMessage.cs:9-23` | VERIFIED | 97% |
| Recipients del legado cuelgan de Campaign | `CampaignRecipient.cs:8-9` | VERIFIED | 98% |
| Esquema propuesto (tablas/constraints) | diseño (este doc) | NEW | 85% |
