# Guía: cómo conectar un microservicio nuevo al sistema de notificaciones por email

> Para cualquier dev backend (.NET) que necesite que su microservicio mande un
> email — de bienvenida, de alerta, de recordatorio, lo que sea. Cubre los
> **2 casos reales** que existen en este backend, con pasos concretos, código
> de referencia real (copiado del repo, no inventado) y una checklist al
> final de cada uno.

---

## 0. Resumen ejecutivo

Ningún microservicio le habla a SMTP directamente, ni arma HTML de emails a
mano, ni le habla al proveedor de correo. Esas 3 responsabilidades viven en
servicios dedicados:

- **Scribe** — dueño de las plantillas y el renderizado (Fluid + layouts).
- **Postmaster** — dueño del envío real (SMTP directo, o vía OAuth a través
  de Connectors), suppression list, idempotencia.

Hay **dos patrones distintos** para llegar del "pasó algo en mi servicio" al
"le llegó un email al usuario", según **quién está esperando la respuesta**:

| | ¿Quién espera la respuesta? | Patrón | Ejemplos reales en este repo |
|---|---|---|---|
| **Caso A — Automático/transaccional** | Nadie en vivo — es una notificación de fondo (bienvenida, reset de password, alerta) | Publicás un **evento de dominio**, `Notification` lo escucha, le pide el render a `Scribe`, y lo despacha (hoy, por defecto, vía `Postmaster`) | Auth (bienvenida, reset de password, OTP), Signature (invitaciones a firmar, recordatorios), Communication (invitación a meeting) |
| **Caso B — Síncrono/humano** | Un usuario real, en el momento, esperando saber si el mail salió o no (como mandar algo desde Gmail) | Le hablás **directo y síncrono** a `Postmaster` por HTTP M2M, sin pasar por `Scribe` (el contenido ya lo compuso el usuario, no hay plantilla) | Correspondence (redactar/responder un email a un customer) |

Regla rápida para decidir cuál te toca:

- ¿Es una notificación automática con contenido fijo (una plantilla con
  variables — "Hola {{nombre}}, tu código es {{codigo}}")? → **Caso A**. Es
  el 99% de los casos.
- ¿Hay un humano en tu frontend esperando una confirmación real de "se
  mandó" en la misma interacción, con contenido que el usuario escribió
  libremente? → **Caso B**. Es raro — solo aplica si estás construyendo algo
  parecido a un cliente de correo.

Si tenés dudas, es Caso A. Seguí leyendo.

---

## 1. Por qué existen dos caminos (contexto rápido)

`Notification` empezó siendo un microservicio "todo-en-uno": templates
propios, motor de render propio, cliente SMTP propio. Con el tiempo se separó
en piezas dedicadas — `Scribe` (templates/render), `Postmaster` (envío),
`Connectors` (conexión OAuth a Gmail/Graph/IMAP), `Correspondence` (inbox
filtrado por customer + composición). Hoy `Notification` es el
**orquestador** para todo lo automático: no renderiza nada él mismo, no manda
SMTP él mismo — solo escucha eventos, le pide el HTML ya armado a `Scribe`, y
le pasa el mensaje a `Postmaster` para que lo mande de verdad.

`Correspondence` es la única excepción real a "todo pasa por Notification":
cuando un empleado le contesta un email a un customer, hay un humano
esperando en el momento — no tiene sentido publicar un evento y esperar a
que algún consumer lo procese eventualmente. Por eso ese único caso le habla
**directo y síncrono** a Postmaster, sin plantilla, sin Scribe, sin Notification
de por medio.

Los dos caminos terminan en el mismo lugar (`Postmaster`), pero **nunca
mezclés los patrones**: si tu notificación es automática, no le hables
directo a Postmaster (te perdés el render, la suppression list te queda
sin idempotencia coherente con el resto, y le agregás carga a un servicio
que no debería recibir tráfico directo de N microservicios distintos).

---

## 2. Caso A — Notificación automática/transaccional (el camino común)

### 2.1 Diagrama de flujo

