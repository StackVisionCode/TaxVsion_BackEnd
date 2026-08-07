# Campaigns Suite — Resumen Ejecutivo

Fecha: 2026-07-28
Estado: **DISEÑO — no implementado** (greenfield salvo reuso explícito)
Lee primero: `00_Overview_And_Index.md`, `02_Context_Map.md`, `05_Master_ADR.md` (ADR-CAMP-000).

## 1. El problema en una frase

TaxVision necesita **campañas multicanal** (email, SMS, WhatsApp, push, in-app) donde el tenant **define y agenda** el envío y **cada mensaje consume saldo real (USD)**. El CRM legado (`CRMTAXPROBACKEND/CampaignService`) lo resolvió con un **monolito** que define, agenda, entrega, integra proveedores y cobra de un wallet TXC alojado en otro servicio, acumulando nueve anti-patrones (§4). Este diseño lo reemplaza conceptualmente con **bounded contexts separados** bajo el principio **creador vs ejecutor** + un **Wallet/Ledger independiente**.

## 2. La decisión (ADR-CAMP-000, APPROVED)

- **Campaigns (NEW)** = creador/definidor: `Campaign` + `CampaignRun` inmutable por ejecución + `Recipients` + audiencia (vía Customer, no snapshot stale) + schedule + stats agregadas. Orquesta; **no** entrega, **no** integra proveedores, **no** tiene secretos.
- **Wallet/Ledger (NEW, `TaxVision.Wallet` independiente)** = saldo real en USD (minor units `long`) por tenant, con **movimientos inmutables** (recarga/reserva/consumo/devolución/ajuste). **Solo Wallet muta saldo**; reutilizable por Campaigns, por SMS individual y futuros consumidores.
- **Ejecutores de canal:** Email = **`TaxVision.Campaigns.Email` (SMTP2GO, NO Postmaster)**; SMS = **`TaxVision.Sms`**; WhatsApp = **`TaxVision.WhatsApp`**; Push = **reusar `Notification` (FcmPushSender)** + contrato bulk; In-app = **reusar `Communication`**; render = **reusar `Scribe`**.
- **Scheduler (NEW)** = disparo temporal con **lease atómico** (fix del doble-scheduler + `Status=Sending` no-atómico del legado).
- **Gate ortogonal:** `module.campaigns` (entitlement de Subscription) = "¿puede usar Campañas?"; el **balance** = "¿cuánto puede enviar?".
- **Top-up** cobrado por PaymentApp (nuevo `SaaSPaymentType`), acreditado a Wallet por evento tras pago exitoso.

## 3. Tabla de evidencia

| # | Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|---|
| E1 | Backend Campaigns nuevo no existe | `Glob src/Services/{Campaign,Campaigns}*` → 0 | VERIFIED | 99% |
| E2 | Wallet/Ledger nuevo no existe | búsqueda repo; Growth difiere "monetary balances" a un Ledger futuro | VERIFIED | 98% |
| E3 | Gate de plan ya sembrado | `SubscriptionPlanCatalogSeeder.cs:59` (Pro), `:83` (Enterprise): `modules:[…,"campaigns",…]` | VERIFIED | 96% |
| E4 | Seam `CampaignId` ya fluye Notification→Postmaster sin interpretar | `PostmasterEmailEvents.cs:37` (request), `:104/:120/:137/:169` (result echoes) | VERIFIED | 97% |
| E5 | Push reusable (FCM) | `Notification/.../Push/FcmPushSender.cs` (flag `Notification:UseFcmPush`) | VERIFIED | 95% |
| E6 | In-app reusable | `Communication` (Node/Socket.IO) | VERIFIED | 95% |
| E7 | Primitiva idempotencia de negocio existe | `ProcessedBusinessMessage.cs:27-74` (`Begin(operation,scopeId,idempotencyKey,requestFingerprint)`) | VERIFIED | 97% |
| E8 | Primitivas `Money`/`IdempotencyKey` (una copia por contexto) | `PaymentApp.Domain/ValueObjects/Money.cs`, `.../IdempotencyKey.cs`, y copias en Subscription/Billing/Codes/PaymentClient | VERIFIED | 97% |
| E9 | Top-up owner = PaymentApp `SaaSPayment` | patrón platform→tenant existente; agregar `SaaSPaymentType` | PARTIAL | 90% |
| E10 | Legado: wallet TXC en ReferralService, cobro al CREAR, TOCTOU | `CreateCampaignCommandHandler.cs:250` (balance), `:278` (debit), `:320` (SaveChanges) — check+debit en 2 HTTP calls, debit antes de guardar | VERIFIED | 95% |
| E11 | Legado: JWT de usuario persistido en texto plano | `CreateCampaignCommandHandler.cs:67` (`BackgroundAuthToken = request.AuthorizationToken`), consumo en `CampaignSendService.cs:112,127`, migración `20260121053231_updatecapmain.cs:15` | VERIFIED | 97% |
| E12 | Legado: secreto de proveedor en texto plano | `CampaignService/appsettings.json:14` (`SMTP2GO:ApiKey`), `:104` (SendGrid) | VERIFIED | 96% |
| E13 | Legado: sin idempotencia por destinatario; marca `Sent` a todo no-fallido | `CampaignSendService.cs:55-69` (todo recipient sin failure → `Status=Sent`) | VERIFIED | 96% |
| E14 | Legado: scheduler = poll loop `Task.Delay`, flip de status no-atómico, recurrentes mutan una fila | `CampaignSchedulerBackgroundService.cs:38` (delay), `:54-59` (`.Where(Status==Scheduled)`), `:115-142` (`ScheduleNextRecurrence` muta la misma fila) | VERIFIED | 96% |
| E15 | Legado: precios por canal en config plana | `appsettings.json:138` Email `0.001`, `:139` Sms `0.05`, `:141` WhatsApp `0.01` (difieren de las notas de diseño 0.015/0.005 — ver `09`) | VERIFIED | 94% |
| E16 | Email nuevo NO reusa Postmaster (exclusivo app principal) | decisión de usuario 2026-07-28 | DECISION | 100% |
| E17 | `CampaignRun` inmutable / render Scribe / lease Scheduler | diseño nuevo, sin código aún | NEW | — |

