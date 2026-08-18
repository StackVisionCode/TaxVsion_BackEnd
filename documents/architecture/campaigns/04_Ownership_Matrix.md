# Campaigns Suite — Matriz de Propiedad (Ownership)

Fecha: 2026-07-28. **Un solo owner por concepto.** Nadie lee/escribe el estado de otro contexto por FK: se coordina por **IDs opacos + eventos** (mismo patrón Growth Codes↔Referrals). "Owner" = único que **muta** el dato y es su fuente de verdad; "Colaboradores" solo lo consumen por contrato.

## 1. Matriz maestra concepto → owner

| Concepto | Owner (fuente de verdad) | Colaboradores (consumen) | Frontera dura |
|---|---|---|---|
| **Precio del plan / suscripción** | **Subscription** | Campaigns (gate), UI | El precio del plan NO es el precio por mensaje. |
| **Entitlement `module.campaigns`** | **Subscription** | Campaigns (consulta gate) | `SubscriptionPlanCatalogSeeder.cs:59,83`. Gate ≠ balance. |
| **Precio por mensaje / por canal** | **Campaigns/Wallet** | ejecutores (informativo), UI | **Nunca el frontend.** USD minor units. Ver `09` OQ-3. |
| **Balance (saldo real USD)** | **Wallet/Ledger** | Campaigns (consulta), ejecutores (SMS individual) | **Solo Wallet muta saldo**, por movimientos inmutables. |
| **LedgerMovement (reserve/consume/refund/topup/adjust)** | **Wallet/Ledger** | — | Nadie más crea asientos. Idempotente `(operation,scopeId,key)`. |
| **Cobro del top-up** | **PaymentApp** (`SaaSPayment` + nuevo `SaaSPaymentType`) | Wallet (credit-on-paid) | Wallet acredita **solo** al recibir payment-succeeded. |
| **Campaign (definición)** | **Campaigns** | Scheduler, UI | Mutable solo en `Draft`. |
| **CampaignRun (ejecución inmutable)** | **Campaigns** | Wallet (reserva asociada), stats | Inmutable; una corrida = un run. |
| **Recipient / Attempt** | **Campaigns** | ejecutores (via dispatch) | Idempotency `(run,recipient,attempt)`. |
| **Audiencia / Segmento (datos de contacto)** | **Customer** | Campaigns (resuelve criterio por ref) | No copiar como snapshot stale. |
| **Criterio de audiencia (query)** | **Campaigns** | Customer (lo ejecuta) | El criterio vive en Campaigns; los datos en Customer. |
| **Schedule / Recurrence / Lease** | **Scheduler** | Campaigns (recibe disparo) | Lease atómico; un solo ejecutor. |
| **Entrega Email** | **Email SMTP2GO** (`TaxVision.Campaigns.Email`) | Campaigns (result), Wallet (consume/refund) | **NO Postmaster.** Secreto SMTP2GO cifrado aquí. |
| **Entrega SMS** | **`TaxVision.Sms`** (NEW, fase 2) | Campaigns, Wallet | Proveedor por decidir (`09` OQ-1). |
| **Entrega WhatsApp** | **`TaxVision.WhatsApp`** (NEW, fase 2) | Campaigns, Wallet | Meta/WABA; costeo por decidir (`09` OQ-2). |
| **Entrega Push** | **Notification (FcmPushSender)** (REUSE) | Campaigns | Se agrega contrato **bulk**; secretos FCM ya viven ahí. |
| **Entrega In-app** | **Communication** (REUSE) | Campaigns | Socket.IO existente. |
| **Render de contenido (Fluid/Liquid)** | **Scribe** (REUSE) | ejecutores | Ejecutor no re-renderiza si el cuerpo ya viaja. |
| **Assets (logos/adjuntos)** | **CloudStorage** (REUSE) | ejecutores (por referencia) | Nunca bytes por el bus (`EmailInlineAssetReference`). |
| **Secretos de proveedor** | **cada ejecutor** (cifrados) | — | Campaigns **no** tiene secretos. Nunca JWT de usuario. |
| **Contrato dispatch/result** | **BuildingBlocks/Messaging** (tipos compartidos de mensajería) | todos los canales | Común a los 5 canales; generaliza `PostmasterEmailEvents.cs`. |

## 2. Quién muta el dinero (regla de oro)

```
PaymentApp  ──(payment succeeded)──►  Wallet.TopUp        (única entrada de USD real)
Campaigns   ──(reserve request)───►   Wallet.Reservation  (aparta, no gasta)
Ejecutor    ──(delivery result)──►    Campaigns ──►  Wallet.Consume | Wallet.Refund
```

**Campaigns y los ejecutores NUNCA escriben el saldo.** Solo **piden** movimientos a Wallet; Wallet decide y asienta. Esto elimina el TOCTOU del legado (`CreateCampaignCommandHandler.cs:250/278/320`, check+debit en 2 HTTP calls antes de `SaveChanges`).

## 3. Primitivas: copia-por-contexto (NO compartir tipos)

| Primitiva | Regla | Fuente de referencia |
|---|---|---|
| `Money(long AmountCents, string Currency)` | **Una copia por bounded context** (Wallet, Campaigns…). No compartir el tipo. | `PaymentApp.Domain/ValueObjects/Money.cs` (+ copias en Subscription/Billing/Codes/PaymentClient) |
| `IdempotencyKey` | Copia por contexto. | `PaymentApp.Domain/ValueObjects/IdempotencyKey.cs` |
| `ProcessedBusinessMessage` | Business-inbox para dedupe de **efecto de negocio** (además del inbox durable de Wolverine). | `Growth/.../Idempotency/ProcessedBusinessMessage.cs:27-74` |

## 4. Fronteras que NO se cruzan

- **Sin FK entre contexts.** IDs opacos + eventos.
- **Postmaster** no se usa para campañas (exclusivo app principal).
- **Subscription** posee precio de plan; **Wallet/Campaigns** posee precio por mensaje.
- **Solo Wallet** muta saldo; **solo ejecutores** guardan secretos de proveedor; **solo Scheduler** posee el reloj/lease.
- **Customer** posee los datos de contacto; Campaigns solo posee el **criterio**.