```
┌──────────────┐  1. Publica TuEventoIntegrationEvent      ┌──────────────┐
│ Tu servicio  │ ──────────────────────────────────────────▶│ RabbitMQ     │
│ (publica el  │     (bus.PublishAsync, outbox de Wolverine) │ taxvision-   │
│  evento,     │                                             │ events       │
│  nada más)   │                                             │ (fanout)     │
└──────────────┘                                             └──────┬───────┘
                                                                      │ 2. Notification
                                                                      │    ya tiene una
                                                                      │    cola propia
                                                                      │    escuchando
                                                                      ▼
                                                              ┌──────────────┐
                                                              │ Notification │
                                                              │ (tu consumer │
                                                              │  vive acá,   │
                                                              │  no en tu    │
                                                              │  servicio)   │
                                                              └──────┬───────┘
                                              3. POST scribe/render  │
                                              (M2M, event_key +      │
                                               variables)            ▼
                                                              ┌──────────────┐
                                                              │ Scribe       │
                                                              │ (busca el    │
                                                              │  template    │
                                                              │  mapeado a   │
                                                              │  tu event_key│
                                                              │  y renderiza)│
                                                              └──────┬───────┘
                                              4. Devuelve            │
                                                 { subject, html,    │
                                                   text, inlineAssets}
                                                                     ▼
                                                              ┌──────────────┐
                                                              │ Notification │
                                                              │ 5. Publica   │
                                                              │ notifications│
                                                              │ .email_send_ │
                                                              │ requested.v1 │
                                                              └──────┬───────┘
                                                                     ▼
                                                              ┌──────────────┐
                                                              │ Postmaster   │
                                                              │ 6. Manda de  │
                                                              │  verdad      │
                                                              │  (SMTP o     │
                                                              │  OAuth vía   │
                                                              │  Connectors) │
                                                              └──────────────┘
```

**Lo único que hace tu servicio es el paso 1.** Todo lo demás (pasos 2-6) ya
existe y funciona — vos solo tenés que escribir el `Handle` del paso 2
(el consumer, que vive en el código de `Notification`, no en el tuyo) y
asegurarte de que el paso 3 tenga una plantilla real para tu `event_key`
(Paso 4 de la sección siguiente).

### 2.2 Paso a paso completo

#### Paso 1 — ¿Ya existe una plantilla para tu evento?

Antes de escribir código, fijate si tu caso ya tiene plantilla en Scribe.
Buscá en `src/Services/Scribe/TaxVision.Scribe.Application/Templates/Seed/NotificationTemplateSeedSource.cs`
— ahí están sembradas las 13 plantillas de sistema (bienvenida, reset de
password, invitación a firmar, etc.). Si tu evento es una variación de algo
que ya existe, quizás no necesitás una plantilla nueva.

Si necesitás una plantilla nueva, seguí al Paso 4 (más abajo, después de
tener el evento armado) — hay dos caminos según si tu plantilla es de
sistema (fija, va en el repo) o algo más puntual.

#### Paso 2 — El evento de dominio, en tu propio servicio

Tu evento hereda de `IntegrationEvent` — te da gratis `EventId`/`TenantId`/
`OccurredOn`/`CorrelationId`:

```csharp
// src/BuildingBlocks/Messaging/IIntegrationEvent.cs (ya existe, no lo tocás)
public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
    public string CorrelationId { get; init; } = string.Empty;
}
```

Tu evento nuevo (en el `BuildingBlocks/Messaging/` de tu servicio, o en su
propia carpeta de contratos si tu servicio ya tiene una):

```csharp
[MessageIdentity("orders.shipped.v1")]  // este string ES tu event_key — lo vas a reusar en Scribe
public sealed record OrderShippedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required string CustomerEmail { get; init; }
    public required string CustomerName { get; init; }
    public required string OrderNumber { get; init; }
    public required string TrackingUrl { get; init; }
}
```

> **Convención de naming**: `{tu-dominio}.{evento_pasado}.v{n}` — todo en
> minúscula, snake_case dentro de cada segmento. Mirá los reales:
> `auth.password_reset_requested.v1`, `signature.request_completed.v1`,
> `connectors.raw_message_received.v1`. La versión (`.v1`) existe para poder
> evolucionar el contrato sin romper consumers viejos — si cambiás el shape
> de forma incompatible, es `.v2`, no un cambio in-place.

#### Paso 3 — Publicalo

