# Scheduler — State Machines

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — Scheduler = disparo temporal con lease atómico (Immediate/Scheduled/Recurring), un `CampaignRun` inmutable por disparo. SIN dinero:** el Scheduler NO reserva/consume/verifica saldo. La regla "para scheduled/recurrente cobrar ANTES según cuántos destinatarios" la hace un **interceptor/PEP externo** colocado sobre la ruta del `RunDue` (antes de que Campaign ejecute); PEP + Wallet son **externos y DIFERIDOS**, no viven en el Scheduler ni en Campaign (ver `../05_Master_ADR.md` D1/D7). Lo que abajo asuma que el Scheduler toca saldo/Wallet queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

Servicio: **TaxVision.Campaigns.Scheduler**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**

Dos máquinas independientes: `ScheduleEntry` (la definición del reloj) y `TriggerOccurrence` (la unidad de disparo con lease). Separarlas es la corrección del legado, donde **una sola** entidad (`Campaign` + `RecurrenceRule`) mezclaba definición y ejecución en un `CampaignStatus` monolítico (`CampaignStatus.cs`), reciclado en cada recurrencia (`CampaignSchedulerService.cs:149` vuelve a `Scheduled`).

## 1. `ScheduleEntry`

```
                 Schedule()
   [*] ─────────────────────────► Active
                                   │  │  │
                    Pause()        │  │  └────────── Cancel() ──────► Cancelled [*]
              ┌───────────────◄────┘  │
              ▼                        │  MaterializeNext()==null
           Paused ── Resume() ──► Active│  (EndAt/MaxOccurrences alcanzado)
              │                        └──────────────────────────► Completed [*]
              └── Cancel() ──► Cancelled [*]
```

| Estado | Significado | Transiciones salientes |
|---|---|---|
| `Active` | el reloj corre; materializa ocurrencias | `Pause`, `Cancel`, → `Completed` (fin de serie), materializa `TriggerOccurrence` |
| `Paused` | reloj detenido, definición viva | `Resume`→`Active`, `Cancel` |
| `Completed` | serie agotada (`OneShot` disparado, o recurrente que alcanzó `EndAtUtc`/`MaxOccurrences`) | terminal |
| `Cancelled` | detenido por el tenant | terminal |

**Reglas duras:**
- `OneShot` pasa a `Completed` **cuando su única `TriggerOccurrence` llega a `Fired`**, no antes (el legado marcaba `Sending` en el handler HTTP antes de cualquier disparo real, `SendCampaignCommandHandler.cs:86`).
- `Pause` **no** cancela ocurrencias ya `Leased`/`Fired`; solo frena la materialización de nuevas. Una ocurrencia en vuelo termina su ciclo.
- `Completed`/`Cancelled` son **absorbentes**: guardas de estado en cada método (Result.Failure si se llama sobre terminal), nunca excepción silenciosa como el `catch` que traga errores en el legado (`CampaignSchedulerBackgroundService.cs:144-147`).

## 2. `TriggerOccurrence`

```
   MaterializeNext()
   [*] ───────────────► Pending
                           │
                Lease() (claim atómico, ver Concurrency_Spec) 
                           ▼
                        Leased ──────────────────────────────┐
                        │   │   │                             │
      lease expira      │   │   └── MarkFailed(reason) ──► Failed
   (reconciliación)     │   │                                 │  (Attempt<max → re-Pending)
   ReleaseExpiredLease()│   └── MarkFired(runRef) ──► Fired [*]│
                        ▼                                      ▼
                     Pending ◄──────────────────────────── Pending (re-lease)
   entry Paused/Cancelled antes del lease
                           └── Skip() ──► Skipped [*]
```

| Estado | Significado | Guarda / efecto |
|---|---|---|
| `Pending` | debido, aún no reclamado | elegible para `Lease` si `DueAtUtc <= now` |
| `Leased` | reclamado por un worker; TTL corriendo | solo el `LeaseOwner` puede `MarkFired`/`MarkFailed`; expiración → reconciliación |
| `Fired` | `StartCampaignRun` publicado en outbox (compromiso transaccional) | **terminal e inmutable**; clave de idempotencia consumida |
| `Failed` | fallo tras agotar `Attempt` máx | terminal; alerta |
| `Skipped` | no se disparó por política, con `SkipReason` | terminal |

