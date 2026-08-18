# Scheduler — Data Model

Servicio: **TaxVision.Campaigns.Scheduler**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**

Postgres. Si SCHED-001 = módulo de Campaigns → esquema propio `scheduler` en la misma BD de Campaigns (aísla las tablas manteniendo transacción local con el outbox). Si servicio propio → BD propia. Multi-tenant **fail-closed**: query filter global por `TenantId` + repos tenant-scoped; barridos de infraestructura (lease/reconciliación) usan `.IgnoreQueryFilters()` **con tenant explícito por fila**, nunca un `.Where` manual global (corrige el anti-patrón del legado, `../05_Master_ADR.md §Anti-patrones 9`).

Dinero: el Scheduler **no** maneja dinero (eso es Wallet). No hay columnas monetarias aquí.

## 1. `scheduler.schedule_entries`

| Columna | Tipo | Notas |
|---|---|---|
| `id` | `uuid` PK | |
| `tenant_id` | `uuid` NOT NULL | query filter global |
| `campaign_id` | `uuid` NOT NULL | referencia **opaca** (sin FK cross-context) |
| `kind` | `smallint` NOT NULL | `1=OneShot, 2=Recurring` |
| `status` | `smallint` NOT NULL | `1=Active,2=Paused,3=Completed,4=Cancelled` |
| `time_zone` | `text` NOT NULL | IANA (`America/New_York`) |
| `anchor_at_utc` | `timestamptz` NOT NULL | primer instante teórico |
| `end_at_utc` | `timestamptz` NULL | límite de fin (recurrente) |
| `max_occurrences` | `int` NULL | límite por conteo |
| `occurrence_count` | `int` NOT NULL default 0 | cuántas se han **materializado** |
| `next_due_at_utc` | `timestamptz` NULL | cache derivada del spec |
| `rec_frequency` | `smallint` NULL | `RecurrenceSpec` desnormalizado (VO) |
| `rec_interval` | `int` NULL | `> 0` (check) |
| `rec_days_of_week` | `smallint[]` NULL | 0..6 |
| `rec_day_of_month` | `smallint` NULL | 1..31 (check) |
| `rec_time_of_day` | `time` NULL | hora local en `time_zone` |
| `created_at_utc` / `updated_at_utc` | `timestamptz` | |
| `row_version` | `xid`/`bytea` | concurrencia optimista |

**Índices:** `(tenant_id, campaign_id)`; parcial `WHERE status = 1` (Active) sobre `next_due_at_utc` para el planificador.
**Checks:** `rec_interval > 0`; `rec_day_of_month BETWEEN 1 AND 31`; `kind=2 ⇒ rec_frequency IS NOT NULL`.

## 2. `scheduler.trigger_occurrences` (INMUTABLE una vez `Fired`)

Corazón del diseño. Una fila por instante debido; nunca se reescribe tras `Fired` (fix de *"recurrentes mutan una fila"*, `../05_Master_ADR.md §Anti-patrones 8`).

| Columna | Tipo | Notas |
|---|---|---|
| `id` | `uuid` PK | = `OccurrenceId` en `StartCampaignRun` |
| `tenant_id` | `uuid` NOT NULL | |
| `schedule_entry_id` | `uuid` NOT NULL | FK **intra**-context a `schedule_entries` |
| `campaign_id` | `uuid` NOT NULL | denormalizado (opaco) |
| `sequence_no` | `int` NOT NULL | 1..N dentro de la serie |
| `due_at_utc` | `timestamptz` NOT NULL | instante teórico |
| `status` | `smallint` NOT NULL | `1=Pending,2=Leased,3=Fired,4=Failed,5=Skipped` |
| `lease_owner` | `text` NULL | id de instancia/worker |
| `lease_until_utc` | `timestamptz` NULL | TTL del lease (reconciliación) |
| `attempt` | `int` NOT NULL default 0 | reintentos por crash |
| `fired_at_utc` | `timestamptz` NULL | disparo real (≠ `due_at_utc`) |
| `campaign_run_id` | `uuid` NULL | rellenado por `CampaignRunStarted` |
| `fail_reason` | `text` NULL | |
| `row_version` | `xid`/`bytea` | claim atómico del lease |
| `created_at_utc` | `timestamptz` | |

**Índices / constraints clave:**
- **`UNIQUE (schedule_entry_id, sequence_no)`** — impide materializar dos veces la misma posición de la serie (idempotencia de `MaterializeNext`).
- Parcial para el dequeue: `WHERE status = 1` (Pending) sobre `(due_at_utc)` — soporta `SELECT … FOR UPDATE SKIP LOCKED`.
- Parcial para reconciliación: `WHERE status = 2` (Leased) sobre `(lease_until_utc)`.
- `UNIQUE (id)` es también la clave de idempotencia que Campaigns usa para el `CampaignRun` — la inmutabilidad de la fila garantiza estabilidad.

## 3. Idempotencia / dedupe (business-inbox)

Se reutiliza el patrón `ProcessedBusinessMessage` (copia por contexto, no tipo compartido; origen `ProcessedBusinessMessage.cs:9`):

`scheduler.processed_business_messages`: `(tenant_id, operation, scope_id, idempotency_key)` UNIQUE, `request_fingerprint` (SHA-256), `status`, `response_json`, `expires_at_utc`, `row_version`. Usado por `Schedule` (dedupe de la API) y por handlers de comandos internos.

## 4. Wolverine (outbox/inbox durable)

Tablas de Wolverine (`wolverine_outgoing_envelopes`, `wolverine_incoming_envelopes`, `wolverine_dead_letters`) en el mismo esquema/BD para que la publicación de `StartCampaignRun` sea transaccional con la marca `Fired`. **No** confiar en la inbox de Wolverine para dedupe de negocio: eso lo hace `trigger_occurrences.id` + `processed_business_messages` (la inbox solo deduplica el envelope de transporte — ver nota en `ProcessedBusinessMessage.cs:5-7`).

## 5. Retención

`trigger_occurrences` en estado terminal (`Fired/Failed/Skipped`) se archivan tras una ventana (ej. 90 días) a una tabla fría o se purgan — pero **después** de que Campaigns confirmó el run, para no perder la clave de idempotencia mientras el run está vivo. `schedule_entries` `Completed/Cancelled` se retienen para auditoría.

## 6. Evidencia

| Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Legado sin entidad de run (fila única mutada) | `RecurrenceRule.cs:8-27`; `CampaignSchedulerService.cs:130-149` | VERIFIED | 97% |
| Legado tenant por `.Where` manual sin filtro global | `../05_Master_ADR.md §Anti-patrones 9`; `CampaignSchedulerService.cs:56-74` | VERIFIED | 90% |
| `ProcessedBusinessMessage` shape reutilizable | `ProcessedBusinessMessage.cs:9-23` | VERIFIED | 97% |
| Esquema `scheduler.*` propuesto | este documento | NEW | — |
