# Push + In-app — Seguridad

Servicio: **Push (reusa `Notification`) + In-app (reusa `Communication`)**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**
Ancla: `../05_Master_ADR.md`, `TaxVsion_BackEnd/CLAUDE.md`, `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`, guía RateLimit.

## 1. Superficie de seguridad de esta integración

Esta integración **no** agrega endpoints públicos ni secretos de proveedor. La superficie es: (a) **eventos de bus** cross-service (M2M implícito por el transporte durable), (b) reuso de dos servicios que ya tienen su postura de seguridad. Por eso la mayor parte del trabajo es **preservar** las invariantes existentes, no crear controles nuevos.

| Vector | Estado | Nota |
|---|---|---|
| Endpoints HTTP nuevos | **Ninguno** | Campaigns posee la API pública; el ejecutor solo consume/publica eventos |
| Secretos de proveedor nuevos | **Ninguno** | Credenciales FCM ya existen (`FcmOptions`); in-app no usa proveedor externo |
| Datos personales en el bus | Título/cuerpo renderizados + `TargetUserId` | minimizar; no logs de OTP/tokens (ver §5) |
| JWT de usuario persistido | **Prohibido** | anti-patrón legado corregido (ver §4) |

## 2. Secretos: reuso, no expansión

- **Push/FCM**: credenciales = cuenta de servicio Firebase montada como **secreto de archivo** (`FcmOptions.ServiceAccountJsonPath`, sección `Notification:Push:Fcm`, `Notification.Infrastructure/Push/FcmOptions.cs:11-13`). El docblock ya fija la regla: "en producción se monta como secreto de archivo (nunca embebido en appsettings ni en variables de entorno en texto plano)". Esta integración **reusa** ese secreto tal cual — **no** introduce credenciales por-campaña ni por-tenant.
- **In-app**: sin proveedor externo → sin secreto de entrega. La emisión es Socket.IO interno (`Communication`).
- **Resultado**: Push/In-app son los únicos ejecutores de la suite que **no** agregan secretos cifrados de proveedor (contraste con Email-SMTP2GO/SMS/WhatsApp).

## 3. Multi-tenant fail-closed

- **Push (Notification, .NET)**: todo el acceso a datos es tenant-scoped por diseño existente — `IPushDeviceTokenRepository.ListActiveForUserAsync(tenantId, userId, …)` (`Senders.cs:54`), `PushDeviceToken : TenantEntity` (`PushDeviceToken.cs:19`). El consumer de campaña **debe** correr con **tenant explícito en el scope de Wolverine** (`TenantId` viaja en `CampaignPushDispatchRequested`) y usar `.IgnoreQueryFilters()`+tenant explícito solo donde la guía lo autoriza (ver `Guia_IgnoreQueryFilters`). Nunca `.Where(t => t.TenantId == x)` manual (anti-patrón legado, `05_Master_ADR §9`).
- **In-app (Communication, Node)**: `notificationEntry` es tenant-scoped; el unique de idempotencia es `(TenantId, SourceEventId, UserId)` (`prisma-notification-repository.ts:30-33`) → un `SourceEventId` colisiona solo dentro del mismo tenant. El emit de socket usa el room `t:{tenantId}:u:{userId}` (`realtime-emitter.ts:38`) → aislamiento por-tenant en el transporte realtime.
- **Cross-tenant en un run**: cada evento dispatch lleva **un** `TenantId`; el ejecutor jamás mezcla tenants en una operación. Un `TargetUserId` que no pertenece al `TenantId` del evento → `NoDevices`/`NoRecipientUser` (fail-closed), nunca entrega cruzada.

## 4. RBAC / identidad / anti-patrón JWT

