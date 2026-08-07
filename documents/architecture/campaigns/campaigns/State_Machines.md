# Campaigns — State Machines

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Tres máquinas de estado independientes: **Campaign** (la definición), **CampaignRun** (una ejecución), **CampaignRecipient.DispatchState** (un destinatario). Cada transición es un método del aggregate que devuelve `Result` y está protegido por un state-guard (rechaza transiciones inválidas de forma idempotente). Coherente con `../06_Cross_Service_Transactional_Protocol.md`.

---

## 1. Campaign

Ciclo de vida de la **definición**. Contrasta con el legado, que aplanaba definición + ejecución en un solo `CampaignStatus` de 9 valores (`CampaignStatus.cs`), incluyendo `Sending`/`Sent`/`Paused` que en realidad describen una ejecución.

```
        create
          │
          ▼
      ┌────────┐  edit content/audience/schedule (permitido solo aquí)
      │ Draft  │◄─────────────┐
      └───┬────┘              │
   MarkReady │ (validación completa)
          ▼                   │
      ┌────────┐  edit ──────►│ (vuelve a Draft, invalida readiness)
      │ Ready  │
      └───┬────┘
   Schedule │ / TriggerNow
          ▼
      ┌───────────┐   (cada disparo NO cambia la Campaign; crea un CampaignRun)
      │ Scheduled │──────────► [Scheduler crea CampaignRun N] ─┐
      └───┬───────┘◄──────── recurrencia re-agenda la MISMA    │
          │                   Campaign (nuevo run, no reset)   │
   Archive │                                                   │
          ▼                                                    │
      ┌──────────┐                                             │
      │ Archived │ (soft; runs históricos permanecen)          │
      └──────────┘                                             ▼
```

Estados:

| Estado | Significado | Transiciones salientes |
|---|---|---|
| `Draft` | Editable, incompleta | `MarkReady`, `Archive` |
| `Ready` | Validada, no agendada | `Schedule`, `TriggerNow`, editar→`Draft`, `Archive` |
| `Scheduled` | Con ScheduleSpec activo (incl. recurrente) | `Unschedule`→`Ready`, `Archive` |
| `Archived` | Retirada (soft) | — (terminal) |

**Clave:** una Campaign `Scheduled` recurrente **permanece `Scheduled`** y genera **N CampaignRun**. No hay estado `Sending`/`Sent` en la Campaign — eso es del run. Corrige el reset destructivo del legado (`CampaignSchedulerBackgroundService.cs:124-135`, que sobreescribe `ScheduledAt`/`SentAt`/`Status` sobre la única fila).

---

## 2. CampaignRun

Ciclo de vida de **una ejecución**. Es donde vive la saga balance+dispatch (ver `Transactional_Protocol.md`). El run es inmutable en su snapshot; solo su estado y contadores mutan.

```
   StartCampaignRun (desde lease del Scheduler)
          │
          ▼
     ┌─────────┐  materializa audiencia (Customer) + congela precio
     │ Created │  estimación de costo = RecipientCount × UnitPriceMinor
     └────┬────┘
  gate check │ module.campaigns (Subscription) — falla-> Rejected
          ▼
     ┌──────────┐  Wallet RESERVE (movimiento inmutable, idempotente)
     │ Reserving│──── reserve falla / saldo insuficiente ──► Rejected
     └────┬─────┘
          ▼
     ┌──────────┐  fan-out: 1 evento dispatch por destinatario (idempotente)
     │Dispatching│──── (backpressure; outbox durable)
     └────┬─────┘
          │ todos los recipients en estado terminal (Delivered/Failed/Suppressed/Bounced)
          ▼
     ┌──────────┐  Wallet: CONSUME entregados + REFUND no-entregados
     │Reconciling│
     └────┬─────┘
          ▼
     ┌──────────┐
     │Completed │ (terminal)   ── liquidación cerrada, CostActual fijado
     └──────────┘

   Cancel/Fail en Reserving/Dispatching:
     Reserving   -> cancel  -> Rejected  (release reserva si existía)
     Dispatching -> Cancel  -> Cancelling -> Reconciling (consume ya entregados, refund resto)
```

Estados:

| Estado | Significado | Wallet | Salientes |
|---|---|---|---|
| `Created` | Snapshot congelado, audiencia materializada | — | `Reserving`, `Rejected` |
| `Reserving` | Solicitando RESERVE | reserve pendiente | `Dispatching`, `Rejected` |
| `Dispatching` | Fan-out en curso | reservado | `Reconciling`, `Cancelling` |
| `Cancelling` | Cancelación solicitada; drenando in-flight | reservado | `Reconciling` |
| `Reconciling` | Agregando results, liquidando | consume/refund | `Completed` |
| `Completed` | Liquidado (CostActual fijo) | liquidado | — terminal |
| `Rejected` | Nunca despachó (gate/reserve/saldo) | release si aplica | — terminal |

