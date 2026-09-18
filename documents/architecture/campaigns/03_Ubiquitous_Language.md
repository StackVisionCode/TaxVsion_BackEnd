# Campaigns Suite — Lenguaje Ubicuo (glosario)

> **REVISIÓN 2026-09-16/17 (ADR-CAMP-001, APPROVED).** Los términos de **Campaign son SIN dinero**: `CampaignRun` no lleva costo ni reserva; `Channel` no lleva precio. Los términos de dinero (Balance/LedgerMovement/Reservation/Consume/Refund/TopUp/Price) pertenecen al **Wallet + PEP externos y DIFERIDOS** (§Dinero), no a Campaign.

Fecha: 2026-07-28 (original) · revisado 2026-09-17. Términos vinculantes. Cada término dice **qué es** y **quién lo posee** (ver `04_Ownership_Matrix.md`). Se refieren entre contexts por **IDs opacos**, nunca por FK.

## Definición y orquestación (contexto Campaigns) — SIN dinero

| Término | Definición | Owner | Notas / anti-término |
|---|---|---|---|
| **Campaign** | Plantilla de intención de envío: **uno o varios canales**, template-ref, **remitente (`SenderRef`) por canal**, criterio de audiencia, `SendMode`. **Mutable solo en `Draft`**; no ejecuta, no envía, **no cobra**. | Campaigns | Legado mezclaba definición+entrega+cobro en una fila (`Campaign.cs`, `BackgroundAuthToken`). |
| **CampaignRun** | **Registro inmutable de UNA ejecución**: audiencia resuelta (Recipients), snapshot congelado (canal/remitente/plantilla), timestamps, **contadores de entrega** y resultado agregado. Una Campaign recurrente produce **N runs**. **Sin costo/reserva.** | Campaigns | Reemplaza al legado que **mutaba la misma fila** en cada recurrencia (`CampaignSchedulerBackgroundService.cs:115-142`). |
| **Recipient** | Un destinatario **dentro de un CampaignRun**: destino resuelto + `dispatch_state` + attempt + canal. Idempotencia `dispatch_id = f(runId, contactRef, channel, attempt)`. | Campaigns | Legado: `CampaignRecipient` por Campaign, sin attempt, marcado `Sent` en masa (`CampaignSendService.cs:55-69`). |
| **Audience** | **Criterio** de a quién enviar, de **3 fuentes combinables**: **Clients** (directorio Customer), **Contactos/Listas propias** (sub-dominio de Campaign, importables, con opt-out), y **Manual**. Se resuelve por referencia en el run (no snapshot stale). | Campaigns (criterio + contactos propios) / Customer (datos de clientes) | Anti-término: **ContactList copiada como snapshot stale**. |
| **Contact / ContactList** | Directorio propio de Campaign para **no-clientes** (importable CSV, opt-out/consentimiento) y sus listas. | Campaigns | Nuevo (arista de contactos). |
| **SenderProfile / SenderRef** | Catálogo del **remitente por canal** (from/dominio, número/sender id, WABA id, app push). Campaign **referencia** el `SenderRef` opaco; el ejecutor lo resuelve contra su config/secretos. | Campaigns (catálogo) / ejecutor (resuelve) | Nuevo (arista "quién envía"). |
| **SendMode / Schedule** | Cuándo se dispara: `Immediate` \| `Scheduled(at)` \| `Recurring(rule)`. | Scheduler (reloj) / Campaigns (modo) | — |
| **Recurrence** | Regla que genera los tiempos de una Campaign `Recurring`; cada disparo crea un **nuevo CampaignRun**. | Scheduler | Anti-término: resetear `SentAt`/`ExecutionCount` sobre una fila. |
| **Lease** | Reserva atómica de exclusividad para procesar un disparo (optimistic-lock), garantiza **un solo ejecutor** al escalar. | Scheduler | Reemplaza doble-scheduler + `Status=Sending` no-atómico. |
| **Template** | Plantilla de contenido **por referencia** (clave + variables), renderizada por Scribe. | Scribe (render) / Campaigns (ref) | — |
| **Channel** | El medio: `Email` \| `Sms` \| `WhatsApp` \| `Push` \| `InApp`. Cada canal tiene un **consumer** en un servicio (existente o nuevo). Contrato dispatch/result **común**. **El precio NO vive aquí.** | Campaigns (enum) / consumer del canal (entrega) | Anti-término: `ChannelConfiguration: Dictionary<string,string>` sin esquema. |
| **Details / Reporting** | Stats agregadas por canal (Requested/Delivered/Failed/Skipped), estado por destinatario (drill-down) e historial de runs. | Campaigns | Nuevo (arista de detalles). |

## Entrega (contexto ejecutores/consumers de canal)

