# Wallet/Ledger — Commands & Events

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- Mensajería = **Wolverine outbox/inbox durable** (at-least-once, nunca exactly-once). Dedupe de efecto de negocio = `ProcessedBusinessMessage`.

---

## 1. Commands (aplicación)

Cada command → handler que carga `TenantBalance`, invoca método del aggregate (→ `Result`), persiste entry + saldo en UNA transacción, publica evento de dominio vía outbox. Todos envueltos por el ejecutor idempotente (`Idempotency_Spec.md`).

| Command | Origen | Scope M2M | Efecto | Evento emitido |
|---|---|---|---|---|
| `ReserveFundsCommand` | Campaigns / SMS (API) | `wallet:reserve` | `Reserve` | `FundsReservedIntegrationEvent` |
| `ConsumeReservationCommand` | Campaigns / SMS (API) | `wallet:consume` | `ConsumeReservation` | `FundsConsumedIntegrationEvent` |
| `RefundReservationCommand` | Campaigns / SMS (API) | `wallet:refund` | `Release/RefundRemainder` | `FundsRefundedIntegrationEvent` |
| `AdjustBalanceCommand` | Admin/Platform (API) | `wallet:adjust` | `Adjust` | `BalanceAdjustedIntegrationEvent` |
| `RechargeBalanceCommand` | **consumer interno** (no API) | n/a | `Recharge` | `BalanceRechargedIntegrationEvent` |
| `FreezeBalanceCommand`/`UnfreezeBalanceCommand` | Admin | `wallet:admin` | Freeze/Unfreeze | `BalanceFrozenIntegrationEvent` / `...Unfrozen...` |

Cada command lleva: `TenantId`, `Currency`, `AmountCents`(o signed), `ScopeId`, `IdempotencyKey`, y el `operation` (para `ProcessedBusinessMessage.Operation`).

## 2. Eventos que Wallet CONSUME (inbound)

### 2.1 Top-up — `WalletTopUpPaymentSucceededIntegrationEvent` (NUEVO, de PaymentApp)

Único camino a `Recharge`. Sigue el patrón exacto de los "PaymentSucceeded" existentes (`SubscriptionRenewalPaymentSucceededIntegrationEvent`: `SaaSPaymentId`, `IdempotencyKey`, `ExternalPaymentReference`, `PaidAtUtc` — ver `src/BuildingBlocks/Messaging/PaymentAppIntegrationEvents/SubscriptionRenewalPaymentSucceededIntegrationEvent.cs:9-15`).

```csharp
public sealed record WalletTopUpPaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid TenantId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required long AmountCents { get; init; }   // USD minor units
    public required string Currency { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string ExternalPaymentReference { get; init; }
    public required DateTime PaidAtUtc { get; init; }
}
```

**Consumer:** `WalletTopUpPaymentSucceededConsumer` → `RechargeBalanceCommand`. Idempotencia: `operation="recharge"`, `scopeId=SaaSPaymentId`, `key=IdempotencyKey` en `ProcessedBusinessMessage` → un pago = una recarga aunque el evento se reentregue (at-least-once). `SourceReference = SaaSPaymentId`.

**Requisito upstream (BLOCKER-WAL-2):** PaymentApp debe añadir `SaaSPaymentType.WalletTopUp = 9` a `src/Services/PaymentApp/TaxVision.PaymentApp.Domain/SaaSPayments/SaaSPaymentType.cs` (hoy va 1..8 hasta `OnboardingInitial`, línea 32) y emitir el evento tras el charge exitoso del top-up. El monto del top-up lo cobra PaymentApp (platform→tenant `SaaSPayment`); Wallet **solo** acredita al recibir el "succeeded".

## 3. Eventos que Wallet PUBLICA (outbound, integración)

Todos `IntegrationEvent` con `TenantId`, `Currency`, importes en cents, `ScopeId`, `IdempotencyKey`, `OccurredAtUtc`. Consumidores típicos: Campaigns (avanza su saga), Observabilidad/BI.

| Evento | Cuándo | Campos clave extra |
|---|---|---|
| `FundsReservedIntegrationEvent` | tras Reserve | `ReservationId`, `AmountCents`, `AvailableCentsAfter` |
| `FundsConsumedIntegrationEvent` | tras Consume | `ReservationId`, `ConsumedCents`, `RemainingCents`, `PostedCentsAfter` |
| `FundsRefundedIntegrationEvent` | tras Release/Refund/Expire | `ReservationId`, `ReleasedCents`, `Reason` (`completed`/`cancelled`/`expired`) |
| `BalanceRechargedIntegrationEvent` | tras Recharge (top-up) | `SaaSPaymentId`, `AmountCents`, `PostedCentsAfter` |
| `BalanceAdjustedIntegrationEvent` | tras Adjust | `SignedAmountCents`, `Reason`, `ActorId` |
| `BalanceLowWarningIntegrationEvent` | Available cruza umbral hacia abajo | `AvailableCents`, `ThresholdCents` (para avisar al tenant de recargar) |
| `ReservationExpiredIntegrationEvent` | sweep de holds vencidos | `ReservationId`, `ReleasedCents` |

**Nota de correlación:** `ScopeId` viaja opaco en todos los eventos, igual que `CampaignId` en `PostmasterEmailEvents.cs:37,104`; Wallet no lo interpreta, Campaigns lo usa para casar el resultado con su run.

## 4. Timers internos

- `ReservationExpirySweep` (job periódico): busca reservas `Held/Held(parcial)` con `ExpiresAtUtc < now`, ejecuta `Release` (motivo=expiry) idempotente. Evita holds colgados por consumidores que murieron sin cerrar (resiliencia que el legado no tenía — su `Task.Run`/poll loop se perdía al reiniciar, `05_Master_ADR.md:45`).

## 5. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Patrón "PaymentSucceeded" de PaymentApp a copiar para top-up | `PaymentAppIntegrationEvents/SubscriptionRenewalPaymentSucceededIntegrationEvent.cs:9-15` | VERIFIED | 96% |
| `SaaSPaymentType` enum 1..8, falta `WalletTopUp` | `PaymentApp.Domain/SaaSPayments/SaaSPaymentType.cs:8-32` | VERIFIED | 96% |
| Wolverine outbox/inbox durable at-least-once (regla de suite) | `00_Overview:45` | DOCUMENTED_ONLY | 88% |
| `ProcessedBusinessMessage` para dedupe de negocio | `Growth/.../Idempotency/ProcessedBusinessMessage.cs:9-124` | VERIFIED | 97% |
| Eventos Reserved/Consumed/Refunded/Recharged | diseño | NEW | n/a |
| `SaaSPaymentType.WalletTopUp=9` a crear upstream | diseño (BLOCKER-WAL-2) | NEW | n/a |
