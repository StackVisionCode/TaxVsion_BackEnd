# Wallet/Ledger — Idempotency Spec

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-10-06
- **Estado:** DISEÑO — no implementado
- **Coherente con:** `00_Plan_And_Architecture.md §3/§5/§6`, `Data_Model.md §1.2`, `Transactional_Protocol.md`, `ADR.md` (ADR-WAL-008).
- Base: índice **`UNIQUE (TenantId, OperationKey)`** sobre `wallet_ledger_entries` + inbox durable de Wolverine. Patrón conceptual de `SqlBusinessIdempotencyExecutor.cs` (copia por-contexto si se requiere reforzar).

> **Modelo prepago (ADR-WAL-005).** La idempotencia ya no gira en torno a `(TenantId, Operation, ScopeId, IdempotencyKey)` de un business-inbox dedicado, sino al **`OperationKey` único en el propio ledger**. Un movimiento = un asiento = una `OperationKey`. Desaparecen las claves `reserve/consume/refund`.

---

## 1. Dos capas de dedupe (independientes)

1. **Transporte (Wolverine inbox durable):** deduplica *envelopes* del bus. No basta: at-least-once puede reentregar tras un reinicio y el dedupe de transporte tiene su propia ventana.
2. **Efecto de negocio (`UNIQUE(TenantId, OperationKey)` en el ledger):** protege la *operación de dinero*. Es la capa que garantiza "un pago = una recarga", "un run = un débito". Un segundo INSERT del mismo asiento colisiona ⇒ no se aplica dos veces el efecto.

Regla de suite: **nunca "exactly-once"**; siempre at-least-once + handlers idempotentes + unique constraints + state guards (`00_Overview:45`).

## 2. `OperationKey` por operación

| Operación | `OperationKey` | `ReferenceId` | Reason |
|---|---|---|---|
| Cobro de run (Debit) | `run:{RunId}` | `RunId` | `CampaignCharge` |
| Recarga (Credit) | `topup:{WalletTopUpId}` | `WalletTopUpId` | `TopUp` |
| Ajuste admin (Credit/Debit) | `adjust:{ticket}` | `TenantId` o ticket | `Adjustment` |

El **candado** es `UNIQUE(TenantId, OperationKey)` en `wallet_ledger_entries` (`Data_Model.md §1.2`). La `OperationKey` es una cadena de negocio estable derivada del `ReferenceId`; no reinterpreta formato (solo no-vacía, ≤200 chars).

## 3. Comportamiento ante reentrega

- **Mismo `OperationKey`** (reentrega del mismo evento) → el INSERT colisiona ⇒ se **replica** el resultado previo (mismo asiento, mismo saldo). Idempotencia verdadera:
  - Cobro de run reentregado ⇒ se vuelve a publicar `CampaignRunAuthorized { RunId }` **sin re-debitar**.
  - Top-up reentregado ⇒ una sola `Credit`.
- **`OperationKey` distinta para el mismo scope** (p.ej. un run `Denied` que tras recarga recibe un nuevo `PendingAuthorization` — misma `run:{RunId}`) ⇒ **no es** una key distinta: sigue siendo `run:{RunId}`. Un run denegado **no dejó asiento**, así que el reintento sí puede debitar ahora que alcanza (no hay colisión porque no hubo INSERT previo exitoso). Un run ya **autorizado** (con asiento) reintenta ⇒ colisión ⇒ replay de `Authorized`.

## 4. Fallo del cuerpo no "envenena" la operación

Si el `Debit`/`Credit` falla antes de insertar el asiento (`Result.Failure`, p.ej. `InsufficientFunds`), **no se persiste ningún asiento** y la TX hace rollback. Un reintento posterior con la misma `OperationKey` puede **volver a intentar** limpio (no queda bloqueado). Solo un asiento efectivamente insertado activa el replay.

## 5. Interacción con el gate `InsufficientFunds`

Complementa el candado: un `Denied(InsufficientFunds)` **no** crea asiento, por lo que es naturalmente reintentable (recargar → reintentar → debita). Un `Authorized` **sí** crea asiento, por lo que reintentarlo replica el resultado sin re-cobrar. Doble protección: candado de `OperationKey` + guarda de saldo del aggregate.

## 6. Retención

El ledger es **permanente** (fuente de verdad auditable); no se purga. Si se adopta además una tabla de dedupe de transporte con TTL, sus filas vencidas se purgan (`IdempotencyRetentionPurge`), pero eso no afecta la idempotencia de negocio, que vive en el ledger inmutable.

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Idempotencia por `UNIQUE(TenantId, OperationKey)` en el ledger | `00_Plan §3` (diseño) | NEW | n/a |
| Patrón conflict-insert → replay respuesta previa | `SqlBusinessIdempotencyExecutor.cs:93-116` (patrón) | VERIFIED | 94% |
| `IdempotencyKey`/clave ≤200, no reinterpreta formato | `PaymentApp.Domain/ValueObjects/IdempotencyKey.cs:10-30` | VERIFIED | 96% |
| At-least-once + handlers idempotentes (regla de suite) | `00_Overview:45` | DOCUMENTED_ONLY | 88% |
| `opKey="run:"+RunId` / `"topup:"+Id` | `00_Plan §5/§6` (diseño) | NEW | n/a |
