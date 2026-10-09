# Wallet/Ledger — Concurrency Spec

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-10-06
- **Estado:** DISEÑO — no implementado
- **Coherente con:** `00_Plan_And_Architecture.md §3`, `Transactional_Protocol.md`, `Idempotency_Spec.md`, `Data_Model.md`, `ADR.md` (ADR-WAL-009).

> **Modelo prepago (ADR-WAL-005): un solo saldo `PostedCents`, sin `HeldCents`.** El `CHECK` de BD es `PostedCents >= 0` (no `Held <= Posted`). No hay reservas que serializar; solo débitos (cobro de run), créditos (top-up) y ajustes sobre el mismo wallet.

---

## 1. Problema

Múltiples operaciones concurrentes sobre el **mismo `Wallet`** (p.ej. dos runs cobrando a la vez, o un top-up mientras se cobra) deben aplicarse en serie coherente sin perder actualizaciones y **sin permitir saldo negativo**. El legado no lo resolvía (débito no-atómico, sin token de concurrencia; `CreateCampaignCommandHandler.cs:250-320`).

## 2. Estrategia: conditional update + RowVersion (optimistic, un ganador)

Cada `wallet_wallets` lleva `RowVersion` (`Data_Model.md`). La escritura de saldo es:

```sql
UPDATE wallet_wallets
   SET PostedCents=@p, RowVersion=@new, UpdatedAtUtc=@now
 WHERE TenantId=@t AND RowVersion=@expected;   -- expected = leído al cargar el aggregate
-- rows_affected = 0  => otra TX ganó => conflicto de concurrencia
```

- **Un ganador:** si dos transacciones leyeron el mismo `RowVersion`, solo una hace `rows_affected=1`; la otra ve `0`.
- **Reintento acotado:** la perdedora recarga el wallet (nuevo `PostedCents`/`RowVersion`) y **reevalúa la guarda** `PostedCents - costo >= 0` antes de reintentar (backoff exponencial, N intentos, luego `Wallet.ConcurrencyConflict`). Reevaluar es esencial: un cobro que era válido puede volverse `InsufficientFunds` tras el débito ganador → falla-cerrado correcto (el run queda `Denied`).
- EF Core: `RowVersion` como `[ConcurrencyToken]`/`IsRowVersion()`; el `DbUpdateConcurrencyException` traduce a reintento.

**Por qué optimistic y no locks pesimistas:** el wallet de un tenant no es punto de contención extremo (un run hace **1 débito**, no miles de writes); optimistic evita deadlocks y bloqueo de conexiones.

## 3. Concurrencia entre cobros del mismo tenant (dos runs a la vez)

Dos `Debit` concurrentes: ambos leen `PostedCents=X`. El ganador aplica `X−c1`. El perdedor reintenta, recarga `PostedCents=X−c1`, reevalúa: si `X−c1 >= c2` procede; si no, `InsufficientFunds` → ese run `Denied`. **Nunca** se sobre-gira (I1 preservada). Esto corrige el TOCTOU legado donde dos débitos podían pasar el check con el mismo saldo leído.

## 4. Concurrencia Debit ↔ Credit (top-up)

Serializados por el mismo `RowVersion` del wallet. Un top-up que llega durante un cobro hace que el perdedor reintente con el saldo ya incrementado — resultado correcto en cualquier orden (suma conmutativa; la guarda se reevalúa). Un top-up puede **habilitar** un cobro que antes no alcanzaba (de ahí la UX "recargar y reintentar", `00_Plan §8-bis`).

## 5. Idempotencia vs concurrencia (interacción)

Son ortogonales pero cooperan:
- El **candado de idempotencia** (`UNIQUE(TenantId, OperationKey)` en el ledger) absorbe reintentos de *la misma* operación: el segundo INSERT del asiento colisiona ⇒ replay del resultado previo — no compite por RowVersion.
- El **RowVersion** serializa operaciones *distintas* sobre el mismo wallet.

## 6. Aislamiento y deadlocks

- Nivel `Read Committed`. Sin `SELECT ... FOR UPDATE` sobre el wallet (se usa el guard condicional). Orden de escritura consistente (ledger → wallet) para minimizar ciclos de lock.

## 7. Garantías resultantes

| Garantía | Mecanismo |
|---|---|
| No lost update | RowVersion conditional update |
| No saldo negativo bajo concurrencia | guarda `PostedCents>=costo` reevaluada tras conflicto + `CHECK(PostedCents>=0)` |
| No doble-cobro/doble-recarga | idempotency por `UNIQUE(TenantId, OperationKey)` |
| Un solo ganador por write | `rows_affected` del UPDATE condicional |

## 8. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| `ProcessedBusinessMessage.RowVersion` existe (patrón optimistic) | `ProcessedBusinessMessage.cs:23` | VERIFIED | 96% |
| Legado sin token de concurrencia (TOCTOU) | `CreateCampaignCommandHandler.cs:250-320` | VERIFIED | 94% |
| Conditional update + reevaluación de guarda (prepago single-debit) | `00_Plan §3` (diseño) | NEW | n/a |
| `CHECK(PostedCents>=0)` sin `HeldCents` | diseño | NEW | n/a |
