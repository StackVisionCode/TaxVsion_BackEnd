# Scheduler — Domain Design

Servicio: **TaxVision.Campaigns.Scheduler**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**

## 1. Responsabilidad (una sola)

El Scheduler es el **owner del reloj**: decide **CUÁNDO** una campaña debe ejecutarse y emite exactamente **un** disparo por ocurrencia debida. No resuelve audiencia, no reserva saldo, no entrega, no conoce canales. Su único efecto de negocio es publicar el comando/evento `StartCampaignRun` hacia Campaigns (ver `Commands_And_Events.md`).

Frontera con Campaigns (ver `../02_Context_Map.md`): `Campaigns → Scheduler` = "agendá este envío"; `Scheduler → Campaigns` = "arrancá el run #N ahora". El Scheduler **no** crea el `CampaignRun` (esa entidad inmutable la posee Campaigns); solo lo **dispara**. Esto mantiene la invariante del anchor: *"Campaigns orquesta; NO entrega"* y el Scheduler *"dispara; NO orquesta el contenido"*.

## 2. Modos de disparo (heredados del legado, corregidos)

| Modo | Semántica | Legado | Diseño nuevo |
|---|---|---|---|
| **Immediate** | disparar ahora, una vez | `SendOption.Immediately` (`SendOption.cs:4`) marcaba `Status=Sending` en el handler HTTP | ocurrencia única con `DueAtUtc = now`, encolada por la misma vía de lease (no un camino especial) |
| **Scheduled** | disparar en `ScheduledAt`, una vez | `SendOption.Scheduled` + `Campaign.ScheduledAt` | `ScheduleEntry(Kind=OneShot, DueAtUtc)` → una `TriggerOccurrence` |
| **Recurring** | serie temporal (Daily/Weekly/Monthly/Yearly) | `SendOption.Recurring` + `RecurrenceRule` (`RecurrenceRule.cs`) mutada en sitio | `ScheduleEntry(Kind=Recurring, RecurrenceSpec)` → N `TriggerOccurrence` inmutables, una por instante |

## 3. Aggregates y entidades

### 3.1 `ScheduleEntry` (aggregate root) — la *definición* del reloj
Referencia opaca a la campaña (`CampaignId`, sin FK cross-context — regla del anchor). Posee:
- `Kind`: `OneShot | Recurring`.
- `TimeZone` (IANA, ej. `America/New_York`) — **obligatorio** para cálculo correcto (el legado calculaba todo en `DateTime.UtcNow` naïve, `RecurrenceCalculator.cs:42`, sin TZ → drift y errores de DST).
- `RecurrenceSpec` (VO, solo si `Recurring`, ver §4).
- `AnchorAtUtc` (primer instante), `EndAtUtc?`, `MaxOccurrences?`.
- `OccurrenceCount` (cuántas se han **materializado**, no mutable ad-hoc: se incrementa solo al crear la próxima `TriggerOccurrence`).
- `Status`: `Active | Paused | Completed | Cancelled` (ver `State_Machines.md`).
- `NextDueAtUtc` (cache derivada del spec; recalculada de forma pura, nunca fuente de verdad para "ya disparé").

**Invariante clave:** una `ScheduleEntry` recurrente **NO** guarda estado de ejecución dentro de sí misma. El legado violaba esto: `RecurrenceRule.ExecutionCount++`, `NextExecutionAt=…`, `Campaign.SentAt=null` reseteados en la misma fila (`CampaignSchedulerBackgroundService.cs:115-126`, `CampaignSchedulerService.cs:130-149`) → sin historia, sin auditoría, y con dos schedulers pisándose la fila.

### 3.2 `TriggerOccurrence` (entidad hija, **INMUTABLE por instante**) — la unidad de lease
Una fila por instante debido. Es la corrección directa del anti-patrón *"sin entidad de run / recurrentes mutan una fila"* (`../05_Master_ADR.md §Anti-patrones 8`) llevada al plano del disparo.

