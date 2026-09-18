# TaxVision.Sms — Commands & Events

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — SMS NO es un servicio nuevo; `TaxVision.Sms` YA EXISTE y ya es M2M** (`SendSmsBatchCommand`, `POST /sms/messages`, `ActorType.Service`). El canal SMS es un **CONSUMER dentro de `TaxVision.Sms`** que procesa `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un microservicio SMS nuevo y/o un Wallet queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- **Servicio:** SMS (`TaxVision.Sms`) — consumer del canal SMS **dentro de `TaxVision.Sms` (ya existente)**
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Mensajería: **Wolverine outbox/inbox durable, at-least-once** (nunca exactly-once). Todo handler idempotente (ver `Idempotency_Spec.md`). Eventos versionados por `[MessageIdentity("sms.….vN")]` (mismo patrón que `PostmasterEmailEvents.cs:24`).

## 1. Commands (intención interna, ejecutados por handlers)

| Command | Origen | Efecto | Idempotencia |
|---|---|---|---|
| `SendIndividualSmsCommand` | HTTP `/api/sms/send` | crea `SmsDispatch` (Segmented) — (removido: sin dinero en el canal; ver banner — antes "inicia saga Wallet") | `Idempotency-Key` header + fingerprint |
| `QuoteSmsCommand` | HTTP `/api/sms/quote` | calcula encoding/segments, sin persistir envío — (removido: sin dinero en el canal; ver banner — antes cost) | pura |
| `ProcessDispatchRequestCommand` | evento `campaign.dispatch.requested.v1` | crea `SmsDispatch` (≡ `CampaignDispatchAttempt`) para un destinatario de campaña; reconstruye el cuerpo desde `ContentRef`/`SmsPayload` | `(TenantId,CampaignRunId,RecipientId,Attempt)` ≡ `UNIQUE(run_id,recipient_id,attempt_no)` |
| `ApplyDeliveryReceiptCommand` | webhook status | transición `Accepted→Delivered/Failed/Undeliverable` — (removido: sin dinero en el canal; ver banner — antes Wallet consume/refund) | `(provider,providerMessageId,eventType)` |
| `ApplyInboundStopCommand` | webhook inbound | muta `SmsOptInRegistry` (STOP/START/HELP) | `(provider,providerMessageId)` |
| `ConfigureSmsProviderCommand` | HTTP config | cifra credenciales, guarda `SmsProviderConfig` | por tenant (upsert) |

## 2. Eventos consumidos (inbound)

### `campaign.dispatch.requested.v1` (de Campaigns, orquestador agnóstico)
```csharp
public sealed record SmsDispatchRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid TenantId { get; init; }
    public Guid? CampaignId { get; init; }        // opaco, se devuelve intacto (patrón PostmasterEmailEvents.cs:37,104)
    public Guid? CampaignRunId { get; init; }
    public required Guid RecipientId { get; init; } // estable por unidad, mismo entre intentos; manual ⇒ id generado, nunca "manual"
    public required int Attempt { get; init; }
    public required string IdempotencyKey { get; init; } // dispatch_id opaco, POR INTENTO
    public required string ToPhone { get; init; }  // E.164 o normalizable
    public required string MessageClass { get; init; } // Transactional|Marketing
    public required string ContentRef { get; init; }        // inmutable: ancla el texto congelado al crear
    public required SmsPayload Payload { get; init; }       // { TemplateRef, Variables } ⇒ texto EXACTO congelado
    public string? SenderIdRef { get; init; }
    // (removido: sin dinero en el canal; ver banner — antes CostQuoteCents / ReservationId)
    // El consumer RECONSTRUYE el cuerpo desde ContentRef/Payload; no adivina desde campos sueltos.
    // Reemplaza el par ad-hoc RenderedBody / TemplateRef+TemplateVariables.
}

