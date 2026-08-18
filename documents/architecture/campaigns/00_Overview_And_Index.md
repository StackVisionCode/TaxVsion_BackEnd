# Campaigns Suite — Overview e Índice de documentación

Fecha: 2026-07-28
Estado: **DISEÑO — no implementado** (greenfield salvo reuso explícito)
Estándar: espeja `documents/architecture/growth/` (tablas de evidencia VERIFIED/PARTIAL/DOCUMENTED_ONLY + %, ADRs con IDs, blockers, file:line).

## Qué es esto

Diseño de la capacidad **Campañas multicanal** de TaxVision. **La Campaña es la CREADORA/definidora del envío, NO la ejecutora.** La ejecución (renderizar + entregar por un proveedor + reportar resultado) vive en **ejecutores de canal independientes**. Ejecutar una campaña **consume balance real (USD)**, gobernado por un **Wallet/Ledger** independiente de movimientos inmutables.

Este diseño reemplaza conceptualmente al monolítico `CampaignService` del CRM legado (`CRMTAXPROBACKEND/CampaignService`), corrigiendo sus anti-patrones (ver `05_Master_ADR.md §Anti-patrones`). Nada del legado se porta literal.

## Servicios / bounded contexts de la suite

| # | Servicio | Rol | Estado base | Deployment |
|---|---|---|---|---|
| 1 | **Campaigns** | Creador/definidor: Campaign, CampaignRun (inmutable), Recipients, Audience, plantillas-ref, stats agregadas. Orquesta; **no** entrega. | GREENFIELD | `TaxVision.Campaigns` |
| 2 | **Wallet/Ledger** | Saldo real prepago por tenant; movimientos INMUTABLES (recarga/reserva/consumo/devolución/ajuste). Reutilizable (Campaigns, SMS individual, futuros). | GREENFIELD | `TaxVision.Wallet` (independiente) |
| 3 | **Scheduler** | Disparo temporal de campañas: inmediato, agendado y recurrente; owner del reloj y del lease de ejecución. | GREENFIELD | `TaxVision.Campaigns.Scheduler` (o módulo de Campaigns — ver ADR) |
| 4 | **Email (SMTP2GO)** | Ejecutor de canal EMAIL para campañas, vía **SMTP2GO**. NO es Postmaster (Postmaster es exclusivo de la app principal, no se reusa). | GREENFIELD | `TaxVision.Campaigns.Email` |
| 5 | **SMS** | Ejecutor de canal SMS (nuevo). También usable para envíos individuales (consume Wallet). | GREENFIELD | `TaxVision.Sms` |
| 6 | **WhatsApp** | Ejecutor de canal WhatsApp (nuevo, WhatsApp Business/Meta). | GREENFIELD | `TaxVision.WhatsApp` |
| 7 | **Push** | Ejecutor de canal PUSH — **REUSA `Notification` (FcmPushSender)**; se agrega el contrato bulk/campaña. In-app **REUSA `Communication`**. | REUSE + glue | `Notification` / `Communication` existentes |

## Principio de separación (creador vs ejecutor)

```
Campaigns (define + orquesta)
  └─ Scheduler dispara el run
       └─ Campaigns resuelve audiencia + estima costo
            └─ Wallet: RESERVE (dinero real, movimiento inmutable)
                 └─ fan-out por destinatario (evento dispatch, idempotente)
                      └─ Ejecutor de canal (Email SMTP2GO / SMS / WhatsApp / Push)
                           renderiza (Scribe) + entrega (proveedor) + reporta resultado
                                └─ Campaigns agrega resultado
                                     └─ Wallet: CONSUME entregados / REFUND no-entregados
```

Contrato transversal **dispatch/result** común a todos los canales (ver `06_Cross_Service_Transactional_Protocol.md`). El seam `CampaignId` ya existe en el sistema nuevo (Notification↔Postmaster) y se generaliza.

## Reglas duras (heredadas de CLAUDE.md + este diseño)

- Dinero = minor units (`long`) + ISO currency; nunca `float` ni montos confiados por el frontend. Balance en **USD real**.
- **Solo Wallet/Ledger muta saldo**, y solo por **movimientos inmutables** (nunca UPDATE de un saldo mutable suelto). Campaigns/SMS jamás tocan el saldo directo.
- At-least-once + handlers idempotentes + unique constraints + state guards + outbox/inbox Wolverine. Nunca "exactly-once". Dedupe de negocio vía `ProcessedBusinessMessage`.
- Idempotencia por `(campaign, recipient, attempt)` en dispatch y por `(operation, scopeId, key)` en Wallet.
- Multi-tenant fail-closed: query filter global + repos tenant-scoped + `.IgnoreQueryFilters()`+tenant explícito en scope Wolverine (ver `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`).
- Todo endpoint público con `[RateLimit]`/`[RateLimitExempt]` (ver guía RateLimit). M2M con audience/scope propios.
- El gate `module.campaigns` (entitlement de Subscription, ya sembrado) = "¿puede usar Campañas?"; el **balance** = "¿cuánto puede enviar?". Son ortogonales.
- Secretos de proveedor (SMTP2GO/SMS/WhatsApp) cifrados; **nunca** JWT de usuario persistido (anti-patrón del legado).

## Índice de la suite

**Fundación transversal (este folder, nivel raíz):**
- `00_Overview_And_Index.md` (este) · `01_Executive_Summary.md` · `02_Context_Map.md` · `03_Ubiquitous_Language.md` · `04_Ownership_Matrix.md` · `05_Master_ADR.md` (ADR-CAMP-000 decomposición) · `06_Cross_Service_Transactional_Protocol.md` (balance + dispatch saga) · `07_MVP_Scope.md` · `08_Implementation_Plan.md` · `09_Open_Questions.md`

**Por servicio (subfolder por microservicio), cada uno con el set estándar:** `Domain_Design.md`, `State_Machines.md`, `API_Contracts.md`, `Commands_And_Events.md`, `Data_Model.md`, `Transactional_Protocol.md`, `Idempotency_Spec.md`, `Concurrency_Spec.md`, `Observability.md`, `Security.md`, `Deployment.md`, `ADR.md`:
- `campaigns/` · `wallet-ledger/` · `scheduler/` · `email-smtp2go/` · `sms/` · `whatsapp/` · `push/` (push = integración/contrato sobre Notification/Communication, set reducido).

## Fuentes (evidencia)
- Legado: `CRMTAXPROBACKEND/CampaignService` (Campaign/Recipient/ContactList/Statistics/Tracking, senders SMTP2GO/Textmaxx/Push, wallet TXC en `ReferralService`) — referencia + anti-patrones.
- Nuevo: `src/Services/{Notification,Postmaster,Scribe,Communication}` (pipeline email/push/in-app), `src/Services/Subscription/.../Entitlements` (`module.campaigns`), `src/Services/PaymentApp/.../SaaSPayments` (top-up), `src/BuildingBlocks/...` (`Money`, `IdempotencyKey`, `ProcessedBusinessMessage`).
