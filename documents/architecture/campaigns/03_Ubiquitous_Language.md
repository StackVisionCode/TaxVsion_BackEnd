# Campaigns Suite — Lenguaje Ubicuo (glosario)

Fecha: 2026-07-28. Términos vinculantes de la suite. Cada término dice **qué es**, **quién lo posee** (ver `04_Ownership_Matrix.md`) y, cuando aplica, el **anti-término** del legado que reemplaza. Los términos se refieren entre contexts por **IDs opacos**, nunca por FK.

## Definición y orquestación (contexto Campaigns)

| Término | Definición | Owner | Notas / anti-término |
|---|---|---|---|
| **Campaign** | Plantilla de intención de envío: canal, template-ref, criterio de audiencia, schedule. **Definición mutable mientras `Draft`**; no ejecuta ni cobra. | Campaigns | Legado mezclaba definición+entrega+cobro en una fila (`Campaign.cs`, `BackgroundAuthToken`, `WalletTransactionRef`). |
| **CampaignRun** | **Registro inmutable de UNA ejecución** de una Campaign: audiencia resuelta, costo estimado/consumido, reserva Wallet asociada, timestamps, resultado agregado. Una Campaign recurrente produce **N runs**. | Campaigns | Reemplaza al legado que **mutaba la misma fila** en cada recurrencia (`CampaignSchedulerBackgroundService.cs:115-142`). Sin run no hay auditoría por ejecución. |
| **Recipient** | Un destinatario **dentro de un CampaignRun** (no de la Campaign): dirección/handle resuelto + estado de dispatch/delivery + attempt. Clave de idempotencia `(campaignRunId, recipientRef, attempt)`. | Campaigns | Legado: `CampaignRecipient` por Campaign, sin attempt, marcado `Sent` en masa (`CampaignSendService.cs:55-69`). |
| **Audience / Segment** | **Criterio** de a quién enviar (segmento/lista/manual), resuelto **por referencia contra Customer en el momento del run** — no un snapshot copiado. La materialización congelada vive en el `CampaignRun` (los `Recipients`), no en la Campaign. | Campaigns (criterio) / Customer (datos) | Anti-término: **ContactList copiada como snapshot stale**. La audiencia es un query, la lista de Recipients del run es su resultado inmutable. |
| **Schedule** | Cuándo se dispara: `Immediate` \| `Scheduled(at)` \| `Recurring(rule)`. | Scheduler | — |
| **Recurrence** | Regla que genera los tiempos de una Campaign `Recurring`; cada disparo crea un **nuevo CampaignRun**. | Scheduler | Anti-término: incrementar `ExecutionCount` y resetear `SentAt` sobre una fila (`CampaignSchedulerBackgroundService.cs:115-126`). |
| **Lease** | Reserva atómica de exclusividad para procesar un run agendado (optimistic-lock/lease), garantiza **un solo ejecutor** al escalar. | Scheduler | Reemplaza doble-scheduler + `Status=Sending` no-atómico. |
| **Template** | Plantilla de contenido **por referencia** (clave + variables), renderizada por Scribe. La Campaign guarda la **referencia**, no HTML congelado. | Scribe (render) / Campaigns (ref) | — |
| **Channel** | El medio: `Email` \| `Sms` \| `WhatsApp` \| `Push` \| `InApp`. Cada canal tiene un **ejecutor** y un **precio por mensaje**. Contrato dispatch/result **común** a todos. | Campaigns (enum) / ejecutor (entrega) | Anti-término: `ChannelConfiguration: Dictionary<string,string>` sin esquema. |

## Entrega (contexto ejecutores de canal)

| Término | Definición | Owner | Notas |
|---|---|---|---|
| **Dispatch** | El **pedido** de enviar UN mensaje a UN Recipient: evento idempotente `(campaignRun, recipient, attempt)` de Campaigns → ejecutor. Lleva `CampaignId`/`CampaignRunId` opacos que el ejecutor **devuelve intactos**. | Campaigns emite / ejecutor consume | Generaliza el seam `CampaignId` de `PostmasterEmailEvents.cs:37`. |
| **Delivery** | El **hecho** de que el proveedor aceptó/entregó el mensaje (o falló/bounce/suppressed). Lo reporta el ejecutor vía evento result. **Dispatch ≠ Delivery**: se despacha una vez, la entrega se confirma después. | Ejecutor de canal | Legado confundía ambos: marcaba `Sent` al encolar, no al entregar (`CampaignSendService.cs:66`). |
| **Result** | Evento de vuelta del ejecutor: `Delivered` \| `Failed` \| `Bounced` \| `Suppressed` \| `ProviderNotConfigured`, con el `CampaignId` de correlación. Alimenta stats y la decisión consume/refund. | Ejecutor emite / Campaigns+Wallet consumen | Espeja `Postmaster*IntegrationEvent` (`PostmasterEmailEvents.cs:90-172`). |
| **Attempt** | Nº de intento de un Recipient. Un retry es un **nuevo attempt**, no re-cuenta el anterior. Parte de la idempotency key. | Campaigns | Fix del doble-conteo de tracking en reintento. |

