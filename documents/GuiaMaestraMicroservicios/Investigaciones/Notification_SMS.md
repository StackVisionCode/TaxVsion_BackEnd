# Notification: canal SMS (Notification → servicio Sms → proveedor)

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\`.
> Etiquetas: **[Código verificado]**, **[Documentación histórica]**, **[Recomendación]**, **[Decisión pendiente]**.

## 1. Estado actual

**[Código verificado]** Dos piezas con responsabilidades separadas:

| Pieza | Responsable de | No hace |
|---|---|---|
| Notification (`NotificationDispatcher.SendSmsAsync` → `SmsDispatch` → `ISmsSender`) | Gate (usuario inactivo + preferencia), `NotificationLog`, token de idempotencia estable, publicar `SmsSendRequestedIntegrationEvent` | No conoce proveedor, no aplica STOP, no cobra |
| Servicio Sms (`SmsSendRequestedConsumer` → `SendSmsBatchCommand`) | Validación E.164, STOP por (tenant, teléfono), política de contacto (si se pide), idempotencia por fila, failover de proveedores, webhooks/DLR, reconciliación, cobro al Wallet (solo envío manual) | No decide a quién avisar |

Proveedores registrados por atributo: `infobip`, `twilio`, `textmaxx`, `generic`, `fake` (este último solo con opt-in). El proveedor es **global de la plataforma** (no por oficina), con cadena de failover.

## 2. Evidencia

### 2.1 Notification
- `src/Services/Notification/TaxVision.Notification.Application/Common/NotificationDispatcher.cs:27-50` `SendSmsAsync(tenantId, phoneNumber, text, category, templateKey, relatedEventId, correlationId, recipientUserId?, idempotencyToken?)` — la categoría es obligatoria.
- `src/Services/Notification/TaxVision.Notification.Application/Common/SmsDispatch.cs:30-41` gate `DispatchChannelGate.AllowsAsync(..., NotificationChannel.Sms, ...)`; `:43-45` `NotificationRecipient.ForPhone` (solo exige no vacío: `src/Services/Notification/TaxVision.Notification.Domain/Notifications/NotificationRecipient.cs:44-47`); log `Sent`/`Failed` según el resultado de **publicar**; `:81-95` token = explícito → `RelatedEventId:N` → `{templateKey}:{userId:N}` → null.
- `src/Services/Notification/TaxVision.Notification.Application/Abstractions/Senders.cs:6-18` `ISmsSender.SendAsync(phone, text, ct, templateKey, idempotencyToken)` con la advertencia: sin token la clave se deriva del cuerpo y un OTP quedaría reversible desde el log.
- `src/Services/Notification/TaxVision.Notification.Infrastructure/Sms/IntegrationEventSmsSender.cs:27-70` publica `SmsSendRequestedIntegrationEvent` con `TenantId` del contexto (falla sin tenant), `CustomerId` = SHA-256 del teléfono (`:73-77`), `IdempotencyKey` = SHA-256 de `tenant|teléfono|templateKey|token` (o `tenant|teléfono|texto` sin token) (`:82-95`), `SourceContext = notification:sms:{templateKey}`.
- `src/Services/Notification/TaxVision.Notification.Infrastructure/DependencyInjection.cs:141-149` `Notification:UseSmsBridge` (por defecto `true`) → puente real; `false` → `LoggingSmsSender`.
- `src/Services/Notification/TaxVision.Notification.Api/Program.cs:242-244` `PublishMessage<SmsSendRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events")`.
- Contrato: `src/BuildingBlocks/Messaging/SmsIntegrationEvents/SmsSendRequestedIntegrationEvent.cs:13-14` `[MessageIdentity("BuildingBlocks.Messaging.SmsIntegrationEvents.SmsSendRequestedIntegrationEvent")]`.
- Política de contacto del cliente: `CustomerContactPolicyFilter` (canal `Sms`) en consumers informativos; **[Documentación histórica]** `project_notifications_audit_owners_and_gaps.md`: los 4 avisos informativos de Signature gatean el canal que van a usar; `SignerInvitedConsumer` y `SignerVerificationChallengeIssuedConsumer` **nunca** se gatean (transaccionales).

