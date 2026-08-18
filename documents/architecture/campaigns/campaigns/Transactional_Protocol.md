# Campaigns — Transactional Protocol

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

La vista **cross-service** completa (balance saga + dispatch saga) vive en `../06_Cross_Service_Transactional_Protocol.md`. Este doc es la **parte de Campaigns**: cómo orquesta el run sin mutar saldo ni entregar, con at-least-once + compensaciones.

Principio: Campaigns es un **orquestador de saga** (no un coordinador 2PC). Cada paso es una transacción local (muta un aggregate + escribe outbox en la misma tx) más un evento; las compensaciones son explícitas.

---

## 1. Por qué saga y no el patrón legado

El legado hacía **TOCTOU no-atómico**: `GetWalletBalance` (HTTP) → chequeo en memoria → `DebitForCampaign` (HTTP) → recién después `SaveChanges` (`CreateCampaignCommandHandler.cs:250,264,278,320`). Entre el check y el debit el saldo puede cambiar (carrera), el debit ocurre **antes** de persistir la campaña (si el save falla, se cobró sin campaña), y el "refund" dependía de un JWT persistido (`CampaignSendService.cs:112-127`). Además cobraba **al crear** (prepay del total estimado), no al entregar.

Este diseño: **reserve → dispatch → consume-entregados / refund-resto**, con movimientos inmutables en Wallet (solo Wallet muta saldo), idempotencia por `runId`, y sin JWT persistido (M2M).

---

## 2. Flujo feliz (saga)

```
[T1] StartCampaignRun
     tx local: crea campaign_run (Created), materializa recipients (Pending),
               congela unit_price_minor, cost_estimate = count × price.
               Guard: gate module.campaigns activo (Subscription). Si no -> Rejected.
     outbox: (ninguno aún)

[T2] ReserveRunFunds
     tx local: run Created -> Reserving.
     outbox: CampaignRunFundsReserveRequested{ runId, amountMinor=cost_estimate, key=(reserve,runId) }

     ── Wallet crea movimiento RESERVE inmutable (idempotente por (reserve,runId)) ──►
     wallet.reservation.confirmed{ runId, reservationId }

[T3] on ReservationConfirmed
     tx local: run Reserving -> Dispatching; guarda wallet_reservation_id (set-once).
     outbox: por cada recipient Pending -> CampaignRecipientDispatchRequested (fan-out)
             (recipient Pending -> Dispatched, dispatched++)

     ── ejecutor entrega y reporta ──►
     channel.dispatch_result{ dispatchIdempotencyKey, outcome }  (N eventos, at-least-once)

[T4] on DispatchResult (por destinatario, idempotente)
     tx local: recipient Dispatched -> Delivered|Failed|Suppressed; contador++ (una vez).
     Al escribir, evalúa cierre: ¿dispatched == delivered+failed+suppressed(+bounced)?

[T5] ReconcileRun (cuando todos terminales)
     tx local: run Dispatching -> Reconciling.
     outbox:
       CampaignRunFundsConsumeRequested{ reservationId, amountMinor=delivered×price, key=(consume,runId) }
       CampaignRunFundsRefundRequested { reservationId, amountMinor=reserved−consumed, key=(refund,runId) }

     ── Wallet aplica consume+refund inmutables (idempotentes) ──►
     wallet.settlement.applied{ runId }

[T6] on SettlementApplied
     tx local: run Reconciling -> Completed; cost_actual = delivered×price (set-once).
     outbox: campaigns.run.completed.v1 (read-model/analytics)
```

**Nota de cobro:** se cobra por **entregado**, no por estimado. La reserva congela fondos; el consume real es `delivered × unit_price`; el resto se devuelve. El legado cobraba el estimado al crear y hacía un refund parcial frágil por "destinatarios inválidos" (`CampaignSendService.cs:76-81`).

---

## 3. Compensaciones y fallos

