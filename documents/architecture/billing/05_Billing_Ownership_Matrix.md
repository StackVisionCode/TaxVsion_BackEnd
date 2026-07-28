# Billing — Matriz de ownership

Fecha: 2026-07-22

Quién es dueño (decide y persiste) de cada capacidad/dato, quién solo conserva snapshots o referencias, y con qué evidencia.

| Capacidad / dato | Owner | Consumidores (snapshot/ref) | Evidencia | Estado |
|---|---|---|---|---|
| Ciclo de vida de la factura (estado, transiciones) | **Billing** | Notification (email), Frontend (lectura) | `08_Billing_State_Machines.md` | NEW |
| Numeración de la factura (`InvoiceNumber`) | **Billing** (`InvoiceNumberSequence`) | — | corrige numeración client-side legada | NEW |
| Totales de la factura (subtotal, impuesto, descuento, total, saldo) | **Billing** (congela snapshot) | recibo, eventos | `10_Billing_Data_Model.md` | NEW |
| Identidad del cliente (maestro) | **Customer** | Billing conserva `CustomerSnapshot` congelado | `src/Services/Customer/` | EXISTING |
| Identidad del emisor / datos del tenant | **Tenant** (maestro) | Billing conserva `IssuerSnapshot`; default en `TenantBillingSettings` | `src/Services/Tenant/` | EXISTING |
| Precio/impuesto de los ítems vendidos | **Catalog (futuro)** | Billing congela `PriceSnapshot`; MVP acepta montos del caller | gancho `Catalog.ResolvePrice` | GAP (BDR-002) |
| Ejecución del cobro (dinero, provider, reintentos) | **PaymentClient** | Billing conserva `InvoicePaymentLink` (`PaymentSource,PaymentId,Status`) | `PaymentClient/…/PaymentPurpose.cs` | EXISTING |
| Hecho financiero (pago exitoso/fallido/reembolso/chargeback) | **PaymentClient / PaymentApp** | Billing consume `payments.*` y aplica a la invoice | `PaymentLifecycleIntegrationEvents.cs` | EXISTING |
| Recibo de pago (comprobante verificable) | **Billing** (`PaymentReceipt`) | Frontend (verificación pública por hash), Notification | `06_Invoices_Domain_Design.md` | NEW |
| Render del PDF (layout) | **Scribe** | Billing pide render, guarda `FileId` | `src/Services/Scribe/` | EXISTING |
| Almacenamiento del PDF | **CloudStorage** (`FileType.Invoice`, `FolderType.Invoices`) | Billing guarda solo `FileId` | enums CloudStorage | EXISTING |
| Envío de email al cliente | **Notification / Postmaster** | Billing publica evento; no envía directo | patrón `Notification` | EXISTING |
| Descuentos por cupón/referido (Codes/Referrals) | **Growth** | Billing NO integra en MVP (gancho futuro) | `documents/architecture/growth/` | OUT (MVP) |
| Facturación del SaaS (planes/seats/add-ons) | **Subscription + PaymentApp** | Billing NO participa | `src/Services/Subscription/` | OUT |

## Invariantes de ownership

1. **Un owner decide y persiste; los demás conservan snapshots o referencias opacas.** Billing congela `CustomerSnapshot`/`IssuerSnapshot`/`PriceSnapshot` al emitir — nunca hace FK cross-service ni relee al cliente/emisor después de emitir (la factura es un documento histórico).
2. **Billing no ejecuta cobros.** Delega a PaymentClient con `PaymentPurpose(InvoicePayment, InvoiceId)` y reacciona a los eventos `payments.*`. El dinero es verdad de PaymentClient.
3. **Billing no calcula impuestos en el MVP.** Acepta montos precalculados (paridad con el legado) y los congela. Cuando exista Catalog, el resolver de precio se vuelve la autoridad y Billing solo snapshotea. (BDR-002.)
4. **Presentación y notificación no viven en Billing.** PDF = Scribe+CloudStorage; email = Notification. Billing orquesta vía M2M/eventos.
5. **El recibo es inmutable salvo Void/Refund.** Su `VerificationHash` sella el contenido; anular/reembolsar cambia estado, no reescribe el hash original.
