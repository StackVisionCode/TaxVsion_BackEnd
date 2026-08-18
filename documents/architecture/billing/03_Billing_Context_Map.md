# Billing — Mapa de contexto

Fecha: 2026-07-22

Muestra cómo Billing se relaciona con el resto de la plataforma y con qué contrato (síncrono M2M, evento de integración, o referencia opaca).

## Diagrama de relaciones

```
                         (humano, JWT + billing.view/manage vía Gateway /billing/*)
                                   │
                                   ▼
   ┌─────────────────────────────────────────────────────────────────────────┐
   │                            TaxVision.Billing                             │
   │   Invoice (root) · PaymentReceipt (root) · TenantBillingSettings ·       │
   │   InvoiceNumberSequence                                                  │
   └───┬───────────────┬───────────────┬───────────────┬─────────────────────┘
       │ M2M           │ M2M           │ M2M           │ eventos (consume)
       │ crear cobro   │ render PDF    │ guardar PDF   │ payments.payment_*
       ▼               ▼               ▼               │
  PaymentClient      Scribe        CloudStorage        │
  (tenant→taxpayer)  (render)      (FileType.Invoice)  │
       │                                               │
       │ publica payments.payment_succeeded/failed/────┘
       │ refunded/cancelled  (PaymentClient / PaymentApp)
       ▼
  RabbitMQ exchange `taxvision-events` (fan-out) ──► cola `billing-events` (inbox durable)

  Billing publica ► billing.invoice.issued / .sent / .paid / .voided ,
                    billing.receipt.issued
       └──► consumidos por Notification (email al cliente) y cualquier lector futuro
```

## Contratos por relación

| Contraparte | Dirección | Mecanismo | Contrato | Nota |
|---|---|---|---|---|
| **Frontend / usuario tenant** | entra | HTTP vía Gateway `/billing/*` | JWT `taxvision-billing`… no: JWT genérico `TaxVision.Services` validado por Gateway; permiso `billing.view`/`billing.manage` en el servicio | CRUD de invoices, enviar, marcar pagada, config PDF, ver recibos |
| **PaymentClient** | Billing → | M2M síncrono (`InvokeAsync` HTTP a `internal/…` de PaymentClient) | crear PaymentLink / Charge con `PaymentPurpose(InvoicePayment, ExternalReferenceId=InvoiceId)` | Billing obtiene la URL de pago y guarda `(PaymentSource,PaymentId)↔InvoiceId` |
| **PaymentClient / PaymentApp** | → Billing | evento de integración (cola `billing-events`) | `payments.payment_succeeded/failed/refunded/cancelled` | Billing correlaciona por su mapeo local (BDR-001) y ejecuta `RecordPayment`/`MarkPaymentFailed`/`RegisterRefund` |
| **Scribe** | Billing → | M2M síncrono | render del PDF de la invoice/recibo a partir de un template + datos | fallback si no hay template (fase 2) |
| **CloudStorage** | Billing → | M2M síncrono | guardar el PDF renderizado bajo `FolderType.Invoices`, `FileType.Invoice`; Billing guarda solo el `FileId` | descarga posterior por `FileId` |
| **Notification** | Billing → | evento de integración | `billing.invoice.sent` / `billing.receipt.issued` disparan el email al cliente | Billing nunca envía email directo (a diferencia del legado) |
| **Customer** | Billing → (opcional) | M2M o snapshot | al crear la invoice, se congela un `CustomerSnapshot` (id real + datos) | sin FK cross-service; Customer sigue siendo dueño de la identidad |
| **Catalog (futuro)** | Billing → | M2M (gancho) | `ResolvePrice` → snapshot de precio por ítem | no existe aún; MVP acepta montos del caller |
| **Subscription** | independiente | — | ninguno directo | Subscription factura el SaaS vía PaymentApp; no cruza con Billing tenant→taxpayer |

## Principio de contexto

- Billing es **dueño único** del documento factura y del recibo (numeración, estado, totales congelados, PDF, hash de verificación).
- PaymentClient es **dueño único** del cobro (dinero, provider, reintentos). Billing nunca ejecuta un cobro por su cuenta; delega y correlaciona.
- Presentación (PDF) y notificación (email) son **capacidades ajenas** (Scribe/CloudStorage/Notification); Billing orquesta, no reimplementa.
- Nada de FKs cross-service: todo enlace externo es por **referencia opaca** (`InvoiceId`, `FileId`, `PaymentId`).
