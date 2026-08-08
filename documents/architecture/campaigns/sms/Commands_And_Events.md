# TaxVision.Sms — Commands & Events

- **Servicio:** SMS (`TaxVision.Sms`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Mensajería: **Wolverine outbox/inbox durable, at-least-once** (nunca exactly-once). Todo handler idempotente (ver `Idempotency_Spec.md`). Eventos versionados por `[MessageIdentity("sms.….vN")]` (mismo patrón que `PostmasterEmailEvents.cs:24`).

## 1. Commands (intención interna, ejecutados por handlers)

| Command | Origen | Efecto | Idempotencia |
|---|---|---|---|
| `SendIndividualSmsCommand` | HTTP `/api/sms/send` | crea `SmsDispatch` (Quoted), inicia saga Wallet | `Idempotency-Key` header + fingerprint |
| `QuoteSmsCommand` | HTTP `/api/sms/quote` | calcula encoding/segments/cost, sin persistir envío | pura |
| `ProcessDispatchRequestCommand` | evento `SmsDispatchRequested` | crea `SmsDispatch` para un destinatario de campaña | `(TenantId,CampaignId,RecipientKey,Attempt)` |
| `ApplyDeliveryReceiptCommand` | webhook status | transición `Accepted→Delivered/Failed/Undeliverable` + Wallet consume/refund | `(provider,providerMessageId,eventType)` |
| `ApplyInboundStopCommand` | webhook inbound | muta `SmsOptInRegistry` (STOP/START/HELP) | `(provider,providerMessageId)` |
| `ConfigureSmsProviderCommand` | HTTP config | cifra credenciales, guarda `SmsProviderConfig` | por tenant (upsert) |

## 2. Eventos consumidos (inbound)

### `SmsDispatchRequested` (de Campaigns) — `sms.dispatch_requested.v1`
```csharp
public sealed record SmsDispatchRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid TenantId { get; init; }
    public Guid? CampaignId { get; init; }        // opaco, se devuelve intacto (patrón PostmasterEmailEvents.cs:37)
    public Guid? CampaignRunId { get; init; }
    public required Guid RecipientId { get; init; }
    public required int Attempt { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string ToPhone { get; init; }  // E.164 o normalizable
    public required string MessageClass { get; init; } // Transactional|Marketing
    public string? RenderedBody { get; init; }     // si viaja resuelto; si null → TemplateRef
    public string? TemplateRef { get; init; }
    public IReadOnlyDictionary<string,string>? TemplateVariables { get; init; }
    public string? SenderIdRef { get; init; }
    public required long CostQuoteCents { get; init; } // estimate de Campaigns (reconciliado con actual)
    public Guid? ReservationId { get; init; }       // si Campaigns ya reservó a nivel run
}
```
**Nota de reserva:** en el flujo de campaña, Campaigns reserva el estimate del run en Wallet; SMS reporta el **actual** (segmentos reales) y Wallet concilia consume/refund. En el flujo individual, SMS reserva por sí mismo. Ver `Transactional_Protocol.md`.

### Eventos Wallet inbound (respuestas a las solicitudes de SMS)
`SmsWalletReservedIntegrationEvent` — `wallet.reserved.v1` (o el nombre del contrato de Wallet); `SmsWalletReserveDeniedIntegrationEvent`; `SmsWalletConsumedIntegrationEvent`; `SmsWalletRefundedIntegrationEvent`. Payload mínimo: `{ TenantId, ReservationId, DispatchId (scopeId), Outcome, AmountCents }`. (Los nombres canónicos los fija `wallet-ledger/Commands_And_Events.md`; SMS consume la forma acordada.)

## 3. Eventos publicados (outbound)

### Wallet (solicitudes; SMS nunca muta saldo)
- `SmsWalletReserveRequested` — `sms.wallet.reserve_requested.v1` `{ TenantId, DispatchId, AmountCents="USD", IdempotencyKey }`
- `SmsWalletConsumeRequested` — `sms.wallet.consume_requested.v1` `{ TenantId, DispatchId, ReservationId, ActualAmountCents, IdempotencyKey }`
- `SmsWalletRefundRequested` — `sms.wallet.refund_requested.v1` `{ TenantId, DispatchId, ReservationId, IdempotencyKey }`

### Result hacia Campaigns (contrato result común)
- `SmsDispatchAccepted` — `sms.dispatch_accepted.v1`
- `SmsDispatchDelivered` — `sms.dispatch_delivered.v1`
- `SmsDispatchFailed` — `sms.dispatch_failed.v1`
- `SmsDispatchSuppressed` — `sms.dispatch_suppressed.v1`

Shape común (espeja `PostmasterEmailDelivery*IntegrationEvent`, `PostmasterEmailEvents.cs:90-172`):
```csharp
public sealed record SmsDispatchDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid TenantId { get; init; }
    public Guid? CampaignId { get; init; }         // devuelto intacto
    public Guid? CampaignRunId { get; init; }
    public required Guid RecipientId { get; init; }
    public required int Attempt { get; init; }
    public required Guid DispatchId { get; init; }
    public string? ProviderMessageId { get; init; }
    public required int Segments { get; init; }
    public required long ActualCostCents { get; init; }
    public required DateTime EventAtUtc { get; init; }
}
// Failed añade FailureCode/Reason; Suppressed añade SuppressionReason (opt-in/STOP), sin costo.
```

## 4. Mapa command/event → aggregate

```
SmsDispatchRequested ─► ProcessDispatchRequestCommand ─► SmsDispatch.Create(Quoted)
                                                          └─► SmsWalletReserveRequested ─┐
SmsWalletReserved ────► (handler) ─► SmsDispatch.Reserved ─► [enviar a proveedor]        │ (saga)
   provider 2xx ──────► SmsDispatch.Accepted ─► SmsDispatchAccepted                      │
webhook DLR delivered ► ApplyDeliveryReceiptCommand ─► SmsDispatch.Delivered             │
                                                       ├─► SmsWalletConsumeRequested      │
                                                       └─► SmsDispatchDelivered ──────────┘
webhook DLR failed ───► SmsDispatch.Failed ─► SmsWalletRefundRequested + SmsDispatchFailed
webhook inbound STOP ─► ApplyInboundStopCommand ─► SmsOptInRegistry.Stop()
```

## 5. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Eventos versionados con `[MessageIdentity]` | `PostmasterEmailEvents.cs:24` | VERIFIED | 97% |
| Result events con `CampaignId` nullable opaco | `PostmasterEmailEvents.cs:90-172` | VERIFIED | 97% |
| Wolverine outbox/inbox at-least-once | `00_Overview_And_Index.md` §Reglas duras | VERIFIED (política) | 95% |
| Nombres/shapes SMS concretos | este documento | NEW | — |
| Nombres canónicos de eventos Wallet | pendiente de `wallet-ledger/` | DOCUMENTED_ONLY | — |
