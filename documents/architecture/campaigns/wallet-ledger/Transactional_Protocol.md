# Wallet/Ledger — Transactional Protocol

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-10-06
- **Estado:** DISEÑO — no implementado
- **Coherente con:** `00_Plan_And_Architecture.md §5`, `../06_Cross_Service_Transactional_Protocol.md`, `Idempotency_Spec.md`, `Concurrency_Spec.md`, `State_Machines.md`.

> **Modelo prepago (ADR-WAL-005).** NO hay saga reserve→consume→refund ni compensación. El cobro de un run es **un único débito** gobernado por el PEP (un intercambio de eventos pedir-autorización → autorizado/denegado). La recarga es el flujo `WalletTopUp`.

---

## 1. Contrato transaccional interno (por movimiento)

Cada operación mutante (`Debit` del run, `Credit` del top-up, `Adjustment`) es **una transacción local ACID** en la DB de Wallet, con idempotencia por `OperationKey` y guarda de concurrencia optimista:

```
BEGIN TX
  wallet = SELECT ... WHERE TenantId=@t          -- carga PostedCents + RowVersion
  result = wallet.Debit(costo) | wallet.Credit(amount)   -- aggregate; Result.Failure => ROLLBACK
  INSERT LedgerEntry (Direction, AmountCents, BalanceAfterCents, Reason,
                      OperationKey, ReferenceId)          -- UNIQUE(TenantId, OperationKey)
     ON CONFLICT (TenantId, OperationKey) -> ROLLBACK; return ReplayPrevious(...)  -- idempotente
  UPDATE wallet SET PostedCents=@p, RowVersion=newver
         WHERE TenantId=@t AND RowVersion=@expected       -- optimistic guard
  IF rows_affected = 0 -> ROLLBACK; retry o Wallet.ConcurrencyConflict
  (top-up) UPDATE wallet_top_ups SET Status=Succeeded
  ENQUEUE integration event (outbox)
COMMIT
```

**Un solo ganador:** el `UPDATE ... WHERE RowVersion=@expected` garantiza que dos transacciones concurrentes sobre el mismo wallet no apliquen ambas (la perdedora ve `rows_affected=0`). Ver `Concurrency_Spec.md`.

**Idempotencia:** el `INSERT` del asiento con `UNIQUE(TenantId, OperationKey)` es el candado. Un reintento del mismo movimiento (misma `opKey`) colisiona ⇒ no duplica el efecto; se replica el resultado previo (`Idempotency_Spec.md`).

## 2. Protocolo de cobro del run (PEP, event-driven)

No hay transacción distribuida 2PC ni saga con compensación. Es un **intercambio de dos eventos** con un único débito idempotente:

```
Campaigns (entre Start y fan-out):
  1. publica CampaignRunPendingAuthorization { RunId, PerChannelUnits }
Wallet (PEP consumer):
  2. costo = Σ canal (PerChannelUnits[canal] × ChannelPrice[canal])
  3. Debit(costo, opKey="run:"+RunId, CampaignCharge)   -- TX local §1
     ├─ PostedCents >= costo -> publica CampaignRunAuthorized { RunId }
     └─ PostedCents <  costo -> publica CampaignRunAuthorizationDenied { RunId, "InsufficientFunds" }
Campaigns (resultado):
  4a. Authorized -> ejecuta fan-out (loop 106-133) + MarkDispatched
  4b. Denied     -> CampaignRun.Reject("InsufficientFunds") -> Rejected (sin fan-out)
```

**Propiedades:**
- **Atomicidad de dinero por movimiento:** el `Debit` es todo-o-nada localmente.
- **At-least-once:** el `PendingAuthorization` puede reentregarse; `opKey="run:"+RunId` lo absorbe (no doble-debita; re-autorizar un run ya autorizado → replay de `Authorized`).
- **Sin saldo negativo jamás:** `Debit` falla-cerrado si `PostedCents < costo` → `Denied`.
- **Sin reembolso:** no hay paso de reconciliación al cerrar el run; `CampaignRunCompleted` se ignora para dinero.
- **Scheduler:** cada fire = run nuevo = su propio `PendingAuthorization` = su propio débito idempotente.