### 2.2 Servicio Sms
- `src/Services/Sms/TaxVision.Sms.Application/Messages/Consumers/SmsSendRequestedConsumer.cs:19-60` envuelve el evento en `SendSmsBatchCommand` con `ActorUserId: null`, `CanViewAll: false`; si el lote no se ejecuta, devuelve `SmsMessageFailedIntegrationEvent` con `MessageId` vacío.
- `src/Services/Sms/TaxVision.Sms.Application/Messages/Commands/SendSmsBatch.cs:44` `ChargeWallet = false` (solo el envío manual lo prende); `:51` `EnforceContactPolicy = false` por defecto ("Los avisos transaccionales (OTP, firma) la dejan en false: Notification ya decide"). Orquestador `[NonTransactional]`: reserva fila → proveedor fuera de transacción → resultado con evento.
- `src/Services/Sms/TaxVision.Sms.Application/Messages/SmsSendGate.cs:17-32` política de contacto antes de reservar saldo; `:59-75` `ApplySuppressionAsync`: política → `Suppressed` con código propio; STOP por (tenant, número) → `Suppressed`.
- `src/Services/Sms/TaxVision.Sms.Domain/ValueObjects/PhoneE164.cs` normaliza y exige `^\+[1-9]\d{6,14}$`.
- `src/Services/Sms/TaxVision.Sms.Domain/Consent/SmsKeywordParser.cs` STOP/STOPALL…, START/UNSTOP/YES, HELP/INFO/AYUDA; `src/Services/Sms/TaxVision.Sms.Application/Webhooks/Commands/ProcessWebhook.cs:211-262` aplica STOP/START al número.
- `src/Services/Sms/TaxVision.Sms.Application/SmsOptions.cs` `DefaultProvider = "fake"`, `ProviderOrder` (failover de plataforma), `MaxBatchSize = 1000`, reconciliación cada 60 s, timeouts de `Pending` (15 min) y resultado desconocido (60 min).
- `src/Services/Sms/TaxVision.Sms.Infrastructure/Providers/SmsProviderRegistration.cs` descubre `[SmsProvider]` por reflexión; valida al arranque que el proveedor elegido exista. Atributos: `Fake/FakeSmsProvider.cs:16` (`RequiresOptIn = true`), `Generic/GenericHttpSmsProvider.cs:21`, `Infobip/InfobipSmsProvider.cs:35`, `Textmaxx/TextmaxxSmsProvider.cs:34`, `Twilio/TwilioSmsProvider.cs:31`.
- Compose: `deploy/docker/docker-compose.yml:1526-1531` `Sms__DefaultProvider: ${SMS_DEFAULT_PROVIDER}` y `Sms__ProviderOrder__0..2`.
- API: `src/Services/Sms/TaxVision.Sms.Api/Controllers/MessagesController.cs:48-50` `POST sms/messages` (`sms.send`, `Idempotency-Key`), `SmsConsentController.cs:30` `POST sms/optouts`, `SmsReadController.cs` (lecturas, `messages/reconcile`), `WebhooksController.cs:32,50` `sms/webhooks/{provider}/status|inbound`.
- Cobro: `src/Services/Sms/TaxVision.Sms.Application/Messages/Abstractions/IWalletSpendClient.cs` + `Infrastructure/Wallet/WalletSpendClient.cs` (reserva/liquidación). Campañas: ya cobradas por Campaigns (`referenceType="campaign-run"`).
- Resultado de entrega: `SmsMessage{Accepted,Delivered,Failed,Suppressed}` los consume **Campaigns** (`src/Services/Campaigns/TaxVision.Campaigns.Application/Runs/Consumers/SmsDeliveryReportConsumer.cs`); **Notification no los consume**.

## 3. Problemas

1. **[Código verificado] El log de Notification dice "Sent" al publicar**, no al entregar. No hay consumer de `SmsMessageDelivered/Failed/Suppressed` en Notification: un SMS suprimido por STOP o rechazado por E.164 inválido queda como `Sent` en `NotificationLog`.
2. **[Código verificado] Validación de teléfono tardía.** Notification solo exige no vacío; la validación E.164 ocurre en Sms. El error no vuelve al log de Notification.
3. **[Código verificado] `CustomerId` sintético.** El puente manda `CustomerId = hash(teléfono)`, así que el `SmsSendGate` (si alguien activara `EnforceContactPolicy`) no encontraría la política real del cliente. La política del cliente para SMS transaccional se decide en Notification (`CustomerContactPolicyFilter`), no en Sms.
4. **[Código verificado] Sin token y sin `RelatedEventId`**, la clave del puente cae a `tenant|teléfono|texto` (dos SMS idénticos legítimos se colapsan; un OTP nuevo sí sale).
5. **[Código verificado] Proveedor global.** No hay número/remitente por oficina. **[Decisión pendiente]** si el SMS debe salir con identidad de la oficina.
6. **[Código verificado] `DefaultProvider = "fake"` en código** y `SMS_DEFAULT_PROVIDER` sin valor por defecto en compose: si falta el secreto, el validador falla el arranque (bien), pero el default de código no es productivo.
7. **[Documentación histórica]** `project_sms_module_readmodel_and_frontend_plan.md` (2026-09-19): SMS como addon de pago y reglas de UI (sin datos técnicos). No re-verificado hoy.
8. **[Documentación histórica]** El Vault de zonas horarias registra la decisión **P32** (ventana de SMS de campañas, recordatorios civiles a las 9:00); en Notification no se encontró ventana horaria para SMS transaccional.

## 4. Buenas prácticas observadas

- Notification no conoce al proveedor; el envío por bus es durable (si Sms está caído, el mensaje espera).
- Token de idempotencia independiente del texto (protege el OTP).
- STOP por (tenant, número) y política de contacto aplicadas antes del proveedor; lo suprimido no se cobra.
- Llamada al proveedor fuera de la transacción (BB-8) con reserva de fila previa.
- Failover de proveedores a nivel plataforma y reconciliación por pull como respaldo de los DLR.
- Cuerpos SMS sensibles redactados en almacenamiento (README de Notification §"Datos sensibles").

