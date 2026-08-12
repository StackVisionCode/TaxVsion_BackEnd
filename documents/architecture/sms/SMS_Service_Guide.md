# TaxVision.Sms — Guía del servicio (as-built) e implementación de proveedores

> Estado: **implementado y verificado en vivo** (Infobip entregó a handset; Twilio llegó a la API con
> credenciales del legado — pendiente AuthToken vigente). Este documento describe el servicio **tal como
> está construido**, no el diseño acoplado a Campaigns de `../campaigns/sms/` (ese fue un diseño previo).

---

## 1. Qué es y principios

`TaxVision.Sms` es un microservicio **independiente y agnóstico de proveedor** para enviar SMS/MMS.
No conoce ningún dominio consumidor (ni Campaigns, ni Billing, ni Payments): solo recibe
`{customerId, to, message, media?}`, lo envía por el proveedor configurado, recibe el webhook de
estado/entrada y publica un evento agnóstico.

Principios que NO se rompen:

- **Agnóstico del dominio** — sin `campaignId`/`invoiceId`/`paymentId`; `sourceContext` es un string opaco.
- **Agnóstico del proveedor** — el dominio y los handlers solo conocen la interfaz `ISmsProvider`.
- **Agregar un proveedor = una clase + un atributo.** Cero cambios en factory, dominio o handlers.
- **Cada envío es un snapshot inmutable** (`to`, `body`, media congelados aunque el customer cambie luego).
- **Idempotencia obligatoria** por `(tenantId, idempotencyKey)`.
- **Media no soportada FALLA explícito** — nunca se degrada a texto.
- **Webhooks propios** — cada servicio es dueño de sus endpoints y secretos; el Gateway solo enruta.
- **La plataforma (SaaS) elige el proveedor** — no el tenant. Failover configurable.

---

## 2. Arquitectura

Cuatro proyectos (Clean Arch + DDD), patrón estándar del backend:

```
src/Services/Sms/
├── TaxVision.Sms.Api             # Controllers, Program.cs, Wolverine, auth, config
├── TaxVision.Sms.Application     # Commands/handlers (CQRS), ISmsProvider, router, validadores
├── TaxVision.Sms.Domain          # Aggregates + VOs (SmsMessage, SmsOptOut, PhoneE164, ...)
└── TaxVision.Sms.Infrastructure  # EF DbContext, repos, adapters de proveedor, migraciones
```

- **DB propia:** `TaxVision_Sms` (SQL Server), filtro global fail-closed por tenant.
- **Mensajería:** Wolverine + RabbitMQ. Cola `sms-events` bindeada al exchange `taxvision-events`.
- **Puertos:** dev 5470, interno 8080. Gateway enruta `/sms/**`.
- **Eventos publicados (agnósticos):** `SmsMessageAccepted/Delivered/Failed/Suppressed` (namespace
  `BuildingBlocks.Messaging.SmsIntegrationEvents`), payload mínimo: `tenantId, messageId, customerId,
  sourceContext?, providerMessageId?, correlationId, failureCode?`.

---

## 3. Flujo de envío

### Endpoint
`POST /sms/messages` — `[Authorize]` + `[AllowActorTypes(Service, TenantAdmin, TenantEmployee)]` +
`[HasPermission("sms.send")]`. Acepta **lote 1..N** con éxito parcial:

```json
{ "messages": [
  { "customerId": "…", "to": "+18095551234", "message": "Hola",
    "media": [{ "url": "…", "contentType": "application/pdf", "fileName": "x.pdf", "sizeBytes": 1000 }],
    "idempotencyKey": "opcional", "sourceContext": "opcional" }
] }
```

Header opcional `X-Correlation-Id` (si no llega, SMS genera uno para todo el lote).

Respuesta (`200`, un resultado por item, independientes):
```json
{ "batchId": "…", "correlationId": "…", "results": [
  { "messageId": "…", "customerId": "…", "to": "+18095551234", "status": "Accepted",
    "providerMessageId": "…", "errorCode": null }
] }
```

### Procesamiento por item (`SendSmsBatchHandler`)
1. Validar `customerId`, normalizar `to` a E.164, validar `message`.
2. **Opt-out gate:** si el número hizo STOP → se persiste `Suppressed` (auditable), **no se envía**.
3. **Idempotencia:** clave explícita o derivada (`hash(tenant|customer|to|body|media)`); si ya existe
   `(tenantId, idempotencyKey)` → devuelve el existente, **no reenvía**.