Con la infraestructura de Wolverine que ya tiene tu servicio (outbox
transaccional, igual que cualquier otro evento que ya publiques):

```csharp
await bus.PublishAsync(new OrderShippedIntegrationEvent
{
    TenantId = order.TenantId,
    CustomerId = order.CustomerId,
    CustomerEmail = customer.Email,
    CustomerName = customer.DisplayName,
    OrderNumber = order.Number,
    TrackingUrl = $"{portalBaseUrl}/orders/{order.Id}/tracking",
    CorrelationId = correlation.CorrelationId,
});
```

Nada más de tu lado. El evento sale por el exchange fanout compartido
(`taxvision-events`) — **cualquier** servicio con una cola escuchando ese
exchange lo recibe, incluida la cola de `Notification`, que ya existe y ya
está escuchando (no hace falta que crees nada de infraestructura de
mensajería vos).

#### Paso 4 — Crear (o sembrar) la plantilla en Scribe

Dos caminos, según qué tipo de plantilla es la tuya:

**4a. Plantilla de sistema** (fija, la misma para todos los tenants, va
versionada en el repo — es el caso normal para notificaciones transaccionales
como "se envió tu pedido"):

Agregá una entrada nueva a `NotificationTemplateSeedSource.All` en
`src/Services/Scribe/TaxVision.Scribe.Application/Templates/Seed/NotificationTemplateSeedSource.cs`
— mirá cualquiera de las 13 entradas existentes como plantilla (nunca mejor
dicho). El seeder (`ScribeNotificationTemplateSeeder.cs`, corre al arrancar
Scribe, es idempotente — si el `TemplateKey` ya existe, lo salta) se encarga
de: subir el HTML a CloudStorage, crear el `EmailTemplate` + versión sobre el
layout `system-base`, publicarla, y crear el `EventTemplateMapping` que
conecta tu `event_key` con la plantilla — todo automático, con solo agregar
la entrada.

**4b. Plantilla puntual/gestionada** (no querés que viva hardcodeada en el
repo — por ejemplo, algo que un admin va a querer editar después sin hacer
un deploy):

Usá la API HTTP real de Scribe (necesitás permiso `scribe.templates.write` —
ver Paso 7):

```http
POST {{ScribeBaseUrl}}/scribe/templates
{ "scope": "System", "templateKey": "orders.shipped", "name": "Pedido enviado", "description": "..." }

POST {{ScribeBaseUrl}}/scribe/templates/{id}/versions
{
  "subject": "Tu pedido {{order_number}} ya salió",
  "htmlContent": "<p>Hola {{customer_name}}, ...</p>",
  "textContent": "Hola {{customer_name}}, ...",
  "layoutId": "...",           // el layout system-base, o el que corresponda
  "layoutVersionNumber": 1,
  "variableDefinitions": [ { "name": "customer_name", "required": true }, ... ]
}

POST {{ScribeBaseUrl}}/scribe/templates/{id}/versions/{versionId}/publish
```

En ambos casos (4a o 4b), el `event_key` que uses en el `EventTemplateMapping`
(paso siguiente) **tiene que ser exactamente el mismo string** que pusiste en
`[MessageIdentity(...)]` de tu evento (Paso 2) — es la única conexión real
entre "mi evento" y "mi plantilla", no hay nada más buscando eso por vos.

#### Paso 5 — Vincular tu evento a la plantilla

Si usaste el seeder (4a), este paso **ya está hecho** — el seeder crea el
`EventTemplateMapping` junto con la plantilla.

Si creaste la plantilla vía API (4b), falta este paso — necesitás permiso
`scribe.event_mappings.write`:

```http
POST {{ScribeBaseUrl}}/scribe/event-mappings
{ "scope": "System", "eventKey": "orders.shipped.v1", "templateKey": "orders.shipped", "priority": 0 }
```

#### Paso 6 — Escribir el consumer (esto va en `Notification`, no en tu servicio)

Este es el único código C# "nuevo" de todo el flujo, y **no vive en tu
microservicio** — vive en `src/Services/Notification/TaxVision.Notification.Application/Consumers/`.
Copiá la estructura exacta de un consumer real que ya funciona
(`PasswordResetRequestedConsumer` en `Consumers/AuthEventConsumers.cs`):

