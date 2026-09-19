# Email (SMTP2GO) — Commands & Events

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — Email NO es un ejecutor dedicado nuevo; es un CONSUMER dentro del servicio EXISTENTE `Notification`** (reusa `SendEmailCommand` con el seam `CampaignId`; SMTP2GO es solo el proveedor que usa Notification). Campaign es un orquestador agnóstico que **no envía**: publica `campaign.dispatch.requested.v1` por destinatario y este consumer lo procesa (`ActorType.Service`) y responde `campaign.dispatch.result.v1`. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un microservicio dedicado `TaxVision.Campaigns.Email` y/o un Wallet queda **superseded** por esta nota. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- Componente: **Consumer/handler dentro del servicio EXISTENTE `Notification`** (SMTP2GO = proveedor que usa Notification); persistencia dentro de Notification.
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- Mensajería: **Wolverine outbox/inbox durable**, at-least-once, handlers idempotentes. Nunca exactly-once.

## 1. Evento CONSUMIDO — dispatch por destinatario (contrato común)

Generaliza el seam `CampaignId` que hoy fluye Notification→Postmaster (`PostmasterEmailEvents.cs:37`). El transporte no interpreta `CampaignId`/`RecipientId`: los transporta de ida y los devuelve intactos en el result.

```csharp
[MessageIdentity("campaign.dispatch.requested.v1")]
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

    // Contenido INMUTABLE (fix #8): referencia + payload tipado; el contenido NO se muta tras publicar.
    public required ContentRef Content { get; init; }   // referencia inmutable al contenido (no bytes)
    // EmailPayload{ ScribeTemplateKey(revisión/hash CONGELADO), Subject, Variables } — NO una key mutable sola
    public EmailPayload? Payload { get; init; }
    // Cuerpo pre-renderizado por Scribe (camino normal), materializado desde el ContentRef/revisión congelada …
    public string? HtmlBody { get; init; }
    public string? TextBody { get; init; }
    public string? Subject  { get; init; }
    // … O se renderiza vía Scribe usando la ScribeTemplateKey CONGELADA + Variables del EmailPayload (fallback)

    public IReadOnlyList<EmailInlineAssetReference>? InlineAssets { get; init; } // referencia, no bytes
    public IReadOnlyList<Guid>? AttachmentFileIds { get; init; }                 // CloudStorage refs
    public string? ReplyTo { get; init; }
    public string? ListUnsubscribeUrl { get; init; }        // one-click, provisto por Campaigns
}
```
> Idéntico patrón de campos-nullable-por-origen que `NotificationsEmailSendRequestedIntegrationEvent` (`PostmasterEmailEvents.cs:25-73`), pero bajo el contrato canónico `campaign.dispatch.requested.v1` y con `Stream=Bulk` (aísla el tráfico bulk del transaccional de Notification; ver `Concurrency_Spec.md §4`). El mismo record sirve para envío transaccional individual (Attempt=1, CampaignId puede ser un id sintético de "single-send").

## 2. Comandos internos (dominio)
| Comando | Handler efecto | Idempotencia |
|---|---|---|
| `ProcessEmailDispatch` | crea `EmailDispatch(Pending)`; chequea suppression; render si aplica; POST SMTP2GO; `MarkSent`/`MarkFailed`/`MarkSuppressed`; emite result | unique `(RunId,RecipientId,Attempt)` + `ProcessedBusinessMessage(IdempotencyKey)` |
| `ApplyProviderWebhook` | proyecta `InboundWebhookEvent` a la transición del dispatch (`MarkDelivered/Bounced/Complained`) + upsert suppression | dedupe `provider_event_id` |
| `VerifyProviderDomain` | dispara verificación SMTP2GO; actualiza `FromDomainVerified` | por scope |
| `RotateProviderKey` | re-cifra `ApiKey`, `KeyVersion++` | — |

## 3. Evento EMITIDO — result por ATTEMPT (de vuelta a Campaigns)

Contrato canónico ÚNICO `campaign.dispatch.result.v1` (ver `../campaigns/`); correlación por `dispatch_id` opaco **por intento**. Mismos IDs de correlación de vuelta que el patrón Postmaster (`PostmasterEmailEvents.cs:103,119,137,151`). Campaigns agrega stats — (removido: disparo de Wallet consume/refund; sin dinero en el canal; ver banner).

```csharp
[MessageIdentity("campaign.dispatch.result.v1")]
public sealed record CampaignDispatchResultIntegrationEvent : IntegrationEvent {
    Guid TenantId, CampaignId, CampaignRunId, RecipientId; int Attempt;
    string DispatchId;               // correlación opaca POR INTENTO
    DispatchOutcome Outcome;         // Accepted | Delivered | Failed | Skipped | Unknown
    string? Reason;                  // detalle de Failed/Skipped/Unknown
    string? ProviderMessageId; DateTime EventAtUtc; }
```

`Outcome` canónico = `Accepted | Delivered | Failed | Skipped | Unknown`:
- **Accepted** — SMTP2GO aceptó/encoló el POST (HTTP 200 / "queued"). **NO** es entrega confirmada.
- **Delivered** — confirmada por **webhook** del proveedor (NO por el 200 de `email/send`).
- **Failed** — error pre-provider, 4xx definitivo, bounce duro o spam complaint.
- **Skipped** — no se envió (suppression hit).
- **Unknown** — timeout / sin evidencia; **reconciliable**, nunca se colapsa a `Failed`.

### 3.0 Alias interno → contrato canónico (un solo contrato de bus)
El `EmailDispatch` tiene estados internos más granulares que el `Outcome`; se PROYECTAN al único evento canónico (no son eventos de bus separados):

| Estado interno de email (alias) | → Outcome canónico | Campos extra |
|---|---|---|
| `Sent` (SMTP2GO 200/queued) | `Accepted` | `ProviderMessageId` |
| `Delivered` (webhook) | `Delivered` | `ProviderMessageId` |
| `Failed` (pre-provider / 4xx def.) | `Failed` | `Reason` |
| `Bounced` (webhook bounce) | `Failed` | `Reason=bounce:{type}`, `ProviderMessageId` |
| `Complained` (webhook spam) | `Failed` | `Reason=complaint` |
| `Suppressed` (no se envió) | `Skipped` | `Reason=suppressed:{reason}` |
| provider timeout / sin evidencia | `Unknown` | — (reconciliable) |

Todos portan `{TenantId, CampaignId, CampaignRunId, RecipientId, Attempt, DispatchId, EventAtUtc}`; `Accepted/Delivered` además `ProviderMessageId`.

### 3.1 Semántica de costeo
— (removido: sin dinero en el canal; ver banner). Cualquier autorización/costeo por balance es un interceptor/PEP externo y DIFERIDO, fuera de este canal.

## 4. Eventos de tracking (opcional, si se hostea open/click propio)
```
[MessageIdentity("campaigns.email.tracking.opened.v1")]
[MessageIdentity("campaigns.email.tracking.clicked.v1")]  // + string LinkUrl
```
Alimentan `CampaignStatistics` en Campaigns (no tocan saldo). **Deduplicados** (un open por dispatch cuenta una vez; corrige el double-count de `CampaignTrackingEvent` legado).

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
| Contrato canónico `campaign.dispatch.{requested,result}.v1` + proyección interna de estados de email | este diseño | NEW | n/a |