4. **Failover de plataforma:** intenta cada proveedor del orden (ver §5). Por cada uno: valida media
   contra sus capabilities; si no la soporta → siguiente; envía; si acepta → listo; si rechaza / cae →
   siguiente. Se recuerda el último error.
5. Persiste `SmsMessage` con el **proveedor que realmente envió** (o el primario si todos fallaron),
   marca `Accepted`/`Failed` y publica el evento correspondiente.

### Estados (`SmsMessageStatus`)
`Pending → Accepted → Delivered` · `→ Failed` · `→ Undeliverable` · `Pending → Suppressed`.
Transiciones idempotentes (los replays de webhook no rompen nada).

---

## 4. Modelo de proveedor (el corazón agnóstico)

### Interfaz
`TaxVision.Sms.Application/Providers/ISmsProvider.cs`:

```csharp
public interface ISmsProvider {
    string Code { get; }                                   // key de keyed-DI (ej. "infobip")
    SmsProviderCapabilities Capabilities { get; }          // media/DLR/inbound/bulk/límites
    Task<Result<SmsSendResult>> SendAsync(SmsSendRequest r, CancellationToken ct);
    Task<Result<IReadOnlyList<SmsSendResult>>> SendBatchAsync(IReadOnlyList<SmsSendRequest> rs, ...);
    Result<SmsSignatureCheck> VerifySignature(string rawPayload, string signatureHeader, string secret, string requestUrl = "");
    Result<SmsDeliveryUpdate> ParseDeliveryReceipt(string rawPayload);   // DLR → estado canónico
    Result<SmsInboundMessage> ParseInbound(string rawPayload);           // MO → STOP/START/HELP
}
```

Todas las operaciones devuelven `Result<T>` — **nunca lanzan** por fallos normales del proveedor.

### Registro por atributo + keyed DI
`TaxVision.Sms.Infrastructure/Providers/SmsProviderRegistration.cs`:
`AddSmsProviders()` descubre por reflexión toda clase con `[SmsProvider("code")]` y la registra
keyed-scoped por su código. `ISmsAdapterFactory.Resolve(code)` la resuelve. **Agregar un proveedor no
toca este archivo.**

### Router (quién elige el proveedor)
`TaxVision.Sms.Application/Providers/ISmsProviderRouter.cs` — `SmsProviderRouter.ResolveOrder()` devuelve
la cadena priorizada de adapters. Es **decisión de plataforma** (config del servicio), no del tenant.

---

## 5. Multi-proveedor y failover (nivel plataforma)

El SaaS registra cuantos adapters quiera; **elige uno por defecto y, opcionalmente, una cadena de
failover**. Config (`SmsOptions`):

- `Sms:DefaultProvider` — proveedor único por defecto (ej. `infobip`).
- `Sms:ProviderOrder` — lista priorizada `["infobip","twilio"]`: envía por el primero; si **rechaza o
  está caído (o no soporta la media)**, reintenta con el siguiente. **Vacío ⇒ solo `DefaultProvider`**
  (sin failover).

Reglas del router:
- Filtra entradas vacías **antes** de decidir (los slots de env `Sms__ProviderOrder__0/1/2` llegan como
  cadenas vacías cuando no se usan — sin filtrar, dejarían la ruta vacía → `sms.noProvider`).
- Deduplica preservando orden.

> El endpoint `/sms/messages` **no cambia** con el failover — el ruteo es interno. Para añadir routing por
> país/prefijo o por costo en el futuro, solo se reemplaza `SmsProviderRouter`, sin tocar el handler.

---

## 6. Adapters incluidos