```csharp
public static class OrderShippedConsumer
{
    public static async Task Handle(
        OrderShippedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var render = (await scribeClient.RenderAsync(
                "orders.shipped.v1",                 // el MISMO event_key del Paso 5
                evt.TenantId,
                new Dictionary<string, object?>
                {
                    ["customer_name"] = evt.CustomerName,
                    ["order_number"] = evt.OrderNumber,
                    ["tracking_url"] = evt.TrackingUrl,
                },
                ct)).EnsureRendered("orders.shipped.v1");

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.CustomerEmail,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: "orders.shipped",
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: render.InlineAssets),
                ct);
        }
    }
}
```

Puntos que hay que entender, no solo copiar:

- **No hace falta registrar nada.** Wolverine descubre este `Handle` solo,
  por convención (`class estática` + método `static Handle(TEvento evt,
  ...dependencias)`), gracias a `options.Discovery.IncludeAssembly(...)` en
  `Notification.Api/Program.cs`. Con soltar el archivo en la carpeta
  correcta alcanza.
- **`.EnsureRendered(eventKey)` es obligatorio, no opcional.** Si Scribe
  falla (está caído, lento, o no encuentra el mapping), esto **lanza una
  excepción real** — que Wolverine atrapa con su política de retry
  (`OnException<Exception>().RetryWithCooldown(1s, 5s, 15s)`) y reintenta.
  **Nunca** hagas `if (render.IsFailure) return;` a mano — eso hace que
  Wolverine piense que el mensaje se procesó bien, y el email se pierde en
  silencio para siempre. (Esto fue un bug real que se arregló en este mismo
  proyecto — no lo reintroduzcas.)
- **`gateway.QueueEmailAsync(...)` es el final de tu responsabilidad.** De
  ahí para adelante (crear el registro de auditoría, publicar el evento que
  consume Postmaster, mandar de verdad) es 100% automático — nunca vas a
  necesitar construir `NotificationsEmailSendRequestedIntegrationEvent` a
  mano, ni saber que existe más allá de esta frase.
- **`InlineAssets: render.InlineAssets`** — pasalo siempre así, tal cual,
  aunque tu plantilla no tenga logo. Es la referencia al logo embebido (CID)
  si la plantilla lo usa; si no lo usa, viaja vacío y no hace nada.

#### Paso 7 — Permisos que podrías necesitar

Si vas a crear plantillas/mappings vía API (Paso 4b/5), necesitás
`scribe.templates.write` + `scribe.event_mappings.write` en tu usuario.
`TenantAdmin`/`PlatformAdmin` ya los tienen por defecto (reciben todos los
permisos no-portal automáticamente) — para el setup inicial, lo más simple
es hacerlo logueado como uno de esos dos roles. Si tu rol no es ninguno de
esos y necesitás hacerlo de forma recurrente, pedí que se le asigne el
permiso (es `IsAssignableByTenant: true`, un `TenantAdmin` te lo puede dar).

**No necesitás ningún permiso nuevo para el consumer en sí** (Paso 6) — el
cliente M2M de `notification-worker` ya tiene `scribe.render` (el permiso
que usa `ScribeRenderClient` para llamar `POST scribe/render`), así que tu
consumer nuevo hereda ese acceso automáticamente por vivir dentro de
`Notification`. Vos **nunca** necesitás tu propio cliente M2M para esto —
esa es justamente la ventaja de que el consumer viva en `Notification` y no
en tu servicio.

#### Paso 8 — Probar

1. Publicá tu evento manualmente (un test de integración, o un endpoint
   temporal de debug) y confirmá que `Notification` lo consume — mirá los
   logs de `notification-api`, deberías ver el `Handle` ejecutarse.
2. Si la plantilla no existe todavía o el `event_key` no matchea, vas a ver
   la excepción `ScribeRenderFailedException` en los logs y Wolverine
   reintentando — es la señal correcta de que algo en el Paso 4/5 está mal,
   no un bug del consumer.
3. Con todo bien armado, deberías ver el email en la bandeja de prueba (o en
   los logs de `postmaster-api`, si estás en un entorno sin SMTP real
   configurado).