| Fallo | Estado en el que ocurre | Compensación |
|---|---|---|
| Gate `module.campaigns` inactivo | `Created` | run → `Rejected`; ninguna reserva; evento `run.rejected` |
| Wallet rechaza (saldo insuficiente) | `Reserving` | run → `Rejected`; nada que liberar (reserve nunca ocurrió) |
| Crash tras RESERVE, antes de Dispatching | `Reserving` | reintento del handler `ReservationConfirmed` (at-least-once) reanuda; si el run se aborta, `CancelRun` → `Reconciling` con consume=0, refund=reserved |
| Crash durante fan-out | `Dispatching` | outbox durable reenvía los dispatch pendientes; recipients ya `Dispatched` no se re-emiten (unique `(run_id, dispatch_idempotency_key)`) |
| `CancelRun` con envíos en vuelo | `Dispatching→Cancelling` | deja de emitir nuevos dispatch; espera results de los en vuelo; `Reconciling` consume los `Delivered`, refund del resto |
| Result nunca llega para algún recipient | `Dispatching` (stuck) | sweeper de timeout: tras `dispatch_deadline`, marca `Failed(timeout)` (idempotente) → permite cierre y refund de esa unidad |
| Settlement (consume/refund) parcial | `Reconciling` | idempotente por `(consume,runId)`/`(refund,runId)`; reintento converge; run no pasa a `Completed` hasta `settlement.applied` |

**Regla de dinero fail-safe:** ante duda se **reserva de más y se devuelve** (refund del no-entregado), nunca se consume de más. Consume ≤ reserved siempre; el refund cierra la diferencia.

---

## 4. Atomicidad local (outbox)

Cada `tx local` escribe la mutación del aggregate **y** el mensaje saliente en la **misma transacción de base de datos** (Wolverine transactional outbox). No hay `Task.Run`/`Task.Delay`/`BackgroundTaskQueue` (anti-patrón legado `CampaignSchedulerBackgroundService.cs:38,78-95` que perdía el fan-out al reiniciar). Si el proceso muere, la outbox reenvía; si el mensaje ya se procesó, el guard idempotente lo absorbe.

---

## 5. Ordenamiento e "todos terminales"

El cierre del run **no** depende de recibir el "último" result (puede llegar duplicado o fuera de orden). Se evalúa por **conteo**: `dispatched == delivered + failed + suppressed + bounced`. Como cada recipient transiciona a terminal una sola vez (guard), el conteo es monótono y el predicado de cierre es estable. Ver `Concurrency_Spec.md §Cierre por conteo`.

---

## 6. Idempotencia de la saga

| Paso | Clave de idempotencia | Constraint |
|---|---|---|
| StartCampaignRun | `occurrence_key` | `UNIQUE(tenant,campaign,occurrence_key)` |
| RESERVE | `(reserve, runId)` | Wallet-side + `ProcessedBusinessMessage` |
| dispatch por recipient | `dispatch_idempotency_key` | `UNIQUE(run_id, dispatch_idempotency_key)` |
| dispatch result | mismo key | guard de estado del recipient |
| CONSUME/REFUND | `(consume,runId)`/`(refund,runId)` | Wallet-side + `ProcessedBusinessMessage` |

Detalle en `Idempotency_Spec.md`.

---

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado: check+debit 2 HTTP calls, debit antes de SaveChanges | `CreateCampaignCommandHandler.cs:250,264,278,320` | VERIFIED | 96% |
| Legado: refund depende de JWT persistido | `CampaignSendService.cs:112-127` | VERIFIED | 96% |
| Legado: cobra estimado al crear + refund frágil por inválidos | `CampaignSendService.cs:76-81` | VERIFIED | 94% |
| Legado: fan-out en `BackgroundTaskQueue`/`Task.Delay` (se pierde al reiniciar) | `CampaignSchedulerBackgroundService.cs:38,78-95` | VERIFIED | 95% |
| Saga reserve→consume/refund con movimientos inmutables | ADR-CAMP-000 §Decisiones/#3, §Anti-patrones #4 | DESIGN | 92% |
| Cobro por entregado (no por estimado) | diseño (este doc §2) | NEW | 88% |
| Sweeper de timeout para recipients stuck | diseño (este doc §3) | NEW | 84% |