| Código | Clase | Auth | Body | Media | DLR webhook | Notas |
|---|---|---|---|---|---|---|
| `fake` | `Providers/Fake/FakeSmsProvider` | — | — | sí (config) | simulado | dev/E2E; `[REJECT]` en el body simula rechazo; firma siempre válida |
| `generic` | `Providers/Generic/GenericHttpSmsProvider` | basic/bearer/apiKeyHeader | json o form (config) | config | HMAC-SHA256 (config) | REST estándar dirigido 100% por config (`RequestMap`/`ResponseMap` planos) |
| `textmaxx` | `Providers/Textmaxx/TextmaxxSmsProvider` | Basic `base64(clientApiKey:userApiToken)` | form | **no** (solo texto) | **no** (legado sin DLR estándar) | caps fijas en código; wire por config |
| `infobip` | `Providers/Infobip/InfobipSmsProvider` | `Authorization: App {apiKey}` | JSON **anidado** `messages[].destinations[].to` | no | `results[].status.groupName` | caps fijas (DLR+inbound+bulk); encoder relajado para `+` literal |
| `twilio` | `Providers/Twilio/TwilioSmsProvider` | Basic `base64(AccountSid:AuthToken)` | **form-urlencoded** | **sí (MMS, `MediaUrl`)** | `MessageStatus` (form) | firma `X-Twilio-Signature` real (HMAC-SHA1 sobre **URL + params ordenados**) |

**Cuándo `generic` vs adapter dedicado:** si el proveedor es REST con body/response planos y auth
estándar → configúralo como `generic`. Si tiene forma **anidada** (Infobip), **form-urlencoded**
(Twilio), un esquema de firma propio (Twilio) o restricciones que deben ir fijas → **adapter dedicado**.

---

## 7. Cómo agregar un proveedor nuevo (receta)

Ejemplo: agregar "AcmeSms". **Solo tocas archivos nuevos + config; nada de factory/dominio/handlers.**

1. **Crear la clase** `src/Services/Sms/TaxVision.Sms.Infrastructure/Providers/Acme/AcmeSmsProvider.cs`:
   ```csharp
   [SmsProvider("acme")]
   public sealed class AcmeSmsProvider(
       IHttpClientFactory httpClientFactory,
       IOptions<SmsProvidersOptions> options,
       HttpResiliencePipelineRegistry resilience,
       ILogger<AcmeSmsProvider> logger) : ISmsProvider
   {
       public const string ProviderCode = "acme";
       public string Code => ProviderCode;
       private SmsProviderConfig Config => options.Value.Providers["acme"];

       // Capacidades: fíjalas en código si son constantes del proveedor (recomendado),
       // o léelas de Config.Capabilities si querés que sean configurables.
       public SmsProviderCapabilities Capabilities { get; } = new() { /* … */ };

       public async Task<Result<SmsSendResult>> SendAsync(SmsSendRequest r, CancellationToken ct) {
           var http = httpClientFactory.CreateClient(nameof(AcmeSmsProvider));
           // 1) construir request (URL de Config.BaseUrl+SendPath, auth, body en el formato de Acme)
           // 2) POST con el circuit-breaker: resilience.GetOrCreate(nameof(AcmeSmsProvider))
           // 3) mapear la respuesta a SmsSendResult(accepted, providerMessageId, errorCode?, errorMessage?)
           // 4) try/catch (HttpRequestException/TaskCanceledException/BrokenCircuitException) → providerUnavailable
       }
       public Task<Result<IReadOnlyList<SmsSendResult>>> SendBatchAsync(...) // loop sobre SendAsync
       public Result<SmsSignatureCheck> VerifySignature(string raw, string sig, string secret, string url = "") // HMAC del proveedor
       public Result<SmsDeliveryUpdate> ParseDeliveryReceipt(string raw)  // → SmsCanonicalStatus
       public Result<SmsInboundMessage> ParseInbound(string raw)          // → SmsInboundKeyword
   }
   ```
2. **Registrar su HttpClient** en `Infrastructure/DependencyInjection.cs`:
   ```csharp
   services.AddHttpClient(nameof(Providers.Acme.AcmeSmsProvider), h => h.Timeout = TimeSpan.FromSeconds(30));
   ```
3. **Config** en `Api/appsettings.json` bajo `Sms:Providers:acme` (`BaseUrl`, `SendPath`, `Auth`,
   `RequestMap`/`ResponseMap` si aplica, `Webhook`, `Capabilities`).
4. **Secretos por env** en `deploy/docker/docker-compose.yml` (servicio `sms-api`) + `.env` — **nunca
   hardcodear la key** en el repo:
   ```yaml
   Sms__Providers__acme__BaseUrl: ${SMS_ACME_BASE_URL:-}
   Sms__Providers__acme__Auth__Credential: ${SMS_ACME_API_KEY:-}
   ```