public sealed record SmsPayload
{
    public required string TemplateRef { get; init; }
    public IReadOnlyDictionary<string,string> Variables { get; init; }
        = new Dictionary<string,string>();
}
```
**Nota:** — (removido: sin dinero en el canal; ver banner — antes reserva del estimate del run en Wallet y conciliación consume/refund). La autorización por balance es un interceptor/PEP externo y DIFERIDO. Ver `Transactional_Protocol.md`.

### ~~Eventos Wallet inbound~~
— (removido: sin dinero en el canal; ver banner — antes `SmsWalletReserved`/`ReserveDenied`/`Consumed`/`Refunded` como respuestas de Wallet).

## 3. Eventos publicados (outbound)

### ~~Wallet (solicitudes)~~
— (removido: sin dinero en el canal; ver banner — antes `SmsWalletReserveRequested`/`ConsumeRequested`/`RefundRequested`). La autorización por balance es un interceptor/PEP externo y DIFERIDO.

### Result hacia Campaigns — `campaign.dispatch.result.v1` (contrato result común)
Un único evento canónico en el bus (`campaign.dispatch.result.v1`) con `Outcome = Accepted | Delivered | Failed | Skipped | Unknown`. Los nombres `SmsDispatch*` son **aliases internos SMS-local**; el evento publicado es siempre el canónico con el `Outcome` correspondiente (no hay eventos `sms.*.vN` propios en el bus):

| Alias SMS-local | Evento canónico publicado | `Outcome` |
|---|---|---|
| `SmsDispatchAccepted` | `campaign.dispatch.result.v1` | `Accepted` (proveedor aceptó 2xx/queued) |
| `SmsDispatchDelivered` | `campaign.dispatch.result.v1` | `Delivered` (DLR de entrega) |
| `SmsDispatchFailed` | `campaign.dispatch.result.v1` | `Failed` (rechazo/DLR fallo no-retryable) |
| `SmsDispatchSuppressed` | `campaign.dispatch.result.v1` | `Skipped` (opt-out/STOP/blocked) |
| `SmsDispatchUnknown` | `campaign.dispatch.result.v1` | `Unknown` (timeout: sin respuesta o DLR ausente tras TTL — nunca `Failed`) |

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
    public required int Segments { get; init; }  // dato de facturación del proveedor; Campaign NO lo tarifa (sólo métrica)
    // (removido: sin dinero en el canal; ver banner — antes ActualCostCents)
    public required DateTime EventAtUtc { get; init; }
}
// Accepted lleva ProviderMessageId; Failed añade FailureCode/Reason; Suppressed añade SuppressionReason (opt-in/STOP);
// Unknown (timeout) no lleva FailureCode terminal (queda pendiente de reconciliación). — (removido: sin dinero en el canal; ver banner).
```

## 4. Mapa command/event → aggregate

```
campaign.dispatch.requested.v1 ─► ProcessDispatchRequestCommand ─► SmsDispatch.Create(Segmented)
                                   (reconstruye cuerpo desde ContentRef/SmsPayload) └─► [enviar a proveedor]
   provider 2xx ──────► SmsDispatch.Accepted ─► campaign.dispatch.result.v1(Accepted)
   send timeout ──────► (sin terminal) ───────► campaign.dispatch.result.v1(Unknown)   // nunca Failed
webhook DLR delivered ► ApplyDeliveryReceiptCommand ─► SmsDispatch.Delivered ─► campaign.dispatch.result.v1(Delivered)
webhook DLR failed ───► SmsDispatch.Failed ─► campaign.dispatch.result.v1(Failed)
DLR ausente tras TTL ─► (reconciliación) ──► campaign.dispatch.result.v1(Unknown)      // nunca Failed
opt-out post-freeze ─► guard re-check en envío ─► SmsDispatch.Suppressed ─► campaign.dispatch.result.v1(Skipped)
webhook inbound STOP ─► ApplyInboundStopCommand ─► SmsOptInRegistry.Stop() (alimenta la supresión de Campaign)
// (removido: sin dinero en el canal; ver banner — antes saga Wallet reserve/consume/refund)
```

## 5. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Eventos versionados con `[MessageIdentity]` | `PostmasterEmailEvents.cs:24` | VERIFIED | 97% |
| Result events con `CampaignId` nullable opaco | `PostmasterEmailEvents.cs:90-172` | VERIFIED | 97% |
| Wolverine outbox/inbox at-least-once | `00_Overview_And_Index.md` §Reglas duras | VERIFIED (política) | 95% |
| `ActorType.Service` ya permitido en el controller existente | `MessagesController.cs:21` | VERIFIED | 96% |
| Nombres/shapes SMS concretos | este documento | NEW | — |
