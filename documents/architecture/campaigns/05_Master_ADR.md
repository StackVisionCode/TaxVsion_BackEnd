# ADR-CAMP-000 — Descomposición de la capacidad Campañas multicanal

Estado: **APPROVED** (decisiones de usuario 2026-07-28)
Fecha: 2026-07-28

## ID y contexto

**ID:** CAMP-000. TaxVision necesita campañas multicanal (email, SMS, WhatsApp, push, in-app) donde el tenant define y agenda campañas y cada envío consume saldo real. El CRM legado lo resolvió con un **monolito** (`CRMTAXPROBACKEND/CampaignService`) que definía, agendaba, entregaba, integraba proveedores, y cobraba de un wallet TXC alojado en otro servicio — con múltiples anti-patrones. Se rediseña como varios bounded contexts/microservicios con separación estricta **creador vs ejecutor** y un **Wallet/Ledger** independiente.

## Evidencia real (VERIFIED contra código)

| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Backend Campaigns no existe | `Glob src/Services/{Campaign,Campaigns}*` → 0 | VERIFIED | 99% |
| Balance/Wallet/Ledger no existe | búsqueda repo + Growth docs difieren "campaign monetary balances" a un future Ledger (`growth/24`,`growth/05`,`growth/03`) | VERIFIED | 98% |
| Email nuevo NO reusa Postmaster | decisión de usuario: Postmaster es exclusivo de la app principal | DECISION | 100% |
| Push reusable | `Notification/.../Push/FcmPushSender.cs` (flag `Notification:UseFcmPush`) | VERIFIED | 95% |
| In-app reusable | `Communication` (Node, Socket.IO) | VERIFIED | 95% |
| SMS/WhatsApp inexistentes | Notification SMS = `LoggingSmsSender` stub; WhatsApp = solo enum reservado en Signature | VERIFIED | 97% |
| Gate de plan ya existe | `module.campaigns` sembrado en tiers medio/alto (`SubscriptionPlanCatalogSeeder.cs:59,83`) | VERIFIED | 96% |
| Top-up owner | PaymentApp `SaaSPayment` (platform→tenant); agregar `SaaSPaymentType` | VERIFIED | 92% |
| Primitivas | `Money`, `IdempotencyKey`, `ProcessedBusinessMessage` (business-inbox, `Growth/.../Idempotency/ProcessedBusinessMessage.cs`) | VERIFIED | 97% |
| Legado: wallet TXC en ReferralService, cobro al crear, TOCTOU no-atómico | `CampaignService/.../CreateCampaignCommandHandler.cs:233-320`, `WalletServiceClient.cs` | VERIFIED | 95% |

## Decisiones (aprobadas por el usuario)

1. **Separación creador/ejecutor.** `Campaigns` define+orquesta; los ejecutores de canal entregan. Un contrato dispatch/result común.
2. **Ejecutores:** Email = **servicio nuevo `TaxVision.Campaigns.Email` con SMTP2GO** (NO Postmaster). SMS = **nuevo `TaxVision.Sms`**. WhatsApp = **nuevo `TaxVision.WhatsApp`**. Push = **reusar `Notification` (FCM)** + contrato bulk. In-app = **reusar `Communication`**. Render = reusar `Scribe` (Fluid).
3. **Balance = dinero real (USD cents)** en un **microservicio `TaxVision.Wallet` independiente**, reutilizable (Campaigns, envío SMS individual, futuros). **Solo Wallet muta saldo, por movimientos INMUTABLES** (recarga/reserva/consumo/devolución/ajuste tipo libro mayor); nadie más toca el saldo.
4. **Scheduler** como owner del disparo temporal (inmediato/agendado/recurrente) con **lease atómico** (fix del doble-scheduler y del `Status=Sending` no-atómico del legado). Se decide en `scheduler/ADR.md` si es servicio propio o módulo de Campaigns.
5. **Gate ortogonal:** `module.campaigns` (entitlement) = permiso de uso; **balance** = capacidad de envío. No se mezclan.
6. **Top-up** de saldo cobrado por PaymentApp (nuevo `SaaSPaymentType`), acreditado a Wallet por evento tras pago exitoso.

## Alternativas consideradas

1. Monolito estilo legado (definición+entrega+proveedor+cobro en un servicio) — **rechazada** (anti-patrones abajo).
2. 4 ejecutores nuevos dedicados sin reusar nada — rechazada (duplica push/in-app que ya funcionan).
3. Balance dentro de Campaigns — rechazada (el usuario lo quiere reutilizable por SMS/otros).
4. **Seleccionada:** Campaigns (creador) + Wallet/Ledger independiente + ejecutores por canal (nuevos Email-SMTP2GO/SMS/WhatsApp, reuso Push/In-app) + Scheduler con lease. 

## Anti-patrones del legado que este diseño DEBE corregir

(De `CRMTAXPROBACKEND/CampaignService`, ver `01_Executive_Summary.md` para file:line.)
1. **Monolito** definición+entrega+proveedor+SMS-opt-in+tracking+wallet en un servicio → separar contexts.
2. **Fan-out síncrono fire-and-forget** (`Task.Run` / poll loop, `Task.Delay` entre mensajes) que se pierde al reiniciar → **outbox + fan-out por evento, por destinatario, idempotente, con backpressure**.
3. **Sin idempotencia por destinatario**; marcar `Sent` a todos los no-fallidos aunque no se intentaran; contadores de tracking que doble-cuentan en reintento de webhook → **idempotency key por (campaign,recipient,attempt)** + `ProcessedBusinessMessage`.
4. **Pago no-atómico / TOCTOU** (check y debit en 2 HTTP calls, debit antes de `SaveChanges`) → **Wallet reserve→consume/refund** con movimientos inmutables + saga.
5. **Secretos y JWT de usuario en la BD en texto plano** (`SmtpProviderConfig.ApiKey`, `Campaign.BackgroundAuthToken`) → secretos cifrados; **nunca** persistir JWT; M2M client-credentials.
6. **Doble scheduler + `Status=Sending` no-atómico** → un scheduler con lease/optimistic-lock (no doble-envío al escalar).
7. **`ChannelConfiguration: Dictionary<string,string>` sin esquema** → contrato por canal tipado y versionado.
8. **Sin entidad de run** (campañas recurrentes mutan una fila) → **CampaignRun inmutable por ejecución** (auditoría, estado y costo por run).
9. **Multi-tenant por `.Where` manual sin query filter** → filtro global fail-closed + repos tenant-scoped.

## Consecuencias

- Más servicios y coordinación distribuida (saga balance+dispatch), a cambio de aislamiento, reutilización del saldo, y ejecución resiliente/idempotente.
- Wallet/Ledger nuevo debe existir antes de que Campaigns pueda ejecutar (dependencia dura — ver `07_MVP_Scope.md`).
- El costo por canal y la moneda (USD) se definen en Wallet/Campaigns, no en el frontend.