5. **Activarlo:** `SMS_DEFAULT_PROVIDER=acme` (o mételo en `SMS_PROVIDER_ORDER_0` para failover).
6. **Tests** en `deploy/tests/TaxVision.Sms.Tests/Providers/AcmeSmsProviderTests.cs` (usa un
   `HttpMessageHandler` que capture el request — ver `TwilioSmsProviderTests`/`InfobipSmsProviderTests`).

Eso es todo. `AddSmsProviders()` lo descubre solo por el atributo `[SmsProvider("acme")]`.

---

## 8. Webhooks (DLR + inbound)

- Endpoints: `POST /sms/webhooks/{provider}/status` (DLR) y `POST /sms/webhooks/{provider}/inbound`
  (STOP/START/HELP). `[AllowAnonymous]` (protegidos por **firma del proveedor**, no por JWT).
- El handler: resuelve el adapter por `{provider}` → `VerifySignature(rawBody, header, secret, requestUrl)`
  → `ParseDeliveryReceipt`/`ParseInbound` → **dedup** por `(providerCode, providerMessageId, eventType)`
  → aplica transición idempotente / opt-out → publica evento.
- **Firma por proveedor:** cada adapter implementa la suya. Twilio necesita la **URL pública exacta**
  del POST (HMAC-SHA1 sobre URL + params ordenados) — por eso `VerifySignature` recibe `requestUrl`, que
  el `WebhooksController` reconstruye desde `X-Forwarded-Proto/Host` (detrás del gateway/túnel). Infobip
  y otros: HMAC-SHA256 del body si se configura `Webhook.Secret`; **fail-closed** si no hay secreto.
- **En local:** el proveedor no alcanza tu `localhost`. Usa un túnel (ej. `ngrok http 5047`) y configura
  en el portal del proveedor el *delivery report* → `https://<túnel>/sms/webhooks/<provider>/status` y el
  inbound → `.../inbound`.

**Alternativa sin webhook:** consultar la API del proveedor. Ej. Infobip `GET /sms/1/logs?messageId=…`,
Twilio `GET /2010-04-01/Accounts/{Sid}/Messages/{Sid}.json` — devuelven el estado real de entrega.

---

## 9. RBAC (`sms.send`)

`POST /sms/messages` está gateado por **dos capas**:

1. **Actor-type** (`[AllowActorTypes(Service, TenantAdmin, TenantEmployee)]`) — quién puede, por tipo.
2. **Permiso** (`[HasPermission("sms.send")]`).

Wiring (patrón RBAC Fase 7 del backend):
- Constante: `BuildingBlocks/Authorization/SmsPermissions.Send = "sms.send"`.
- Catálogo Auth: `PermissionCatalog.cs` (GUID `a1000000-…-158`, humano-asignable → **TenantAdmin** lo
  recibe vía `SystemRoleDefaults`) + migración `AddSmsPermission`.
- Caller M2M: el cliente de servicio (ej. `notification-worker`) lleva `sms.send` en su claim `perm`
  (config `ServiceAuth:Clients` de Auth / docker-compose). El actor `Service` **bypassea** la proyección.
- Lado SMS: `PermissionPolicyProvider` + `AddUserPermissionsSource("Projection")` + proyección local
  (`UserPermissionsProjection`/`RolePermissionsProjection`) mantenida por 2 consumers Wolverine de los
  eventos de Auth (`UserRolesChanged`/`RolePermissionsChanged`), registrados **explícitamente** con
  `Discovery.IncludeType(typeof(...))` (la discovery convencional omite clases estáticas).

Verificado en vivo: M2M con permiso → 200; M2M sin permiso → 403; JWT humano (PlatformAdmin) → 200.

---

## 10. Referencia de configuración

| Env var | Config bindeada | Para qué |
|---|---|---|
| `SMS_DEFAULT_PROVIDER` | `Sms:DefaultProvider` | proveedor por defecto (`fake`/`infobip`/`twilio`/…) |
| `SMS_PROVIDER_ORDER_0/1/2` | `Sms:ProviderOrder[]` | cadena de failover (vacío = sin failover) |
| `SMS_DB_CONNECTION` | `ConnectionStrings:Default` | SQL Server `TaxVision_Sms` |
| `SMS_INFOBIP_BASE_URL` | `Sms:Providers:infobip:BaseUrl` | ej. `https://vyg8je.api.infobip.com` |
| `SMS_INFOBIP_API_KEY` | `…:infobip:Auth:Credential` | **secreto** (`Authorization: App {key}`) |
| `SMS_INFOBIP_SENDER_ID` | `…:infobip:SenderId` | remitente (ej. `InfoSMS`) |
| `SMS_TWILIO_CREDENTIAL` | `…:twilio:Auth:Credential` | **secreto** `AccountSid:AuthToken` |
| `SMS_TWILIO_FROM` | `…:twilio:SenderId` | número Twilio `+1…` o Messaging Service Sid |
| `SMS_GENERIC_BASE_URL` | `…:generic:BaseUrl` | endpoint del proveedor genérico |