### 2.3 Checklist final — Caso A

- [ ] Evento nuevo hereda `IntegrationEvent`, tiene `[MessageIdentity("tu.evento.v1")]`
- [ ] Tu servicio publica el evento (`bus.PublishAsync`) — nada más de tu lado
- [ ] Plantilla creada — vía seeder (`NotificationTemplateSeedSource`) o vía API (`POST scribe/templates` + versión + publish)
- [ ] `EventTemplateMapping` conecta tu `event_key` exacto a la plantilla (automático si usaste el seeder)
- [ ] Consumer nuevo en `Notification.Application/Consumers/` (NO en tu servicio), con `correlation.Push` primero, `.EnsureRendered(...)` siempre, `InlineAssets: render.InlineAssets`
- [ ] Cero código de SMTP, cero HTML armado a mano, cero llamada directa a Postmaster
- [ ] Probado: el evento dispara el consumer, el consumer renderiza, el email sale

### 2.4 Errores comunes

- **Hacer `if (render.IsFailure) return;` en vez de `.EnsureRendered(...)`**
  — el error más grave posible acá. El email se pierde para siempre sin
  ningún rastro, sin retry, sin log de error real. Usá siempre
  `.EnsureRendered(eventKey)`.
- **`event_key` que no matchea exactamente** entre `[MessageIdentity(...)]`
  del evento, la llamada a `scribeClient.RenderAsync(...)`, y el
  `EventTemplateMapping` — los 3 tienen que ser el string idéntico,
  carácter por carácter. Un typo silencioso acá se manifiesta como
  `ScribeRenderFailedException` en runtime, no como un error de compilación.
- **Escribir el consumer en tu propio servicio** en vez de en
  `Notification` — no existe ningún otro lugar del repo donde este patrón
  se repita; si te encontrás armando tu propio `IScribeRenderClient` o
  publicando `notifications.email_send_requested.v1` a mano, es una señal
  de que estás reinventando algo que ya existe.
- **Crear un cliente M2M nuevo para llamar a Scribe** — no hace falta,
  nunca. El consumer vive en `Notification`, que ya tiene el permiso
  `scribe.render` en su cliente de servicio.
- **Usar Caso B (Postmaster directo) para algo automático** — le saca a tu
  notificación el render de Scribe, la mete en el mismo tráfico M2M que
  Correspondence (pensado para volumen humano, no de fondo), y te obliga a
  armar el HTML vos mismo. Si no hay un humano esperando en el momento, es
  Caso A.

---

## 3. Caso B — Envío síncrono/humano (raro)

### 3.1 Cuándo usar este patrón

Solo cuando las dos cosas son verdad al mismo tiempo:

1. Hay un usuario real, en tu frontend, esperando saber en la misma
   interacción si el email salió o no (como apretar "Enviar" en Gmail).
2. El contenido es libre/compuesto por el usuario — no una plantilla con
   variables fijas.

El único caso real en este repo es `Correspondence` (un empleado le
responde a un customer). Si tu caso no es prácticamente idéntico a eso, es
Caso A.

### 3.2 Diagrama de flujo

```
┌──────────────┐  1. POST /tu-servicio/... (el usuario aprieta "Enviar")
│ Frontend     │ ─────────────────────────────────────────────▶┌──────────────┐
│ (humano      │                                                │ Tu servicio  │
│  esperando)  │  4. 200 OK { sentMessageId } o error real      │              │
│              │◀───────────────────────────────────────────────│              │
└──────────────┘                                                └──────┬───────┘
                                                    2. POST postmaster/            │
                                                       correspondence-messages     │
                                                       (M2M, ServiceOnly,          │
                                                       síncrono, SIN reintento)    ▼
                                                                            ┌──────────────┐
                                                                            │ Postmaster   │
                                                                            │ 3. Manda de  │
                                                                            │  verdad y    │
                                                                            │  responde en │
                                                                            │  la misma    │
                                                                            │  request     │
                                                                            └──────────────┘
```

Notá que acá **no aparece Scribe en ningún lado** — el HTML/texto ya lo
armó el usuario (o tu propio servicio, a partir de lo que el usuario
escribió), no hay plantilla que renderizar.

### 3.3 Paso a paso

