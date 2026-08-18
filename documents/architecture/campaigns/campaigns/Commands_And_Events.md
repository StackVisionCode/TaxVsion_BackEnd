# Campaigns — Commands & Events

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Mensajería = **Wolverine outbox/inbox durable** (at-least-once; nunca exactly-once). Todo handler es idempotente (state-guard + `ProcessedBusinessMessage`). Los eventos de integración cross-context viven en `BuildingBlocks.Messaging` con `[MessageIdentity("...vN")]` versionado (mismo patrón que `PostmasterEmailEvents.cs:24`). Correlación opaca por `CampaignId`/`RunId`/`dispatchIdempotencyKey`, transportada sin interpretar por los ejecutores.

Coherente con `../06_Cross_Service_Transactional_Protocol.md`.

---

## 1. Commands internos (dentro de Campaigns)

| Command | Origen | Aggregate | Efecto | Guard |
|---|---|---|---|---|
| `CreateCampaign` | API | Campaign | crea `Draft` | permiso `campaigns:write` |
| `UpdateCampaign` | API | Campaign | edita | solo `Draft` |
| `MarkCampaignReady` | API | Campaign | `Draft→Ready` | validación completa |
| `ScheduleCampaign` | API | Campaign | fija ScheduleSpec + registra en Scheduler | `Ready`/`Scheduled` |
| `StartCampaignRun` | Scheduler (`RunDue`) / API trigger | CampaignRun | crea run, materializa audiencia, congela precio | lease válido + gate |
| `ReserveRunFunds` | saga | CampaignRun | pide Wallet RESERVE | `Created` |
| `DispatchRun` | saga | CampaignRun | fan-out por destinatario | `Reserving`→confirmado |
| `RecordDispatchResult` | ejecutor (evento) | CampaignRecipient | avanza DispatchState | idempotente por key |
| `RecordTrackingEvent` | ejecutor (evento) | CampaignRecipient | tracking set-once | dedupe providerEventId |
| `ReconcileRun` | saga (cierre) | CampaignRun | Wallet CONSUME/REFUND, fija CostActual | todos recipients terminales |
| `CancelRun` | API | CampaignRun | `Dispatching→Cancelling` | run activo |

Todas las mutaciones son **métodos del aggregate que devuelven `Result`** (no setters). Nada de lógica de negocio en el handler más allá de cargar → invocar método → persistir → publicar.

---

## 2. Eventos de integración EMITIDOS (Campaigns → bus)

Namespace propuesto `BuildingBlocks.Messaging.CampaignIntegrationEvents`. Versionados.

### Hacia Wallet (saga de balance)

```csharp
[MessageIdentity("campaigns.run.funds_reserve_requested.v1")]
public sealed record CampaignRunFundsReserveRequested : IntegrationEvent {
    public required Guid TenantId { get; init; }
    public required Guid WalletAccountId { get; init; }   // id opaco
    public required Guid RunId { get; init; }
    public required long AmountMinor { get; init; }       // USD cents
    public required string Currency { get; init; }        // "USD"
    public required string IdempotencyKey { get; init; }  // f(reserve, runId)
}

[MessageIdentity("campaigns.run.funds_consume_requested.v1")]  // entregados
[MessageIdentity("campaigns.run.funds_refund_requested.v1")]   // no-entregados / cancel
```

`AmountMinor`+`Currency` = copia local de `Money` (una por bounded context, no tipo compartido — ADR-CAMP-000 §Primitivas). Corrige el legado que pasaba `decimal` de dólares y hacía debit antes de `SaveChanges` (`CreateCampaignCommandHandler.cs:278,320`).

### Hacia ejecutores (dispatch, contrato COMÚN por destinatario)

```csharp
[MessageIdentity("campaigns.recipient.dispatch_requested.v1")]
public sealed record CampaignRecipientDispatchRequested : IntegrationEvent {
    public required Guid TenantId { get; init; }
    public required Guid RunId { get; init; }
    public required Guid RecipientId { get; init; }
    public required int AttemptNo { get; init; }
    public required string DispatchIdempotencyKey { get; init; } // f(RunId,RecipientId,AttemptNo)
    public required string Channel { get; init; }                // Email|Sms|WhatsApp|Push|InApp
    // destino resuelto (uno según canal) — PII mínima
    public string? Email { get; init; }
    public string? PhoneE164 { get; init; }
    public string? PushTokenRef { get; init; }
    // render por referencia — el ejecutor invoca Scribe; el cuerpo NO viaja como bytes crudos aquí
    public required string ScribeTemplateKey { get; init; }
    public IReadOnlyDictionary<string,string>? TemplateVariables { get; init; }
    public string? Subject { get; init; }                        // email
    // correlación opaca de vuelta (el ejecutor la devuelve intacta)
    public Guid? CampaignId { get; init; }
}
```

