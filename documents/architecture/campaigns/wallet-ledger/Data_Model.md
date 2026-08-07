# Wallet/Ledger — Data Model

- **Servicio:** `TaxVision.Wallet` (DB propia; sin FK cross-context)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- Multi-tenant **fail-closed**: query filter global por `TenantId` + repos tenant-scoped + `.IgnoreQueryFilters()`+tenant explícito en scopes Wolverine (ver `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`).

---

## 1. Tablas

### 1.1 `wallet_tenant_balances` (aggregate root)

| Columna | Tipo | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `TenantId` | uuid | NOT NULL |
| `Currency` | char(3) | NOT NULL |
| `PostedCents` | bigint | NOT NULL, `CHECK (PostedCents >= 0)` |
| `HeldCents` | bigint | NOT NULL, `CHECK (HeldCents >= 0)`, `CHECK (HeldCents <= PostedCents)` |
| `Status` | smallint | NOT NULL (0=Active,1=Frozen) |
| `RowVersion` | bytea / rowversion | concurrency token (optimistic) |
| `CreatedAtUtc`/`UpdatedAtUtc` | timestamptz | NOT NULL |

- **UNIQUE (`TenantId`,`Currency`)** — un balance por tenant y moneda.
- `CHECK (HeldCents <= PostedCents)` codifica la invariante `Available >= 0` a nivel BD (defensa en profundidad; el aggregate ya la garantiza). `Available` = `PostedCents − HeldCents` (columna calculada o derivada en lectura).

### 1.2 `wallet_reservations`

| Columna | Tipo | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `TenantId` | uuid | NOT NULL |
| `Currency` | char(3) | NOT NULL |
| `AmountCents` | bigint | NOT NULL, `CHECK > 0` |
| `ConsumedCents` | bigint | NOT NULL DEFAULT 0, `CHECK (ConsumedCents BETWEEN 0 AND AmountCents)` |
| `Status` | smallint | NOT NULL (0=Held,1=Consumed,2=Released,3=Expired) |
| `ConsumerContext` | varchar(40) | NOT NULL (etiqueta) |
| `ScopeId` | uuid | NOT NULL (opaco) |
| `IdempotencyKey` | varchar(200) | NOT NULL |
| `ExpiresAtUtc` | timestamptz | NULL |
| `RowVersion` | bytea | concurrency token |
| `CreatedAtUtc`/`UpdatedAtUtc` | timestamptz | NOT NULL |

- `RemainingCents` = `AmountCents − ConsumedCents` (derivado).
- **UNIQUE (`TenantId`,`ScopeId`,`Operation='reserve'`)** vía la fila `ProcessedBusinessMessage` (no se crean dos reservas para el mismo scope+key). Índice `(TenantId, Status)` para el sweep de expiración; índice `(TenantId, ScopeId)` para lookup por consumidor.

### 1.3 `wallet_ledger_entries` (INMUTABLE, append-only)

| Columna | Tipo | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `TenantId` | uuid | NOT NULL |
| `Currency` | char(3) | NOT NULL |
| `Kind` | smallint | NOT NULL (0=Recharge,1=Reserve,2=Consume,3=Refund,4=Adjust) |
| `SignedAmountCents` | bigint | NOT NULL |
| `BalanceAfterPostedCents` | bigint | NOT NULL |
| `BalanceAfterHeldCents` | bigint | NOT NULL |
| `ReservationId` | uuid | NULL (FK lógica intra-context) |
| `Operation` | varchar(40) | NOT NULL |
| `ScopeId` | uuid | NOT NULL |
| `IdempotencyKey` | varchar(200) | NOT NULL |
| `SourceReference` | varchar(200) | NULL (SaaSPaymentId, reason...) |
| `ActorType` | varchar(20) | NOT NULL |
| `ActorId` | varchar(100) | NULL |
| `CreatedAtUtc` | timestamptz | NOT NULL |

- **Sin `UpdatedAtUtc`, sin setters.** Append-only.
- Índices: `(TenantId, CreatedAtUtc)` para auditoría paginada; `(TenantId, ScopeId)`; `(ReservationId)`.

### 1.4 `wallet_processed_business_messages` (business-inbox)

Copia por-contexto de `ProcessedBusinessMessage` (`Growth/.../Idempotency/ProcessedBusinessMessage.cs:9-124`): `TenantId`, `Operation`, `ScopeId`, `IdempotencyKey`, `RequestFingerprint`(sha256 64-hex), `Status`, `ResponseJson`, `RowVersion`, `CreatedAtUtc`, `CompletedAtUtc`, `ExpiresAtUtc`.

- **UNIQUE (`TenantId`,`Operation`,`ScopeId`,`IdempotencyKey`)** — el candado de idempotencia. La colisión en INSERT (`ConflictException`) dispara el replay de la respuesta previa (`SqlBusinessIdempotencyExecutor.cs:97-116`).

## 2. Grants a nivel BD (inmutabilidad forzada)

El rol de aplicación tiene sobre `wallet_ledger_entries`: `SELECT`, `INSERT`. **Revocados `UPDATE`, `DELETE`.** Correcciones = nuevos entries `Adjust`/`Refund`, nunca edición. Esto hace imposible el `WalletTransaction.IsActive` mutable del legado (`ReferralService/Domain/WalletTransaction.cs:21`).

## 3. Consistencia transaccional

Un movimiento = UNA transacción que:
1. Inserta/actualiza fila en `wallet_processed_business_messages` (candado).
2. Inserta `wallet_ledger_entries` (append).
3. Actualiza `wallet_tenant_balances` (`PostedCents`/`HeldCents`) con guarda `WHERE RowVersion = @expected` (optimistic; ver `Concurrency_Spec.md`).
4. Inserta/actualiza `wallet_reservations` si aplica.
5. Encola evento de integración en outbox Wolverine.

Todo commit atómico. Sin el TOCTOU de dos HTTP calls del legado.

## 4. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Multi-tenant fail-closed (query filter global + `.IgnoreQueryFilters()`+tenant) | `Guia_IgnoreQueryFilters...md`; `00_Overview:47` | VERIFIED | 90% |
| `ProcessedBusinessMessage` unique (tenant,op,scope,key) → conflict → replay | `ProcessedBusinessMessage.cs`; `SqlBusinessIdempotencyExecutor.cs:97-116,175-181` | VERIFIED | 96% |
| Legado con saldo mutable + flag IsActive (a evitar) | `ReferralService/Domain/WalletTransaction.cs:12-21` | VERIFIED | 96% |
| Ledger append-only con grants revocados | diseño | NEW | n/a |
| CHECK (HeldCents<=PostedCents) codifica Available>=0 | diseño | NEW | n/a |