**Guards clave:**
- `Created → Reserving` solo si el gate `module.campaigns` está activo (ortogonal al balance).
- `Reserving → Dispatching` solo con `WalletReservationId` confirmado.
- `Dispatching → Reconciling` solo cuando `Dispatched == Delivered + Failed + Suppressed + Bounced` (todos los recipients terminales). Este cierre es **idempotente**: re-evaluarlo no re-liquida.
- `Reconciling → Completed` fija `CostActual` una vez (guard set-once).

---

## 3. CampaignRecipient.DispatchState

Ciclo de vida de **un destinatario dentro de un run**. Reemplaza el `RecipientStatus` legado de 9 valores (`RecipientStatus.cs`) que mezclaba estado de dispatch con estado de tracking (Opened/Clicked) en la misma máquina lineal.

```
     materialize
        │
        ▼
   ┌─────────┐  dispatch event emitido (idempotencyKey = f(RunId,RecipientId,AttemptNo))
   │ Pending │
   └────┬────┘
        ▼
   ┌────────────┐   result del ejecutor (correlacionado por idempotencyKey)
   │ Dispatched │
   └────┬───────┘
        ├─ delivered.succeeded  ─► Delivered  (set DeliveredAtUtc once)  → CONSUME 1
        ├─ delivery.failed      ─► Failed     (FailureCode)              → REFUND 1
        ├─ delivery.suppressed  ─► Suppressed (no se intentó)            → REFUND 1
        └─ delivery.bounced     ─► Bounced    (sobre un Delivered previo)→ (ya consumido)
```

Estados de **dispatch** (terminal = liquidable):

| Estado | Significado | Efecto Wallet |
|---|---|---|
| `Pending` | Materializado, aún no despachado | — |
| `Dispatched` | Evento emitido, esperando result | (reservado) |
| `Delivered` | Ejecutor confirmó entrega al MTA/proveedor | CONSUME 1 unidad |
| `Failed` | Falló tras reintentos del ejecutor | REFUND 1 unidad |
| `Suppressed` | Suppression list / no se intentó | REFUND 1 unidad |
| `Bounced` | Bounce posterior a Delivered (webhook) | sin cambio de saldo (ya consumido); marca calidad |

**Tracking (ortogonal al dispatch, no es máquina lineal):** `Open` y `Click` son *señales set-once* sobre un recipient ya `Delivered`. `FirstOpenAtUtc`/`FirstClickAtUtc` se fijan una vez; `OpenCount`/`ClickCount` incrementan con dedupe por `ProcessedBusinessMessage(operation, recipientId, providerEventId)`. Un webhook duplicado **no** avanza estado ni doble-cuenta (corrige anti-patrón legado #3, ADR-CAMP-000).

**Reintentos:** un reintento de dispatch incrementa `AttemptNo` → nueva `DispatchIdempotencyKey`. El ejecutor deduplica por su lado; Campaigns nunca crea un recipient nuevo por reintento (corrige el fan-out fire-and-forget legado que perdía trabajo al reiniciar, `CampaignSchedulerBackgroundService.cs:38` `Task.Delay` loop).

---

## 4. Acoplamiento entre máquinas

| Evento | Recipient | RunCounters | RunStatus |
|---|---|---|---|
| dispatch emitido | Pending→Dispatched | Dispatched++ | (Dispatching) |
| delivered | Dispatched→Delivered | Delivered++ | evalúa cierre |
| failed/suppressed | Dispatched→Failed/Suppressed | Failed/Suppressed++ | evalúa cierre |
| todos terminales | — | — | Dispatching→Reconciling |
| liquidación hecha | — | — | Reconciling→Completed |

El cierre del run se dispara por **conteo idempotente**, no por un "último callback" (que puede llegar duplicado o fuera de orden). Ver `Concurrency_Spec.md`.

---

## 5. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado aplana definición+ejecución en un `CampaignStatus` de 9 valores | `CampaignStatus.cs:4-12` | VERIFIED | 98% |
| Legado mezcla dispatch+tracking en `RecipientStatus` lineal | `RecipientStatus.cs:4-12` | VERIFIED | 97% |
| Legado resetea la misma fila en recurrencia | `CampaignSchedulerBackgroundService.cs:124-135` | VERIFIED | 96% |
| Result events con `CampaignId` opaco de vuelta ya existen (modelo del contrato) | `PostmasterEmailEvents.cs:91-172` | VERIFIED | 97% |
| Separación 3 máquinas (Campaign/Run/Recipient) | diseño ADR-CAMP-000 §Decisiones/#8 | DESIGN | 90% |
| Cierre de run por conteo idempotente | diseño (este doc §4) | NEW | 87% |
