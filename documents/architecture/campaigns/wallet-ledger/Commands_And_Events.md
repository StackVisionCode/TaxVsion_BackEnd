# Wallet/Ledger — Commands & Events

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-10-06
- **Estado:** DISEÑO — no implementado
- **Coherente con:** `00_Plan_And_Architecture.md §5/§6`, `Domain_Design.md`, `State_Machines.md`, `Idempotency_Spec.md`.
- Mensajería = **Wolverine outbox/inbox durable** (at-least-once, nunca exactly-once). Dedupe de efecto de negocio = índice `UNIQUE(TenantId, OperationKey)` en el ledger (ADR-WAL-008).

> **Modelo prepago (ADR-WAL-005).** Desaparecen `Reserve/Consume/Refund` y sus eventos (`FundsReserved/Consumed/Refunded`). El cobro de campaña es un **único débito** disparado por el evento del PEP; la recarga es el flujo `WalletTopUp`.

---

## 1. Commands (aplicación)

Cada command → handler que carga `Wallet`, invoca `Credit`/`Debit` (→ `Result`), persiste asiento + saldo en UNA transacción, publica evento de integración vía outbox. Idempotentes por `OperationKey`.

| Command | Origen | Efecto | Evento emitido |
|---|---|---|---|
| `AuthorizeCampaignRunCommand` | **consumer del PEP** (no API) | `Debit(costo, opKey="run:"+RunId, CampaignCharge)` | `CampaignRunAuthorized` / `...Denied` |
| `CreditWalletTopUpCommand` | **consumer interno** (no API) | `Credit(amount, opKey="topup:"+Id, TopUp)` | `WalletCreditedIntegrationEvent` (opcional, BI) |
| `AdjustBalanceCommand` | Admin/Platform (API) | `Credit`/`Debit`(`opKey="adjust:"+ticket, Adjustment`) | `BalanceAdjustedIntegrationEvent` |
| `StartWalletTopUpCommand` | tenant (`POST /wallet/top-up`) | crea `WalletTopUp(Pending)` | `WalletTopUpDueIntegrationEvent` |
| `FreezeWalletCommand`/`UnfreezeWalletCommand` | Admin (opcional) | Freeze/Unfreeze | `WalletFrozenIntegrationEvent` / `...Unfrozen...` |

Cada movimiento lleva: `TenantId`, `AmountCents`, `OperationKey`, `Reason`, `ReferenceId`.

## 2. Eventos que Wallet CONSUME (inbound)

### 2.1 Autorización de cobro — `CampaignRunPendingAuthorizationIntegrationEvent` (NUEVO, de Campaigns)

Dispara el débito del run. Campaigns **cuenta** unidades por canal (no sabe de precios) — sigue money-agnostic (ADR-WAL-007).

```csharp
public sealed record CampaignRunPendingAuthorizationIntegrationEvent : IntegrationEvent
{
    public required Guid TenantId { get; init; }
    public required Guid CampaignId { get; init; }
    public required Guid RunId { get; init; }
    public required IReadOnlyDictionary<string, int> PerChannelUnits { get; init; } // { "Email":n,"Sms":m,"Push":p,"WhatsApp":w }
    public required string TriggeredBy { get; init; }  // user/scheduler
}
```

**Consumer:** `CampaignRunPendingAuthorizationConsumer` → calcula `costo = Σ canal (PerChannelUnits[canal] × ChannelPrice[canal])` → `AuthorizeCampaignRunCommand` → `Debit`. `opKey="run:"+RunId`, `referenceId=RunId`, `reason=CampaignCharge`. Suficiente → publica `Authorized`; insuficiente → `Denied(InsufficientFunds)` (no debita).

### 2.2 Top-up succeeded/failed — de PaymentApp (NUEVOS)

`WalletTopUpPaymentSucceededIntegrationEvent` / `WalletTopUpPaymentFailedIntegrationEvent`, espejo de los de plan-change.

```csharp
public sealed record WalletTopUpPaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid TenantId { get; init; }
    public required Guid WalletTopUpId { get; init; }   // ReferenceId del Credit
    public required long AmountCents { get; init; }      // USD minor units
    public required string Currency { get; init; }
    public required string ExternalPaymentReference { get; init; }
    public required DateTime PaidAtUtc { get; init; }
}
```

