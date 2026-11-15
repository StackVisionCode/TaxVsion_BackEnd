# Checklist — Notificaciones (email, SMS, in-app, push) y plantillas Scribe

> Propósito: que un servicio avise a staff o clientes sin llamar a proveedores: publica su propio evento y un consumer en Notification decide destinatarios, canales, textos, preferencias, idempotencia, zona horaria y enlaces.
> No existe un evento genérico "NotificationRequested": cada servicio publica su evento de integración con `TenantId`.

**Cuándo aplica:** el servicio necesita que alguien reciba un correo, SMS, campana o push.

**Formato:** *Verificación* = comando, prueba o archivo; *Origen* = investigación. Condicionales entre corchetes.

---

## 1. En el servicio de dominio

- [ ] **Evento propio** en `src/BuildingBlocks/Messaging/<Svc>IntegrationEvents/` con `TenantId`, `EventId` estable, IDs (no datos personales si no hacen falta) y fechas en UTC; `[MessageIdentity]`.
  - Verificación: `MessageIdentityTests` (BB). · Origen: [Notification_Email](../Investigaciones/Notification_Email.md) §5 y §9.1.
- [ ] **Guardar y publicar en la misma transacción (outbox)** y ruta `PublishMessage<T>().ToRabbitExchange("taxvision-events")`.
  - Verificación: `EventRoutingContractTests`, `SaveBeforePublishTests` (BB); prueba de rutas de runtime del servicio. · Origen: [Notification_Email](../Investigaciones/Notification_Email.md) §9.1.
- [ ] **El servicio no llama a Sms ni a proveedores** ni usa `POST sms/messages` (es el envío manual del CRM: cobra Wallet y exige `sms.send`).
  - Verificación: revisión de clientes HTTP del servicio. · Origen: [Notification_SMS](../Investigaciones/Notification_SMS.md) §5 y §9.4.
- [ ] **El servicio no arma links de correo**: publica `TenantId` (y el slug si lo tiene); el consumer de Notification compone la URL. Ver [Tenant_Links](Tenant_Links.md).
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §5.

## 2. Consumer en Notification (todos los canales)

- [ ] **Consumer en `Notification/Application/Consumers/`**, clase singular, `correlation.Push(...)`, kind nuevo en `NotificationKinds` + `NotificationCatalog` (categoría, canales, obligatorio, `ResourceType`, `Action`).
  - Verificación: `NotificationCatalogArchitectureTests.Every_kind_referenced_by_a_consumer_exists_in_the_catalog` y `Mandatory_in_app_entries_include_InApp_channel`. · Origen: [Notification_Email](../Investigaciones/Notification_Email.md) §5; [Notification_InApp_Push](../Investigaciones/Notification_InApp_Push.md) §5.
- [ ] **Orden**: render del correo primero (es lo único que puede lanzar), luego campana y push por destinatario; así un reintento no repite campana ni push.
  - Verificación: patrón `OfficeWispReviewDueConsumer` y su prueba. · Origen: [Notification_Email](../Investigaciones/Notification_Email.md) §2.3; [Notification_InApp_Push](../Investigaciones/Notification_InApp_Push.md) §5.
- [ ] **Sin destinatarios → log y salir**; direcciones deduplicadas por correo.
  - Verificación: prueba del consumer. · Origen: [Notification_Email](../Investigaciones/Notification_Email.md) §2.3.
- [ ] **Evento con `TenantId` vacío → falla cerrado.**
  - Verificación: prueba del consumer. · Origen: [Notification_Email](../Investigaciones/Notification_Email.md) §7.

## 3. Email (Notification → Scribe → Postmaster)

- [ ] **`EnsureRendered` obligatorio** (nunca `if (IsFailure) return`); Scribe sin mapping → cola de errores; 5xx → reintento.
  - Verificación: patrón `ScribeRenderFailureTests` (`No_mapping_goes_straight_to_error_queue`, `Server_error_is_retried`). · Origen: [Notification_Email](../Investigaciones/Notification_Email.md) §4 y §7.
