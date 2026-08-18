# Push + In-app — Comandos y Eventos

Servicio: **Push (reusa `Notification`) + In-app (reusa `Communication`)**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**
Ancla: `Integration_Design.md`, `../06_Cross_Service_Transactional_Protocol.md`, `PostmasterEmailEvents.cs` (patrón base).

Todos los contratos **NEW**. Mensajería = **Wolverine outbox/inbox durable, at-least-once** (nunca exactly-once); dedupe de efecto por business-inbox. El shape espeja `NotificationsEmailSendRequestedIntegrationEvent`/`PostmasterEmailDelivery*` (`BuildingBlocks/Messaging/EmailIntegrationEvents/PostmasterEmailEvents.cs`): correlación **opaca** que el ejecutor transporta de ida y vuelta sin interpretar.

## 1. Convenciones

- Namespace propuesto: `BuildingBlocks.Messaging.CampaignDispatchEvents` (compartido .NET; consumido por Node vía el `eventType` string del `[MessageIdentity]`).
- Dinero: **no aparece** en estos contratos. Push/In-app **no** reciben ni reportan montos (`05_Master_ADR` regla dura: solo Wallet muta saldo; Campaigns hace reserve/consume según el `Outcome`).
- Correlación por-destinatario: `(CampaignId, RunId, RecipientId, Attempt)`. `RecipientId` y `CampaignId`/`RunId` son **opacos** para el ejecutor.
- `TargetUserId` = destinatario direccionable (UserId interno del tenant). Push/In-app **no** aceptan email/teléfono.

## 2. Dispatch (Campaigns → ejecutor), por destinatario

### 2.1 Push — `campaigns.push.dispatch_requested.v1`

```csharp
[MessageIdentity("campaigns.push.dispatch_requested.v1")]
public sealed record CampaignPushDispatchRequested : IntegrationEvent
{
    public required Guid TenantId { get; init; }          // tenant scope explícito (Wolverine)
    public required Guid CampaignId { get; init; }         // opaco (correlación)
    public required Guid RunId { get; init; }              // run inmutable (opaco)
    public required Guid RecipientId { get; init; }        // opaco (correlación)
    public required int  Attempt { get; init; }            // parte de la idempotency key
    public required Guid TargetUserId { get; init; }       // destinatario real (resuelve devices)
    public required string Category { get; init; }         // NotificationCategory → respeta preferencia
    public required string Title { get; init; }            // ya renderizado (Scribe aguas arriba)
    public required string Body  { get; init; }            // ya renderizado
    public IReadOnlyDictionary<string,string>? Data { get; init; } // ActionUrl/deep-link/badge (tipado)
    public required string IdempotencyKey { get; init; }   // determinística: campaign:run:recipient:attempt
}
```

### 2.2 In-app — `campaigns.inapp.dispatch_requested.v1`

Mismo shape que 2.1, salvo:
- Reemplaza `Data` por `Metadata: Record<string,unknown>` (deep-link/kind) — se persiste en `notificationEntry.MetadataJson`.
- Agrega `Priority: Low|Normal|High|Urgent` (mapea a `NotificationPriority`, `Communication/.../notification.ts:10-16`).
- `Kind: string` (ej. `campaign.broadcast`) → `notificationEntry.Kind`.
- **`SourceEventId` = `IdempotencyKey`**: Communication usa `sourceEventId` como clave de `createIfMissing` (unique `(TenantId,SourceEventId,UserId)`, `prisma-notification-repository.ts:30-33`) → dedupe sin schema nuevo.

## 3. Result (ejecutor → Campaigns), por destinatario

Un **único** evento result por canal con `Outcome` (más lean que los 5 eventos separados de Postmaster, pero misma correlación opaca). Campaigns lo agrega al `RunId` y decide consume/refund.

### 3.1 Push — `campaigns.push.dispatch_result.v1`

```csharp
[MessageIdentity("campaigns.push.dispatch_result.v1")]
public sealed record CampaignPushDispatchResult : IntegrationEvent
{
    public required Guid TenantId { get; init; }
    public required Guid CampaignId { get; init; }   // devuelto intacto
    public required Guid RunId { get; init; }         // devuelto intacto
    public required Guid RecipientId { get; init; }   // devuelto intacto
    public required int  Attempt { get; init; }
    public required string Outcome { get; init; }     // ver enum §3.3
    public int DevicesTried { get; init; }            // observabilidad
    public int DevicesDelivered { get; init; }
    public string? FailureReason { get; init; }       // null si Delivered
    public required DateTime EventAtUtc { get; init; }
}
```