**`SkipReason` (fix #29):** `paused_or_cancelled` (la entry se pausó/canceló antes del disparo) · `misfire` (había una ocurrencia más reciente que coalesció, `Concurrency_Spec.md §5`) · `stale` (vencida más allá de `MisfireGrace`) · `overlap` (el run anterior de la entry seguía activo, `§5.1`). Todo `Skipped` emite evento auditado.

**Invariantes (correcciones del legado):**
1. **Un disparo por ocurrencia.** El paso `Leased → Fired` publica `StartCampaignRun` y marca `Fired` en la **misma transacción** (outbox). Ninguna otra instancia puede reclamar una ocurrencia ya `Leased`/`Fired` (claim condicional por `RowVersion`+`LeaseUntilUtc`). Esto elimina el doble-disparo del legado, donde `Status=Sending` se seteaba y guardaba sin lock atómico y **dos** BackgroundServices distintos (`CampaignSchedulerBackgroundService` + `CampaignSchedulerService`) escaneaban la misma tabla (ver `Concurrency_Spec.md §doble-scheduler`).
2. **`Fired` es inmutable.** La recurrencia siguiente es una **fila nueva** (`SequenceNo+1`), no un reset de la actual. El legado hacía `campaign.SentAt=null; Status=Scheduled` sobre la misma fila (`CampaignSchedulerBackgroundService.cs:124-126`).
3. **Lease expirado ≠ disparado.** Si un worker muere entre `Lease` y `Fired`, `LeaseUntilUtc` vence y la reconciliación devuelve la ocurrencia a `Pending` (mismo `Id`, `Attempt++`). Como `StartCampaignRun` es idempotente por `OccurrenceId` (ver `Idempotency_Spec.md`), un re-disparo tras crash no duplica el run.
4. **No solapar (fix #29).** Antes de `Fired`, si la entry tiene un `ActiveRunRef` vivo (run anterior sin cerrar) → la ocurrencia va a `Skipped(overlap)` + alerta, no a `Fired`. El ref se limpia con `campaign.run.completed.v1` (o el `overlap_watchdog`). Ver `Concurrency_Spec.md §5.1`.
5. **Misfire = coalesce (fix #29).** Tras downtime, entre varias ocurrencias vencidas de una entry solo la **más reciente** dentro de `MisfireGrace` va a `Fired`; las demás → `Skipped(misfire)`; todas fuera de gracia → `Skipped(stale)`. Ver `Concurrency_Spec.md §5`.

## 3. Materialización de la próxima ocurrencia (recurrentes)

Al llegar `TriggerOccurrence` a `Fired`, un handler pide a la `ScheduleEntry` `MaterializeNext(clock)`:
- calcula `next = RecurrenceSpec.Next(spec, tz, lastDueAtUtc)` (función pura);
- si `next == null` (o supera `EndAtUtc`/`MaxOccurrences`) → `ScheduleEntry.MarkCompleted()`;
- si no → crea `TriggerOccurrence(Pending, SequenceNo+1, DueAtUtc=next)` e incrementa `OccurrenceCount`.

Materialización y `Fired` viven en la misma transacción del handler de `StartCampaignRun`-confirmado (o se derivan de un evento `CampaignRunStarted`), garantizando que la serie nunca se detiene por un crash intermedio: si la materialización no se comitea, la reconciliación la reintenta desde la última ocurrencia `Fired`. **Materializar una a la vez** (no pre-generar toda la serie) evita el problema del legado de reglas infinitas y mantiene el horizonte acotado.

## 4. Evidencia

| Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Estado monolítico reciclado por recurrencia | `CampaignSchedulerService.cs:88,149`; `CampaignSchedulerBackgroundService.cs:124-126` | VERIFIED | 96% |
| `Sending` seteado en handler HTTP antes del disparo | `SendCampaignCommandHandler.cs:86`; `UpdateCampaignCommandHandler.cs:88` | VERIFIED | 95% |
| Errores tragados en `catch` sin transición | `CampaignSchedulerBackgroundService.cs:144-147`; `CampaignSchedulerService.cs:104-110` | VERIFIED | 95% |
| Dos máquinas (`ScheduleEntry`/`TriggerOccurrence`) | este documento | NEW | — |
