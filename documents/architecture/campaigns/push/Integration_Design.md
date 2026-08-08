# Push + In-app — Diseño de integración (canal REUSE)

Servicio: **Push (reusa `Notification`/`FcmPushSender`) + In-app (reusa `Communication`)**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**
Ancla: coherente con `../00_Overview_And_Index.md`, `../02_Context_Map.md`, `../05_Master_ADR.md`.

## 1. Qué es esto (y qué NO es)

No es un microservicio nuevo. Es la **integración de la suite de Campañas con dos servicios existentes** que ya entregan Push e In-app **por evento, a un solo destinatario**:

- **Push** → `Notification` (`FcmPushSender`, FCM HTTP v1) — hoy expuesto vía `NotificationDispatcher.SendPushAsync` (single-recipient).
- **In-app** → `Communication` (Node/Socket.IO) — hoy expuesto vía el use-case `pushNotification` (single-recipient), disparado consumiendo eventos de dominio.

El trabajo de diseño = definir el **contrato bulk/campaña** sobre esos senders single-recipient, el **fan-out por destinatario**, el **reporte de result de vuelta a Campaigns**, y **qué se agrega mínimamente** a cada servicio. Push/In-app **no** definen audiencia, **no** agendan, **no** tocan balance: solo entregan un destinatario y reportan.

## 2. Evidencia (clasificación + confianza)

| Hecho | Evidencia (file:line) | Clasif. | Conf. |
|---|---|---|---|
| Push real ya funciona, pero **single-recipient** | `Notification.Infrastructure/Push/FcmPushSender.cs:20,50` (`SendAsync(PushMessage)`, un token) | VERIFIED | 96% |
| Orquestación push existente es por-usuario (fan-out solo entre *dispositivos* de UN usuario) | `Notification.Application/Common/NotificationDispatcher.cs:79-165` (`SendPushAsync`, loop sobre `devices`) | VERIFIED | 96% |
| Contrato de sender push desacoplado del proveedor (`PushMessage`, `IPushSender`, `PushErrorCodes.TokenInvalid`) | `Notification.Application/Abstractions/Senders.cs:18-44` | VERIFIED | 97% |
| Token muerto → `TokenInvalid` → el caller revoca el device (no reintenta contra fantasma) | `FcmPushSender.cs:64-71`, `NotificationDispatcher.cs:150-151`, `Senders.cs:46-67` (`RevokeAsync`) | VERIFIED | 95% |
| Tokens de dispositivo son **por-tenant, únicos, self-service** (UserId del JWT, no del body) | `Notification.Domain/Notifications/PushDeviceToken.cs:19-73`, `Api/Controllers/PushDevicesController.cs` | VERIFIED | 95% |
| In-app real ya funciona, single-recipient, **idempotente por (tenant, sourceEventId, user)** | `Communication/.../use-cases/push-notification.ts:1-68`, `.../persistence/prisma-notification-repository.ts:9-36` (unique → P2002) | VERIFIED | 95% |
| Communication ya tiene **business-inbox** (dedupe de efecto) además del unique de arriba | `Communication/.../rabbit/consumer-runtime.ts:232` (`processedEvents.tryMarkProcessed`) | VERIFIED | 92% |
| Emisión realtime por-usuario ya existe (`emitToUser`, room `t:{tenant}:u:{user}`) | `Communication/.../ports/realtime-emitter.ts:38`, `contracts/socket/notification-socket-events.ts:45-55` | VERIFIED | 94% |
| El **seam `CampaignId` opaco** ya fluye Notification↔Postmaster (definidor lo pone, ejecutor lo devuelve intacto) | `BuildingBlocks/Messaging/EmailIntegrationEvents/PostmasterEmailEvents.cs:37,103-104,119-120` | VERIFIED | 97% |
| `NotificationLog` **nunca** guarda el cuerpo (solo canal/destinatario/plantilla/estado) | `Notification.Domain/Notifications/NotificationLog.cs:21-24` | VERIFIED | 96% |
| Contrato bulk/campaña sobre push/in-app | — no existe | NEW | 99% |
| Business-inbox en `Notification` (copia de `ProcessedBusinessMessage`) para dedupe por destinatario | Growth tiene el patrón; Notification no lo tiene aún | NEW | 90% |
| Legado: sender push **batchea en un loop síncrono**, sin idempotencia por destinatario, `ChannelConfiguration` dict sin esquema, precio push = 0 | `CRMTAXPROBACKEND/CampaignService/Infrastructure/Services/PushNotificationCampaignSender.cs:50,115-121,126-196` | VERIFIED | 94% |

