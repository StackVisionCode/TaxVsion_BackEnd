# Campaigns Suite — Resumen Ejecutivo

> **REVISIÓN 2026-09-16/17 (ADR-CAMP-001, APPROVED).** Este resumen se actualizó a las decisiones del usuario: **Campaign = orquestador agnóstico de canal con CERO dinero** (D1); gestiona todas las aristas pero **no envía** (D2); los canales **reusan servicios existentes** vía consumers (D3); el **acceso ya está cableado** (D4); el orquestador **supersede** la feature email-only de Notification (D5); primeros canales **Email + SMS** (D6); la **autorización por saldo es un interceptor/PEP externo**, con Wallet, ambos **DIFERIDOS** (D7). El texto de abajo ya refleja esto; la versión original centrada en dinero quedó superseded.

Fecha: 2026-07-28 (original) · revisado 2026-09-17
Estado: **DISEÑO — no implementado** (greenfield salvo reuso explícito)
Lee primero: `00_Overview_And_Index.md`, `02_Context_Map.md`, `05_Master_ADR.md` (ADR-CAMP-000 + ADR-CAMP-001).

## 1. El problema en una frase

TaxVision necesita **campañas multicanal** (email, SMS, WhatsApp, push, in-app) donde el tenant **define y agenda** el envío. El CRM legado (`CRMTAXPROBACKEND/CampaignService`) lo resolvió con un **monolito** que define, agenda, entrega, integra proveedores y cobra de un wallet TXC, acumulando nueve anti-patrones (§4). Este diseño lo reemplaza con **bounded contexts separados** bajo el principio **creador/orquestador vs ejecutor**, donde **Campaign solo orquesta campaña** (no envía, no cobra) y los canales son **consumers dentro de servicios existentes**.

## 2. La decisión (ADR-CAMP-000 + ADR-CAMP-001, APPROVED)

- **Campaigns (NEW)** = creador/orquestador agnóstico de canal: `Campaign` + `CampaignRun` inmutable por ejecución + `Recipients` + **aristas** (remitente `SenderProfile`, contactos/listas propias, clientes vía Customer, envío inmediato, schedule/recurrente, **detalles/reporting**). Orquesta `resolver audiencia → fan-out dispatch por destinatario → result → agregar → cerrar run`. **No** entrega, **no** integra proveedores, **no** tiene secretos, **no** maneja dinero.
- **Canales = consumers en servicios existentes (D3):** Email/Push → **`Notification`** (Email reusa `SendEmailCommand`; Push reusa `FcmPushSender` + contrato bulk); SMS → **`TaxVision.Sms`** (ya existe, ya M2M: `SendSmsBatchCommand`, `MessagesController.cs:21`); WhatsApp → **`TaxVision.WhatsApp`** nuevo, fase posterior; In-app → **`Communication`**; render → **`Scribe`**. Cada canal consume `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`.
- **Scheduler (NEW/módulo)** = disparo temporal con **lease atómico** (fix del doble-scheduler + `Status=Sending` no-atómico del legado); un `CampaignRun` por disparo.
- **Acceso ya cableado (D4):** permiso `campaigns.manage` (`PermissionCatalog.cs:39`), módulo `campaigns` (`PermissionModuleMap.cs:45`), entitlement `module.campaigns` (Pro/Enterprise). No se re-agrega. Dos verificaciones ortogonales: RBAC + entitlement. **Ninguna de dinero.**
- **Dinero = FUERA de Campaign y DIFERIDO (D1/D7):** la regla "sin saldo no se envía; para scheduled/recurrente cobrar antes según cuántos destinatarios" la hace un **interceptor/PEP externo** delante de la ejecución (trigger + cada `RunDue`) apoyado en un **Wallet** separado. Ambos DIFERIDOS; hoy el seam está abierto. Campaign no cambia cuando entren.

## 3. Tabla de evidencia

| # | Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|---|
| E1 | Backend Campaigns nuevo no existe | `Glob src/Services/{Campaign,Campaigns}*` → 0 | VERIFIED | 99% |
| E2 | Acceso ya sembrado: permiso + módulo + entitlement `campaigns` | `PermissionCatalog.cs:39,474-482`, `PermissionModuleMap.cs:45`, `SubscriptionPlanCatalogSeeder.cs:59,83` | VERIFIED | 96% |
| E3 | Seam `CampaignId` ya fluye Notification→Postmaster sin interpretar | `PostmasterEmailEvents.cs:37,104` | VERIFIED | 97% |
| E4 | SMS ya existe y ya es M2M (consumer de dispatch factible) | `Sms/.../MessagesController.cs:21`, `SendSmsBatch.cs` | VERIFIED | 96% |
| E5 | Email `SendEmailCommand` soporta recipients+adjuntos, fija `campaignId:null`, no permite Service | `Notification/.../SendEmail.cs` | VERIFIED | 95% |
| E6 | Push reusable (FCM), hoy 1:1 | `Notification/.../Push/FcmPushSender.cs` (flag `Notification:UseFcmPush`) | VERIFIED | 95% |
| E7 | In-app reusable | `Communication` (Node/Socket.IO) | VERIFIED | 95% |
| E8 | Primitiva idempotencia de negocio existe | `ProcessedBusinessMessage.cs:27-74` | VERIFIED | 97% |
| E9 | Legado: JWT de usuario persistido en texto plano | `Campaign.cs:87`, consumo `CampaignSendService.cs:112-127` | VERIFIED | 97% |
| E10 | Legado: secreto de proveedor en texto plano | `CampaignService/appsettings.json` (`SMTP2GO:ApiKey`) | VERIFIED | 96% |
| E11 | Legado: sin idempotencia por destinatario; marca `Sent` a todo no-fallido | `CampaignSendService.cs:55-69` | VERIFIED | 96% |
| E12 | Legado: scheduler poll `Task.Delay`, flip no-atómico, recurrentes mutan una fila | `CampaignSchedulerBackgroundService.cs:38,54-59,115-142` | VERIFIED | 96% |
| E13 | Campaign sin dinero; PEP+Wallet externos y diferidos | decisión del usuario 2026-09-16/17 (ADR-CAMP-001 D1/D7) | DECISION | 99% |

## 4. Anti-patrones del legado que este diseño corrige

(Detalle en `05_Master_ADR.md §Anti-patrones`.)

1. **Monolito** → bounded contexts separados; Campaign solo orquesta.
2. **Fan-out fire-and-forget** (`Task.Run`/poll + `Task.Delay`, se pierde al reiniciar) → outbox + fan-out por evento por destinatario, idempotente.
3. **Sin idempotencia por destinatario** (`CampaignSendService.cs:55-69`) → `dispatch_id = f(runId,recipientRef,channel,attempt)` + `ProcessedBusinessMessage`.
4. **Cobro no-atómico dentro del creador** → el dinero sale de Campaign por completo; si vuelve, es un PEP externo (D7).
5. **Secretos + JWT en BD texto plano** → secretos cifrados **en los ejecutores**, **nunca** JWT persistido, M2M client-credentials.
6. **Doble scheduler + `Status=Sending` no-atómico** → un scheduler con lease/optimistic-lock.
7. **`ChannelConfiguration: Dictionary<string,string>` sin esquema** → contrato por canal tipado y versionado.
8. **Sin entidad de run** (recurrentes mutan una fila) → `CampaignRun` inmutable por ejecución.
9. **Multi-tenant por `.Where` manual** → query filter global fail-closed + repos tenant-scoped.

## 5. Dependencias / notas de alcance

| ID | Nota | Estado |
|---|---|---|
| **N1** | Email: cerrar el gap `campaignId:null` + permitir `ActorType.Service` en el controller de Notification para el consumer de dispatch. | por hacer (fase Email+SMS) |
| **N2** | Push: agregar contrato **bulk** sobre Notification (hoy 1:1). | fase posterior |
| **N3** | WhatsApp: servicio nuevo `TaxVision.WhatsApp` (Meta/WABA), onboarding + plantillas. | fase posterior |
| **N4** | Dinero: PEP de autorización + Wallet + top-up (PaymentApp) — **DIFERIDO**; el seam ya está definido (D7). | diferido |

**Ya NO hay "BLK-1 Wallet antes que todo":** Campaign ejecuta sin ningún servicio de dinero.

## 6. Alcance MVP (detalle en `07`)

**IN:** orquestador `Campaigns` + audiencia (Clients/Contactos/Manual) + remitente (`SenderProfile`) + envío **inmediato** + contrato dispatch/result + **Email y SMS** (consumers en Notification / TaxVision.Sms) + **detalles/reporting**. **OUT/diferido:** Scheduler-recurrente avanzado, Push bulk, WhatsApp, tracking de engagement, y **todo el dinero** (PEP + Wallet + top-up). **Sin dependencia dura de Wallet.**