| Término | Definición | Owner | Notas |
|---|---|---|---|
| **Dispatch** | El **pedido** de enviar UN mensaje a UN Recipient: evento idempotente `campaign.dispatch.requested.v1` con `dispatch_id`, de Campaigns → consumer del canal. Lleva ids opacos que el consumer **devuelve intactos**. | Campaigns emite / consumer procesa | Generaliza el seam `CampaignId` de `PostmasterEmailEvents.cs:37`. |
| **Delivery** | El **hecho** de que el proveedor aceptó/entregó (o falló). Lo reporta el consumer vía `campaign.dispatch.result.v1`. **Dispatch ≠ Delivery.** | Consumer del canal | Legado confundía ambos: marcaba `Sent` al encolar (`CampaignSendService.cs:66`). |
| **Result** | Evento de vuelta del consumer: `Delivered` \| `Failed` \| `Skipped`, con el `dispatch_id` de correlación. Alimenta los contadores/stats. *(Suppressed/Bounced y tracking open/click = diferidos post-MVP.)* | Consumer emite / Campaigns consume | Espeja `Postmaster*IntegrationEvent` (`PostmasterEmailEvents.cs:104`). |
| **Attempt** | Nº de intento de un Recipient. Un retry es un **nuevo attempt** (nuevo `dispatch_id`). | Campaigns | Fix del doble-conteo en reintento. |

## Dinero (contexto Wallet + PEP) — **EXTERNO y DIFERIDO, no es de Campaign**

> Estos términos existen para cuando entre el dinero. **Ninguno vive en Campaign.** El PEP los aplica interceptando el trigger/RunDue; el Wallet los asienta.

| Término | Definición | Owner |
|---|---|---|
| **PEP (interceptor de autorización de ejecución)** | Punto de verificación **externo** delante de la ejecución (trigger manual y cada `RunDue`): calcula `recipientCount` (+recurrencia), consulta el Wallet, **cobra/reserva antes** y **veta** si no alcanza. Campaign solo ve triggers ya autorizados. | (nuevo, diferido) |
| **Balance** | Saldo **real prepago en USD** (minor units `long`) por tenant, **derivado de movimientos inmutables**. | Wallet (diferido) |
| **LedgerMovement** | Asiento **inmutable**: `TopUp` \| `Reservation` \| `Consume` \| `Refund` \| `Adjustment`. Solo Wallet crea asientos. | Wallet (diferido) |
| **Reservation / Consume / Refund** | Apartar antes de ejecutar / gastar lo entregado / devolver lo no consumido. Los orquesta el **PEP/Wallet**, **no Campaign**. | Wallet (diferido) |
| **TopUp** | Recarga: PaymentApp cobra (nuevo `SaaSPaymentType`) → Wallet asienta `TopUp`. | PaymentApp/Wallet (diferido) |
| **Price (per message/channel)** | Costo por mensaje de un canal. Lo define el Wallet/PEP, **nunca Campaign ni el frontend**. | Wallet/PEP (diferido) |

## Gate ortogonal (lo que SÍ verifica Campaign hoy)

| Término | Definición | Owner |
|---|---|---|
| **RBAC `campaigns.manage`** | Permiso "este usuario puede gestionar Campañas" (ya en `PermissionCatalog.cs:39`). | Auth (proyección local) |
| **Entitlement `module.campaigns`** | "Este tenant **puede usar** Campañas" (sembrado Pro/Enterprise, `SubscriptionPlanCatalogSeeder.cs:59,83`). | Subscription |

**No hay una tercera verificación de balance dentro de Campaign** (eso es el PEP externo).

## Términos PROHIBIDOS / ambiguos

| No usar | Por qué | Usar en su lugar |
|---|---|---|
| **Cualquier campo de dinero dentro de Campaign** (costo, saldo, reserva, precio) | Campaign no maneja dinero (D1). | Delegado al **PEP + Wallet externos** |
| **"Wallet TaxCoin / TXC / puntos"** | Moneda virtual del legado. El Wallet futuro es **USD real**. | **Balance (USD)** (en el Wallet, diferido) |
| **"Enviado (Sent)" como sinónimo de entregado** | Legado marcaba `Sent` al encolar. | **Dispatched** (pedido) vs **Delivered** (confirmado) |
| **"ContactList" como fuente de verdad de audiencia** | Snapshot stale. | **Audience** (criterio) + **Recipients del run** (resultado inmutable) |
| **"la campaña" para una ejecución** | Ambiguo. | **Campaign** (plantilla) vs **CampaignRun** (ejecución) |
| **"ChannelConfiguration genérico"** | Dictionary sin esquema. | **Contrato de canal tipado/versionado** |
| **"BackgroundAuthToken / token guardado"** | JWT persistido en texto plano. | **M2M client-credentials** por request |
| **"Postmaster para campañas"** | Exclusivo de la app principal. | **Email como consumer en Notification** |
| **"Ejecutor dedicado nuevo por canal"** | Salvo WhatsApp, se reusan servicios existentes. | **Consumer en Notification / TaxVision.Sms** |