- [ ] **`RelatedEventId = evt.EventId` siempre**; no pasar `IdempotencyKey` propio salvo que incluya el destinatario (la clave por defecto es `{RelatedEventId:N}:{to}`).
  - Verificación: prueba "fan-out a 2 destinatarios → 2 correos; reintento del mismo evento → 0 nuevos" (patrón `QueueEmailAsync_fans_out_to_distinct_recipients_but_dedupes_a_retry_for_the_same_recipient`). · Origen: [Notification_Email](../Investigaciones/Notification_Email.md) §3.2, §5 y §7.
- [ ] [si el destinatario es staff] **`RecipientUserId` + `Category`** (y `NotificationKind` si difiere del template): sin ellos no aplican inactividad ni preferencia.
  - Verificación: pruebas "preferencia apagada → `Suppressed`; categoría obligatoria → sale; usuario inactivo → no sale". · Origen: [Notification_Email](../Investigaciones/Notification_Email.md) §3.1 y §5.
- [ ] [si el destinatario es cliente y el aviso no es crítico] **`CustomerContactPolicyFilter.AllowsAsync(...)`**; lo transaccional (OTP, invitación a firmar) sale siempre.
  - Verificación: prueba "cliente con canal apagado → sin correo; OTP → sale". · Origen: [Notification_Email](../Investigaciones/Notification_Email.md) §5.
- [ ] [si el correo lleva token o secreto] **`Confidential = true`** (va directo a la cola de Postmaster, no al fanout).
  - Verificación: patrón `QueueEmailAsync_sends_confidential_email_only_to_the_postmaster_queue`. · Origen: [Notification_Email](../Investigaciones/Notification_Email.md) §4; [Tenant_Links](../Investigaciones/Tenant_Links.md) §2.4.
- [ ] **Estado real por callbacks** (Sent ≠ Delivered); el consumer no asume entrega al encolar.
  - Verificación: `PostmasterDeliveryStatusConsumersTests` como referencia. · Origen: [Notification_Email](../Investigaciones/Notification_Email.md) §2.4.

## 4. SMS (Notification → Sms → proveedor)

- [ ] **`dispatcher.SendSmsAsync(...)`** con `relatedEventId: evt.EventId`; `idempotencyToken` estable si el texto lleva un secreto.
  - Verificación: pruebas "mismo evento reintentado → misma `IdempotencyKey`; dos destinatarios → dos claves (incluye teléfono)". · Origen: [Notification_SMS](../Investigaciones/Notification_SMS.md) §5, §7 y §9.1.
- [ ] [si es staff] `recipientUserId`; [si es cliente y no crítico] `CustomerContactPolicyFilter.AllowsAsync(..., CustomerContactChannel.Sms, ...)`.
  - Verificación: pruebas "staff inactivo o con preferencia apagada → sin SMS ni log". · Origen: [Notification_SMS](../Investigaciones/Notification_SMS.md) §5 y §7.
- [ ] **Validar E.164 antes de llamar**: el log de Notification dice "Sent" al publicar, no al entregar.
  - Verificación: revisión del consumer; diagnóstico de entrega en Sms. · Origen: [Notification_SMS](../Investigaciones/Notification_SMS.md) §3.1-3.2 y §5.

## 5. In-app (campana) y push

- [ ] **In-app**: `RecordInAppAsync` con `SourceEventId = evt.EventId`, `GroupKey` cuando el aviso se repite, `ResourceId/ResourceContext` en vez de URL; textos `TitleKey/BodyKey` en el front (`notification-i18n` de CRM/portal).
  - Verificación: pruebas "mismo evento → una campana", "`GroupKey` repetido sin leer → actualiza", "usuario de A no recibe campana de B". · Origen: [Notification_InApp_Push](../Investigaciones/Notification_InApp_Push.md) §5 y §7.
- [ ] **Push**: `SendPushAsync` con `SkipWhenNoDevices = true` para avisos no críticos, `Data` con `kind/resourceType/resourceId` y `Link` con `TenantEmailLinks`.
  - Verificación: prueba "sin dispositivos → sin log de error"; patrón `NotificationDispatcherPushTests`. · Origen: [Notification_InApp_Push](../Investigaciones/Notification_InApp_Push.md) §5 y §7.
