# Guía DEVS — Estado real de entrega y listener IMAP

Cómo sabe TaxVision qué pasó con un correo después de enviarlo (entregado, rebotado, queja) y cómo se
entera al instante de que llegó correo a un buzón IMAP. Plan completo: `Implementaciones/Correo/Plan_Push_Entrada_y_Estado_Entrega.md`.

Servicios involucrados: **Postmaster** (dueño del estado de entrega), **Notification** (recibe el
webhook único de SMTP2GO y propaga el estado a sus registros), **Connectors** (buzones del tenant,
rebotes DSN y listener IMAP), **CRM** (pantalla *Settings → Email activity* y pestaña *Email delivery*
del perfil del cliente).

---

## 1. Idioma propio: la capa anticorrupción de Postmaster

Ningún nombre de proveedor entra al dominio. Todo aviso se traduce a esto:

| Concepto | Dónde | Qué es |
|---|---|---|
| `DeliveryOutcome` | `Domain/Sending` | Resultado de entrega por destinatario. Solo avanza: `Accepted < Deferred < Delivered`; finales `SoftBounced`, `HardBounced`, `Rejected`, `Complained`. Lo decide `DeliveryOutcomePrecedence`. |
| `EngagementSignal` | `Domain/Sending` | Aperturas, clics, bajas. Son señales aproximadas: suman contadores, nunca cambian el outcome. |
| `DeliveryFeedback` | `Domain/ValueObjects` | Un aviso ya traducido: outcome **o** señal, hora, origen, id del evento en el origen y diagnóstico corto. |
| `FeedbackSource` | `Domain/ValueObjects` | De dónde vino (`Smtp2Go`, `Dsn`). Solo el nombre. |
| `OurMessageId` | `Domain/ValueObjects` | Message-ID que ponemos nosotros: `pm.{sentMessageId:N}@{dominio}`. |
| `DeliveryFeedbackReceipt` | `Domain/Feedback` | Recibo por `(origen, id del evento)`: el mismo aviso dos veces no se aplica dos veces. |
| `UnmatchedDeliveryFeedback` | `Domain/Feedback` | Avisos que no se pudieron atar a un correo nuestro. Se guardan 30 días para diagnóstico. |

### Cómo se ata un aviso a nuestro correo (en este orden)

1. Message-ID que empieza con `pm.` → `SentMessage.Id`.
2. Header `X-TaxVision-Message-Id` (si el proveedor lo devuelve).
3. `SentMessage.InternetMessageId` (el que devolvió el buzón del tenant en Gmail/Graph/SMTP).
4. Id propio del proveedor + dirección del destinatario.

Se manda **un MIME por correo** (no uno por destinatario); el destinatario se identifica por su dirección.

---

## 2. Cómo llegan los avisos

```
SMTP2GO ──webhook único──► Notification (/notifications/campaign-email/webhooks/smtp2go)
                               │  ProviderWebhookRouter
                               ├─ campaña ─────────────► Campaigns (como antes)
                               ├─ pm. / X-TaxVision ──► EmailProviderFeedbackReceivedIntegrationEvent ─► Postmaster
                               └─ otro ────────────────► métrica smtp2go_webhook_unrouted_total

Postmaster (job de reconciliación) ──pull /v3/activity/search──► SMTP2GO   (si hay SMTP2GO_API_KEY)

Buzón del tenant recibe un rebote (DSN/NDR) ─► Connectors detecta y parsea (RFC 3464)
                               └─► ConnectorsDeliveryReportDetectedIntegrationEvent ─► Postmaster
```

Todos terminan en `DeliveryFeedbackApplier`, que es el mismo para push y pull:

- ata el aviso al correo (orden de arriba);
- comprueba el tenant: un rebote que llegó al buzón de una oficina **solo** puede tocar correos de esa oficina (`ExpectedTenantId`);
- guarda el recibo y aplica el outcome con la precedencia;
- `HardBounced`/`Complained` → agrega la dirección a la lista de supresión;
- si algo cambió y el correo vino de Notification, publica `PostmasterEmailDelivered/Bounced/Complained/Engagement` para que Notification actualice su registro.

---

## 3. Agregar un proveedor nuevo

Un proveedor entra con **un traductor (push)**, **un poller (pull)** o los dos. Nada más cambia.

### 3.1 Origen

Agregar el nombre en `FeedbackSource` (`Domain/ValueObjects/FeedbackSource.cs`):

