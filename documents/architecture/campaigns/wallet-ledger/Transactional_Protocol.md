# Wallet/Ledger — Transactional Protocol

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- Ver `06_Cross_Service_Transactional_Protocol.md` (saga completa balance+dispatch), `Idempotency_Spec.md`, `Concurrency_Spec.md`, `State_Machines.md`.

---

## 1. Contrato transaccional interno (por movimiento)

Cada operación mutante (Reserve/Consume/Refund/Adjust/Recharge) es **una transacción local ACID** en la DB de Wallet, envuelta por el ejecutor idempotente y con guarda de concurrencia optimista. Pseudocódigo (patrón `SqlBusinessIdempotencyExecutor.cs:23-164` adaptado):

```
BEGIN TX
  claim = ProcessedBusinessMessage.Begin(tenant, op, scopeId, key, fingerprint)
  INSERT claim                       -- UNIQUE(tenant,op,scope,key)
  ON CONFLICT -> ROLLBACK; return ResolveExisting(...)   -- replay respuesta previa
  balance = SELECT ... WHERE TenantId=@t AND Currency=@c   -- carga RowVersion
  result = balance.<Method>(...)     -- aggregate; Result.Failure => ROLLBACK, no claim
  INSERT LedgerEntry (append)
  UPDATE tenant_balances SET Posted=@p, Held=@h, RowVersion=newver
         WHERE Id=@id AND RowVersion=@expected      -- optimistic guard
  IF rows_affected = 0 -> ROLLBACK; retry o Wallet.ConcurrencyConflict
  UPSERT reservation (si aplica)
  claim.Complete(200, responseJson)
  ENQUEUE integration event (outbox)
COMMIT
```

**Un solo ganador:** el `UPDATE ... WHERE RowVersion=@expected` garantiza que dos transacciones concurrentes sobre el mismo balance no puedan ambas aplicar (la perdedora ve `rows_affected=0`). Ver `Concurrency_Spec.md`.

## 2. Saga distribuida reserve → consume/refund (visión Wallet)

Wallet es participante pasivo: expone operaciones idempotentes; el **orquestador es el consumidor** (Campaigns para un run, SMS para un envío suelto). No hay transacción distribuida 2PC; hay **saga con compensación** sobre operaciones idempotentes.

```
Consumidor (Campaigns run):
  1. RESERVE(est, scope=RunId, key=run-reserve-{RunId})     -> Held
     └─ si InsufficientFunds -> el run no arranca (gate de balance)
  2. ... fan-out + entrega por destinatario (fuera de Wallet) ...
  3. CONSUME(realDelivered, key=run-consume-{RunId})         -> Consumed/parcial
  4. REFUND(remainder, key=run-refund-{RunId})               -> Released
Compensación (cancelación antes/durante):
  REFUND(all remaining, key=run-cancel-{RunId})              -> Released
```

**Propiedades:**
- **Atomicidad de dinero por movimiento** (no de la saga entera): cada paso es todo-o-nada localmente.
- **At-least-once:** cada paso puede reentregarse; la idempotencia por clave lo absorbe (paso 1 repetido no doble-reserva; paso 3 repetido no doble-cobra).
- **Sin saldo negativo jamás:** Reserve falla-cerrado si `Available < est`; Consume nunca excede `Remaining`.
- **Progreso garantizado:** si el consumidor muere tras Reserve sin Consume/Refund, el `ReservationExpirySweep` libera el hold (`Commands_And_Events.md §Timers`). El dinero no queda atrapado.

## 3. Orden y aislamiento

- **Nivel de aislamiento:** `Read Committed` + optimistic concurrency (RowVersion). No se usa `Serializable` (evita contención); la corrección viene del guard condicional, no del nivel de aislamiento.
- **Orden reserve-antes-de-consume:** garantizado por el consumidor. Si llega un Consume para un `ReservationId` inexistente (reordenamiento del bus) → `404 Wallet.ReservationNotFound`; el consumidor reintenta (at-least-once) hasta que el Reserve materialice. El ejecutor idempotente no marca `Complete` en fallo, permitiendo reintento limpio.
- **Recharge (top-up) vs Reserve:** independientes; ambos serializados por el RowVersion del balance. Un top-up concurrente con una reserva simplemente reintenta el perdedor.

## 4. Fallos y compensación

| Escenario | Comportamiento |
|---|---|
| Reserve OK, consumidor crashea | Sweep expira el hold; `ReservationExpired` event; fondos vuelven a Available. |
| Consume parcial, luego cancelación | Refund del remaining; reserva Released tras consumo parcial. |
| Evento top-up duplicado | Segundo consumo → replay (mismo `scopeId=SaaSPaymentId`, misma key); una sola Recharge. |
| Refund reentregado | Replay; no doble-devuelve. |
| Adjust que iría a negativo | `422 Wallet.AdjustWouldGoNegative`; sin efecto. |

## 5. Anti-patrones legados corregidos aquí

| Legado | Evidencia | Corrección |
|---|---|---|
| Check + debit en 2 HTTP calls (TOCTOU) | `CreateCampaignCommandHandler.cs:250,264,278` | Reserve atómico single-op con guard `Available>=amt`. |
| Debit antes de `SaveChanges` (no atómico) | `CreateCampaignCommandHandler.cs:278,320` | Entry + saldo + claim en UNA TX. |
| Débito único al crear, sin ajuste por resultado | `CreateCampaignCommandHandler.cs:233-320` | Reserve→Consume(real)→Refund(resto). |
| Sin idempotencia en debit/refund | `WalletServiceClient.cs:101,198` | `Idempotency-Key` obligatorio + `ProcessedBusinessMessage`. |

## 6. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Patrón TX idempotente con savepoint/conflict/replay | `SqlBusinessIdempotencyExecutor.cs:57-164` | VERIFIED | 96% |
| Legado TOCTOU no-atómico | `CreateCampaignCommandHandler.cs:250-320` | VERIFIED | 95% |
| Saga con compensación sobre ops idempotentes | diseño + `06_Cross_Service...md` | NEW | n/a |
| Sweep de expiración garantiza no-atrapamiento | diseño | NEW | n/a |