## 4. Anti-patrones del legado que este diseño corrige

Resumen (detalle y decisión en `05_Master_ADR.md §Anti-patrones`):

1. **Monolito** (definición+entrega+proveedor+cobro juntos) → bounded contexts separados.
2. **Fan-out síncrono fire-and-forget** (`Task.Run`/poll + `Task.Delay`, se pierde al reiniciar) → outbox + fan-out por evento por destinatario, idempotente.
3. **Sin idempotencia por destinatario** (`CampaignSendService.cs:55-69`) → key `(campaign,recipient,attempt)` + `ProcessedBusinessMessage`.
4. **Pago no-atómico TOCTOU** (`CreateCampaignCommandHandler.cs:250/278/320`) → Wallet reserve→consume/refund con movimientos inmutables + saga.
5. **Secretos + JWT en BD texto plano** (`appsettings.json:14`, `:67 BackgroundAuthToken`) → secretos cifrados, **nunca** JWT persistido, M2M client-credentials.
6. **Doble scheduler + `Status=Sending` no-atómico** → un scheduler con lease/optimistic-lock.
7. **`ChannelConfiguration: Dictionary<string,string>` sin esquema** → contrato por canal tipado y versionado.
8. **Sin entidad de run** (recurrentes mutan una fila, `CampaignSchedulerBackgroundService.cs:124`) → `CampaignRun` inmutable por ejecución.
9. **Multi-tenant por `.Where` manual** → query filter global fail-closed + repos tenant-scoped.

## 5. Blockers explícitos

| ID | Blocker | Impacto | Desbloqueo |
|---|---|---|---|
| **BLK-1** | **Wallet/Ledger debe existir antes de que Campaigns pueda ejecutar** (dependencia dura). | Sin saldo real no hay reserve→consume; ejecutar sin él repetiría el TOCTOU del legado. | Fase 1 = Wallet primero (ver `08`). |
| **BLK-2** | `SaaSPaymentType` para top-up no existe aún en PaymentApp. | Sin él no hay recarga de saldo (no se puede probar el ciclo completo). | Agregar tipo + handler credit-on-paid (ver `08` Fase 2). |
| **BLK-3** | Precio por canal/moneda **no definido** con autoridad (config legado difiere de las notas de diseño, E15). | Bloquea la estimación de costo y el `reserve`. | Decisión de negocio (ver `09` OQ-3). |
| **BLK-4** | Proveedor SMS y costeo WhatsApp sin decidir. | Bloquea ejecutores SMS/WhatsApp (fase 2, fuera de MVP). | Ver `09` OQ-1/OQ-2. |
| **BLK-5** | Política de refund por **no-entregado** (bounce/suppressed/soft-fail) sin definir. | Determina qué se consume vs devuelve tras el resultado del ejecutor. | Ver `06` §Refund + `09` OQ-4. |
| **BLK-6** | Contrato bulk sobre Notification (push) no existe (hoy solo transaccional 1:1). | Push en MVP requiere el contrato bulk. | Ver `08` Fase 4. |

## 6. Alcance MVP (detalle en `07`)

**IN:** Wallet real USD + Campaigns (creador/orq.) + Email SMTP2GO + Scheduler (lease) + reuse Push (contrato bulk). **OUT/diferido:** WhatsApp (fase 2), SMS (fase 2), A/B testing, monedas virtuales/TaxCoin, segmentación avanzada. **Dependencia dura:** Wallet antes que cualquier ejecución (BLK-1).
