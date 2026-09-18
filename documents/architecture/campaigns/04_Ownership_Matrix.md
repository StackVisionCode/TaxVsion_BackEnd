# Campaigns Suite — Matriz de Propiedad (Ownership)

> **REVISIÓN 2026-09-16/17 (ADR-CAMP-001, APPROVED).** Campaign **no posee ningún concepto de dinero**. Las filas de Balance/Movimientos/Precio/Top-up pertenecen al **Wallet + PEP externos y DIFERIDOS**. Los canales son **consumers dentro de servicios existentes** (salvo WhatsApp).

Fecha: 2026-07-28 (original) · revisado 2026-09-17. **Un solo owner por concepto.** Nadie lee/escribe el estado de otro contexto por FK: se coordina por **IDs opacos + eventos**. "Owner" = único que **muta** el dato; "Colaboradores" solo lo consumen por contrato.

## 1. Matriz maestra concepto → owner

| Concepto | Owner (fuente de verdad) | Colaboradores (consumen) | Frontera dura |
|---|---|---|---|
| **Entitlement `module.campaigns`** | **Subscription** | Campaigns (consulta gate) | `SubscriptionPlanCatalogSeeder.cs:59,83`. Gate de feature, **no** dinero. |
| **Permiso `campaigns.manage`** | **Auth** (proyección local en cada servicio) | Campaigns (`[HasPermission]`) | `PermissionCatalog.cs:39`, `PermissionModuleMap.cs:45`. Ya cableado. |
| **Campaign (definición)** | **Campaigns** | Scheduler, UI | Mutable solo en `Draft`. Sin dinero. |
| **CampaignRun (ejecución inmutable)** | **Campaigns** | reporting, (PEP externo lo observa) | Inmutable; una corrida = un run. **Sin costo/reserva.** |
| **Recipient / Attempt** | **Campaigns** | consumers de canal (vía dispatch) | Idempotencia `dispatch_id = f(runId,contactRef,channel,attempt)`. |
| **Contact / ContactList (no-clientes)** | **Campaigns** | — | Sub-dominio propio; importable; opt-out obligatorio. |
| **SenderProfile / SenderRef** | **Campaigns** (catálogo) | consumer del canal (lo resuelve) | Campaign referencia un id opaco; el secreto vive en el ejecutor. |
| **Audiencia (datos de clientes)** | **Customer** | Campaigns (resuelve por ref) | No copiar como snapshot stale. |
| **Criterio de audiencia** | **Campaigns** | Customer (lo ejecuta) | El criterio vive en Campaigns; los datos de clientes en Customer. |
| **Schedule / Recurrence / Lease** | **Scheduler** | Campaigns (recibe `RunDue`) | Lease atómico; un solo ejecutor. Sin dinero. |
| **Entrega Email** | **Notification** (consumer, reusa `SendEmailCommand`) | Campaigns (result) | **NO Postmaster.** Secreto SMTP en Notification. |
| **Entrega SMS** | **`TaxVision.Sms`** (consumer, ya M2M) | Campaigns (result) | Ya existe; `MessagesController.cs:21`. |
| **Entrega WhatsApp** | **`TaxVision.WhatsApp`** (NEW, fase posterior) | Campaigns (result) | Meta/WABA; consumer del contrato, sin dinero. |
| **Entrega Push** | **Notification (FcmPushSender)** (consumer + bulk) | Campaigns (result) | Contrato bulk nuevo; secretos FCM ya ahí. |
| **Entrega In-app** | **Communication** (REUSE) | Campaigns (result) | Socket.IO existente. |
| **Render de contenido (Fluid/Liquid)** | **Scribe** (REUSE) | consumers de canal | El consumer no re-renderiza si el cuerpo ya viaja. |
| **Assets (logos/adjuntos)** | **CloudStorage** (REUSE) | consumers (por referencia) | Nunca bytes por el bus. |
| **Secretos de proveedor** | **cada consumer/ejecutor** (cifrados) | — | Campaigns **no** tiene secretos. Nunca JWT de usuario. |
| **Contrato dispatch/result** | **BuildingBlocks/Messaging** | todos los canales (+ PEP/Wallet futuros) | Común a todos los canales; generaliza `PostmasterEmailEvents.cs`. |
| **— DINERO (todo lo de abajo es EXTERNO y DIFERIDO) —** | | | |
| **Autorización por saldo (PEP)** | **PEP externo** (NEW, DIFERIDO) | intercepta trigger/RunDue | Fuera de Campaign; calcula count+recurrencia, cobra/reserva antes, veta. |
| **Balance (saldo real USD)** | **Wallet/Ledger** (NEW, DIFERIDO) | PEP | **Solo Wallet muta saldo**, movimientos inmutables. |
| **LedgerMovement (reserve/consume/refund/topup/adjust)** | **Wallet/Ledger** (DIFERIDO) | — | Nadie más crea asientos. |
| **Precio por mensaje / canal** | **Wallet/PEP** (DIFERIDO) | UI (informativo) | **Nunca Campaign ni el frontend.** |
| **Cobro del top-up** | **PaymentApp** (nuevo `SaaSPaymentType`) (DIFERIDO) | Wallet (credit-on-paid) | Wallet acredita solo al recibir payment-succeeded. |

## 2. Quién decide "se envía o no" (regla de oro, revisada)

```
HOY (sin dinero):
  RBAC campaigns.manage  ─┐
                          ├─► Campaigns ejecuta (fan-out dispatch → consumers de canal)
  entitlement module.campaigns ┘

FUTURO (con dinero, EXTERNO):
  trigger / RunDue ──► [PEP externo] ──consulta──► [Wallet] ──► ¿saldo para N destinatarios?
                            │  sí → deja pasar el trigger ya autorizado ──► Campaigns ejecuta
                            └─ no → veta ANTES de ejecutar (el run ni arranca)
```

**Campaigns nunca escribe el saldo ni lo consulta.** El PEP y el Wallet viven fuera; Campaign solo recibe triggers autorizados. Esto elimina de raíz el TOCTOU del legado sin meter dinero en el orquestador.

## 3. Primitivas: copia-por-contexto (NO compartir tipos)

| Primitiva | Regla | Fuente de referencia |
|---|---|---|
| `ProcessedBusinessMessage` | Business-inbox para dedupe de **efecto de negocio** (además del inbox Wolverine). Campaign lo usa para escrituras de API, no para dinero. | `Growth/.../Idempotency/ProcessedBusinessMessage.cs:27-74` |
| `IdempotencyKey` | Copia por contexto. | patrón del monorepo |
| `Money` | **No existe en Campaign.** Solo en el Wallet (diferido), como copia propia. | (Wallet, futuro) |

## 4. Fronteras que NO se cruzan

- **Campaign no toca dinero** (ni Balance, ni Money, ni reserve/consume/refund, ni precio).
- **Sin FK entre contexts.** IDs opacos + eventos.
- **Postmaster** no se usa para campañas (exclusivo app principal).
- **Solo Wallet** (diferido) muta saldo; **solo los consumers/ejecutores** guardan secretos de proveedor; **solo Scheduler** posee el reloj/lease.
- **Customer** posee los datos de clientes; Campaigns posee el **criterio** y sus **contactos propios** (no-clientes).