- **Sin JWT de usuario persistido**. El legado guardaba `Campaign.BackgroundAuthToken` (JWT de usuario en texto plano) para refunds diferidos (`05_Master_ADR §Anti-patrones 5`). Esta integración **no** persiste ningún token de usuario: la comunicación cross-service es **M2M por el bus durable** (Wolverine outbox/inbox), con el tenant en el scope del mensaje, no un JWT robado de un request humano.
- **Registro de dispositivos (self-service, sin cambios)**: `PushDevicesController` exige `[Authorize]`, deriva `UserId`/`TenantId` **del JWT, nunca del body** (docblock del controller) → un usuario solo toca sus propios tokens. `[AllowActorTypes(TenantEmployee,TenantAdmin,CustomerPortal,PlatformAdmin)]`. Esta postura se **preserva**.
- **M2M HTTP (si lo hubiera)**: no se prevé HTTP cross-service en este flujo (todo por bus). Cualquier llamada HTTP futura entre Campaigns y estos servicios usaría client-credentials con **audience/scope** propios (RBAC acumulativo, sin bypass, `05_Master_ADR`).

## 5. Minimización y contenido sensible

- `NotificationLog` **nunca** almacena el cuerpo completo (puede contener OTP/tokens): solo canal, destinatario, plantilla y estado (`NotificationLog.cs:21-24`). El log de una campaña push guarda `CampaignId`/`RunId`/`templateKey`, **no** el `Body`. Preservar esta invariante.
- Los eventos dispatch **sí** llevan `Title`/`Body` renderizados (necesario para entregar). Mitigación: (a) son mensajes efímeros en la outbox/inbox durable, no un store de largo plazo; (b) prohibido loguear el `Body` a nivel INFO (el sender ya **enmascara** el token, `FcmPushSender.cs:69,85`); (c) campañas no deben transportar OTP/secretos (son marketing/broadcast, no transaccionales).
- `TargetUserId` es un ID interno opaco — **no** PII directa. `RecipientId`/`CampaignId`/`RunId` son GUIDs opacos.

## 6. Rate limiting / backpressure

- **Endpoints públicos nuevos**: ninguno → nada nuevo que anotar con `[RateLimit]`. El endpoint reusado ya cumple: `PushDevicesController` usa `[RateLimit("notification.g.push_device")]` en Register/Revoke.
- **Consumo de campaña**: no se gobierna por `[RateLimit]` (categoría HTTP) sino por **backpressure de la cola** (Wolverine / ConsumerRuntime) — el fan-out de Campaigns respeta la capacidad del ejecutor; FCM tiene sus propios límites de cuota (un `FailedTransient` por throttling se reintenta con el mismo `Attempt`).
- Si se agrega algún endpoint admin/test (ej. "enviar push de prueba de campaña"), **debe** llevar `[RateLimit(categoría)]` o `[RateLimitExempt]` explícito (guía `RateLimit/Guia_Nuevos_Servicios_Endpoints.md`).

## 7. Preferencias como control de seguridad/consentimiento

Respetar `IUserNotificationPreferenceRepository` (opt-out) **no** es solo UX: para push de marketing es un requisito de consentimiento. El consumer push consulta la preferencia con la `Category` del evento (patrón `NotificationDispatcher.cs:209-223`); campañas = categoría **no-locked** → opt-out se honra → `SuppressedByPreference` (no-billable, sin entrega). Ninguna campaña puede marcar su categoría como locked para saltarse el opt-out.

## 8. Checklist de seguridad (diseño)

- [ ] Consumer push corre con tenant explícito en scope Wolverine; sin `.Where` manual de tenant.
- [ ] Business-inbox (Notification) e idempotencia (Communication) evitan re-entrega/doble-cobro.
- [ ] Cero secretos nuevos; FCM sigue como secreto de archivo montado.
- [ ] Cero JWT de usuario persistido; cross-service = M2M por bus.
- [ ] `NotificationLog` sin cuerpo; sin log de `Body` en INFO; token enmascarado.
- [ ] Opt-out (categoría no-locked) honrado → `SuppressedByPreference`.
- [ ] Cualquier endpoint nuevo con `[RateLimit]`/`[RateLimitExempt]`.