## 3. Principio: Campaigns hace el fan-out; el ejecutor es por-destinatario

El anti-patrón legado (`PushNotificationCampaignSender.SendBatchAsync`, `:126-196`) mete el **loop de destinatarios dentro del sender** (síncrono, sin idempotencia, se pierde al reiniciar). Este diseño lo invierte:

```
Campaigns (owner del run + audiencia + outbox)
  └─ por CADA destinatario resuelto → publica UN evento dispatch (idempotente, outbox durable)
        ├─ Push  → Notification consume 1 evento = 1 destinatario
        │             → resuelve devices del user (ListActiveForUserAsync)
        │             → IPushSender.SendAsync por device (REUSE FcmPushSender, best-effort)
        │             → publica 1 result (Delivered/Failed/NoDevices/Suppressed) con CampaignId/RunId/RecipientId intactos
        └─ In-app → Communication consume 1 evento = 1 destinatario
                      → pushNotification use-case (createIfMissing idempotente)
                      → emitToUser (best-effort si hay socket; si offline, queda persistida)
                      → publica 1 result
  └─ Campaigns agrega results → Wallet consume entregados / refund no-entregados
```

**Notification/Communication siguen siendo por-destinatario.** Un evento dispatch = un destinatario. El fan-out (miles de destinatarios) es responsabilidad de Campaigns (su outbox + backpressure), no del ejecutor. Esto reusa `SendPushAsync`/`pushNotification` casi tal cual, envueltos por un consumer nuevo.

## 4. Identidad del destinatario (crítico)

Push e In-app **no** entregan a un email/teléfono: entregan a un **UserId interno** (destinatario que tiene una sesión / dispositivos registrados en este tenant). Por eso:

- La audiencia que Campaigns resuelve (vía `Customer`, ver `02_Context_Map`) debe producir **UserIds** para estos dos canales. Un contacto sin UserId es **no-entregable por push/in-app** (resultado explícito `NoDevices`/`NoRecipientUser`), **no** un `Sent` silencioso (anti-patrón legado `:71-77`, marca fail solo si `RecipientId==Guid.Empty` pero luego el monolito marcaba Sent a los no-fallidos).
- `RecipientId` en el contrato dispatch es **opaco para el ejecutor** (mismo criterio que `CampaignId`): Campaigns lo usa para correlacionar el result; Push/In-app solo necesitan el `TargetUserId` y lo devuelven intacto.

## 5. Render del contenido

- **Push**: título + cuerpo cortos. Se renderizan **aguas arriba** (Campaigns con Scribe/Fluid, ver `02_Context_Map`) y **viajan renderizados** en el evento dispatch. El ejecutor **no** re-renderiza (mismo criterio que email: `PostmasterEmailEvents` lleva el `HtmlBody` ya armado). `PushMessage.Data` (`Senders.cs:22`) transporta `ActionUrl`/deep-link/metadata tipada.
- **In-app**: `title`/`body`/`metadata` (deep-link, kind) también renderizados aguas arriba y persistidos por `Notification.create` (`Communication/.../notification.ts:46-83`, trunca title a 200 / body a 1000 — Campaigns debe respetar esos límites o el ejecutor los recorta).

## 6. Qué se agrega mínimamente