- [ ] [si el destinatario es cliente del portal] Campana con su `UserId` de portal.
  - Verificación: prueba del consumer. · Origen: [Notification_InApp_Push](../Investigaciones/Notification_InApp_Push.md) §7.

## 6. Plantilla en Scribe

- [ ] **Seed en `NotificationTemplateSeedSource.All`** (System) con `EventKey` = constante del consumer = mapping (no tiene que ser igual a `[MessageIdentity]`), `LayoutKey` según quién habla (`system-base` plataforma, `tenant-base` oficina), variables declaradas con tipo y descripción, y `ContentVersion` explícito mayor que el último.
  - Verificación: prueba "la plantilla declara todas sus variables y lleva su versión" (patrón `Wisp_review_template_declares_every_variable_it_uses_and_carries_version_23`); `All_seed_html_and_subjects_parse_as_fluid_and_pass_safety`. · Origen: [Scribe_Templates](../Investigaciones/Scribe_Templates.md) §3.2, §5, §7 y §9.1.
- [ ] **Saber que Scribe republica por hash de contenido** (`ContentHash`), no por `ContentVersion`: subir la versión sigue siendo la convención de etiqueta y lo exigen las pruebas.
  - Verificación: `ScribeNotificationTemplateSeederTests.Changed_html_without_bumping_content_version_is_republished`. · Origen: [Scribe_Templates](../Investigaciones/Scribe_Templates.md) §3.1.
- [ ] **Cada rama `if/else` renderizada con valores reales.**
  - Verificación: prueba de render de la plantilla. · Origen: [Scribe_Templates](../Investigaciones/Scribe_Templates.md) §7.
- [ ] **Audiencia explícita**: `ScribeRenderAudience.Client` para cliente, portal o externos; `ScribeReader.From(idioma, zona)` cuando se conoce al lector.
  - Verificación: prueba "cliente → `Audience = Client` y link de portal; empleado → CRM". · Origen: [Scribe_Templates](../Investigaciones/Scribe_Templates.md) §5; [Notification_Email](../Investigaciones/Notification_Email.md) §7.
- [ ] **Orden de despliegue: Scribe antes que Notification** cuando se agregan plantillas o variables tipadas.
  - Verificación: plan de despliegue del cambio. · Origen: [Scribe_Templates](../Investigaciones/Scribe_Templates.md) §5 y §9.4.

## 7. Zona horaria y fechas

- [ ] **Toda fecha a Scribe tipada**: `ScribeDates.Instant(utc)` (`{"$instant": ...}`) o `ScribeDates.Date(civil)` (`{"$date": ...}`); nunca "UTC" escrito ni formato propio.
  - Verificación: prueba "fecha tipada no contiene 'UTC' y sale en la zona de la oficina A (Nueva York) y de la oficina B (Los Ángeles)" (patrón `TypedDateTemplatesTests`). · Origen: [Scribe_Templates](../Investigaciones/Scribe_Templates.md) §1, §5 y §7.
- [ ] **Expiración de tokens en la plantilla** con `ScribeDates.Instant(expiresAtUtc)`.
  - Verificación: revisión del consumer. · Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §2.4 y §9.5.

## 8. Enlaces por oficina

- [ ] **Host con `ITenantHostResolver` + helpers de `TenantEmailLinks`**; destino por actor (`CustomerPortal` → `ClientBase`, staff → `StaffBase`). Detalle en [Tenant_Links](Tenant_Links.md).
  - Verificación: prueba "oficina A → host A; oficina B → host B" con `FakeTenantHostResolver`. · Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §5 y §7.

## Decisiones pendientes

- Gate de email obligatorio (no opt-in) para staff ([Notification_Email](../Investigaciones/Notification_Email.md) §5).
- Ciclo de estado Sms → Notification y ventana horaria/consentimiento para SMS transaccional ([Notification_SMS](../Investigaciones/Notification_SMS.md) §5).
- Revocar dispositivos push al cerrar sesión; unificar la fuente in-app ([Notification_InApp_Push](../Investigaciones/Notification_InApp_Push.md) §5).
- Override de oficina sin versión publicada y estrategia de idioma ([Scribe_Templates](../Investigaciones/Scribe_Templates.md) §5).
