# Wallet/Ledger — Data Model

- **Servicio:** `TaxVision.Wallet` (DB propia `TaxVision_Wallet`; sin FK cross-context)
- **Fecha:** 2026-10-06
- **Estado:** DISEÑO — no implementado
- **Coherente con:** `00_Plan_And_Architecture.md §3`, `Domain_Design.md`, `ADR.md` (ADR-WAL-003/005/006/008).
- Multi-tenant **fail-closed**: query filter global por `TenantId` + repos tenant-scoped + `.IgnoreQueryFilters()`+tenant explícito en scopes Wolverine (ver `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`).

> **Modelo prepago (ADR-WAL-005): sin `HeldCents`, sin tabla de reservas.** Las tablas `wallet_reservations` y la columna `HeldCents` del modelo anterior **ya no existen**. Un solo saldo `PostedCents` y un ledger append-only con idempotencia por `OperationKey`.

---

## 1. Tablas

### 1.1 `wallet_wallets` (aggregate root)

| Columna | Tipo | Constraints |
|---|---|---|
| `TenantId` | uuid | PK (uno por tenant) |
| `Currency` | char(3) | NOT NULL |
| `PostedCents` | bigint | NOT NULL, `CHECK (PostedCents >= 0)` |
| `RowVersion` | bytea / rowversion | concurrency token (optimistic) |
| `CreatedAtUtc`/`UpdatedAtUtc` | timestamptz | NOT NULL |

- **PK (`TenantId`)** — un wallet por tenant (MVP: moneda única USD).
- `CHECK (PostedCents >= 0)` codifica la invariante "sin saldo negativo" a nivel BD (defensa en profundidad; el aggregate ya la garantiza).

### 1.2 `wallet_ledger_entries` (INMUTABLE, append-only)

| Columna | Tipo | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `TenantId` | uuid | NOT NULL |
| `Direction` | smallint | NOT NULL (0=Credit, 1=Debit) |
| `AmountCents` | bigint | NOT NULL, `CHECK (AmountCents > 0)` |
| `BalanceAfterCents` | bigint | NOT NULL (snapshot de `PostedCents` tras el asiento) |
| `Reason` | smallint | NOT NULL (0=TopUp, 1=CampaignCharge, 2=Adjustment) |
| `OperationKey` | varchar(200) | NOT NULL |
| `ReferenceId` | uuid | NOT NULL (topUpId o runId) |
| `ActorType` | varchar(20) | NULL (system/admin) |
| `ActorId` | varchar(100) | NULL |
| `CreatedAtUtc` | timestamptz | NOT NULL |

- **Sin `UpdatedAtUtc`, sin setters.** Append-only.
- **UNIQUE (`TenantId`, `OperationKey`)** — el **candado de idempotencia** (ADR-WAL-008). Un reintento del mismo movimiento colisiona en INSERT ⇒ no duplica el débito/crédito; se replica el resultado previo (ver `Idempotency_Spec.md`).
- Índices: `(TenantId, CreatedAtUtc)` para auditoría/ledger paginado; `(TenantId, ReferenceId)` para lookup por run/top-up.

### 1.3 `wallet_channel_prices` (catálogo de plataforma)

| Columna | Tipo | Constraints |
|---|---|---|
| `Channel` | smallint | PK (0=Email,1=Sms,2=Push,3=WhatsApp) |
| `UnitPriceCents` | bigint | NOT NULL, `CHECK (>= 0)` |
| `Currency` | char(3) | NOT NULL |
| `Active` | boolean | NOT NULL DEFAULT true |
| `UpdatedAtUtc` | timestamptz | NOT NULL |

- Catálogo **global de plataforma** (no tenant-scoped; no lleva query filter). Editable solo por PlatformAdmin (`PUT /wallet/pricing/{channel}`).
- Semilla inicial (placeholder, ajustable): Email 1¢, Push 1¢, SMS 5¢, WhatsApp 5¢. **El diseño no hardcodea los números.**
- Opcional futuro: historial de precios (`wallet_channel_price_history`) para auditar cambios de tarifa; no en MVP.

### 1.4 `wallet_top_ups` (recargas en curso)

| Columna | Tipo | Constraints |
|---|---|---|
| `Id` | uuid | PK (= `topUpId`, `ReferenceId` del Credit) |
| `TenantId` | uuid | NOT NULL |
| `AmountCents` | bigint | NOT NULL, `CHECK (> 0)` |
| `Currency` | char(3) | NOT NULL |
| `Status` | smallint | NOT NULL (0=Pending,1=Succeeded,2=Failed) |
| `SaaSPaymentReference` | varchar(200) | NULL (ref del charge de PaymentApp) |
| `RowVersion` | bytea | concurrency token |
| `CreatedAtUtc`/`UpdatedAtUtc` | timestamptz | NOT NULL |

- Rastrea la recarga mientras PaymentApp cobra (Stripe off-session). Al `WalletTopUpPaymentSucceeded` → `Status=Succeeded` + `Credit(opKey="topup:"+Id)`. Al `...Failed` → `Status=Failed` (no acredita). Ver `State_Machines.md §2`.

> **No hay** tabla `wallet_reservations` ni `wallet_processed_business_messages`: la idempotencia vive en el índice único `(TenantId, OperationKey)` del ledger (ADR-WAL-008). La capa de transporte la cubre el inbox durable de Wolverine.

## 2. Grants a nivel BD (inmutabilidad forzada)

El rol de aplicación tiene sobre `wallet_ledger_entries`: `SELECT`, `INSERT`. **Revocados `UPDATE`, `DELETE`.** Correcciones = nuevos asientos `Adjustment`, nunca edición. Esto hace imposible el `WalletTransaction.IsActive` mutable del legado (`ReferralService/Domain/WalletTransaction.cs:21`).

## 3. Consistencia transaccional

Un movimiento = UNA transacción que:
1. Inserta `wallet_ledger_entries` (append) — el INSERT con `UNIQUE(TenantId, OperationKey)` es el candado de idempotencia (conflicto ⇒ replay del resultado previo).
2. Actualiza `wallet_wallets` (`PostedCents`) con guarda `WHERE RowVersion = @expected` (optimistic; ver `Concurrency_Spec.md`).
3. (Top-up) actualiza `wallet_top_ups.Status`.
4. Encola evento de integración en outbox Wolverine.

Todo commit atómico. Sin el TOCTOU de dos HTTP calls del legado.

## 4. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Multi-tenant fail-closed (query filter global + `.IgnoreQueryFilters()`+tenant) | `Guia_IgnoreQueryFilters...md`; `00_Overview:47` | VERIFIED | 90% |
| Idempotencia por `UNIQUE(TenantId, OperationKey)` en el ledger | `00_Plan §3` (diseño); patrón `SqlBusinessIdempotencyExecutor.cs` | NEW | n/a |
| Legado con saldo mutable + flag IsActive (a evitar) | `ReferralService/Domain/WalletTransaction.cs:12-21` | VERIFIED | 96% |
| Ledger append-only con grants revocados | diseño | NEW | n/a |
| `CHECK (PostedCents >= 0)` codifica "sin saldo negativo" | diseño | NEW | n/a |
| Tablas `Wallets`/`LedgerEntries`/`ChannelPrices` (migración inicial F1) | `00_Plan §10 F1` | NEW | n/a |