```csharp
public static readonly FeedbackSource Mailgun = new("Mailgun");
```

### 3.2 Traductor (si el proveedor tiene webhook)

En `Postmaster.Infrastructure/Feedback/<Proveedor>/`, implementar `IDeliveryFeedbackTranslator`:

```csharp
public sealed class MailgunWebhookFeedbackTranslator : IDeliveryFeedbackTranslator
{
    public FeedbackSource Source => FeedbackSource.Mailgun;

    public Result<IReadOnlyList<TranslatedFeedback>> Translate(string rawPayload)
    {
        // 1. Leer el JSON. Si no se puede → Result.Failure (se registra y no se aplica nada).
        // 2. Por cada evento: mapear su nombre a DeliveryOutcome o EngagementSignal.
        //    Un evento que no nos interesa no se devuelve (no es error).
        // 3. DeliveryFeedback.ForOutcome(...) / ForSignal(...) con un sourceEventId ESTABLE:
        //    si el proveedor reintenta el webhook, el id tiene que ser el mismo (no usar "ahora").
        // 4. new TranslatedFeedback(messageId, correlationHeader, providerMessageId, rcpt, feedback, trozoDelPayload)
    }
}
```

Reglas:
- El vocabulario del proveedor vive **solo** en esta carpeta (como `Smtp2GoEventVocabulary`).
- `RawPayload` es el pedazo de ese evento, para auditoría; nunca el cuerpo del correo.
- Si el proveedor no da hora del evento, armar el id con datos del propio evento.

Registro en `Postmaster.Infrastructure/DependencyInjection.cs`:

```csharp
services.AddSingleton<IDeliveryFeedbackTranslator, MailgunWebhookFeedbackTranslator>();
```

Para que el webhook llegue: los endpoints públicos de proveedores viven en Notification (uno por
proveedor, verificado con `SharedSecretWebhookVerifier` de `BuildingBlocks.Web`). Para un proveedor
nuevo se agrega su endpoint allí y, para los correos del sistema, se publica
`EmailProviderFeedbackReceivedIntegrationEvent` con `Source = "Mailgun"` y el payload crudo; el
consumer de Postmaster busca el traductor por ese nombre. Ver `ProviderWebhookRouter` (SMTP2GO).

### 3.3 Poller (si el proveedor tiene API de consulta)

Implementar `IDeliveryFeedbackPoller`:

```csharp
public sealed class MailgunEventsFeedbackPoller(HttpClient http, IOptions<MailgunFeedbackOptions> options)
    : IDeliveryFeedbackPoller
{
    public FeedbackSource Source => FeedbackSource.Mailgun;

    public async Task<IReadOnlyList<TranslatedFeedback>> PollAsync(
        IReadOnlyList<PendingDeliveryRef> pending, CancellationToken ct)
    {
        // Preguntar por los destinatarios pendientes (sin outcome final) y traducir igual que el webhook.
    }
}
```

- Lo llama `DeliveryFeedbackReconciliationJob` (líder único con `JobLeaderLock`) para correos con más de `DeliveryFeedback:ReconcileAfterHours` sin outcome final.
- Sin API key configurada, el poller no hace nada: el push sigue funcionando.
- El cliente HTTP va en la carpeta del proveedor; el test de arquitectura `PostmasterArchitectureTests` tiene la lista de namespaces que pueden usar `HttpClient` — agregar el nuevo con un comentario.

### 3.4 Tests mínimos

- Traductor: cada evento del vocabulario → outcome/señal esperado; evento desconocido se ignora; payload roto falla; el mismo evento da el mismo `sourceEventId`.
- Poller: respuesta del proveedor → `TranslatedFeedback`; sin API key no llama.
- Deploy: si hay secreto nuevo, fitness test de que el workflow lo escribe y el compose lo pasa con el nombre que lee la config (ver `DeliveryFeedbackDeployWiringTests`).

---

## 4. Vía buzón del tenant (Gmail, Graph, IMAP/SMTP)

- Postmaster manda a Connectors `ClientMessageId = OurMessageId`. SMTP lo usa tal cual; Gmail lo lee de vuelta (`format=metadata`); Graph envía por borrador (`POST /me/messages` → `/send`) para obtener `internetMessageId`.
- Postmaster guarda `SentMessage.InternetMessageId` (índice propio) para atar los rebotes.
- **Fallback (F5):** si el buzón del tenant falla con `MailboxUnavailable` o `ProviderUnavailable` en un correo transaccional con buzón preferido, Postmaster lo reenvía por el proveedor del sistema con `Reply-To` = el buzón. Lo decide `MailboxFallbackPolicy`; cada intento queda en `DeliveryAttempt` y la línea de tiempo muestra el paso.