**Consumer:** `WalletTopUpPaymentSucceededConsumer` → `CreditWalletTopUpCommand` → `Credit(amount, opKey="topup:"+WalletTopUpId, reason=TopUp)`. Idempotente (candado `UNIQUE(TenantId, OperationKey)`): un pago = una recarga aunque el evento se reentregue (at-least-once). `WalletTopUpPaymentFailed` → marca el `WalletTopUp` `Failed` (no acredita).

**Requisito upstream (BLOCKER-WAL-2):** PaymentApp debe añadir `SaaSPaymentType.WalletTopUp` (`SaaSPaymentType.cs:7-49`, siguiente valor), un `WalletTopUpDueConsumer` (espejo de `SubscriptionPlanChangeDueConsumer.cs:21-61`) que cobre Stripe off-session, y un brazo `WalletTopUp` en `SaaSPaymentResultPublisher.PublishByTypeAsync` (`SaaSPaymentResultPublisher.cs:76-119`) que emita succeeded/failed.

## 3. Eventos que Wallet PUBLICA (outbound, integración)

| Evento | Cuándo | Campos clave |
|---|---|---|
| `CampaignRunAuthorizedIntegrationEvent` | tras `Debit` del run con éxito | `RunId` |
| `CampaignRunAuthorizationDeniedIntegrationEvent` | saldo insuficiente (no debita) | `RunId`, `Reason="InsufficientFunds"` |
| `WalletTopUpDueIntegrationEvent` | tras `POST /wallet/top-up` | `TenantId`, `WalletTopUpId`, `AmountCents`, `Currency` |
| `WalletCreditedIntegrationEvent` *(opcional, BI)* | tras `Credit` (top-up) | `TenantId`, `AmountCents`, `PostedCentsAfter` |
| `BalanceAdjustedIntegrationEvent` | tras `Adjustment` | `SignedAmountCents`, `Reason`, `ActorId` |
| `BalanceLowWarningIntegrationEvent` | `PostedCents` cruza umbral hacia abajo | `PostedCents`, `ThresholdCents` (avisar al tenant de recargar) |

**Nota de correlación:** `RunId`/`WalletTopUpId` viajan como `ReferenceId` opaco; Campaigns casa `Authorized`/`Denied` con su run por `RunId`. El `CorrelationId` de Wolverine se propaga.

## 4. Lo que ya NO existe (modelo anterior)

| Elemento anterior | Reemplazo |
|---|---|
| `ReserveFundsCommand` / `FundsReservedIntegrationEvent` | — (sin holds) |
| `ConsumeReservationCommand` / `FundsConsumedIntegrationEvent` | el único `Debit` del run al autorizar |
| `RefundReservationCommand` / `FundsRefundedIntegrationEvent` | — (sin reembolso) |
| `ReservationExpirySweep` (timer) | — (sin holds que expirar) |

## 5. Timers internos

- `IdempotencyRetentionPurge` (opcional): si se adopta una tabla de claims de transporte, purga vencidos. En el modelo base, el ledger es permanente y no se purga.
- No hay sweep de expiración de reservas (ya no existen reservas).

## 6. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Patrón money-IN de PaymentApp a clonar (Due→charge→result) | `SubscriptionPlanChangeDueConsumer.cs:21-61`; `SaaSPaymentResultPublisher.cs:76-119` | VERIFIED | 90% |
| `SaaSPaymentType` (agregar `WalletTopUp`) | `SaaSPaymentType.cs:7-49` | VERIFIED | 99% |
| Shape del dispatch por unidad (para contar PerChannelUnits) | `CampaignDispatchEvents.cs:11-29`; `StartCampaignRunCommand.cs:177-192` | VERIFIED | 92% |
| Wolverine outbox/inbox durable at-least-once (regla de suite) | `00_Overview:45` | DOCUMENTED_ONLY | 88% |
| Dedupe de negocio por `UNIQUE(TenantId, OperationKey)` | `00_Plan §3` (diseño) | NEW | n/a |
| Eventos PEP + WalletTopUp | `00_Plan §5/§6` (diseño) | NEW | n/a |