## 3. Protocolo de recarga (top-up, money-IN)

Clona plan-change de PaymentApp (ADR-WAL-010):

```
Wallet:    POST /wallet/top-up { amountCents } -> crea WalletTopUp(Pending)
           -> publica WalletTopUpDue { TenantId, WalletTopUpId, AmountCents }
PaymentApp: WalletTopUpDueConsumer -> ChargeSaaSPayment(Type=WalletTopUp, Provider=Stripe,
            off-session contra tarjeta guardada)
           -> WalletTopUpPaymentSucceeded | WalletTopUpPaymentFailed
Wallet:    Succeeded -> Credit(amount, opKey="topup:"+WalletTopUpId, TopUp) + WalletTopUp.Succeeded
           Failed    -> WalletTopUp.Failed (no acredita)
```

At-least-once + `opKey="topup:"+WalletTopUpId` ⇒ un pago = una recarga.

## 4. Orden y aislamiento

- **Nivel de aislamiento:** `Read Committed` + optimistic concurrency (RowVersion). No `Serializable`; la corrección viene del guard condicional.
- **Débito vs Credit (top-up) concurrentes:** serializados por el `RowVersion` del wallet; el perdedor reintenta con el saldo ya actualizado (la suma es conmutativa; la guarda `PostedCents>=costo` se reevalúa). Un top-up que llega durante un intento de cobro puede **convertir** un `Denied` en `Authorized` en el reintento.

## 5. Fallos

| Escenario | Comportamiento |
|---|---|
| `PendingAuthorization` duplicado (ya autorizado) | Replay de `Authorized`; no doble-debita (`opKey="run:"+RunId`). |
| `PendingAuthorization` con saldo insuficiente | `Denied(InsufficientFunds)`; sin débito; run `Rejected`. Recargar + reintentar re-evalúa. |
| Evento top-up duplicado | Replay; una sola `Credit` (`opKey="topup:"+Id`). |
| `Adjustment` que iría a negativo | `Wallet.AdjustWouldGoNegative`; sin efecto. |
| Consumer cae tras publicar `PendingAuthorization` | Reentrega at-least-once; idempotente. Sin holds que limpiar. |

## 6. Anti-patrones legados corregidos aquí

| Legado | Evidencia | Corrección |
|---|---|---|
| Check + debit en 2 HTTP calls (TOCTOU) | `CreateCampaignCommandHandler.cs:250,264,278` | `Debit` atómico single-op con guard `PostedCents>=costo`. |
| Debit antes de `SaveChanges` (no atómico) | `CreateCampaignCommandHandler.cs:278,320` | Asiento + saldo en UNA TX. |
| Sin idempotencia en debit | `WalletServiceClient.cs:101,198` | `OperationKey` + `UNIQUE(TenantId, OperationKey)`. |
| Campaigns acoplado al cobro | `CreateCampaignCommandHandler.cs:233-320` | PEP event-driven; Campaigns money-agnostic (solo cuenta unidades). |

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Patrón TX idempotente con conflict/replay | `SqlBusinessIdempotencyExecutor.cs` (patrón) | VERIFIED | 92% |
| Legado TOCTOU no-atómico | `CreateCampaignCommandHandler.cs:250-320` | VERIFIED | 95% |
| Gate Start→fan-out en Campaigns | `StartCampaignRunCommand.cs:83`, `106-133` | VERIFIED | 95% |
| Patrón money-IN plan-change a clonar | `SubscriptionPlanChangeDueConsumer.cs:21-61` | VERIFIED | 90% |
| Cobro single-debit event-driven (PEP) | `00_Plan §5` (diseño) | NEW | n/a |
