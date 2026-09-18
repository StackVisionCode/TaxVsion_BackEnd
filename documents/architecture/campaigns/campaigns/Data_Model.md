# Campaigns — Data Model

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-09-16 (revisión: **sin columnas de dinero/saldo/costo**)
- **Estado:** DISEÑO — no implementado
- **Persistencia:** PostgreSQL (EF Core), esquema propio `campaigns`. Multi-tenant fail-closed (query filter global por `TenantId` + repos tenant-scoped).

> **Regla dura:** el modelo de datos de Campaign **no tiene ninguna columna monetaria** (ni precio, ni costo, ni saldo, ni id de reserva/wallet, ni currency). Campaign guarda **definición, ejecución y contadores de entrega**, nada más. Cualquier medición de dinero la lleva otro servicio con su propio esquema.

Cada aggregate tiene su tabla raíz con `TenantId` y `RowVersion` (concurrencia optimista). Sin FK cross-context: `CampaignId`, `AudienceRef`, `TemplateRef`, `SenderRef` son ids/strings opacos.

---

## 1. Tablas

### 1.1 `campaigns.campaign` (aggregate Campaign)

| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid | query filter global, índice |
| `name` | text | |
| `created_by_user_id` | uuid | |
| `channels` | smallint[] / jsonb | uno o varios: Email/Sms/WhatsApp/Push/InApp |
| `channel_config` | jsonb | **tipado + versionado** (`schema_version`), no `Dictionary<string,string>` suelto |
| `channel_config_schema_version` | int | |
| `sender_selection` | jsonb | `SenderRef` opaco **por canal** (from/dominio, sender id/número, WABA, app push); Campaign solo lo referencia |
| `audience_kind` | smallint | Clients(Customer) / ContactList / Manual (combinables) |
| `audience_ref` | jsonb | ids opacos (segment/list) + contactos manuales explícitos |
| `template_ref` | jsonb | key(s) Scribe / contenido por canal |
| `subject` | text null | email |
| `send_mode` | smallint | Immediate / Scheduled / Recurring |
| `recurrence_rule` | jsonb null | (owner lógico del reloj: Scheduler) |
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
| `triggered_by` | text null | userId (Manual) o "scheduler" |
| `triggered_at_utc` | timestamptz | |
| `channel_snapshot` | jsonb | **congelado** al disparar |
| `sender_snapshot` | jsonb | `SenderRef` por canal congelado |
| `audience_snapshot_ref` | text | referencia a la materialización |
| `template_snapshot` | jsonb | |
| `recipient_count` | int null | **total congelado de UNIDADES** (destinatario/canal); se fija al terminar la materialización |
| `materialization_complete` | bool | audiencia totalmente materializada (habilita cierre) |
| `emission_complete` | bool | fan-out totalmente emitido (habilita cierre) |
| `audience_cursor` | jsonb null | checkpoint durable de materialización paginada (fix #12) |
| `emit_cursor` | jsonb null | checkpoint durable del fan-out por lotes (fix #12) |
| `counter_dispatched` / `_accepted` / `_delivered` / `_failed` / `_skipped` / `_unknown` | int | RunCounters — **caché** (fuente de verdad = unidades; ver §3) |
| `run_status` | smallint | Created / Materializing / Dispatching / Cancelling / Completed / PartiallyFailed / Failed / Cancelled / Rejected |
| `rejection_reason` | text null | p.ej. `gate_module_disabled` |
| `finished_at_utc` | timestamptz null | set-once al cerrar |
| `row_version` | bytea/xmin | |

Estadísticas **por canal**: derivadas de las unidades (`GROUP BY channel, dispatch_state`), no columnas propias del run; si se materializan como read-model, es una proyección aparte (`Observability.md`), no la fuente de verdad.

**Sin columnas de dinero:** no hay `unit_price`, `cost_estimate/actual`, `currency`, `wallet_account_id`, `wallet_reservation_id`. El run se cierra por **conteo de unidades** contra `recipient_count`, no por liquidación monetaria.

**Unique constraint clave:** `UNIQUE (tenant_id, campaign_id, occurrence_key)` → dos entregas del mismo `RunDue`/trigger crean **un** run (corrige doble-scheduler, ADR-CAMP-000 #6).

Índices: `(tenant_id, campaign_id, triggered_at_utc desc)`, `(tenant_id, run_status)` (para el sweeper de cierre).

### 1.3 `campaigns.campaign_recipient` (UNIDAD destinatario/canal, hijo de CampaignRun)

Una fila por **unidad** `(run, contactRef, channel)` (fix #01/#09). Guarda el estado y el **outcome final** de la unidad; los intentos individuales viven en §1.3b.

| Columna | Tipo | Notas |
|---|---|---|
| `id` (recipient_id) | uuid PK | **estable por unidad**; referenciado por los intentos |
| `tenant_id` | uuid | |
| `run_id` | uuid FK → campaign_run(id) | FK intra-context |
| `contact_ref` | text | id **estable** (Customer, Contact propio, o **id generado por entrada manual** — nunca el literal `"manual"`, fix #09) |
| `channel` | smallint | canal de esta unidad |
| `email` | text null | destino resuelto (según canal) |
| `phone_e164` | text null | |
| `push_token_ref` | text null | referencia, no token crudo |
| `dispatch_state` | smallint | Pending / Dispatched / Accepted / Delivered / Failed / Skipped / Unknown |
| `eligible_at_utc` | timestamptz null | quiet-hours: la unidad no se despacha antes de este instante (diferida, no Skipped; `Domain_Design.md §7.5`) |
| `skip_reason` | text null | opt_out / no_destination / sender_unverified / frequency_cap / channel_pref (ver `Domain_Design.md §7.5`) |
| `current_attempt_no` | int | intento vigente (0 si aún Pending) |
| `dispatch_deadline_utc` | timestamptz null | vencimiento del intento vigente → sweeper a `Unknown` (fix #04/#22) |
| `provider_ref` | text null | opaco del ejecutor (último intento) |
| `failure_reason` | text null | |
| `accepted_at_utc` / `delivered_at_utc` | timestamptz null | set-once |
| `settled_at_utc` | timestamptz null | cuando entró a un estado que cuenta para cierre |
| `row_version` | bytea/xmin | |

**Unique constraint clave:** `UNIQUE (run_id, contact_ref, channel)` → una unidad por destinatario/canal. Índice `(run_id, dispatch_state)` para el cierre por conteo y `(run_id, dispatch_state, dispatch_deadline_utc)` para el sweeper.

### 1.3b `campaigns.campaign_dispatch_attempt` (un intento; fix #09/#22)

| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid | |
| `run_id` | uuid | |
| `recipient_id` | uuid FK → campaign_recipient(id) | la unidad |
| `attempt_no` | int | 1,2,3… (reintento legítimo = nuevo intento) |
| `dispatch_id` | text | `f(run_id, recipient_id, channel, attempt_no)` |
| `outcome` | smallint null | Accepted/Delivered/Failed/Unknown del intento |
| `provider_ref` | text null | |
| `emitted_at_utc` / `resolved_at_utc` | timestamptz null | |

**Unique constraint clave:** `UNIQUE (run_id, dispatch_id)` y `UNIQUE (run_id, recipient_id, attempt_no)` → un intento por (unidad, attempt); reentrega del bus del mismo intento = no-op; nuevo intento = fila nueva (fix #09). El result se correlaciona por `dispatch_id` del **intento**, no de la unidad, para no pisar un intento posterior.

> **Diferido (post-fase actual):** tracking de engagement (`first_open_at_utc`, `open_count`, estados `Suppressed`/`Bounced`) — el modelo actual rastrea **entrega/aceptación**, no engagement (ver `State_Machines.md`).

### 1.4 `campaigns.contact` / `campaigns.contact_list` (sub-dominio propio)

Directorio de contactos **no-cliente** importable (CSV) con opt-out/consentimiento, y listas. Tablas raíz con `tenant_id` + `row_version`; `contact` con `email`/`phone_e164`/`opt_out_at_utc`; `contact_list_member` (N:M contact↔list). Ver `Domain_Design.md §Contactos`. Sin campos monetarios.

### 1.5 `campaigns.sender_profile` (catálogo de remitentes)

`SenderProfile` por tenant/canal: `channel`, `sender_ref` (from/dominio, número, WABA id, app push), `display_name`, `verified_at_utc`. Campaign **referencia** el `sender_ref`; el ejecutor lo resuelve contra su config/secretos. Sin campos monetarios.

### 1.5b `campaigns.contact_send_ledger` (frequency cap entre campañas, §7.5.1)

Contador atómico por contacto/canal/ventana para el `MaxSendsPerContactPerWindow` (configurable por tenant). Se **incrementa al emitir el dispatch** (no solo al materializar), de modo que dos campañas concurrentes no superen el cap.

| Columna | Tipo | Notas |
|---|---|---|
| `tenant_id` | uuid | |
| `contact_identity` | text | destino normalizado (mismo criterio que el dedupe, §3b) |
| `channel` | smallint | |
| `window_start_utc` | timestamptz | inicio de la ventana (rolling o calendario, config del tenant) |
| `sent_count` | int | incrementado atómicamente; comparado contra el cap |
| `row_version` | bytea/xmin | |

`UNIQUE (tenant_id, contact_identity, channel, window_start_utc)`. El chequeo es `UPDATE ... SET sent_count=sent_count+1 WHERE sent_count < @cap` (o `INSERT ... ON CONFLICT`): `rowcount=0` ⇒ cap alcanzado ⇒ unidad `Skipped(frequency_cap)`. Config del cap/ventana vive en el tenant (no en el frontend). Retención: purgar ventanas vencidas.

### 1.6 `campaigns.processed_business_message` (idempotencia de efecto de negocio)

Copia local del patrón `ProcessedBusinessMessage` (`Growth/.../Idempotency/ProcessedBusinessMessage.cs:9-23`): `operation`, `scope_id`, `idempotency_key`, `request_fingerprint` (SHA-256 hex), `status` (Processing/Completed/Failed), respuesta cacheada, `expires_at_utc`, `row_version`. Ver `Idempotency_Spec.md`.

`UNIQUE (tenant_id, operation, scope_id, idempotency_key)`.

---

## 2. Diagrama de relaciones

```
campaign (1) ──opaco── (N) campaign_run          [distintos aggregates; sin FK física]
                              │ FK (intra-context)
                              ▼
                   campaign_recipient (N)  [una UNIDAD por (contacto, canal)]
                              │ FK (intra-context)
                              ▼
                campaign_dispatch_attempt (N)  [un intento por (unidad, attempt_no)]

contact (N) ──(N:M)── contact_list               [sub-dominio propio de Campaign]
sender_profile (N)                               [catálogo propio de Campaign]

campaign.audience_ref  ──opaco──► Customer (otro service)
campaign.template_ref  ──opaco──► Scribe   (otro service)
campaign.sender_selection ──opaco──► resuelto por cada ejecutor (otro service)
```

Solo hay FK física **dentro** del context. Todo cruce a otro bounded context es id opaco (mismo principio que Growth Codes↔Referrals, `../02_Context_Map.md`). **No hay ninguna referencia a un contexto de dinero/wallet.**

---

## 3. Contadores: caché + rollup (estrategia canónica, fix #07)

`RunCounters` (`counter_dispatched/accepted/delivered/failed/skipped/unknown`) son **caché** en `campaign_run` para lectura O(1); la **fuente de verdad** son las unidades `campaign_recipient`. El result muta **solo** la fila de la unidad; un **rollup** (batch / al evaluar cierre) recomputa `counter_*` desde las unidades. **El cierre no consulta la caché**, sino un `COUNT` autoritativo por estado. Esto evita la fila caliente del run bajo fan-out grande y coincide con `Concurrency_Spec.md §3` (antes ambos docs se contradecían). Corrige el doble-conteo del legado (`CampaignStatistics` sin dedupe).

## 3b. Identidad de destino y dedupe de la unión (fix #14 — política por defecto, confirmar)

La audiencia une **Clients (Customer) + Listas propias + Manual**; una misma persona puede aparecer en varias fuentes. Política por defecto (documentada aquí, sujeta a confirmación de negocio/legal):

- **Identidad normalizada por `(tenant, channel, destino_normalizado)`** (email en minúsculas/trim; teléfono en E.164). La materialización crea **una sola unidad** por identidad+canal aunque el destino llegue por varias fuentes.
- **La supresión (opt-out) prevalece** sobre cualquier membresía activa: si el destino tiene opt-out en ese canal → unidad `Skipped(opt_out)`, no se envía.
- **No** se fusionan personas por coincidencias que puedan ser legítimas (misma dirección, distinta persona) más allá de la identidad de destino por canal.
- La unicidad `UNIQUE(run_id, contact_ref, channel)` no basta por sí sola para el dedupe de la unión: la materialización resuelve la identidad **antes** de asignar `contact_ref`.

---

## 4. Retención / PII

- `email`/`phone_e164`/`push_token_ref` en `campaign_recipient` (y `contact`) son PII mínima. Política de retención: purga/anonimización tras N días de `run_status` terminal (ver `Security.md`). Los contadores agregados sobreviven a la purga (no son PII).
- **Nunca** se persiste JWT de usuario (corrige `Campaign.BackgroundAuthToken`, `Campaign.cs:87`).

---

## 5. Migraciones

- EF Core migrations en `TaxVision.Campaigns.Infrastructure`. Esquema `campaigns`.
- Query filter global `HasQueryFilter(e => e.TenantId == _tenant.Current)` en todas las entidades; escrituras cross-tenant solo vía `.IgnoreQueryFilters()` + tenant explícito auditado (guía `Guia_IgnoreQueryFilters`).

---

## 6. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado usa `Dictionary<string,string>` para config de canal | `Campaign.cs:39` | VERIFIED | 98% |
| Legado copia contactos/listas dentro de Campaign (stale) | `Campaign.cs:25-27` | VERIFIED | 94% |
| Legado persiste JWT (`BackgroundAuthToken`) | `Campaign.cs:87`; usado en `CampaignSendService.cs:112-127` | VERIFIED | 97% |
| `ProcessedBusinessMessage` shape a copiar | `Growth/.../ProcessedBusinessMessage.cs:9-23` | VERIFIED | 97% |
| Recipients del legado cuelgan de Campaign | `CampaignRecipient.cs:8-9` | VERIFIED | 98% |
| Esquema sin columnas de dinero (Campaign solo campaña) | ADR-CAMP-001 D1 (decisión del usuario) | DECISION | 99% |
| Esquema propuesto (tablas/constraints) | diseño (este doc) | NEW | 85% |
