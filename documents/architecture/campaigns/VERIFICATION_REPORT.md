# Campaigns Suite — Informe de Verificación contra el repo (2026-09-17)

Pase de validación de las citas `VERIFIED` de la suite contra el código real del workspace (`TaxVsion_BackEnd`, `TaxVsion_Front`). Responde a la observación del review: *"los % VERIFIED son sobre los textos, no sobre un commit"*. Método: 4 exploraciones de solo-lectura sobre clusters de citas.

## Resumen

- **Sistema nuevo (acceso, canales, mensajería, realtime, primitivas): CONFIRMADO** — todas las citas numéricas clave cayeron exactas o con un matiz menor. La arquitectura reusa lo que dice reusar.
- **Repo legado (`CRMTAXPROBACKEND/CampaignService`): NO está en el workspace** (solo `TaxVision/campaigns.rar` comprimido, sin extraer). Por tanto **todas las citas legadas quedan "no verificables aquí"** — **no** refutadas, solo no comprobables contra fuente en este workspace. Ver §5.

## 1. Acceso / autorización — CONFIRMADO

| Cita de la doc | Real | Veredicto |
|---|---|---|
| `campaigns.manage` en `PermissionCatalog.cs:39` | `src/Services/Auth/Domain/Roles/PermissionCatalog.cs:39` | ✅ exacto |
| Definición (módulo `campaigns`, MinPlanTier Pro) `:474-482` | idem `:474-482` | ✅ |
| TenantAdmin lo recibe por defecto | `PermissionCatalog.cs:1908-1914` (regla de **bundle**: no-customer-portal ∧ no-platform ∧ no-dangerous) | ✅ en efecto (implícito, no línea explícita) |
| `PermissionModuleMap.cs:45` (`campaigns.`→`campaigns`) | `src/BuildingBlocks/Authorization/PermissionModuleMap.cs:45` | ✅ exacto |
| Entitlement `module.campaigns` en Pro/Enterprise `:59,:83` | listas `"campaigns"` en `SubscriptionPlanCatalogSeeder.cs:59,:83`; prefijo `module.` aplicado en `:125` | ✅ (la key se forma en :125) |
| Gate enforce vs log-only + flag | `PermissionPolicyProvider.cs:50-88`, flag `Authorization:ModuleGate:Enforce` leído en `:68-71`, **default `false` (LOG-ONLY)** | ✅ coincide con #17 |

**Matiz corregido en la doc:** no existe una clase `ModuleGate` dedicada; la lógica está **inline** en `PermissionPolicyProvider` (`BuildingBlocks.Web.ActorTypeAuthorization`) + `PermissionModuleMap`. El único "ModuleGate" es la categoría de logger. (Aplicado en `campaigns/Security.md`.)

## 2. Entrypoints de canal — CONFIRMADO (8/8)

| Cita | Real | Veredicto |
|---|---|---|
| SMS `MessagesController.cs:21` permite `ActorType.Service` | `src/Services/Sms/TaxVision.Sms.Api/Controllers/MessagesController.cs:21` | ✅ |
| `SendSmsBatchCommand` + `POST /sms/messages` | `SendSmsBatch.cs:29`; ruta `MessagesController.cs:19,38` | ✅ |
| Email `SendEmail.cs` soporta recipients+adjuntos | `SendEmail.cs:16-24` | ✅ |
| Email fija `campaignId:null` | `SendEmail.cs:58` | ✅ |
| Email controller **no** permite `Service` | `EmailSendController.cs:29` | ✅ |
| Push `FcmPushSender` + flag `Notification:UseFcmPush` | `FcmPushSender.cs:20`; `DependencyInjection.cs:103-111` (default `false`) | ✅ |
| Push es 1:1 (sin bulk) | `Senders.cs:18-29` (un token por llamada) | ✅ |

## 3. Primitivas de mensajería — CONFIRMADO