Este es el seam `CampaignId` generalizado a los 5 canales — mismo modelo que `NotificationsEmailSendRequestedIntegrationEvent.CampaignId` (`PostmasterEmailEvents.cs:37`), pero ahora emitido **por destinatario** (no por notificación transaccional suelta) y con `DispatchIdempotencyKey` explícito.

### Read-model / analytics (no transaccionales)

```csharp
[MessageIdentity("campaigns.run.completed.v1")]   // CostActual fijo, contadores finales
[MessageIdentity("campaigns.run.rejected.v1")]    // gate/reserve/saldo
```

---

## 3. Eventos de integración CONSUMIDOS (bus → Campaigns)

### Desde ejecutores (result común)

```csharp
[MessageIdentity("channel.dispatch_result.v1")]
public sealed record ChannelDispatchResult : IntegrationEvent {
    public required string DispatchIdempotencyKey { get; init; }
    public required string Outcome { get; init; }   // Delivered|Failed|Suppressed
    public string? ProviderMessageId { get; init; }
    public string? FailureCode { get; init; }
    public Guid? CampaignId { get; init; }           // devuelta intacta
    public required DateTime EventAtUtc { get; init; }
}

[MessageIdentity("channel.tracking_event.v1")]
public sealed record ChannelTrackingEvent : IntegrationEvent {
    public required string DispatchIdempotencyKey { get; init; }
    public required string Kind { get; init; }       // Open|Click|Bounce
    public required string ProviderEventId { get; init; }  // dedupe key
    public string? BounceType { get; init; }
    public required DateTime EventAtUtc { get; init; }
}
```

Se mapean 1:1 a las variantes existentes de Postmaster (`PostmasterEmailDeliverySucceeded/Failed/Bounced/Suppressed`, `PostmasterEmailEvents.cs:91-155`) — el contrato genérico es su generalización multicanal. El handler avanza `DispatchState` **con guard idempotente**: un result duplicado o fuera de orden no cambia estado ni doble-liquida (corrige `CampaignSendService.cs:63-68` que marcaba `Sent` a todos sin confirmación real).

### Desde Wallet

```csharp
[MessageIdentity("wallet.reservation.confirmed.v1")]  // Reserving -> Dispatching
[MessageIdentity("wallet.reservation.rejected.v1")]   // insufficient -> Rejected
[MessageIdentity("wallet.settlement.applied.v1")]     // consume/refund aplicado
```

### Desde Scheduler

```csharp
[MessageIdentity("scheduler.run_due.v1")]  // { campaignId, triggerKind, leaseToken, occurrenceKey }
```

`occurrenceKey` (p.ej. `campaignId:2026-08-04T09:00Z`) hace idempotente la creación del run: dos entregas del mismo `RunDue` crean **un** run (unique constraint sobre `(CampaignId, OccurrenceKey)`). Corrige el doble-scheduler legado (ADR-CAMP-000 §Anti-patrones #6).

---

## 4. Mapa de saga (resumen)

```
RunDue ─► StartCampaignRun ─► CampaignRunFundsReserveRequested
                                        │
                 wallet.reservation.confirmed ─► DispatchRun
                                        │
              (por destinatario) CampaignRecipientDispatchRequested ══► ejecutor
                                        │
                 channel.dispatch_result ─► RecordDispatchResult (idempotente)
                                        │  (todos terminales)
                                   ReconcileRun ─► funds_consume/refund_requested
                                        │
                              wallet.settlement.applied ─► CampaignRunCompleted
```

Detalle transaccional y compensaciones en `Transactional_Protocol.md`.

---

## 5. Reglas de emisión

- **Outbox durable:** todo evento se escribe en la misma transacción que muta el aggregate (Wolverine outbox); nunca `Task.Run` fire-and-forget (anti-patrón legado `CampaignSchedulerBackgroundService.cs:78-95`, `BackgroundTaskQueue`).
- **Tenant explícito** en el scope Wolverine al procesar (`.IgnoreQueryFilters()` + tenant del envelope), ver `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md` y `Security.md`.
- **At-least-once:** cada handler asume redelivery; idempotencia obligatoria (`Idempotency_Spec.md`).

---

## 6. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Patrón `[MessageIdentity(vN)]` + correlación opaca | `PostmasterEmailEvents.cs:24,37,104` | VERIFIED | 97% |
| Variantes result a mapear (succeeded/failed/bounced/suppressed) | `PostmasterEmailEvents.cs:91-155` | VERIFIED | 96% |
| Legado usaba `BackgroundTaskQueue`/`Task.Run` fire-and-forget | `CampaignSchedulerBackgroundService.cs:78-95` | VERIFIED | 95% |
| Legado debit antes de SaveChanges | `CreateCampaignCommandHandler.cs:278,320` | VERIFIED | 96% |
| Contrato dispatch común multicanal | diseño (este doc §2) | NEW | 86% |
| `occurrenceKey` idempotencia de run | diseño (este doc §3) | NEW | 85% |
