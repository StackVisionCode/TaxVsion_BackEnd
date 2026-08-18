# Wallet/Ledger — Concurrency Spec

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- Ver `Transactional_Protocol.md`, `Idempotency_Spec.md`, `Data_Model.md`.

---

## 1. Problema

Múltiples operaciones concurrentes sobre el **mismo `TenantBalance`** (p.ej. dos runs reservando a la vez, o un top-up mientras se reserva) deben aplicarse en serie coherente sin perder actualizaciones y **sin permitir saldo negativo**. El legado no lo resolvía (débito no-atómico, sin token de concurrencia; `CreateCampaignCommandHandler.cs:250-320`).

## 2. Estrategia: conditional update + RowVersion (optimistic, un ganador)

Cada `wallet_tenant_balances` y `wallet_reservations` lleva `RowVersion` (`Data_Model.md`). La escritura de saldo es:

```sql
UPDATE wallet_tenant_balances
   SET PostedCents=@p, HeldCents=@h, RowVersion=@new, UpdatedAtUtc=@now
 WHERE Id=@id AND RowVersion=@expected;   -- expected = leído al cargar el aggregate
-- rows_affected = 0  => otra TX ganó => conflicto de concurrencia
```

- **Un ganador:** si dos transacciones leyeron el mismo `RowVersion`, solo una hace `rows_affected=1`; la otra ve `0`.
- **Reintento acotado:** la perdedora recarga el balance (nuevo `Available`/`RowVersion`) y **reevalúa la guarda de negocio** antes de reintentar (backoff exponencial, N intentos, luego `409 Wallet.ConcurrencyConflict`). Reevaluar es esencial: un Reserve que era válido puede volverse `InsufficientFunds` tras la operación ganadora → falla-cerrado correcto.
- EF Core: `RowVersion` como `[ConcurrencyToken]`/`IsRowVersion()`; el `DbUpdateConcurrencyException` traduce a reintento.

**Por qué optimistic y no locks pesimistas:** el balance de un tenant no es punto de contención extremo (las campañas grandes hacen 1 Reserve, no miles de writes al balance); optimistic evita deadlocks y bloqueo de conexiones. Consume incremental por lote sí puede concurrir → el reintento acotado lo absorbe.

## 3. Concurrencia entre reservas del mismo tenant

Dos Reserve concurrentes: ambos leen `Available=X`. El ganador aplica `Held+=a1`. El perdedor reintenta, recarga `Available=X−a1`, reevalúa: si `X−a1 >= a2` procede; si no, `InsufficientFunds`. **Nunca** se sobre-reserva (I1 preservada). Esto corrige el TOCTOU legado donde dos débitos podían pasar el check con el mismo saldo leído.

## 4. Concurrencia Reserve ↔ Recharge (top-up)

Serializadas por el mismo `RowVersion` del balance. Un top-up que llega durante una reserva simplemente hace que el perdedor reintente con el saldo ya incrementado — resultado correcto en cualquier orden (la suma es conmutativa; la guarda se reevalúa).

## 5. Idempotencia vs concurrencia (interacción)

Son ortogonales pero cooperan:
- El **candado de idempotencia** (`UNIQUE(tenant,op,scope,key)`) serializa reintentos de *la misma* operación: el segundo INSERT del claim colisiona y hace replay (`SqlBusinessIdempotencyExecutor.cs:97-116`) — no compite por RowVersion.
- El **RowVersion** serializa operaciones *distintas* sobre el mismo balance.
- Petición aún `Processing` (concurrente, misma key): `Wallet.OperationInProgress` (`SqlBusinessIdempotencyExecutor.cs:195-199`) — evita dos ejecuciones en paralelo del mismo efecto.

## 6. Aislamiento y deadlocks

- Nivel `Read Committed`. Sin `SELECT ... FOR UPDATE` sobre el balance (se usa el guard condicional). Orden de escritura consistente (claim → ledger → balance → reservation) para minimizar ciclos de lock.
- El sweep de expiración toma reservas una a una (idempotente); si compite con un Consume tardío, el RowVersion de la reserva decide el ganador (Consume gana si llegó primero; el Release ve estado terminal y hace replay/no-op).

## 7. Garantías resultantes

| Garantía | Mecanismo |
|---|---|
| No lost update | RowVersion conditional update |
| No saldo negativo bajo concurrencia | guarda `Available>=amt` reevaluada tras conflicto + `CHECK(Held<=Posted)` |
| No doble-reserva/doble-cobro | idempotency claim + state guards |
| Un solo ganador por write | `rows_affected` del UPDATE condicional |
| Progreso (sin holds atrapados) | sweep de expiración |

## 8. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| `ProcessedBusinessMessage.RowVersion` existe (patrón optimistic) | `ProcessedBusinessMessage.cs:23` | VERIFIED | 96% |
| `OperationInProgress` para concurrencia de misma key | `SqlBusinessIdempotencyExecutor.cs:195-199` | VERIFIED | 95% |
| Legado sin token de concurrencia (TOCTOU) | `CreateCampaignCommandHandler.cs:250-320` | VERIFIED | 94% |
| Conditional update + reevaluación de guarda | diseño | NEW | n/a |