## 5. Recomendación

- **[Recomendación]** Un servicio nuevo nunca llama a Sms ni al proveedor: publica su evento y el consumer de Notification usa `dispatcher.SendSmsAsync(...)` con `relatedEventId: evt.EventId` y, para contenido sensible, `idempotencyToken` estable.
- **[Recomendación]** Pasar `recipientUserId` cuando el destinatario es staff (aplica inactividad y preferencia); para clientes, `CustomerContactPolicyFilter.AllowsAsync(..., CustomerContactChannel.Sms, ...)` solo en avisos no críticos.
- **[Recomendación]** Validar E.164 antes de llamar (o al menos no confiar en el log como prueba de entrega).
- **[Decisión pendiente]** Cerrar el ciclo de estado (Sms → Notification) para que el log refleje entrega/supresión.
- **[Decisión pendiente]** Ventana horaria y consentimiento (TCPA/10DLC) para SMS transaccional; hoy solo hay STOP.

## 6. Riesgos

- "No me llegó el SMS" no se diagnostica desde Notification (hay que ir a Sms).
- Número inválido → suprimido/fallido en Sms sin reflejo en el origen.
- Sin proveedor configurado → el servicio no arranca (validador), los eventos se acumulan en la cola.
- Costos: el envío transaccional no se cobra al Wallet (`ChargeWallet=false`); el costo lo absorbe la plataforma.

## 7. Pruebas

### Existentes
| Archivo | Casos |
|---|---|
| `deploy/tests/TaxVision.Notification.Tests/NotificationDispatcherSmsTests.cs` | `A_sent_sms_is_logged_as_Sent`, `A_failed_sms_is_logged_as_Failed_and_does_not_throw`, `The_explicit_token_wins`, `Without_a_token_the_event_id_is_used`, `An_inactive_recipient_gets_no_sms_and_no_log`, `A_disabled_preference_suppresses_the_sms` |
| `deploy/tests/TaxVision.Notification.Tests/IntegrationEventSmsSenderTests.cs` | `Publishes_sms_request_with_tenant_and_derived_identity`, `Idempotency_key_with_token_is_independent_of_text`, `Fails_when_no_tenant_context` |
| `deploy/tests/TaxVision.Sms.Tests/SmsSendRequestedConsumerTests.cs` | Consumer genérico |
| `deploy/tests/TaxVision.Sms.Tests/Application/SendSmsBatchHandlerTests.cs`, `SmsFailoverPolicyTests.cs`, `SmsProviderRouterTests.cs`, `ProcessWebhookHandlerTests.cs` | Lote, failover, ruteo, webhooks |
| `deploy/tests/TaxVision.Sms.Tests/Domain/PhoneE164Tests.cs`, `SmsKeywordParserTests.cs`, `SmsOptOutTests.cs` | E.164, palabras clave, opt-out |
| `deploy/tests/TaxVision.Sms.Tests/Providers/*ProviderTests.cs` | Infobip, Twilio, Textmaxx, Generic, registro |
| `deploy/tests/TaxVision.Sms.Tests/Integration/*` | Lote, liquidación, concurrencia, opt-out, política (namespace `.Integration.`) |

### Las que debe tener un consumer nuevo con SMS
- Mismo evento reintentado → una sola publicación con la misma `IdempotencyKey`.
- Dos destinatarios del mismo evento → dos claves distintas (la clave incluye el teléfono).
- Staff inactivo o con preferencia apagada → sin SMS ni log.
- Cliente con SMS apagado en su política → sin SMS (si el aviso no es crítico); OTP → sale igual.
- Oficina A/B → `TenantId` correcto en el evento (el puente lo toma del contexto).
- Sin tenant en contexto → falla (no publica).

## 8. Dependencias

Servicio Sms (proveedores, opt-out, Wallet), Customer (política de contacto vía proyección en Notification y en Sms), Auth (directorio de usuarios para inactividad), Wallet (solo envío manual), Campaigns (consume los resultados).

## 9. Material para la guía

1. En el consumer de Notification:
   ```csharp
   await dispatcher.SendSmsAsync(
       tenantId: evt.TenantId,
       phoneNumber: phone,
       text: body,                         // render de push/texto vía Scribe si aplica
       category: NotificationCategory.DocumentsAndSignatures,
       templateKey: "orders.shipped.sms",
       relatedEventId: evt.EventId,
       correlationId: correlation.CorrelationId,
       recipientUserId: staffUserId,       // null para clientes/externos
       idempotencyToken: null,             // obligatorio y estable si el texto lleva un secreto
       ct: ct);
   ```
2. Cliente no crítico: `CustomerContactPolicyFilter.AllowsAsync(tenantId, customerId, CustomerContactChannel.Sms, repo, ct)` antes de enviar.
3. Config: `Notification__UseSmsBridge` (true), `Sms__DefaultProvider`, `Sms__ProviderOrder__N`, `Sms__Providers__{code}__...` (secretos fuera del repo).
4. No usar `POST sms/messages` desde un servicio: es el envío manual del CRM (cobra Wallet y exige `sms.send`).