### 6.1 A `Notification` (Push) — .NET, dentro del deploy existente
1. **Contratos** (en `BuildingBlocks.Messaging`): eventos dispatch/result de push (ver `Commands_And_Events.md`), espejando el shape y la convención `[MessageIdentity]` de `PostmasterEmailEvents.cs`.
2. **Consumer** `CampaignPushDispatchConsumer`: dedupe por `(campaign, run, recipient, attempt)` → resuelve devices (`ListActiveForUserAsync`, `Senders.cs:54`) → envía por device reusando `IPushSender` → respeta preferencia del user (`IUserNotificationPreferenceRepository`, ver `NotificationDispatcher.cs:209-223`) → publica result. Reusa toda la lógica de `SendPushAsync` (incl. revocación por `TokenInvalid`); solo cambia el disparador (evento de campaña) y la idempotencia por destinatario.
3. **Business-inbox** (`ProcessedBusinessMessage`, copia-por-contexto desde Growth): Notification hoy **no** lo tiene → tabla + migración nueva.
4. **Cero** cambios a `FcmPushSender`, **cero** secretos nuevos (credenciales FCM ya existen, `FcmOptions.cs`).

### 6.2 A `Communication` (In-app) — Node, dentro del deploy existente
1. **Binding de consumer** nuevo en `ConsumerRuntime` para el evento in-app dispatch, mapeándolo a `pushNotification` con `sourceEventId` = clave determinística `(campaign, run, recipient, attempt)` → la doble idempotencia ya existente (`createIfMissing` unique `:30-33` + `ProcessedEventStore` `:232`) cubre el dedupe sin schema nuevo.
2. **Publicación del result** vía el outbox existente (`outbox-drainer.ts`).
3. **Cero** secretos nuevos; **cero** endpoints públicos nuevos.

## 7. Semántica de "entregado" por canal (define billing)

| Canal | Delivered = | NoDelivery no-billable | Fuente |
|---|---|---|---|
| Push | ≥1 dispositivo activo aceptado por FCM (best-effort multi-device) | `NoDevices` (sin tokens activos), `SuppressedByPreference` | `NotificationDispatcher.cs:116-165` |
| In-app | notificación **persistida** (`createIfMissing=true`); el emit por socket es best-effort (offline → la ve al reconectar) | `AlreadyDelivered` (dedupe, `created=false`), `NoRecipientUser` | `push-notification.ts:49-67` |

Campaigns **consume balance solo por `Delivered`**; `Failed` transitorio se reintenta (mismo `attempt` → dedupe), `Failed` permanente/`No*` → refund de la reserva (ver `../06_Cross_Service_Transactional_Protocol.md`).

## 8. Anti-patrones legado corregidos aquí

| Legado (`PushNotificationCampaignSender.cs`) | Corrección |
|---|---|
| Loop de destinatarios dentro del sender, síncrono (`:126-196`) | Fan-out por evento en Campaigns; ejecutor por-destinatario |
| Sin idempotencia por destinatario | Business-inbox `(campaign,run,recipient,attempt)` (Notification) + `createIfMissing`/`ProcessedEventStore` (Communication) |
| `ChannelConfiguration: Dictionary<string,string>` sin esquema (`:115-121`) | Evento dispatch tipado y versionado (`.v1`) |
| Sin entidad de run (recurrentes mutan una fila) | `CampaignRun` inmutable vive en Campaigns; result se ancla al `RunId` |
| Cost check no atómico / cobro fuera de un ledger (`:93-99`) | Reserve→consume/refund en Wallet; push/in-app nunca tocan saldo |
| `RecipientId==Empty`→fail, resto→Sent silencioso | `NoDevices`/`NoRecipientUser` explícitos y no-billable |

## 9. Blockers / dependencias duras

- **BLOCKER-PUSH-1**: audiencia debe resolver a **UserId** para estos dos canales; sin el mapeo contacto→userId en Campaigns, push/in-app no tienen destinatario direccionable. (Depende de `campaigns/` audiencia.)
- **BLOCKER-PUSH-2**: `Notification` necesita business-inbox (`ProcessedBusinessMessage`) — hoy no existe ahí. (NEW, ver `Deployment.md`.)
- **BLOCKER-PUSH-3**: compatibilidad de transporte de contratos entre **Wolverine (.NET, Notification/Campaigns)** y el **ConsumerRuntime propio de Communication (Node)** — el `MessageIdentity`/envelope debe alinearse (ver `Deployment.md §4`).
- **DECISIÓN abierta**: precio push/in-app. Legado = 0 (gratis). Si el catálogo de Wallet los deja en 0, la reserva/consumo es un movimiento de monto 0 pero el contrato saga es idéntico. Ver `../09_Open_Questions.md`.