## Dinero (contexto Wallet/Ledger)

| Término | Definición | Owner | Notas |
|---|---|---|---|
| **Balance** | Saldo **real prepago en USD** (minor units `long`) por tenant. **Derivado de los movimientos**, nunca un campo mutable suelto. | Wallet | Anti-término: wallet TXC (moneda virtual) en ReferralService con saldo mutable. Balance = dinero real, no TaxCoin. |
| **LedgerMovement** | Asiento **inmutable** del libro mayor: `TopUp` \| `Reservation` \| `Consume` \| `Refund` \| `Adjustment`, con `(operation, scopeId, idempotencyKey)`. Nunca se edita ni borra; correcciones = nuevo movimiento. | Wallet | **Solo Wallet crea movimientos.** Campaigns/ejecutores jamás mutan saldo. |
| **Reservation** | Movimiento que **aparta** fondos para un CampaignRun (o envío individual) **antes** del fan-out. Baja el disponible sin gastarlo aún. Idempotente por `(reserve, campaignRunId, key)`. | Wallet | Reemplaza el **debit al crear** del legado (`CreateCampaignCommandHandler.cs:278`). |
| **Consume** | Convierte parte/toda una Reservation en **gasto definitivo** tras `Delivery` confirmada. | Wallet | Se consume **por entrega confirmada**, no por dispatch. |
| **Refund** | Devuelve al disponible la parte **reservada y no consumida** (no-entregados, bounce, suppressed, run cancelado). | Wallet | Reemplaza refund del legado vía JWT persistido (`CampaignSendService.cs:120-127`). |
| **TopUp** | Recarga de saldo: PaymentApp cobra (nuevo `SaaSPaymentType`) → evento payment-succeeded → Wallet crea movimiento `TopUp`. **Único ingreso de dinero real** al balance. | PaymentApp (cobro) / Wallet (crédito) | Idempotente por el id del pago. |
| **Adjustment** | Movimiento manual/administrativo (corrección, cortesía) con auditoría. | Wallet | — |
| **Price (per message/channel)** | Costo por mensaje de un canal, en USD minor units. Lo define Wallet/Campaigns, **nunca el frontend**. | Campaigns/Wallet | Legado en config plana (`appsettings.json:138-141`). Ver `09` OQ-3. |

## Gate ortogonal

| Término | Definición | Owner |
|---|---|---|
| **Entitlement `module.campaigns`** | Permiso booleano "este tenant **puede usar** Campañas" (sembrado en tiers Pro/Enterprise, `SubscriptionPlanCatalogSeeder.cs:59,83`). | Subscription |
| **Capacidad de envío** | "**Cuánto** puede enviar" = función del **Balance**. Ortogonal al entitlement: tener el módulo no da saldo; tener saldo sin el módulo no habilita Campañas. | Wallet + Subscription |

## Términos PROHIBIDOS / ambiguos

| No usar | Por qué | Usar en su lugar |
|---|---|---|
| **"Wallet TaxCoin / TXC / puntos"** | Es moneda virtual del legado (ReferralService). Este Wallet es **USD real**. | **Balance (USD)** / **LedgerMovement** |
| **"Debit / cobrar al crear"** | Implica el TOCTOU del legado (debit antes de entregar). | **Reserve** (antes) + **Consume** (tras delivery) |
| **"Enviado (Sent)" como sinónimo de entregado** | Legado marcaba `Sent` al encolar. | **Dispatched** (pedido) vs **Delivered** (confirmado) |
| **"ContactList" como fuente de verdad** | Snapshot stale. | **Audience/Segment** (criterio) + **Recipients del CampaignRun** (resultado inmutable) |
| **"la campaña" para referirse a una ejecución** | Ambiguo entre plantilla y corrida. | **Campaign** (plantilla) vs **CampaignRun** (ejecución) |
| **"ChannelConfiguration genérico"** | Dictionary sin esquema. | **Contrato de canal tipado/versionado** |
| **"BackgroundAuthToken / token guardado"** | JWT persistido en texto plano. | **M2M client-credentials** por request |
| **"Postmaster para campañas"** | Postmaster es exclusivo de la app principal. | **Email SMTP2GO** (`TaxVision.Campaigns.Email`) |