Campos: `Id`, `ScheduleEntryId`, `TenantId`, `CampaignId`, `SequenceNo` (1..N dentro de la serie), `DueAtUtc`, `Status` (`Pending | Leased | Fired | Failed | Skipped`), `LeaseOwner?`, `LeaseUntilUtc?`, `Attempt`, `FiredAtUtc?`, `RowVersion`. Una vez `Fired`, **no** se reescribe: la próxima ocurrencia es una **fila nueva**. Esto da: idempotencia natural por ocurrencia, historia completa, y una clave estable para `StartCampaignRun` (ver `Idempotency_Spec.md`).

## 4. `RecurrenceSpec` (Value Object)

Reemplaza a `RecurrenceRule` (entidad mutable) por un VO puro e inmutable. Superficie idéntica a la del legado para paridad funcional, pero validada y sin estado de ejecución:

| Campo | Tipo | Notas vs legado |
|---|---|---|
| `Frequency` | `Daily/Weekly/Monthly/Yearly` | = `RecurrenceType.cs` |
| `Interval` | `int > 0` | legado no validaba `>0` |
| `DaysOfWeek` | `set<DayOfWeek>` | legado `List<int>?` sin esquema (`RecurrenceRule.cs:17`) |
| `DayOfMonth` | `int? 1..31` | clamp a fin de mes (legado ya lo hacía, `RecurrenceCalculator.cs:77`) |
| `TimeOfDay` | `TimeOnly` | legado `string?` parseado en runtime (`RecurrenceCalculator.cs:23`) |
| `EndAtUtc` / `MaxOccurrences` | límites de fin | idénticos |

El cálculo del próximo instante es una **función pura** `Next(spec, tz, afterUtc) -> DateTimeOffset?` (ver `Concurrency_Spec.md §clock`), determinista y testeable, con `IClock` inyectado. El legado tenía **dos** algoritmos divergentes (`RecurrenceCalculator.CalculateNextExecution` vs `CampaignSchedulerService.CalculateNextExecution:158-169`) que producían fechas distintas para la misma regla — un bug estructural que este VO único elimina.

## 5. Mutaciones vía métodos del aggregate (Result)

Coherente con CLAUDE.md (*"mutaciones por métodos del aggregate devolviendo Result"*):
`ScheduleEntry.Schedule(...)`, `.Pause()`, `.Resume()`, `.Cancel()`, `.MaterializeNext(clock) -> Result<TriggerOccurrence>`, `.MarkCompleted()`. `TriggerOccurrence.Lease(owner, ttl, clock)`, `.MarkFired(runRef)`, `.MarkFailed(reason)`, `.ReleaseExpiredLease()`. Ningún setter público.

## 6. Evidencia

| Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Modos Immediate/Scheduled/Recurring existen en legado | `CRMTAXPROBACKEND/CampaignService/Domains/Enums/SendOption.cs:2-6` | VERIFIED | 99% |
| Recurrencia Daily/Weekly/Monthly/Yearly | `Domains/Enums/RecurrenceType.cs:3-9` | VERIFIED | 99% |
| Regla de recurrencia es entidad mutable con estado de ejecución | `Domains/Entities/RecurrenceRule.cs:8-27` (`ExecutionCount`,`NextExecutionAt`,`LastExecutedAt`) | VERIFIED | 98% |
| Dos algoritmos de "próxima ejecución" divergentes | `Infrastructure/Services/RecurrenceCalculator.cs:9-83` vs `CampaignSchedulerService.cs:158-169` | VERIFIED | 96% |
| Cálculo sin timezone (UTC naïve) | `RecurrenceCalculator.cs:42`, `:25` (`nextDate.Date.Add`) | VERIFIED | 94% |
| Scheduler nuevo no existe | Glob `src/Services/**/Scheduler*` → 0 en TaxVsion_BackEnd | VERIFIED | 97% |
| `TriggerOccurrence`/`ScheduleEntry` como diseño | este documento | NEW | — |
| Recomendación módulo-vs-servicio | `ADR.md` (SCHED-001) | NEW | — |

## 7. Blockers

- **B-SCHED-1 (dep. dura):** `StartCampaignRun` requiere que Campaigns exponga el contrato de arranque de run y su `CampaignRun` inmutable. Sin Campaigns el Scheduler no tiene destino (ver `../07_MVP_Scope.md`).
- **B-SCHED-2:** decisión módulo-vs-servicio propio se resuelve en `ADR.md` (SCHED-001) antes de fijar el modelo de despliegue/DB.
