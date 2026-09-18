# Campaigns Suite — Overview e Índice de documentación

Fecha: 2026-07-28 · **Revisión de alcance: 2026-09-16**
Estado: **DISEÑO — no implementado** (greenfield salvo reuso explícito)
Estándar: espeja `documents/architecture/growth/` (tablas de evidencia VERIFIED/PARTIAL/DOCUMENTED_ONLY + %, ADRs con IDs, blockers, file:line).

> **Revisión 2026-09-16 (decisiones del usuario):**
> 1. **Wallet DIFERIDO — va, pero no ahora.** Esta fase es **Wallet-free** (sin estimación/reserva/consumo/costo); el diseño deja los ganchos para añadir el Wallet como capa posterior sin rehacer el modelo. Los docs de `wallet-ledger/` quedan como diseño **futuro**, fuera del alcance inmediato.
> 2. **Orquestador agnóstico de canal + todas las aristas.** Campaign gestiona **quién envía (remitente), los contactos, los clientes, los envíos inmediatos, el schedule/recurrentes y los detalles/reportes** — pero **no envía**.
> 3. **Reuso de servicios de canal existentes:** Email/Push → `Notification`; SMS → `TaxVision.Sms` (ya M2M-ready). WhatsApp nuevo. NO se crean servicios ejecutores dedicados (p. ej. no un servicio "Email SMTP2GO" aparte).
> 4. **Acceso ya cableado:** permiso `campaigns.manage`, módulo `campaigns` y entitlement `module.campaigns` (Pro/Enterprise) YA existen — no se re-agregan.

## Qué es esto

Diseño de la capacidad **Campañas multicanal** de TaxVision. **La Campaña es la CREADORA/orquestadora del envío, NO la ejecutora.** La ejecución (renderizar + entregar por un proveedor + reportar resultado) vive en **ejecutores de canal** (reusando `Notification`/`TaxVision.Sms`). El orquestador cubre **todas las aristas**: remitente (SenderProfile por canal), audiencia (clientes Customer + contactos/listas propias + manual), modo de envío (inmediato/agendado/recurrente) y detalles/reporting. **En esta fase NO consume balance/dinero** (Wallet diferido).

Este diseño reemplaza conceptualmente al monolítico `CampaignService` del CRM legado (`CRMTAXPROBACKEND/CampaignService`), corrigiendo sus anti-patrones (ver `05_Master_ADR.md §Anti-patrones`). Nada del legado se porta literal.

## Servicios / bounded contexts de la suite

| # | Componente | Rol | Estado base | Deployment |
|---|---|---|---|---|
| 1 | **Campaigns** | Orquestador: Campaign, CampaignRun (inmutable), Recipients, **Contactos/Listas**, **SenderProfiles**, audiencia, `SendMode`, **detalles/stats**. Orquesta todas las aristas; **no** entrega. | GREENFIELD | `TaxVision.Campaigns` |
| 2 | **Scheduler** | Disparo temporal: inmediato, agendado y recurrente; owner del reloj y del **lease atómico** de ejecución. | GREENFIELD | módulo de Campaigns (o servicio propio — ver ADR) |
| 3 | **Email (ejecutor)** | Consumer de `campaign.dispatch.requested.v1(channel=Email)` **dentro de `Notification`** → `SendEmailCommand` con seam `CampaignId` → publica result. REUSA el pipeline de correo existente. | REUSE + consumer | `Notification` existente |
| 4 | **SMS (ejecutor)** | Consumer `channel=Sms` **dentro de `TaxVision.Sms`** → `SendSmsBatchCommand` (ya acepta `ActorType.Service`) → publica result. | REUSE + consumer | `TaxVision.Sms` existente |
| 5 | **Push (ejecutor)** | Consumer `channel=Push` **en `Notification`** — requiere **contrato bulk** sobre `FcmPushSender` (hoy 1:1). In-app **REUSA `Communication`**. | REUSE + glue | `Notification` / `Communication` |
| 6 | **WhatsApp (ejecutor)** | Consumer `channel=WhatsApp` (nuevo, WhatsApp Business/Meta). Fase posterior. | GREENFIELD | `TaxVision.WhatsApp` (nuevo) |
| — | **Wallet/Ledger** | Saldo real prepago + movimientos inmutables. **DIFERIDO (va, pero no ahora)** — diseño futuro en `wallet-ledger/`, fuera del alcance de esta fase. | DIFERIDO | `TaxVision.Wallet` (futuro) |

## Principio de separación (creador vs ejecutor)

