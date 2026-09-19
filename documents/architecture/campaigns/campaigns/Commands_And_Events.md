# Campaigns — Commands & Events

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-09-17 (revisión v2 — contrato de contenido #02, Accepted/Unknown #03/#04, sender-status #19, routing #16; sin Wallet)
- **Estado:** DISEÑO — no implementado

Mensajería = **Wolverine outbox/inbox durable** (at-least-once; nunca exactly-once). Todo handler es idempotente (state-guard + `ProcessedBusinessMessage`). Los eventos de integración cross-context viven en `BuildingBlocks.Messaging.CampaignIntegrationEvents` con alias dotted-lowercase `.v1` (el tipo viaja en el header AMQP `type`; el lado Node lo mapea, ver `Guia_Creacion_Microservicio.md §3.5`). Correlación opaca por `CampaignId`/`RunId`/`DispatchId`, transportada sin interpretar por los ejecutores.

Coherente con `../06_Cross_Service_Transactional_Protocol.md`. **Campaign solo campaña — sin dinero:** no hay comandos ni eventos de reserve/consume/refund/costo/saldo, ni "ganchos" en `CampaignRun`. Los eventos que Campaign **sí** publica (`campaign.run.started/dispatch.requested/dispatch.result/run.completed.v1`) son los que un **interceptor de autorización (PEP)** y un **Wallet** externos y DIFERIDOS pueden interceptar/consumir por fuera para cobrar por saldo, sin que Campaign cambie. Ver `Domain_Design.md §8.1`, `../05_Master_ADR.md` D7.

---

## 1. Commands internos (dentro de Campaigns)

**Campaña:**
| Command | Origen | Aggregate | Efecto | Guard |
|---|---|---|---|---|
| `CreateCampaign` | API | Campaign | crea `Draft` | `[HasPermission("campaigns.manage")]` |
| `UpdateCampaignDraft` | API | Campaign | edita canales/contenido/audiencia | solo `Draft` |
| `SelectSender` | API | Campaign | fija `SenderRef` por canal | solo `Draft` |
| `SetAudience` | API | Campaign | fija AudienceSpec (Clients+Lists+Manual) | solo `Draft` |
| `TriggerCampaignNow` | API | Campaign→CampaignRun | `Immediate`: valida + `MarkReady` interno + crea run (NO estado `Sending`, fix #10) | validación completa + gate |
| `ScheduleCampaign` | API | Campaign | fija `SendMode` Scheduled/Recurring + registra en Scheduler | validación completa |
| `StartCampaignRun` | Scheduler (`run_due`) / TriggerNow | CampaignRun | crea run (encola `DispatchRun` durable), **materializa audiencia paginada** (aplica opt-out) | lease/gate válidos |
| `DispatchRun` / `EmitDispatchBatch` | interno (outbox) | CampaignRun | materializa por páginas y hace fan-out por lotes con checkpoint (fix #08/#12) | idempotente por cursor |
| `ApplyDispatchResult` | ejecutor (evento) | CampaignRecipient | avanza DispatchState (Accepted/Delivered/Failed/Unknown) | idempotente por `DispatchId` |
| `UnscheduleCampaign` | API | Campaign | cancela la **agenda** (Scheduled→Ready) | estado agendado |
| `CancelCampaignRun` | API | CampaignRun | cancela una **ejecución** por `runId` (drena in-flight) | run cancelable |

**Contactos / Listas (sub-dominio):**
| Command | Efecto | Guard |
|---|---|---|
| `CreateContact` / `UpdateContact` | alta/edición de contacto | `campaigns.manage` |
| `ImportContacts` | CSV → contactos (dedupe email/teléfono) | `campaigns.manage` |
| `SetContactOptOut` | opt-out por canal (consentimiento) | `campaigns.manage` |
| `CreateContactList` / `AddContactsToList` / `RemoveFromList` | gestión de listas | `campaigns.manage` |

**Remitentes:**
| Command | Efecto | Guard |
|---|---|---|
| `CreateSenderProfile` / `UpdateSenderProfile` | alta/edición de remitente por canal (referencia, sin secretos) | `campaigns.manage` |
| `DisableSenderProfile` | baja | `campaigns.manage` |

Todas las mutaciones son **métodos del aggregate que devuelven `Result`** (no setters). El handler solo: cargar → invocar método → persistir → publicar.

---

## 2. Eventos de integración EMITIDOS (Campaigns → bus)

### Hacia los ejecutores — contrato de DISPATCH común (por destinatario/canal)

```csharp
/// campaign.dispatch.requested.v1   (TenantId proviene de IntegrationEvent base)
public sealed record CampaignDispatchRequestedIntegrationEvent : IntegrationEvent {
    public required Guid RunId { get; init; }
    public required Guid RecipientId { get; init; }
    public required int AttemptNo { get; init; }
    public required string DispatchId { get; init; }   // f(RunId,RecipientId,Channel,AttemptNo) — dedupe del intento
    public required string Channel { get; init; }       // Email | Sms | WhatsApp | Push
    public required string SenderRef { get; init; }     // el ejecutor lo resuelve a su proveedor
    // destino resuelto (uno según canal) — PII mínima
    public string? Email { get; init; }
    public string? PhoneE164 { get; init; }
    public string? WhatsAppE164 { get; init; }
    public string? PushTokenRef { get; init; }
    // CONTENIDO por referencia inmutable + payload tipado por canal (fix #02/#18)
    public required string ContentRef { get; init; }    // apunta a la REVISIÓN congelada del contenido del run/canal
    public required CampaignChannelPayload Payload { get; init; } // union tipada y versionada por canal
    public Guid CampaignId { get; init; }               // correlación opaca (devuelta intacta)
}

/// Payload tipado por canal (versionado). El consumer SMS reconstruye EXACTO el texto de la API (fix #02).
public abstract record CampaignChannelPayload { public int SchemaVersion { get; init; } }
public sealed record EmailPayload : CampaignChannelPayload {         // Email
    public required string ScribeTemplateKey { get; init; }         // revisión inmutable (o hash) — fix #18
    public string? Subject { get; init; }
    public IReadOnlyDictionary<string,string>? Variables { get; init; }
}
public sealed record SmsPayload : CampaignChannelPayload {           // SMS
    public required string TemplateRef { get; init; }               // resuelve al texto congelado
    public IReadOnlyDictionary<string,string>? Variables { get; init; }
}   // (WhatsAppPayload: plantilla WABA; PushPayload: título+cuerpo) — análogos
```

Es el seam `CampaignId` generalizado a todos los canales — mismo modelo que `NotificationsEmailSendRequestedIntegrationEvent.CampaignId` (`PostmasterEmailEvents.cs:37`), pero emitido **por unidad destinatario/canal** y con `DispatchId` explícito. **El contenido viaja como `ContentRef` inmutable + `Payload` tipado por canal** — así el consumer SMS reconstruye exactamente el texto que muestra la API sin adivinar campos (fix #02), y el email usa una **revisión congelada** de plantilla (fix #18). **Campaigns nunca envía**: solo publica; cada ejecutor consume su `Channel`.

**Routing por canal (fix #16):** el contrato es común, pero **no** todos los canales comparten cola. Cada canal enruta a su propia cola de dispatch (`campaign.dispatch.<channel>`) y su cola de result; así un consumer no recibe unidades de otro canal ni se difunde PII a todos. El envelope lleva `type` + `channel` para el routing key.

### Ciclo de vida (read-model / analytics, no transaccionales)

```csharp
/// campaign.run.started.v1   { runId, campaignId, triggeredBy, recipientCount }
/// campaign.run.completed.v1 { runId, campaignId, counters finales }
```

`PublishMessage<T>()` por cada tipo publicado en `Program.cs` (guardrail: falta uno y el evento nunca sale del outbox, `Guia_Creacion_Microservicio.md §3.6`).

---

## 3. Eventos de integración CONSUMIDOS (bus → Campaigns)

### Desde los ejecutores — contrato de RESULT común

```csharp
/// campaign.dispatch.result.v1
public sealed record CampaignDispatchResultIntegrationEvent : IntegrationEvent {
    public required Guid RunId { get; init; }
    public required string DispatchId { get; init; }   // dedupe key (del INTENTO)
    public required string Outcome { get; init; }       // Accepted | Delivered | Failed | Skipped | Unknown (fix #03/#04)
    public string? ProviderRef { get; init; }
    public string? Reason { get; init; }
    public Guid CampaignId { get; init; }               // devuelta intacta
}

/// campaign.sender.status_changed.v1  (ejecutor → Campaigns; verificación/revocación de remitente, fix #19)
public sealed record CampaignSenderStatusChangedIntegrationEvent : IntegrationEvent {
    public required string Channel { get; init; }
    public required string SenderRef { get; init; }
    public required string Status { get; init; }         // Pending | Verified | Disabled
    public string? Reason { get; init; }
}
```

Cada ejecutor (Notification para Email/Push, `TaxVision.Sms` para SMS, WhatsApp nuevo) publica el result tras **aceptar/entregar**; un `200 OK`/"encolado" es `Accepted`, no `Delivered` (fix #03). El handler `ApplyDispatchResult` avanza `DispatchState` **con guard idempotente** por `DispatchId` del intento: duplicado/fuera de orden = no-op; timeout = `Unknown` por el sweeper (no un result). `campaign.sender.status_changed.v1` actualiza el `SenderProfile` de forma idempotente (fix #19). Se mapea a las variantes de Postmaster para email (`PostmasterEmailEvents.cs:104`).

### Desde Scheduler

```csharp
/// campaign.scheduler.run_due.v1  { campaignId, triggerKind, leaseToken, occurrenceKey }
```

`occurrenceKey` (p. ej. `campaignId:2026-08-04T09:00Z`) hace idempotente la creación del run: dos entregas del mismo `run_due` crean **un** run (unique constraint `(CampaignId, OccurrenceKey)`). Corrige el doble-scheduler legado (ADR-CAMP-000 §Anti-patrones #6).

---

## 4. Mapa de saga (resumen, sin dinero)

```
SendNow / run_due ─► StartCampaignRun (materializa audiencia, aplica opt-out)
                              │
     (por destinatario/canal) CampaignDispatchRequested ══► ejecutor de canal
                              │                                  (Notification / Sms / WhatsApp)
                              │◄══ CampaignDispatchResult ◄── entrega + reporte
                              │
              ApplyDispatchResult (idempotente por DispatchId) ─► RunCounters
                              │  (todos terminales)
                       CampaignRunCompleted
```

Detalle transaccional y resiliencia a reinicio en `Transactional_Protocol.md`.

---

## 5. Reglas de emisión

- **Outbox durable:** todo evento se escribe en la misma transacción que muta el aggregate (Wolverine outbox); nunca `Task.Run` fire-and-forget (anti-patrón legado `CampaignSchedulerBackgroundService.cs:78-95`).
- **Tenant explícito** en el scope Wolverine al procesar (`.IgnoreQueryFilters()` + tenant del envelope), ver `Security.md`.
- **At-least-once:** cada handler asume redelivery; idempotencia obligatoria (`Idempotency_Spec.md`).

---

## 6. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Seam `CampaignId` opaco + patrón de alias/versionado | `PostmasterEmailEvents.cs:24,37,104` | VERIFIED | 97% |
| Variantes result a mapear (succeeded/failed/bounced/suppressed) | `PostmasterEmailEvents.cs:91-155` | VERIFIED | 96% |
| SMS consumer M2M-ready (ActorType.Service) | `Sms/.../MessagesController.cs:21` | VERIFIED | 96% |
| `TenantId` en el base `IntegrationEvent` (no redeclarar) | `BuildingBlocks/Messaging/IIntegrationEvent.cs:15` | VERIFIED | 97% |
| Legado usaba `Task.Run` fire-and-forget | `CampaignSchedulerBackgroundService.cs:78-95` | VERIFIED | 95% |
| Contrato dispatch/result común multicanal | diseño (este doc §2-3) | NEW | 87% |
| `occurrenceKey` idempotencia de run | diseño (este doc §3) | NEW | 85% |
| Wallet diferido (va, pero no ahora); contrato listo para añadirlo | decisión del usuario 2026-09-16 | DECISION | 99% |