`SmsProviderConfig` (por proveedor, `Sms:Providers:{code}`): `BaseUrl, SendPath, HttpMethod, BodyFormat
(json|form), SenderId, Auth{Type,HeaderName,Credential}, RequestMap{To,From,Body,Media},
ResponseMap{ProviderMessageIdPath}, Webhook{Secret,SignatureHeader,ProviderMessageIdPath,StatusPath,
StatusMap,…}, Capabilities{SupportsMedia,SupportsDeliveryReceipts,MaxMediaItems,…}`.

> **Los secretos SIEMPRE por env/secret-manager, nunca en el repo.** `appsettings.json` deja `Credential`
> vacío y un comentario. La `.env` guarda los valores reales (fuera de git).

---

## 11. Testing

`deploy/tests/TaxVision.Sms.Tests/` (xUnit + fakes escritos a mano, sin Moq). **103 tests.**
`dotnet test deploy/tests/TaxVision.Sms.Tests`. Cubre: dominio (VOs, transiciones, opt-out, proyecciones),
Application (validador de media, handler con happy/opt-out/idempotencia/media-fail/reject/parcial +
**failover**), router (orden, dedup, slots vacíos), webhooks (firma/dedup/transición/resolución), y cada
adapter real (auth header, body, parse de DLR/inbound, firma).

Para un adapter nuevo, calca `Providers/TwilioSmsProviderTests.cs`: `HttpMessageHandler` que captura el
request + `IHttpClientFactory` de una sola instancia + `HttpResiliencePipelineRegistry` real.

---

## 12. Operaciones y gotchas

- **Activar un proveedor real:** pon sus secretos en `.env`, `SMS_DEFAULT_PROVIDER=<code>` (o failover),
  recreá `sms-api`: `docker compose --env-file ../../.env up -d --no-deps --force-recreate sms-api`.
- **Cuentas trial:** Infobip/Twilio en modo demo **solo entregan a números verificados** (envío aceptado
  pero DLR = `REJECTED / DESTINATION_NOT_REGISTERED` en Infobip, o error `20003`/`21608` en Twilio).
- **Imagen stale = trampa clásica:** si el build Docker falla (típicamente restore de NuGet), `up
  --force-recreate` recrea de la imagen **vieja** en silencio → parece "no se aplicó el cambio". Verificá
  el build: `docker compose build sms-api 2>&1 | grep -iE "naming to|error|NU[0-9]{4}"` y que la imagen sea
  reciente (`docker images taxvision/sms-api:dev`). El Dockerfile usa un **cache mount de NuGet** para que
  los reintentos retomen la descarga.
- **Comprobar qué proveedor está activo:** `docker exec taxvision-sms-api sh -c 'echo $Sms__DefaultProvider'`.

---

## 13. Estado y pendientes

**Hecho + verificado:** dominio + envío + idempotencia + opt-out + dedup + eventos; RBAC `sms.send`
(M2M y humano); router + failover (demo en vivo `generic`→`fake`); adapters `fake/generic/textmaxx/
infobip/twilio`; Infobip **entregó a handset** en vivo; 103 tests verdes.

**Pendiente (no bloqueante):**
- Twilio: **AuthToken vigente** (el del `appsettings` legado da `20003`; el `AccountSid`/`From` sirven).
  Credenciales guardadas en `.env`, listas para activar.
- Webhooks DLR/inbound **en vivo** (requiere túnel + configurar el portal del proveedor).
- Persistencia del `TwilioPrice`/costeo y del `perm`-projection para usuarios no-admin no disparada en
  vivo (env sin tenants provisionados; el onboarding tiene un bug aparte — tarea separada).

Ver también: `../campaigns/sms/ADR.md` (diseño previo, contexto) y las memorias del proyecto.
