# Email (SMTP2GO) — Commands & Events

- Servicio: **TaxVision.Campaigns.Email**
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- Mensajería: **Wolverine outbox/inbox durable**, at-least-once, handlers idempotentes. Nunca exactly-once.

## 1. Evento CONSUMIDO — dispatch por destinatario (contrato común)

Generaliza el seam `CampaignId` que hoy fluye Notification→Postmaster (`PostmasterEmailEvents.cs:37`). El transporte no interpreta `CampaignId`/`RecipientId`: los transporta de ida y los devuelve intactos en el result.

```csharp
[MessageIdentity("campaigns.email.dispatch_requested.v1")]
public sealed record CampaignEmailDispatchRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid   TenantId { get; init; }
    public required Guid   CampaignId { get; init; }        // correlación opaca (no FK)
    public required Guid   CampaignRunId { get; init; }     // run inmutable origen
    public required Guid   RecipientId { get; init; }
    public required int    Attempt { get; init; }
    public required string IdempotencyKey { get; init; }    // (Campaign,Recipient,Attempt) canónico
    public required string To { get; init; }
    public required string ProviderScope { get; init; }     // "System" | "Tenant"

    // Cuerpo pre-renderizado por Scribe (camino normal) …
    public string? HtmlBody { get; init; }
    public string? TextBody { get; init; }
    public string? Subject  { get; init; }
    // … O plantilla a renderizar por el ejecutor vía Scribe (fallback)
    public string? TemplateKey { get; init; }
    public IReadOnlyDictionary<string,string>? TemplateVariables { get; init; }

    public IReadOnlyList<EmailInlineAssetReference>? InlineAssets { get; init; } // referencia, no bytes
    public IReadOnlyList<Guid>? AttachmentFileIds { get; init; }                 // CloudStorage refs
    public string? ReplyTo { get; init; }
    public string? ListUnsubscribeUrl { get; init; }        // one-click, provisto por Campaigns
}
```
> Idéntico patrón de campos-nullable-por-origen que `NotificationsEmailSendRequestedIntegrationEvent` (`PostmasterEmailEvents.cs:25-73`), pero namespaced a `campaigns.email.*` y con `Stream=Bulk`. El mismo record sirve para envío transaccional individual (Attempt=1, CampaignId puede ser un id sintético de "single-send").

## 2. Comandos internos (dominio)
| Comando | Handler efecto | Idempotencia |
|---|---|---|
| `ProcessEmailDispatch` | crea `EmailDispatch(Pending)`; chequea suppression; render si aplica; POST SMTP2GO; `MarkSent`/`MarkFailed`/`MarkSuppressed`; emite result | unique `(RunId,RecipientId,Attempt)` + `ProcessedBusinessMessage(IdempotencyKey)` |
| `ApplyProviderWebhook` | proyecta `InboundWebhookEvent` a la transición del dispatch (`MarkDelivered/Bounced/Complained`) + upsert suppression | dedupe `provider_event_id` |
| `VerifyProviderDomain` | dispara verificación SMTP2GO; actualiza `FromDomainVerified` | por scope |
| `RotateProviderKey` | re-cifra `ApiKey`, `KeyVersion++` | — |

## 3. Eventos EMITIDOS — result por destinatario (de vuelta a Campaigns)

Mismos IDs de correlación de vuelta que el patrón Postmaster (`PostmasterEmailEvents.cs:103,119,137,151`). Campaigns agrega stats; la saga de `../06_...` dispara Wallet consume/refund.

```csharp
[MessageIdentity("campaigns.email.dispatch.sent.v1")]        // aceptado por SMTP2GO
public sealed record CampaignEmailDispatchSentIntegrationEvent : IntegrationEvent {
    Guid TenantId, CampaignId, CampaignRunId, RecipientId; int Attempt;
    string? ProviderMessageId; DateTime EventAtUtc; }

[MessageIdentity("campaigns.email.dispatch.delivered.v1")]   // webhook delivered
[MessageIdentity("campaigns.email.dispatch.failed.v1")]      // + string Reason  (refund)
[MessageIdentity("campaigns.email.dispatch.bounced.v1")]     // + string BounceType, Reason
[MessageIdentity("campaigns.email.dispatch.complained.v1")]  // spam complaint
[MessageIdentity("campaigns.email.dispatch.suppressed.v1")]  // + string SuppressionReason (refund; no se envió)
```
Todos portan `{TenantId, CampaignId, CampaignRunId, RecipientId, Attempt, EventAtUtc}`; `sent/delivered/bounced` además `ProviderMessageId`.

### 3.1 Semántica de costeo (quién dispara qué en Wallet)
| Evento | Señal a la saga | Nota |
|---|---|---|
| `suppressed` | **refund** (no se llamó al provider) | terminal |
| `failed` | **refund** (pre-provider / 4xx definitivo) | terminal |
| `sent` | **consume** (aceptado) | opcional: consume diferido a `delivered` según política |
| `delivered` | confirma consume | — |
| `bounced` (hard) | (política) sin refund por defecto + suppression | provider ya procesó |
| `complained` | sin refund + suppression | entregado antes de queja |

## 4. Eventos de tracking (opcional, si se hostea open/click propio)
```
[MessageIdentity("campaigns.email.tracking.opened.v1")]
[MessageIdentity("campaigns.email.tracking.clicked.v1")]  // + string LinkUrl
```
No afectan Wallet; alimentan `CampaignStatistics` en Campaigns. **Deduplicados** (un open por dispatch cuenta una vez; corrige el double-count de `CampaignTrackingEvent` legado).

## 5. Reglas de emisión
- Todo handler que emite result lo hace **en la misma transacción** que la mutación del `EmailDispatch` vía la **outbox** de Wolverine (atomicidad estado↔evento; no fire-and-forget del legado).
- El scope tenant se propaga explícito al publicar (ver `Guia_IgnoreQueryFilters...`), no se infiere de ambient state.
- `IdempotencyKey` del inbound se propaga como clave de dedupe en `ProcessedBusinessMessage` del handler consumidor (Campaigns) también.

## 6. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Patrón campo-nullable-por-origen + `CampaignId` opaco | `PostmasterEmailEvents.cs:24-73` | VERIFIED | 96% |
| Result events succeeded/failed/bounced/suppressed ya existen para Postmaster | `PostmasterEmailEvents.cs:90-172` | VERIFIED | 96% |
| `ProcessedBusinessMessage` como business-inbox de dedupe | `Growth/.../Idempotency/ProcessedBusinessMessage.cs` | VERIFIED | 95% |
| Eventos `campaigns.email.*` nuevos | este diseño | NEW | n/a |
| Semántica consume/refund por evento | pendiente fijar en `../06_...` + `wallet-ledger/` | DOCUMENTED_ONLY | 70% |
