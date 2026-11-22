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

## Push payload

Todo push lleva el mismo contrato (`PushPayloads`), pensado para la web de hoy y una app móvil:

| Clave de `Data` | Contenido |
|---|---|
| `kind` | kind del catálogo (Campañas usa `campaign.push`) |
| `resourceType`, `resourceId`, `customerId` | el mismo recurso que el in-app del consumer |
| `tenantId`, `recipientUserId` | oficina y usuario destinatario |
| `sourceEventId` | solo si el aviso crea su propia fila en la bandeja; el clic la busca con `GET /communication/notifications/by-source/{id}` |
| `link` | igual que `Link` |
| `section`, `callId`, `conversationId`, `messageId` | propias de chat y llamadas |

- `Link` (solo web): `{base}/n/open?rt=&rid=&cid=&ev=`. Base del CRM para staff y del portal para cuentas de cliente. Solo ids, nunca texto. WISP y retiro conservan su enlace directo.
- Título y cuerpo genéricos (`PushText`, sección `push` del archivo de textos) en el idioma de cada destinatario (`IRecipientLocale`): se ven en la pantalla bloqueada, sin datos. El in-app conserva el detalle.
- Sin dispositivo no se escribe fila en `NotificationLogs`.
- `PushDispatchRequest.TimeToLive` (llamada entrante: 45 s) y `Platforms` (filtro por plataforma; nadie lo usa todavía).
- Dispositivos: guardan la sesión de Auth (`sid`); `auth.session.ended.v1` los revoca. El mismo token en otra oficina se desactiva en el alta (la última alta gana).

Pendiente del proyecto móvil: deep links de app, canales de Android por categoría, APNs directo e insignia del icono.

## Archivo de textos

`TaxVision.Notification.Domain/Catalog/NotificationCopy.json` es la fuente única de los textos:

| Sección | Para qué | Quién la lee |
|---|---|---|
| `kinds`, `aliases` | Textos in-app por kind (EN/ES) | CRM y portal traducen la bandeja; el backend arma el respaldo inglés (`InAppFallback`) |
| `push` | Título y cuerpo del push por kind (más `communication.call.incoming.video`) | Solo el backend (`PushText`) |
| `sms` | SMS por clave (código de acceso y firma) | Solo el backend (`SmsText`) |
| `email` | Textos que el sistema mete dentro de un correo (motivos, frases de alerta, respaldos como "your plan") | Solo el backend (`EmailText`); lo que escribió una persona viaja tal cual |

- Idiomas: `en` y `es`. Sin idioma, o con otro, sale en inglés (también el SMS del código).
- SMS en español del código: sin tildes ni emoji (cada carácter fuera de GSM-7 encarece el mensaje).
- **Copia a los frontends: el archivo entero**, sin recortar secciones. Las tres copias deben tener el mismo `sha256`:
  - `TaxVsion_Front/src/app/features/notifications/data-access/notification-copy.json`
  - `CLIENTTAXPROFRONTEND/src/app/core/notifications/notification-copy.json`
  - Comprobación: `sha256sum` de las tres rutas (backend en LF; en CRM y portal compara lo versionado o normaliza a LF).
  Las secciones `push`, `sms` y `email` no llevan secretos; los frontends las ignoran.