Mirá `src/Services/Correspondence/TaxVision.Correspondence.Infrastructure/Postmaster/PostmasterClient.cs`
como referencia completa — es exactamente este patrón.

1. **Armá tu propio cliente M2M** hacia Postmaster (no hay nada compartido
   para reusar acá, porque cada servicio arma su propio contenido) —
   `IHttpClientFactory`, timeout de 30s, token de servicio vía
   `POST auth/service-token`. **Sin retry automático** — la idempotencia
   real vive en Postmaster, del lado del reintento manual del usuario, no
   de vos reintentando en silencio.

2. **Llamá `POST postmaster/correspondence-messages`** — gateado por policy
   `ServiceOnly` (`actor_type=Service`, no hace falta ningún `[HasPermission]`
   específico), body:

   ```json
   {
     "tenantId": "...",
     "correspondenceDraftId": "...",   // tu propio Id, sirve de idempotencia
     "accountId": "...",               // cuenta de email conectada (vía Connectors)
     "subject": "...", "html": "...", "text": "...",
     "to": ["..."], "cc": [], "bcc": [],
     "attachments": [ { "fileId": "...", "filename": "...", "contentType": "...", "sizeBytes": 0 } ],
     "replyContext": null
   }
   ```

3. **Devolvé la respuesta real al frontend** — `200 OK` con
   `{ sentMessageId, providerMessageId }` si salió, o el código de error
   real (403 cuenta no encontrada, 409 todos los destinatarios suprimidos,
   502 falla de proveedor) si no — el usuario tiene que enterarse del motivo
   real, no un "algo salió mal" genérico.

### 3.4 Checklist — Caso B

- [ ] Confirmaste que hay un humano esperando la respuesta en la misma
      interacción (si no, es Caso A)
- [ ] Cliente M2M propio hacia Postmaster, timeout 30s, sin retry automático
- [ ] `correspondenceDraftId`/tu-propio-Id sirve de key de idempotencia — no
      inventes tu propio mecanismo, reusá el que ya existe en Postmaster
- [ ] Devolvés el código de error real al frontend, no un 500 genérico

---

## 4. Tabla comparativa

| | Caso A (Automático) | Caso B (Síncrono/humano) |
|---|---|---|
| ¿Quién espera la respuesta? | Nadie, es de fondo | Un usuario real, en el momento |
| ¿Pasa por Scribe? | Sí, siempre | No — contenido libre |
| ¿Dónde vive tu código? | Un evento que publicás + un consumer en `Notification` | Un cliente M2M propio en tu servicio |
| ¿Necesitás plantilla? | Sí | No |
| ¿Necesitás cliente M2M propio? | No (heredás el de `Notification`) | Sí |
| Ejemplo real | Auth, Signature, Communication | Correspondence |

---

## 5. Referencia rápida

### Permisos de Scribe (`ScribePermissions.cs`)

```csharp
public const string TemplatesRead = "scribe.templates.read";
public const string TemplatesWrite = "scribe.templates.write";
public const string LayoutsRead = "scribe.layouts.read";
public const string LayoutsWrite = "scribe.layouts.write";
public const string EventMappingsRead = "scribe.event_mappings.read";
public const string EventMappingsWrite = "scribe.event_mappings.write";
public const string Render = "scribe.render";   // solo M2M, nunca se lo des a un rol humano
```

`TenantAdmin`/`PlatformAdmin` tienen todos los `.write` por defecto.
`SystemEmployee` solo tiene los `.read`.

### Endpoints de Scribe relevantes

| Endpoint | Quién lo llama | Permiso |
|---|---|---|
| `POST scribe/render` | Notification (M2M, automático) | `scribe.render` |
| `POST scribe/templates` | Vos, si armás una plantilla vía API (Paso 4b) | `scribe.templates.write` |
| `POST scribe/templates/{id}/versions` | Idem | `scribe.templates.write` |
| `POST scribe/templates/{id}/versions/{versionId}/publish` | Idem | `scribe.templates.write` |
| `POST scribe/event-mappings` | Vos, si no usaste el seeder (Paso 5) | `scribe.event_mappings.write` |

### El endpoint de Postmaster para Caso B