### 3.2 In-app — `campaigns.inapp.dispatch_result.v1`

Mismo shape, sin `DevicesTried/Delivered`; agrega `bool Persisted` y `bool SocketEmitted` (emit best-effort; ver §5).

### 3.3 Enum `Outcome` (contrato común dispatch/result)

| Outcome | Billable | Push | In-app |
|---|---|---|---|
| `Delivered` | **Sí (consume)** | ≥1 device aceptado por FCM | notificación persistida (`created=true`) |
| `AlreadyDelivered` | No (idempotente) | inbox ya vio `(campaign,run,recipient,attempt)` | `createIfMissing=false` |
| `NoRecipientUser` | No (refund) | `TargetUserId` vacío/desconocido | idem |
| `NoDevices` | No (refund) | user sin tokens activos (`ListActiveForUserAsync`→0) | n/a |
| `SuppressedByPreference` | No (refund) | user opt-out (categoría no locked) | opt-out |
| `FailedTransient` | No (retry) | error FCM recuperable | error de infra recuperable |
| `FailedPermanent` | No (refund) | todos los devices fallaron def. | error no recuperable |

`AlreadyDelivered` **no** es un error: es la respuesta idempotente a un re-entrego at-least-once (el ejecutor re-emite el mismo result que la primera vez, o uno neutro que Campaigns ignora).

## 4. Idempotencia (por destinatario)

- **Push**: business-inbox `ProcessedBusinessMessage` (copia-por-contexto en Notification) con clave `(TenantId, "campaigns.push", IdempotencyKey)`. Se marca **antes** de publicar el result, en la misma transacción que el `NotificationLog` — un re-entrego encuentra la marca → `AlreadyDelivered`, no re-envía a FCM.
- **In-app**: doble guarda ya existente — `createIfMissing` (unique `(TenantId,SourceEventId,UserId)`) + `ProcessedEventStore.tryMarkProcessed` (`consumer-runtime.ts:232`). `SourceEventId=IdempotencyKey`.
- Clave = `campaign:run:recipient:attempt`. **`Attempt` distinto = envío distinto** (reintento deliberado de Campaigns tras `FailedTransient`), no un duplicado.

## 5. Realtime (solo In-app)

Tras `pushNotification`, Communication emite `NotificationSocketEvents.Received` (`contracts/socket/notification-socket-events.ts:52`) al room `t:{tenant}:u:{user}` (`realtime-emitter.ts:38`). El emit es **best-effort**: si el user está offline no hay socket, pero la notificación quedó persistida y aparece al reconectar (query de no-leídas). Por eso `Delivered` de in-app = **persistida**, no "socket ack'd" — `SocketEmitted` es solo telemetría.

## 6. Comandos internos (no cruzan el bus de la suite)

Ninguno nuevo público. Se **reusan** los comandos existentes de registro de dispositivo (fuera del alcance de campañas, self-service del usuario final):
- `RegisterPushDeviceTokenCommand` / `RevokePushDeviceTokenCommand` (`Notification.Application/Push/Commands/PushDeviceCommands.cs`, vía `PushDevicesController`). Poblan la proyección de devices que el consumer de campaña lee. **Sin cambios.**

## 7. Preferencias del usuario (gate por canal, ortogonal al balance)

El consumer push **debe** consultar `IUserNotificationPreferenceRepository.IsEnabledAsync` (patrón `NotificationDispatcher.cs:209-223`) con la `Category` del evento. Campañas = categoría **no-locked** (marketing/broadcast) → opt-out se respeta → `SuppressedByPreference`. Solo categorías locked (seguridad/cuenta) ignoran la preferencia, y una campaña **nunca** es locked.

## 8. Trazabilidad

Cada evento (dispatch y result) porta `CorrelationId` de `IntegrationEvent` + la tupla `(CampaignId,RunId,RecipientId,Attempt)`, generalizando el `CampaignId` opaco que hoy fluye Notification→Postmaster→callbacks (`PostmasterEmailEvents.cs:37,103,119`). El `NotificationLog` (push) referencia `CampaignId`/`RunId` pero **no** el cuerpo (`NotificationLog.cs:21-24`).