```
Campaigns (define + orquesta todas las etapas)
  └─ SendNow (inmediato) / Scheduler dispara el run
       └─ Campaigns resuelve audiencia (Clients + Listas + Manual, aplica opt-out)
            └─ fan-out por destinatario/canal (evento dispatch, idempotente por DispatchId)
                 └─ Ejecutor de canal (Notification=Email/Push · TaxVision.Sms=SMS · WhatsApp)
                      resuelve el remitente (SenderRef) + renderiza (Scribe) + entrega + reporta result
                           └─ Campaigns agrega el result → contadores + detalles por destinatario
   (Wallet: DIFERIDO — cuando entre, se inserta RESERVE antes del fan-out y CONSUME/REFUND al cierre)
```

Contrato transversal **dispatch/result** común a todos los canales (ver `06_Cross_Service_Transactional_Protocol.md`). El seam `CampaignId` ya existe en el sistema nuevo (Notification↔Postmaster) y se generaliza.

## Reglas duras (heredadas de CLAUDE.md + este diseño)

- **(Diferido, cuando entre el Wallet)** Dinero = minor units (`long`) + ISO currency; nunca `float` ni montos confiados por el frontend; balance en USD real; **solo Wallet/Ledger muta saldo** por movimientos inmutables. En esta fase NO aplica (Wallet-free).
- At-least-once + handlers idempotentes + unique constraints + state guards + outbox/inbox Wolverine. Nunca "exactly-once". Dedupe de negocio vía `ProcessedBusinessMessage`.
- Idempotencia por `(campaign, recipient, attempt)` en dispatch y por `(operation, scopeId, key)` en Wallet.
- Multi-tenant fail-closed: query filter global + repos tenant-scoped + `.IgnoreQueryFilters()`+tenant explícito en scope Wolverine (ver `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`).
- Todo endpoint público con `[RateLimit]`/`[RateLimitExempt]` (ver guía RateLimit). M2M con audience/scope propios.
- El gate `module.campaigns` (entitlement de Subscription, ya sembrado) = "¿puede usar Campañas?"; el **balance** = "¿cuánto puede enviar?". Son ortogonales.
- Secretos de proveedor (SMTP2GO/SMS/WhatsApp) cifrados; **nunca** JWT de usuario persistido (anti-patrón del legado).

## Índice de la suite

**Fundación transversal (este folder, nivel raíz):**
- `00_Overview_And_Index.md` (este) · `01_Executive_Summary.md` · `02_Context_Map.md` · `03_Ubiquitous_Language.md` · `04_Ownership_Matrix.md` · `05_Master_ADR.md` (ADR-CAMP-000 + ADR-CAMP-001) · `06_Cross_Service_Transactional_Protocol.md` · `07_MVP_Scope.md` · `08_Implementation_Plan.md` · `09_Open_Questions.md` · `10_Frontend_MVP.md` (UI mínima Email/SMS)
- **Referencia viva:** `ARCHITECTURE_DIAGRAM.md` (diagrama de bloques + secuencia) · `REVIEW_REMEDIATION.md` (índice de correcciones del review externo + escenarios de aceptación) · `VERIFICATION_REPORT.md` (citas del sistema nuevo verificadas contra el repo; legado no-en-workspace).

**Por servicio (subfolder por microservicio), cada uno con el set estándar:** `Domain_Design.md`, `State_Machines.md`, `API_Contracts.md`, `Commands_And_Events.md`, `Data_Model.md`, `Transactional_Protocol.md`, `Idempotency_Spec.md`, `Concurrency_Spec.md`, `Observability.md`, `Security.md`, `Deployment.md`, `ADR.md`:
- **Alcance de esta fase:** `campaigns/` (orquestador + contactos/listas + remitentes + detalles) · `scheduler/` · integración por canal en `email-smtp2go/` (léase: **consumer en Notification**), `sms/` (**consumer en TaxVision.Sms**), `push/` (bulk sobre Notification), `whatsapp/` (nuevo, fase posterior).
- **Diferido (futuro):** `wallet-ledger/` — el diseño queda documentado pero **fuera del alcance inmediato** (Wallet va, pero no ahora).

## Fuentes (evidencia)
- Legado: `CRMTAXPROBACKEND/CampaignService` (Campaign/Recipient/ContactList/Statistics/Tracking, senders SMTP2GO/Textmaxx/Push, wallet TXC en `ReferralService`) — referencia + anti-patrones.
- Nuevo: `src/Services/{Notification,Postmaster,Scribe,Communication}` (pipeline email/push/in-app), `src/Services/Subscription/.../Entitlements` (`module.campaigns`), `src/Services/PaymentApp/.../SaaSPayments` (top-up), `src/BuildingBlocks/...` (`Money`, `IdempotencyKey`, `ProcessedBusinessMessage`).
