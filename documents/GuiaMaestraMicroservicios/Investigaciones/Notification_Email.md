# Notification: canal email (y contrato general para pedir una notificación)

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\`.
> Etiquetas: **[Código verificado]**, **[Documentación histórica]**, **[Recomendación]**, **[Decisión pendiente]**.

## 1. Estado actual

**[Código verificado]** No existe un evento genérico "NotificationRequested". El servicio de dominio publica **su propio evento de integración** (con `TenantId`) al exchange `taxvision-events`; un **consumer dentro de Notification** decide destinatarios, canales y textos. El flujo de email es:

```
Servicio X ──evento──► Notification (consumer)
   1. destinatarios (IRecipientResolver / IUserEmailResolver / proyecciones)
   2. links por oficina (ITenantHostResolver + TenantEmailLinks)
   3. render  ──POST scribe/render (M2M)──► Scribe
   4. IEmailDispatchGateway.QueueEmailAsync
        - gate (usuario inactivo + preferencia de correo) si trae RecipientUserId + Category
        - idempotencia (TenantId, RelatedEventId, TemplateKey, To)
        - NotificationLog (Pending) + NotificationDispatchAttempt (Queued)
        - guarda → publica notifications.email_send_requested.v1 (outbox)
Postmaster ──► dedup (TenantId + IdempotencyKey) ──► carril (System / TenantPreferred / Tenant) ──► proveedor
Postmaster ──callbacks──► Notification: succeeded→Sent, delivered→Delivered, failed, bounced, suppressed, provider_not_configured
```

## 2. Evidencia

### 2.1 Contrato de envío
- `src/Services/Notification/TaxVision.Notification.Application/Abstractions/IEmailDispatchGateway.cs:17-20` `QueueEmailAsync(EmailDispatchRequest)`.
- `:54-80` `EmailDispatchRequest`: `TenantId`, `To`, `Subject`, `HtmlBody`, `TextBody`, `TemplateKey`, `RelatedEventId`, `CorrelationId`, `IdempotencyKey?`, `Cc/Bcc`, `TemplateVariables`, `Scope`, `Stream`, `AttachmentFileIds`, `InlineAssets`, `Confidential`, `InlineAttachments`, `RecipientUserId?`, `Category?`, `RenderedTemplateVersion/LayoutVersion`, `NotificationKind?`.
- `:100-105` `EmailDispatchScope { System, Tenant, TenantPreferred }`; `:107-111` `Stream { Transactional, Bulk }`.
- Evento a Postmaster: `src/BuildingBlocks/Messaging/EmailIntegrationEvents/PostmasterEmailEvents.cs:25` `[MessageIdentity("notifications.email_send_requested.v1")] NotificationsEmailSendRequestedIntegrationEvent` (el cuerpo viaja en el evento).

### 2.2 Gateway
- `src/Services/Notification/TaxVision.Notification.Application/Common/EventBasedEmailDispatchGateway.cs`
  - `:41` adjuntos inline ≤ 256 KB.
  - `:62-80` **gate de entrega** solo si `RecipientUserId` y `Category` vienen: `NotificationDeliveryGate.IsAllowedAsync(..., NotificationChannel.Email, NotificationKind ?? TemplateKey)` → `Suppressed`.
  - `:88-117` idempotencia previa: `GetByRelatedEventIdAsync(TenantId, RelatedEventId, TemplateKey, To)`; si existe, no reenvía.
  - `:165-171` `IdempotencyKey = request.IdempotencyKey ?? "{RelatedEventId:N}:{to.ToLowerInvariant()}" ?? "{logId}:{attemptId}"`.
  - `:200-205` guarda y luego publica; `Confidential` → `SendToQueueAsync("postmaster-events")` (no al fanout).
- Gate: `src/Services/Notification/TaxVision.Notification.Application/Common/NotificationDeliveryGate.cs:35-58` usuario inactivo (directorio local; sin fila = activo); `:65-101` preferencia: `AccountSecurity` nunca se apaga (`:74`), lo obligatorio por catálogo para ese canal pasa (`:77-83`), sin `UserId` pasa (`:84-85`).
- `src/Services/Notification/TaxVision.Notification.Domain/Preferences/NotificationCategory.cs` categorías `AccountSecurity`, `DocumentsAndSignatures`, `StorageAndQuota`, `Billing`, `Collaboration`, `Reminders`, `ClientRequests`, `Calendar`; `NotificationCategoryRules.cs:10` `IsLocked`.
- Política de contacto del cliente (canal Email/SMS): `src/Services/Notification/TaxVision.Notification.Application/Common/CustomerContactPolicyFilter.cs` (`AllowsAsync`, `AllowsForOptionalCustomerAsync`; sin fila = permitido; externos siempre reciben). Solo para avisos no críticos.

### 2.3 Consumer de referencia
- `src/Services/Notification/TaxVision.Notification.Application/Consumers/OfficeWispReviewDueConsumer.cs`
  - `:23` `EmailEventKey = "tenant.wisp_review_due.v1"`, `:24` categoría.
  - `:18-20` (doc) el correo se renderiza **una vez y primero**: es lo único que puede lanzar; así un reintento no repite campana ni push.
  - `:54-67` destinatarios; sin destinatarios → log y salir.
  - `:70-71` host y URL por oficina.
  - `:118-128` direcciones deduplicadas por correo.
  - `:132-144` render + `EnsureRendered`.
  - `:146-160` `ToDispatchRequest(...) with { RecipientUserId = userId, Category = Category }` → el gateway aplica la preferencia.

### 2.4 Callbacks de Postmaster y estados
- `src/Services/Notification/TaxVision.Notification.Application/Consumers/Postmaster/PostmasterCallbackConsumers.cs`
  - `:47` `PostmasterEmailDeliverySucceededConsumer` → intento `Sent` (proveedor aceptó).
  - `:190` `PostmasterEmailDeliveredConsumer` → `Delivered`; si llega antes que `Sent` lanza `DependencyNotReadyException` (reintento programado).
  - `:94`, `:139`, `:234`, `:275` failed, bounced, suppressed, provider_not_configured.
  - `:27-44` `FindLogAsync` filtra por tenant del callback; mismatch → warning y descarte.
- `PostmasterOutboundEmailCallbackConsumers.cs` (correos manuales `OutboundEmailMessage`, incluye complained/engagement).
- Postmaster: `src/Services/Postmaster/TaxVision.Postmaster.Application/Consumers/NotificationsEmailSendRequestedConsumer.cs` (dedupe, carril, `SentMessage` en `Sending`; envío real fuera de la transacción por `DispatchSentMessageHandler`); `src/Services/Postmaster/TaxVision.Postmaster.Infrastructure/Idempotency/SqlIdempotencyGuard.cs` (tabla `EmailIdempotency`, PK compuesta); carriles en `Sending/Lanes/{SystemLaneSender,MailboxLaneSender,LanePreparation}.cs`.

### 2.5 Reintentos y DLQ
- `src/BuildingBlocks/Messaging/WolverineFailurePolicies.cs:63-73` permanentes (`JsonException`, `ArgumentException`, `FormatException`, `NotSupportedException`) → cola de errores; `:80-85` resto: 1 s, 5 s, 15 s; `:109-112` `DependencyNotReadyException` → reintento programado y luego cola de errores.
- `src/Services/Notification/TaxVision.Notification.Api/Program.cs:218` outbox durable; `:254-264` cola `notification-events` (bind al fanout, `MaximumParallelMessages(5)`, inbox durable); `:269-271` middleware de tenant; `:276-277` `UseTaxVisionConflictPolicy` + `ApplyStandardFailurePolicies`.
- Render: rechazo de Scribe → cola de errores directa; transitorio → reintento (`Scribe_Templates.md`).

### 2.6 Datos sensibles y retención
- `src/Services/Notification/README.md` §"Datos sensibles": asuntos OTP se guardan `[redacted]` (`SensitiveNotificationContent.ForStorage` en el gateway). §"Retención de logs": `NotificationRetentionJob`, 12 meses por defecto.

## 3. Problemas

1. **[Documentación histórica] vs [Código verificado] — gate de preferencias y email.** `feedback_gate_de_preferencias_no_cubre_email.md` (2026-08-14) dice que el correo "esquiva entero" el gate. **Hoy el gateway lo aplica**, pero solo si el consumer pasa `RecipientUserId` **y** `Category` (`EventBasedEmailDispatchGateway.cs:62-80`; prueba `Mandatory_template_ignores_email_preference`). Un consumer que no los pase sigue esquivando la preferencia y la inactividad. Es opt-in por llamada.
2. **[Código verificado] Idempotencia con destinatario: corregida.** La memoria `feedback_email_idempotency_key_must_include_recipient.md` describía el colapso de un fan-out; el código ya incluye `To` en el chequeo y en la clave (prueba `QueueEmailAsync_fans_out_to_distinct_recipients_but_dedupes_a_retry_for_the_same_recipient`). Un consumer que pase su propio `IdempotencyKey` sin destinatario reintroduce el bug.
3. **[Código verificado] Carrera sin lock.** El chequeo previo por `RelatedEventId` no tiene lock (comentario en `:156-162`); la defensa final es `SqlIdempotencyGuard` de Postmaster.
4. **[Código verificado] `RelatedEventId` nulo** → clave `{logId}:{attemptId}` nueva en cada reintento: sin idempotencia real.
5. **[Código verificado] Preferencias sin pantalla.** **[Documentación histórica]** `project_notifications_audit_owners_and_gaps.md`: `GET/PUT /notifications/preferences` está "dormido" por decisión del dueño (sin UI en CRM; el portal la retiró). La tabla vacía equivale a "todo encendido".
6. **[Código verificado] `InProcessEmailDispatchGateway`** sigue existiendo detrás de `Notification:UsePostmasterDispatch` (por defecto `true`); ignora logos inline y adjuntos.
7. **[Documentación histórica]** `project_carril_de_envio_por_layout_y_override_platformadmin.md`: Postmaster no leía `AttachmentFileIds` y el carril del buzón perdía el logo (2026-10-03). No se re-verificó hoy en Postmaster.

## 4. Buenas prácticas observadas

- Render primero y una sola vez; luego envíos por destinatario.
- `EnsureRendered` obligatorio (nunca `if (IsFailure) return`).
- Guardar antes de publicar; outbox transaccional.
- Correos con secretos por cola directa (`Confidential`).
- Estado real de entrega por callbacks (Sent ≠ Delivered), con tolerancia al desorden.
- Callbacks validados por tenant.
- Versión de plantilla/layout registrada en el log.

## 5. Recomendación

- **[Recomendación]** El servicio nuevo publica un evento propio con `TenantId`, `EventId` estable, IDs (no datos personales si no hacen falta) y fechas en UTC; el consumer va en `Notification/Application/Consumers/`.
- **[Recomendación]** Para staff, pasar siempre `RecipientUserId` + `Category` (y `NotificationKind` si el kind difiere del template) para que apliquen inactividad y preferencia.
- **[Recomendación]** Para clientes, gatear con `CustomerContactPolicyFilter` solo lo no crítico; lo transaccional (OTP, invitación a firmar) sale siempre.
- **[Recomendación]** `RelatedEventId = evt.EventId` siempre; no pasar `IdempotencyKey` propio salvo que incluya el destinatario.
- **[Recomendación]** Registrar el kind en `NotificationKinds` + `NotificationCatalog` (la prueba de arquitectura lo exige si el consumer lo referencia).
- **[Decisión pendiente]** Hacer el gate de email obligatorio (no opt-in) para destinatarios staff.

## 6. Riesgos

- Consumer sin `RecipientUserId`/`Category` → la pantalla de preferencias (cuando exista) mentiría.
- Scribe o Postmaster caídos → reintentos y DLQ (vigilada, alerta a 5 min según `WolverineFailurePolicies.cs:31-33`).
- `provider_not_configured` en carril `Tenant` si la oficina no configuró su buzón.
- Desorden de callbacks (Delivered antes que Sent) → reintento programado, no pérdida.

## 7. Pruebas

### Existentes
| Archivo | Casos |
|---|---|
| `deploy/tests/TaxVision.Notification.Tests/EventBasedEmailDispatchGatewayTests.cs` | `QueueEmailAsync_publishes_event_and_persists_log_in_Pending_Queued_state`, `QueueEmailAsync_sends_confidential_email_only_to_the_postmaster_queue`, `QueueEmailAsync_derives_a_stable_idempotency_key_from_RelatedEventId_across_retries`, `QueueEmailAsync_fans_out_to_distinct_recipients_but_dedupes_a_retry_for_the_same_recipient`, `SucceededCallback_transitions_attempt_to_Sent`, `Callback_for_unknown_log_is_silently_dropped`, `Mandatory_template_ignores_email_preference` |
| `.../PostmasterDeliveryStatusConsumersTests.cs`, `PostmasterOutboundEmailCallbackConsumersTests.cs` | Transiciones por callback |
| `.../OfficeWispReviewDueConsumerTests.cs` | Consumer de referencia (correo + campana + push) |
| `.../AuthAccountEmailsTests.cs` | Confidencial, token protegido, cliente→portal / empleado→CRM |
| `.../ScribeRenderFailureTests.cs`, `ScribeInlineAssetsPropagationTests.cs` | Render fallido, logos |
| `.../Integration/EmailConsumerTransactionIntegrationTests.cs` | Transacción real (namespace `.Integration.`, fuera del gate) |
| `.../Architecture/NotificationCatalogArchitectureTests.cs` | `Every_kind_referenced_by_a_consumer_exists_in_the_catalog` |
| `deploy/tests/TaxVision.Postmaster.Tests/...` | Dedupe y carriles (no detallado aquí) |

### Las que debe tener un consumer nuevo
- Fan-out a 2 destinatarios → 2 correos; reintento del mismo evento → 0 correos nuevos.
- Staff con preferencia apagada → `Suppressed`; categoría obligatoria → sale.
- Usuario inactivo → no sale.
- Oficina A → link y logo de A; oficina B → de B.
- Cliente → link de portal y `Audience = Client`; empleado → CRM.
- Scribe 5xx → excepción (reintento); 404 con código → cola de errores.
- Evento con `TenantId` vacío → falla cerrado.

## 8. Dependencias

Scribe (render M2M), Postmaster (envío y callbacks), Auth (`primary-host`, contacto de usuario, token M2M), proyecciones locales (`UserEmailDirectory`, owners, cuentas de portal, `CustomerContactPolicyProjection`), RabbitMQ + SQL Server (outbox/inbox).

## 9. Material para la guía

1. En el servicio de dominio:
   ```csharp
   // guardar el agregado y publicar en la misma transacción (Wolverine outbox)
   await bus.PublishAsync(new OrderShippedIntegrationEvent { TenantId = tenantId, OrderId = id, ShippedAtUtc = now });
   ```
   Declarar `[MessageIdentity(...)]` y `options.PublishMessage<...>().ToRabbitExchange("taxvision-events")`.
2. En Notification, consumer (forma real de `OfficeWispReviewDueConsumer`):
   ```csharp
   var render = (await scribe.RenderAsync(EmailEventKey, evt.TenantId, variables, ct)).EnsureRendered(EmailEventKey);
   foreach (var (userId, address) in addresses)
       await gateway.QueueEmailAsync(
           render.ToDispatchRequest(evt.TenantId, address, EmailEventKey, evt.EventId, correlation.CorrelationId)
               with { RecipientUserId = userId, Category = NotificationCategory.Collaboration },
           ct);
   ```
3. Plantilla en Scribe (ver `Scribe_Templates.md`) y kind en `NotificationKinds`/`NotificationCatalog`.
4. Checklist: render primero; `EnsureRendered`; `RelatedEventId = evt.EventId`; `RecipientUserId + Category` para staff; `Confidential` si hay token; contact policy para clientes en lo no crítico.