| Cita | Real | Veredicto |
|---|---|---|
| `CampaignId` en request `PostmasterEmailEvents.cs:37` y result `:104` | exactos; además echo en `:120,137,152,169` | ✅ |
| `[MessageIdentity(...)]` alias/versión | `PostmasterEmailEvents.cs:24,91,110,126,143,162` | ✅ |
| `ProcessedBusinessMessage` (Begin/Complete/Fail, fingerprint SHA-256) `:9-23,27-105,52-56` | exactos (estados en `ProcessedBusinessMessageStatus.cs`) | ✅ |
| `IntegrationEvent` base tiene `TenantId` | `IIntegrationEvent.cs:15` (`abstract record IntegrationEvent`) | ✅ (valida #13; no redeclarar) |
| Exchange fanout `taxvision-events` + outbox/inbox durable | `.ToRabbitExchange("taxvision-events")` + `UseDurableOutbox/Inbox` en cada `Program.cs` (Growth `:142,151,154`, etc.) | ✅ (config **por servicio**, no centralizada) |

## 4. Realtime + `Money` — CONFIRMADO

| Cita | Real | Veredicto |
|---|---|---|
| Relay `CLR_TYPE_TO_EVENT_TYPE` (Communication) | `Communication/src/infrastructure/rabbit/consumer-runtime.ts:48-158` (usado en `:326`) | ✅ |
| `emitToTenant`/`emitToUser` (Socket.IO) | `socket-realtime-emitter.ts:47-58` | ✅ |
| Frontend `communication-realtime.service.ts` | `TaxVsion_Front/src/app/core/realtime/communication-realtime.service.ts:27,98-99` (path `/communication/socket.io`) | ✅ |
| `Money` VO (long cents, ISO, rechaza negativos) | `PaymentApp/.../ValueObjects/Money.cs:6-28` | ✅ (patrón replicado en 6+ servicios) |

## 5. Citas LEGADAS — NO VERIFICABLES en este workspace

El repo `CRMTAXPROBACKEND/CampaignService` **no está presente** (solo `TaxVision/campaigns.rar`, comprimido, no extraído). Por tanto, todas las citas del tipo `CRMTAXPROBACKEND/.../CampaignSchedulerBackgroundService.cs`, `CreateCampaignCommandHandler.cs`, `CampaignSendService.cs`, `Campaign.cs`, `RecurrenceRule.cs`, `RecurrenceCalculator.cs`, `SmtpProviderConfig.cs`, `WalletServiceClient.cs`, etc. son **UNVERIFIED (legacy no en workspace)** — no se refutan, no se comprueban.

**Matiz importante:** `CampaignSchedulerService.cs` **sí existe**, pero en el sistema **actual** (`Notification/TaxVision.Notification.Api/Jobs/CampaignSchedulerService.cs`) — es la **feature email-only que el diseño supersede (D5)**, no el CRM legado. Algunas citas legadas pueden en realidad referirse a esta feature actual (verificable) y no al CRM.

**Recomendación:** reclasificar en la suite las citas legadas de `VERIFIED (94-98%)` a **`VERIFIED-legacy (fuera de workspace)`**, o **extraer `campaigns.rar`** a un directorio de referencia y re-verificarlas. Mientras tanto, la solidez del diseño **no** depende de ellas: describen el anti-patrón a corregir, no el contrato a reusar (ese sí está CONFIRMADO, §1-4).

## 6. Correcciones aplicadas tras este pase

- `campaigns/Security.md` §2 (#17): la lógica de gate es **inline en `PermissionPolicyProvider`** (no una clase `ModuleGate`); flag `Authorization:ModuleGate:Enforce` default `false`.
- `campaigns/Commands_And_Events.md`: `TenantId` confirmado en el base `IIntegrationEvent.cs:15`.
- Este informe + nota en `REVIEW_REMEDIATION.md` sobre el estado legado (no-en-workspace).
