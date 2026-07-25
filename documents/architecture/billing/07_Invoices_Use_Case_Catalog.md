# Invoices — Catálogo de casos de uso

Fecha: 2026-07-22

Cada slice vertical de `TaxVision.Billing.Application` es `<Verbo><Sustantivo>/` con `Command`+`Handler`+`Result`. Handlers `static` con dependencias inyectadas por método (convención Wolverine). Los que mutan se envuelven en `IBillingIdempotencyExecutor.ExecuteAsync(...)`.

| # | Caso de uso | Comando | Actor / disparo | Efecto | Estado resultante | Eventos |
|---|---|---|---|---|---|---|
| UC-01 | Crear borrador de factura | `CreateInvoiceDraftCommand` | Tenant (HTTP) | Crea `Invoice` en `Draft` con líneas/descuento/totales provisionales | `Draft` | — |
| UC-02 | Editar borrador | `UpdateInvoiceDraftCommand` | Tenant (HTTP) | Recompone líneas/descuento/totales | `Draft` | — |
| UC-03 | Emitir factura | `IssueInvoiceCommand` | Tenant (HTTP) | Asigna `InvoiceNumber` (sequence), congela snapshots/totales | `Draft → Issued` | `billing.invoice.issued` |
| UC-04 | Enviar factura | `SendInvoiceCommand` | Tenant (HTTP) | Render PDF (Scribe→CloudStorage); si método=Online, crea PaymentLink en PaymentClient (`PurposeKind=InvoicePayment`, `ExternalReferenceId=InvoiceId`) y guarda `InvoicePaymentLink`; publica evento para que Notification mande el email | `Issued → Sent` | `billing.invoice.sent` (+ `billing.invoice.payment_link_created` opcional) |
| UC-05 | Marcar pagada (manual) | `RecordManualPaymentCommand` | Tenant (HTTP) | Aplica pago (Cash/Check/…); genera `PaymentReceipt`; render PDF watermark "Paid" | `Sent/PartiallyPaid → Paid/PartiallyPaid` | `billing.invoice.paid` / `billing.invoice.partially_paid`, `billing.receipt.issued` |
| UC-06 | Registrar pago online (evento) | `RecordOnlinePaymentCommand` (interno) | Consumer de `payments.payment_succeeded` | Correlaciona por `InvoicePaymentLink`; aplica pago; genera recibo | `Sent/PartiallyPaid → Paid/PartiallyPaid` | `billing.invoice.paid`, `billing.receipt.issued` |
| UC-07 | Registrar fallo de pago (evento) | `RegisterPaymentFailureCommand` (interno) | Consumer de `payments.payment_failed` | Actualiza `InvoicePaymentLink`; no cambia estado del documento | `Sent` (sin cambio) | `billing.invoice.payment_failed` |
| UC-08 | Registrar reembolso (evento) | `RegisterRefundCommand` (interno) | Consumer de `payments.payment_refunded` | Reduce `AmountPaid`; marca recibo `Refunded` | `Paid → PartiallyPaid` (o `Refunded` lógico) | `billing.invoice.refunded` |
| UC-09 | Anular factura | `VoidInvoiceCommand` | Tenant (HTTP) | Anula; si hay link de pago pendiente, cancela en PaymentClient (M2M) | `Draft/Issued/Sent/PartiallyPaid → Voided` | `billing.invoice.voided` |
| UC-10 | Eliminar borrador | `DeleteInvoiceDraftCommand` | Tenant (HTTP) | Soft-delete (solo `Draft`) | `Draft` (DeletedAtUtc) | — |
| UC-11 | Ver factura | `GetInvoiceByIdQuery` | Tenant (HTTP) | Detalle completo | — | — |
| UC-12 | Listar facturas del tenant | `ListInvoicesQuery` | Tenant (HTTP) | Página real (Skip/Take) + filtros (estado, cliente, texto, rango fecha) | — | — |
| UC-13 | Ver facturas de un cliente | `ListInvoicesByCustomerQuery` | Tenant (HTTP) | Por `CustomerSnapshot.CustomerId` | — | — |
| UC-14 | Descargar PDF de factura | `GetInvoicePdfQuery` | Tenant (HTTP) | Descarga por `PdfFileId` desde CloudStorage | — | — |
| UC-15 | Reenviar factura | `ResendInvoiceCommand` | Tenant (HTTP) | Re-dispara email (Notification) al destinatario | `Sent` (sin cambio) | `billing.invoice.sent` (reenvío) |
| UC-16 | Ver recibos de una factura | `ListReceiptsByInvoiceQuery` | Tenant (HTTP) | Recibos de la invoice | — | — |
| UC-17 | Ver recibos de un cliente | `ListReceiptsByCustomerQuery` | Tenant (HTTP) | Recibos por cliente | — | — |
| UC-18 | Verificar recibo por hash | `VerifyReceiptQuery` | Público (`[AllowAnonymous]`) | Recomputa `ValidateHash()`; flag de manipulación | — | — |
| UC-19 | Descargar PDF de recibo | `GetReceiptPdfQuery` | Tenant (HTTP) | Descarga por `PdfFileId` | — | — |
| UC-20 | Reenviar recibo | `ResendReceiptCommand` | Tenant (HTTP) | Re-publica evento de recibo (Notification) | — | `billing.receipt.issued` (reenvío) |
| UC-21 | Leer/actualizar config de billing | `GetBillingSettingsQuery` / `UpdateBillingSettingsCommand` | Tenant admin (HTTP) | CRUD de `TenantBillingSettings` (emisor default + PDF + numeración) | — | — |

## Notas de diseño por caso

- **UC-04 (Send)** es el reemplazo del `GetByIdInvoiceSendHandler` legado, que hacía dos cosas mezcladas (poner `sent` + emitir el evento de email/pago). Aquí se separa: transición de estado + orquestación (PDF, PaymentClink, evento de notificación). El branch legado IntelliPay-vs-directo se vuelve `PaymentMethod == Online` vs. resto.
- **UC-06/07/08** son consumers idempotentes (inbox durable + idempotencia por `PaymentReference`), reemplazan al `UpdateInvoicePaidHandlers` (que consumía `ServiceInvoiceUpdate`).
- **UC-18** es público como en el legado (`[AllowAnonymous]` en `verify`), único endpoint sin auth, y solo lee/valida hash.
- **UC-12** corrige el bug del legado (`GetAllInvoicesHandler` calculaba paginación pero no la aplicaba): aquí `Skip/Take` real.
- Todos los comandos de escritura reciben `Idempotency-Key` por header y `TenantId`/`ActorUserId` del JWT (no de query param — corrige el gap de seguridad legado).