---

## 5. Listener IMAP (`connectors-mailbox-listener`)

Gmail y Graph avisan por push. IMAP no: sin el listener, el correo IMAP entra con la reconciliación cada 15 min.

```
connectors-mailbox-listener (N réplicas)
  MailboxListenerService ── cada 15 s ──► MailboxListenerCoordinator
      renueva leases Redis  connectors:listener:lease:{accountId}  (TTL 60 s)
      suelta cuentas que ya no están Active/IMAP
      toma cuentas nuevas hasta MaxAccountsPerInstance
  ImapIdleSession (una por cuenta)
      credenciales descifradas justo antes del login (ConnectThrottle: 10 a la vez, 3 por host)
      IDLE 4 min (sin IDLE: NOOP cada 3 min)
      cambio → espera 3 s → ConnectorsMailboxChangeDetectedIntegrationEvent ─► taxvision-events
      login rechazado → ConnectorsMailboxAuthFailedIntegrationEvent y la sesión termina
      marca connectors:listener:alive:{accountId} (TTL 10 min)

Connectors API
  ConnectorsMailboxChangeDetectedConsumer → ReconcileAccountCommand (el de siempre)
  ConnectorsMailboxAuthFailedConsumer     → cuenta en Error + aviso "reconecta tu buzón"
  ReconciliationJob → cuenta IMAP con marca alive: solo una vez por hora
```

Reglas del listener:
- **Nunca escribe en la base**: solo lee cuentas y credenciales y publica avisos. Connectors API es el único escritor.
- Publica a `taxvision-events` como el resto de servicios. No crear colas ni exchanges propios.
- Sin `ProtocolLogger` de MailKit (el protocolo lleva el login). Los logs llevan `AccountId`, nunca dirección ni contraseña.
- Apagarlo nunca rompe nada: la reconciliación vuelve sola a cada 15 min cuando vence la marca `alive`.

Configuración (`Connectors:MailboxListener`, en compose `Connectors__MailboxListener__*`):

| Clave | Default | Nota |
|---|---|---|
| `Enabled` | `false` | Secret `CONNECTORS_LISTENER_ENABLED`. |
| `MaxAccountsPerInstance` | 500 | Se fija con la prueba de carga. |
| `IdleMinutes` / `PollMinutes` | 4 / 3 | Menos de 5 min por NAT y balanceadores. |
| `ChangeDebounceSeconds` | 3 | Junta una ráfaga en un aviso. |
| `MaxConcurrentConnects` / `PerHost` | 10 / 3 | Evita "too many login attempts". |
| `LeaseSeconds` / `TickSeconds` | 60 / 15 | |
| `MaxBackoffMinutes` | 10 | Reconexión 5 s → 10 min con ±20 %. |

Métricas (meter `TaxVision.Connectors.MailboxListener`): `sessions_active`, `sessions_idle`,
`sessions_polling`, `sessions_backoff`, `changes_detected_total`, `reconnects_total`, `auth_failures_total`.

Escalar = más réplicas del contenedor; los leases reparten las cuentas solos.

---

## 6. Dónde se ve

- **CRM → Settings → Email activity** (`postmaster.messages.read`): `GET /postmaster/messages` (filtros `status`, `recipient`, `from`, `to`) y `GET /postmaster/messages/{id}`. Los destinatarios salen enmascarados salvo para TenantAdmin/PlatformAdmin.
- **Perfil del cliente → Email delivery**: la misma lista filtrada por el email del cliente.
- Postman: `TaxVision_Postmaster` → *Messages* (`ListActivity`, `GetDetail`); `TaxVision_Notification` → *Campaign Email Webhook* → `SMTP2GO System Hard Bounce`.

## 7. Configuración de SMTP2GO (una vez)

- Webhook del usuario SMTP del sistema → la misma URL de campañas, formato JSON, eventos `processed`, `delivered`, `bounce`, `reject`, `spam`, `open`, `click`, `unsubscribe`, Bearer = `CAMPAIGN_EMAIL_WEBHOOK_SECRET`.
- `SMTP2GO_API_KEY` (solo lectura de actividad) habilita el pull de respaldo. Sin ella, solo push.
