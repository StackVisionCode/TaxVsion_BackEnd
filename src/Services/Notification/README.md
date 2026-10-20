# Notification

Notification decide qué aviso se envía, a quién, por cuáles canales y según qué preferencias. El envío material de email pertenece a Postmaster; SMS, push e in-app usan sus adaptadores o eventos dedicados.

## Campaign email webhook

`POST /notifications/campaign-email/webhooks/smtp2go` recibe estados de entrega de SMTP2GO y publica `campaign.dispatch.result.v1`.

- `CampaignEmail:WebhookSecret` es obligatorio. Si falta, el endpoint responde `503` y no publica eventos.
- Enviar el secreto como `Authorization: Bearer <secret>` es la opción recomendada.
- `X-Webhook-Secret` se conserva por compatibilidad.
- `?key=` se conserva temporalmente y genera un warning porque puede terminar en logs de infraestructura.
- Una credencial incorrecta responde `401`.
- La comparación usa hashes SHA-256 y `CryptographicOperations.FixedTimeEquals`.

En Docker, definir `CAMPAIGN_EMAIL_WEBHOOK_SECRET`. No guardar el valor en el repositorio.

## Retención de logs

`NotificationRetentionJob` elimina en lotes los `NotificationLogs` anteriores al periodo configurado. El barrido es deliberadamente cross-tenant, usa un lock distribuido en Redis y elimina también sus intentos de despacho.

- `Notification:Retention:Enabled`: habilita el job.
- `Notification:Retention:RetentionMonths`: meses conservados; por defecto `12`.
- `Notification:Retention:BatchSize`: tamaño máximo de cada lote; por defecto `500`.
- `Notification:Retention:IntervalHours`: intervalo entre ejecuciones; por defecto `24`.
- `Notification:Retention:LockMinutes`: duración del lock distribuido; por defecto `30`.

En Docker se configuran con `NOTIFICATION_RETENTION_ENABLED`, `NOTIFICATION_RETENTION_MONTHS`, `NOTIFICATION_RETENTION_BATCH_SIZE`, `NOTIFICATION_RETENTION_INTERVAL_HOURS` y `NOTIFICATION_RETENTION_LOCK_MINUTES`.

## Puente in-app (Notification → Communication)

`NotificationDispatcher.RecordInAppAsync` escribe el `NotificationLog` de auditoría y, cuando recibe un `InAppNotificationRequest`, publica `notification.inapp.requested.v1` (outbox transaccional) hacia Communication, que entrega la campana. El contrato lleva `Kind`, `Category`, `Priority`, `TitleKey`/`BodyKey`, `ResourceType`/`Action` resueltos desde `NotificationCatalog`, más los datos del hecho (actor, recurso, `Params`, `FallbackTitle`/`FallbackBody`, `GroupKey`, `SourceEventId`).

- El gate de preferencias (canal in-app) se aplica antes de publicar; lo obligatorio salta el filtro de suscripción, no el de capacidad.
- Si el `Kind` no está en el catálogo no se publica nada (solo queda el log) y se registra un warning.
- `SourceEventId` = `EventId` del hecho original, para que el dedupe `(TenantId, SourceEventId, UserId)` de la bandeja siga funcionando.

## Audiencias locales

`IRecipientResolver` resuelve destinatarios sin HTTP durante el manejo de eventos. Valida usuario activo, capacidad, modulo habilitado y exclusion del actor antes de publicar.

- `TenantOwnerAudience`: selecciona el TenantAdmin activo mas antiguo del tenant.
- `CustomerPortal`: resuelve la cuenta de portal activa del cliente.
- `MailboxAudience`: usa el owner de un buzon personal; para oficina usa preparadores asignados y, sin asignacion, TenantAdmins con `connectors.accounts.office.read`.
- `Participants` y `ByPermission`: validan participantes explicitos o la interseccion de capacidad y permiso de suscripcion.

Las proyecciones `TenantOwnerProjections` y `CustomerPortalAccountProjections` se mantienen con eventos de Auth y con un backfill M2M periodico. Configuracion:

- `Notification:RecipientProjections:AuthBaseUrl`
- `Notification:RecipientProjections:ReconciliationEnabled`
- `Notification:RecipientProjections:ReconciliationIntervalHours`
- `Notification:RecipientProjections:ReconciliationPageSize`

El cliente M2M adquiere un token para `PlatformTenant` y llama directamente a los endpoints internos de Auth. Un fallo conserva la proyeccion actual y se reintenta en la siguiente ejecucion.

## Alertas operativas

Notification lleva a la campana las alertas de billing, cuenta de pagos, llamadas perdidas y documentos subidos por clientes. Billing usa owner mas suscriptores de `notifications.billing.alerts`, siempre intersectados con `billing.view`; los pagos de facturas confirmados notifican solo a suscriptores. Los documentos del portal se notifican al preparador asignado despues de que CloudStorage completa el escaneo seguro.

Las alertas de oficina usan dos llaves. El permiso `notifications.*.alerts` expresa la suscripcion, pero nunca reemplaza la capacidad operativa:

- Billing: owner obligatorio mas suscriptores de `notifications.billing.alerts`, validados con `billing.view`.
- Storage: owner obligatorio mas suscriptores de `notifications.storage.alerts`, validados con `cloudstorage.settings.manage` y el modulo habilitado.
- Security: usuario afectado mas owner y suscriptores de `notifications.security.tenant_alerts`, validados con `audit.view`.
- Mailbox: el buzon personal notifica solo a su owner; el buzon de oficina notifica a quien lo conecto y a suscriptores de `notifications.mailbox.alerts`, validados con `connectors.accounts.office.read`.

Los eventos de fallo OAuth y expiracion del watch incluyen `IsOfficeMailbox` y `MailboxOwnerUserId` como campos opcionales. Los envelopes anteriores que no los traen conservan el comportamiento compatible y notifican solo a `CreatedByUserId`.

## Datos sensibles

Los asuntos de plantillas OTP se envían completos al proveedor, pero se guardan como `[redacted]` en Notification y Postmaster. Los cuerpos SMS sensibles siguen la misma regla en Sms.
