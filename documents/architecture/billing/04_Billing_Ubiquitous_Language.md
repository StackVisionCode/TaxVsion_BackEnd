# Billing — Lenguaje ubicuo

Fecha: 2026-07-22

Términos canónicos del dominio de facturación tenant→taxpayer. La columna "Legado" mapea al nombre en `CRMTAXPROBACKEND` para trazabilidad.

| Término (nuevo) | Definición | Legado |
|---|---|---|
| **Invoice** | Documento comercial que un tenant emite a un cliente/taxpayer por bienes/servicios. Aggregate root, dueño de su ciclo de vida. | `InvoiceData` |
| **InvoiceNumber** | Identificador comercial legible y único por tenant, asignado por el servidor al emitir (`INV-{yyyyMMdd}-{seq}`). | `InvoiceData.Number` (client-supplied — se corrige) |
| **InvoiceStatus** | Estado del ciclo de vida: `Draft`, `Issued`, `Sent`, `PartiallyPaid`, `Paid`, `Voided`. Enum, no string. | `InvoiceData.Status` (`"Draft"/"sent"/"paid"/"canceled"`) |
| **InvoiceLineItem** | Línea de detalle: descripción, cantidad, precio unitario, tasa de impuesto, total de línea. Entidad interna del aggregate. | `Item` |
| **Discount** | Descuento a nivel de invoice: porcentaje (basis points) o monto fijo (cents), más el monto aplicado congelado. VO. | `Discount` (owned) |
| **CustomerSnapshot** | Copia congelada de la identidad del cliente al emitir (CustomerId real, nombre, email, teléfono, TaxId, dirección). VO. | `InvoiceData.Customer` (`CreateCustomerDto` embebido, con `TaxId` abusado para guardar el GUID — se corrige) |
| **IssuerSnapshot** | Copia congelada de la identidad del emisor (el tenant) al emitir: nombre, dirección, contacto, `LogoFileId`. VO. | `InvoiceCompanyInfo` (con logo en base64 — se cambia a `FileId`) |
| **Money** | Monto monetario en centavos + moneda ISO-4217. `Money(long AmountCents, string Currency)`. | `decimal` suelto (se corrige) |
| **BasisPoints** | Unidad de porcentaje entera (1% = 100 bps) para descuentos/impuestos porcentuales. | porcentaje en `decimal` |
| **PaymentReceipt** | Comprobante verificable de un pago aplicado a una invoice. Aggregate root propio, con hash SHA-256 auto-verificable. | `PaymentReceipt` |
| **ReceiptNumber** | Identificador del recibo, server-generado (`RCP-{yyyy}-{seq}`). | `PaymentReceipt.ReceiptNumber` |
| **VerificationHash** | SHA-256 que sella un recibo para verificación pública anti-manipulación. | `PaymentReceipt.VerificationHash` |
| **TenantBillingSettings** | Configuración por tenant: identidad de emisor por defecto, ajustes de PDF (template, tamaño, orientación, mostrar logo/pie), y política de numeración. Aggregate de soporte. | `InvoicePDFSettings` (DbSet `ConfigSetting`, doblaba como default por company) |
| **InvoiceNumberSequence** | Contador monótono por tenant (y opcionalmente por período) que garantiza numeración server-side sin colisión. Aggregate de soporte. | (no existía; el número venía del cliente) |
| **PaymentPurposeReference** | Enlace opaco entre una invoice y un cobro de PaymentClient: `PurposeKind=InvoicePayment`, `ExternalReferenceId=InvoiceId`. | correlación por `InvoiceId` en `PaymentToken` (IntelliPay) |
| **InvoicePaymentLink** | Registro interno del cobro asociado a una invoice: `(PaymentSource, PaymentId, Status, PayUrl?)`. Sirve para correlacionar los eventos `payments.*` (BDR-001). | metadata del `PaymentToken` en PaymentService |
| **Issue (emitir)** | Transición `Draft → Issued`: asigna número, congela snapshots y totales, vuelve el documento inmutable en su detalle. | (implícito; el legado ponía `sent` al enviar) |
| **Send (enviar)** | Transición `Issued → Sent`: dispara render de PDF + email al cliente (con o sin link de pago según método). | `GetByIdInvoiceSendHandler` (`Status="sent"`) |
| **RecordPayment (registrar pago)** | Aplica un pago a la invoice (`Sent/PartiallyPaid → Paid/PartiallyPaid`), genera `PaymentReceipt`. Disparado por evento `payments.payment_succeeded` o por marca manual. | `MarkInvoiceAsPaidHandler` + `UpdateInvoicePaidHandlers` |
| **Void (anular)** | Transición `Draft/Issued/Sent/PartiallyPaid → Voided`: anula la invoice; si hay cobro pendiente, cancela en PaymentClient. No aplica a `Paid` (eso es reembolso). | `CancelInvoiceHandler` (`"canceled"`) |
| **PaidWatermark** | Sello visual "Paid" sobre el PDF con fecha/monto/método/hash al quedar pagada. | `PaidWatermarkInfo` + `AddPaidWatermark` (QuestPDF) |
| **PaymentMethod** | Medio de pago aplicado: `Card`, `Cash`, `Check`, `BankTransfer`, `Other`, `Online` (link PaymentClient). | `InvoiceData.PaymentMethod` (string libre) |

## Reglas de nomenclatura

- Errores con prefijo dotted `Billing.Invoice.*`, `Billing.Receipt.*`, `Billing.Settings.*` (mapeados en `ErrorHttpMapping`).
- Eventos de integración con `EventType` dotted `billing.invoice.issued`, `billing.receipt.issued`, etc.
- Aggregates `sealed class`, VOs `sealed record`, enums serializados a string en BD (`HasConversion<string>()`).