| Endpoint | Quién lo llama | Policy |
|---|---|---|
| `POST postmaster/correspondence-messages` | Tu propio cliente M2M (Caso B únicamente) | `ServiceOnly` |

### Grant M2M (client-credentials) — igual en todo el backend

```
POST {{AuthBaseUrl}}/auth/service-token
{ "clientId": "...", "clientSecret": "...", "tenantId": "..." }

→ { "accessToken": "...", "expiresInSeconds": 600, "tokenType": "Bearer" }
```

Si tu servicio necesita su propio cliente M2M (solo para Caso B — Caso A
nunca lo necesita), seguí el mismo patrón de registro que usa
`documents/Guia_Conectar_Microservicio_a_CloudStorage.md` §2.2 Paso 2 para
CloudStorage — es el mismo mecanismo de `ServiceAuth:Clients` en
`docker-compose.yml`, solo que con los permisos de Postmaster/Scribe en vez
de los de CloudStorage. El índice libre siguiente hoy es `5` (`0`=Notification,
`1`=CommunicationTranscriptWorker, `2`=Signature, `3`=Customer,
`4`=Correspondence).

### Flag de despacho de Notification

`Notification:UsePostmasterDispatch` — hoy `true` por defecto (Postmaster es
el camino real). Existe un camino viejo de SMTP directo detrás del mismo
flag en `false`, mantenido solo como rollback de emergencia — no lo uses
como referencia para código nuevo, es código heredado en proceso de
retirarse.

---

## 6. Archivos de referencia en el repo (para copiar-pegar)

**Caso A**:

- `src/Services/Notification/TaxVision.Notification.Application/Consumers/AuthEventConsumers.cs` — varios consumers reales completos, `PasswordResetRequestedConsumer` es el más simple para copiar.
- `src/Services/Notification/TaxVision.Notification.Application/Consumers/Signature/SignerInvitedConsumer.cs` — otro ejemplo real, distinto dominio de origen.
- `src/Services/Notification/TaxVision.Notification.Application/Abstractions/IScribeRenderClient.cs` — el contrato que usás.
- `src/Services/Notification/TaxVision.Notification.Application/Abstractions/ScribeRenderResultExtensions.cs` — el `.EnsureRendered(...)` que tenés que usar siempre.
- `src/Services/Notification/TaxVision.Notification.Application/Abstractions/IEmailDispatchGateway.cs` — el `EmailDispatchRequest` completo.
- `src/Services/Scribe/TaxVision.Scribe.Application/Templates/Seed/NotificationTemplateSeedSource.cs` — las 13 plantillas de sistema, tu modelo para una plantilla nueva (Paso 4a).
- `src/Services/Scribe/TaxVision.Scribe.Api/Controllers/EmailTemplatesController.cs` + `EventTemplateMappingsController.cs` — la API manual (Paso 4b/5).
- `src/Services/Notification/TaxVision.Notification.Api/Program.cs` — cómo Wolverine descubre tu consumer solo, y cómo está bindeada la cola de Notification al exchange compartido.

**Caso B**:

- `src/Services/Correspondence/TaxVision.Correspondence.Infrastructure/Postmaster/PostmasterClient.cs` — el cliente completo.
- `src/Services/Postmaster/TaxVision.Postmaster.Api/Controllers/CorrespondenceMessagesController.cs` — el endpoint del otro lado.

**Infraestructura**:

- `src/BuildingBlocks/Messaging/IIntegrationEvent.cs` — la base de todo evento.
- `src/BuildingBlocks/Messaging/EmailIntegrationEvents/PostmasterEmailEvents.cs` — el contrato interno que Notification↔Postmaster usan (no lo tocás para Caso A, pero está acá si necesitás entender el detalle).
- `deploy/docker/docker-compose.yml` (bloque `auth-api`, sección `ServiceAuth__Clients__N`) — cómo se registra un cliente M2M nuevo, si tu caso lo necesita (Caso B).

---

*Última actualización: 18 de julio de 2026, tras el pase de hardening que
cerró la migración de Notification (retiro del envío SMTP directo, flag
`UsePostmasterDispatch` en `true` por defecto) y arregló el bug real del
permiso `scribe.render` que tenía roto el M2M Notification→Scribe.*
