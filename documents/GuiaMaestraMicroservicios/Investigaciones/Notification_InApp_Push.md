# Notification: canales in-app (campana) y push, más la matriz de canales

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\`.
> Etiquetas: **[Código verificado]**, **[Documentación histórica]**, **[Recomendación]**, **[Decisión pendiente]**.

## 1. Estado actual

**[Código verificado]**

**In-app.** Notification decide y publica; Communication (Node) persiste, cuenta no leídos y emite por socket.
```
consumer (Notification) ─► NotificationDispatcher.RecordInAppAsync
   gate (inactivo + preferencia InApp) ─► NotificationLog (Sent) ─► guarda
   ─► notification.inapp.requested.v1 (outbox)
Communication ─► valida (zod) ─► NotificationEntry (dedupe TenantId+SourceEventId+UserId, GroupKey) 
   ─► socket room t:{tenantId}:u:{userId}: notification.received|updated + unread_count_changed
CRM/portal ─► GET /communication/notifications(/since, /unread-count), read, read-all, mark-many-*
```
Además hay consumers Node "legacy" que crean campanas directo; un flag por grupo (`InAppSourceGate`) decide quién publica para evitar duplicados.

**Push.** Todo en Notification: registro de dispositivos por API, fan-out por dispositivo, FCM HTTP v1 (Android, iOS vía token FCM y Web), revocación de token inválido y al suspender/retirar usuario. Communication no tiene push.

## 2. Evidencia

### 2.1 In-app (Notification)
- `src/Services/Notification/TaxVision.Notification.Application/Common/NotificationDispatcher.cs:97-120` `RecordInAppAsync(tenantId, recipient, subject, category, templateKey, relatedEventId, correlationId, recipientUserId?, inApp?)`.
- `src/Services/Notification/TaxVision.Notification.Application/Common/InAppDispatch.cs:27-78` gate → log `Sent` → `SaveChanges` → publica; `:90-94` kind fuera del catálogo → solo log + warning; `:96-124` trunca a los límites del consumer Node (título 200, cuerpo 1000, kind 64…) y resuelve `Category`, `Priority`, `TitleKey/BodyKey`, `ResourceType`, `Action` **desde el catálogo**.
- `src/Services/Notification/TaxVision.Notification.Application/Common/InAppNotificationRequest.cs` (datos del hecho: `Kind`, `RecipientUserId`, `SourceEventId`, `SourceEventType`, `OccurredAtUtc`, `FallbackTitle/Body`, `ResourceId`, `ResourceContext`, `Params`, `GroupKey`, actor).
- Contrato: `src/BuildingBlocks/Messaging/NotificationIntegrationEvents/InAppNotificationRequestedIntegrationEvent.cs:5-6` `[MessageIdentity("notification.inapp.requested.v1")]`.
- Catálogo: `src/Services/Notification/TaxVision.Notification.Domain/Catalog/NotificationCatalogEntry.cs:5-21` (`Kind`, `Category`, `DefaultPriority`, `RecipientPolicy`, `AllowedChannels`, `IsMandatory`, `TitleKey`, `BodyKey`, `ResourceType`, `Action`, `MandatoryChannels`); `NotificationCatalog.cs` conjuntos `InApp`, `Email`, `InAppEmail`, `InAppEmailPush`, `Transactional` (InApp+Email+Sms); `NotificationKinds.cs` (96 constantes).
- Doble fuente: `src/Services/Notification/TaxVision.Notification.Application/Abstractions/InAppSourceGate.cs` grupos `CustomerImport`, `Connectors`, `CloudStorageLegal`, `CloudStoragePolicy`, `Support`, `Signature`; por defecto `Communication` (`Notifications__InApp__<Grupo>__Source=Notification` para encender).
- Ejemplo real con deep link y agrupación: `src/Services/Notification/TaxVision.Notification.Application/Consumers/OfficeWispReviewDueConsumer.cs:163-201` (`ResourceContext = { section = "compliance" }`, `GroupKey = "tenant.wisp_review_due:{tenant}:{dueOn}:{stage}"`, `:197` "un aviso sin leer … se actualiza en vez de duplicarse").
- README: `src/Services/Notification/README.md` §"Puente in-app".

### 2.2 In-app (Communication, Node)
- `src/Services/Communication/src/application/event-handlers/notification-inapp-consumers.ts` esquema zod (`recipientUserId` uuid, `category` enum de 8 valores, `priority`, `action ∈ open|review|sign|pay|reconnect`, `sourceEventId` uuid, `occurredAtUtc` con offset); trunca en vez de rechazar; `CreatedAtUtc` = hora del servidor (`OccurredAtUtc` aparte); duplicado → no emite; emite `Received`/`Updated` y `UnreadCountChanged`.
- `src/Services/Communication/prisma/schema.prisma:1216` `@@unique([TenantId, SourceEventId, UserId])`; `:1218` `@@index([TenantId, UserId, GroupKey, ReadAtUtc])`.
- `src/Services/Communication/src/infrastructure/socket/socket-realtime-emitter.ts:57-64` `emitToUser` → room `t:{tenantId}:u:{userId}` (tenant en la clave de la room).
- `src/Services/Communication/src/api/http/routes/notifications.route.ts:52` lista, `:80` `/since`, `:104` `/unread-count`, `:116` `/read-all`, `:140` `/:id/read`, `:162` `/mark-many-read`, `:182` `/mark-many-unread`, `:202` `/delete-many`.
- `src/Services/Communication/src/contracts/socket/notification-socket-events.ts` canal separado de los eventos de sesión.
- Retención: `src/Services/Communication/src/infrastructure/schedulers/purge-scheduler.ts:35,90-91` (`notificationReadRetentionDays`, `notificationUnreadRetentionDays`).

### 2.3 Push
- `src/Services/Notification/TaxVision.Notification.Api/Controllers/PushDevicesController.cs:22` `notifications/push/devices`; `:24` actores TenantEmployee, TenantAdmin, CustomerPortal, PlatformAdmin; `:29` `POST` (`Platform`, `Token`, `DeviceId?`; `UserId` y `TenantId` del JWT); `:48` `DELETE {tokenId}`; rate limit `notification.g.push_device`.
- `src/Services/Notification/TaxVision.Notification.Application/Push/Commands/PushDeviceCommands.cs:32-37` token ya registrado **en el mismo tenant** → `Reactivate` con el usuario nuevo; `:56-72` revocar solo los propios.
- `src/Services/Notification/TaxVision.Notification.Domain/Notifications/PushDeviceToken.cs:6-11` `PushPlatform { Fcm, Apns, Web }`.
- `src/Services/Notification/TaxVision.Notification.Application/Common/PushDispatch.cs:62` `SendAsync`, `:74` `DispatchAsync` (gate → dispositivos activos → `SkipWhenNoDevices` → fan-out), `:152` dedupe por evento/kind/destinatario (`WasSentAsync`, `:267`), `:211-212` `TokenInvalid` → revoca ese dispositivo; reintento dirigido por `DeviceIds`; sin dispositivo → log `Skipped (no_device)`.
- `src/Services/Notification/TaxVision.Notification.Application/Abstractions/Senders.cs:39-48` `PushMessage(Token, Platform, Title, Body, Data, Topic, Tag, Link)`; `:54-61` `IPushAttemptRecorder` (rastro fuera de la transacción, NOT-07).
- `src/Services/Notification/TaxVision.Notification.Infrastructure/Push/FcmPushSender.cs:12-22` Firebase Admin SDK, inicialización perezosa y tolerante (sin credencial → no-op con log); `FcmOptions.cs:11-13` `Notification:Push:Fcm:ServiceAccountJsonPath`.
- `src/Services/Notification/TaxVision.Notification.Infrastructure/DependencyInjection.cs:160-168` `Notification:UseFcmPush` → `FcmPushSender`, si no `LoggingPushSender`.
- `src/Services/Notification/TaxVision.Notification.Application/Push/Consumers/UserLifecyclePushDeviceConsumer.cs` `UserDeactivated` y `UserOffboarded` → revoca todos los dispositivos (al reactivar no se restauran).
- Ejemplo de push con deep link: `OfficeWispReviewDueConsumer.cs:78-101` (`Data = { kind, resourceType, resourceId }`, `Link = settingsUrl`, `SkipWhenNoDevices = true`).
- Render de texto push: `IScribeRenderClient.RenderTextAsync` (`Channel = "Push"`) y `src/Services/Scribe/TaxVision.Scribe.Application/Templates/Validation/PushTemplateLength.cs`.

### 2.4 Gate común
- `src/Services/Notification/TaxVision.Notification.Application/Common/DispatchChannelGate.cs:11-35` inactivo primero, preferencia después, métricas `RecordNoRecipient`/`RecordSuppressedByPreference`.
- `NotificationDeliveryGate.cs:65-101` reglas de preferencia (ver `Notification_Email.md`).

## 3. Problemas

1. **[Código verificado] Doble fuente in-app.** Seis grupos pueden crear campana en Node o en Notification según flags espejo (`COMMUNICATION_INAPP_SOURCE_*` / `Notifications__InApp__*`). Un flag desalineado = duplicado o silencio. **[Documentación histórica]** la auditoría (2026-10-19/20) deja el "flip" por grupo pendiente de E2E.
2. **[Código verificado] "Sent" in-app = publicado**, no persistido en Node (comentario en `InAppDispatch.cs:74-76`). Un payload que Node rechace va a su DLQ sin reflejo en Notification.
3. **[Código verificado] Logout no revoca push.** Solo `UserDeactivated`/`UserOffboarded` revocan. No se encontró consumer de cierre de sesión; depende de que el front llame `DELETE`. El reuso de token es por tenant (`FindByTokenAsync(TenantId, Token)`): el mismo dispositivo puede quedar activo en dos oficinas.
4. **[Código verificado] vs [Documentación histórica] — flag de FCM.** `deploy/docker/docker-compose.yml:952` `Notification__UseFcmPush: ${NOTIFICATION_USE_FCM_PUSH:-true}`, pero `.github/workflows/deploy.yml:419-420` comenta que el default en compose es `false`. Y la auditoría de notificaciones marca "FASE 9 Push — DIFERIDA D8", mientras el código ya tiene push (Fase 7 del plan de notificaciones dinámicas).
5. **[Código verificado] Preferencias de push/in-app sin pantalla** (endpoint dormido): hoy todo encendido salvo lo que el catálogo marca.
6. **[Código verificado] Deep links in-app** dependen de que el front conozca `resourceType/resourceContext`; el push lleva URL absoluta por oficina (`Link`). Dos contratos distintos para el mismo destino.

## 4. Buenas prácticas observadas

- Catálogo único (kind → categoría, prioridad, canales, recurso, acción, obligatorio) con pruebas de arquitectura.
- In-app sin URL absoluta: recurso + acción; el front resuelve la ruta (independiente del host).
- Dedupe in-app por `(TenantId, SourceEventId, UserId)` y agrupación por `GroupKey` (actualiza en vez de duplicar).
- Rooms de socket con tenant en la clave.
- Push: fan-out best-effort, revocación por `TokenInvalid`, reintento solo a dispositivos con fallo transitorio, rastro fuera de la transacción.
- `UserId` del JWT, nunca del body, al registrar dispositivos.

## 5. Recomendación

- **[Recomendación]** Kind nuevo: constante en `NotificationKinds` + entrada en `NotificationCatalog` (canales permitidos, obligatorio o no, `ResourceType`, `Action`) + textos `TitleKey/BodyKey` en el front (CRM/portal `notification-i18n`).
- **[Recomendación]** In-app: siempre `SourceEventId = evt.EventId`, `GroupKey` cuando el aviso se repite, `ResourceId/ResourceContext` en vez de URL.
- **[Recomendación]** Push: `SkipWhenNoDevices = true` para avisos no críticos; `Data` con `kind/resourceType/resourceId` y `Link` con `TenantEmailLinks`.
- **[Recomendación]** Orden en el consumer: render del correo primero; luego campana y push por destinatario (así un reintento no los repite).
- **[Decisión pendiente]** Revocar dispositivos al cerrar sesión (evento de Auth) y política de token compartido entre oficinas.
- **[Decisión pendiente]** Unificar la fuente in-app (retirar los handlers Node legacy) tras el E2E por grupo.

## 6. Riesgos

- Push con datos de clientes en un teléfono tras logout.
- Campana duplicada o ausente por flags desalineados.
- Sin credencial FCM: el push termina en log (no falla el consumer).
- Node rechaza payload → DLQ de Communication.

## 7. Pruebas

### Existentes
| Archivo | Casos |
|---|---|
| `deploy/tests/TaxVision.Notification.Tests/NotificationDispatcherInAppTests.cs` | `RecordInAppAsync_con_contexto_publica_el_evento_con_campos_del_catalogo`, `RecordInAppAsync_mandatory_kind_omite_preferencia_apagada`, `RecordInAppAsync_suprimido_por_preferencia_no_publica_ni_loguea`, `RecordInAppAsync_kind_fuera_del_catalogo_guarda_el_log_y_no_publica`, `RecordInAppAsync_recorta_a_los_limites_del_consumer_node` |
| `.../NotificationDispatcherPushTests.cs` | `SendPushAsync_fanout_best_effort_un_dispositivo_entregado_ya_cuenta_como_Sent`, `SendPushAsync_revoca_el_device_cuando_el_proveedor_confirma_TokenInvalid`, `SendPushAsync_deduplica_un_evento_ya_enviado_por_kind_y_destinatario`, `A_device_retry_reaches_only_that_device_even_when_the_event_was_already_sent`, `Sin_dispositivo_el_log_queda_Skipped_no_device_y_no_llama_a_FCM` |
| `.../NotificationDispatcherInactiveRecipientTests.cs` | `Inactive_recipient_gets_no_push`, `Inactive_recipient_gets_no_in_app_notification` |
| `.../Lifecycle/UserLifecyclePushDeviceConsumerTests.cs` | `Deactivation_revokes_every_active_device_of_the_user`, `Offboarding_revokes_every_active_device_and_redelivery_is_a_noop` |
| `.../Architecture/NotificationCatalogArchitectureTests.cs` | `Every_kind_referenced_by_a_consumer_exists_in_the_catalog`, `Mandatory_in_app_entries_include_InApp_channel` |
| `.../Integration/PushFanOutIntegrationTests.cs` | Fan-out real (namespace `.Integration.`) |
| `src/Services/Communication/tests/unit/notification-inapp-consumers.test.ts`, `notification.test.ts`, `notification-queries.test.ts`, `notification-retention.test.ts`, `prisma-notification-repository.test.ts` | Consumer Node, dominio, consultas, retención, repositorio |

### Las que debe tener un servicio/consumer nuevo
- Kind nuevo presente en el catálogo (lo exige la prueba de arquitectura).
- Mismo evento reintentado → una sola campana (dedupe) y un solo push.
- `GroupKey` repetido sin leer → actualiza, no duplica.
- Usuario de la oficina A no recibe campana de la oficina B (room y `TenantId`).
- Destinatario inactivo o con preferencia apagada → nada; kind obligatorio → sale.
- Cliente del portal (`CustomerPortal`) recibe in-app con su `UserId` de portal.
- Push sin dispositivos con `SkipWhenNoDevices` → sin log de error.

## 8. Dependencias

Communication (persistencia, socket, consultas), Firebase (FCM), Auth (eventos de ciclo de vida; directorio de usuarios), RabbitMQ, frontends CRM/portal (i18n por `kind`, resolución de rutas).

## 9. Material para la guía

### Matriz de canales
| Canal | Responsable | Contrato de entrada | Preferencias | Idempotencia | Estado real | Pruebas base |
|---|---|---|---|---|---|---|
| Email | Notification (decide) + Scribe (render) + Postmaster (envío) | `IEmailDispatchGateway.QueueEmailAsync` → `notifications.email_send_requested.v1` | Gate solo si `RecipientUserId`+`Category`; contact policy del cliente a mano | `(TenantId, RelatedEventId, TemplateKey, To)` + `IdempotencyKey` `{eventId:N}:{to}` en Postmaster | Callbacks Sent/Delivered/Failed/Bounced/Suppressed | `EventBasedEmailDispatchGatewayTests` |
| SMS | Notification (decide) + Sms (envío, STOP, proveedor) | `NotificationDispatcher.SendSmsAsync` → `SmsSendRequestedIntegrationEvent` | Gate por `recipientUserId`; contact policy a mano; STOP en Sms | Token estable (`EventId`) → clave SHA-256 con teléfono | Solo en Sms (Notification no lo recibe) | `NotificationDispatcherSmsTests`, `IntegrationEventSmsSenderTests` |
| In-app | Notification (decide) + Communication (persiste/socket) | `RecordInAppAsync` + `InAppNotificationRequest` → `notification.inapp.requested.v1` | Gate siempre (InApp) | `(TenantId, SourceEventId, UserId)` + `GroupKey` | Node; Notification marca Sent al publicar | `NotificationDispatcherInAppTests` |
| Push | Notification (todo) + FCM | `SendPushAsync`/`DispatchPushAsync` con `PushDispatchRequest` | Gate siempre (Push) | Dedupe por evento/kind/destinatario | Log por envío; `TokenInvalid` revoca | `NotificationDispatcherPushTests` |

### Fragmentos reales
```csharp
// Campana (OfficeWispReviewDueConsumer)
await dispatcher.RecordInAppAsync(
    evt.OfficeTenantId, NotificationRecipient.ForUser(userId), notice.Title, Category,
    NotificationKinds.TenantWispReviewDue, evt.EventId, correlation.CorrelationId,
    recipientUserId: userId,
    inApp: new InAppNotificationRequest(
        NotificationKinds.TenantWispReviewDue, userId, evt.EventId, "tenant.office_wisp_review_due.v1",
        evt.OccurredOn, notice.Title, FallbackBody: notice.Body,
        ResourceId: evt.OfficeTenantId.ToString(),
        ResourceContext: new Dictionary<string, string> { ["section"] = "compliance" },
        GroupKey: $"tenant.wisp_review_due:{evt.OfficeTenantId:N}:{evt.DueOn:yyyyMMdd}:{evt.Stage}"),
    ct: ct);

// Push
await dispatcher.SendPushAsync(new PushDispatchRequest(
    evt.OfficeTenantId, userId, notice.Title, notice.Body, Category,
    NotificationKinds.TenantWispReviewDue, NotificationKinds.TenantWispReviewDue,
    evt.EventId, correlation.CorrelationId,
    Data: new Dictionary<string, string> { ["kind"] = NotificationKinds.TenantWispReviewDue, ["resourceId"] = evt.OfficeTenantId.ToString() },
    Link: settingsUrl, SkipWhenNoDevices: true), ct);
```
Config: `Notification__UseFcmPush`, `Notification__Push__Fcm__ServiceAccountJsonPath` (secreto de archivo), `Notifications__InApp__<Grupo>__Source`.
