# Billing — Commands y eventos

Fecha: 2026-07-22

Catálogo completo de comandos, eventos de dominio y eventos de integración. Los contratos de integración viven en `src/BuildingBlocks/Messaging/BillingIntegrationEvents/` (patrón `GrowthIntegrationEvents`). Los que Billing consume ya existen en `PaymentIntegrationEvents/`.

## Taxonomía

- **Comando**: intención de mutar, procesada por un handler (`bus.InvokeAsync<Result<T>>`). No sale del servicio.
- **Evento de dominio** (`IDomainEvent`): hecho interno emitido por el aggregate, drenado por `BillingDbContext.SaveChanges` al outbox durable.
- **Evento de integración** (`IntegrationEvent`): hecho publicado al exchange `taxvision-events` para otros servicios.
- **Notificación interna**: no aplica (Billing no usa notificaciones MediatR).
- **Métrica**: contadores/histogramas en `BillingMetrics` (Observability).

## Envelope de integración

`BillingIntegrationEvent : IntegrationEvent` (base `src/BuildingBlocks/Messaging/IIntegrationEvent.cs`) añade, igual que `GrowthIntegrationEvent`:
`EventId, EventType, EventVersion, OccurredAt, CorrelationId, CausationId, TraceId, TenantId, AggregateId, AggregateVersion`.

Regla: **nunca** publicar PII sensible extendida, montos en texto plano fuera de `AmountCents`, ni el `VerificationHash` completo en eventos de notificación (se pasa por referencia/URL, no inline).

## Comandos (Application)

| Comando | Slice | Idempotente | Nota |
|---|---|---|---|
| `CreateInvoiceDraftCommand` | `Invoices/CreateInvoiceDraft` | sí (key) | — |
| `UpdateInvoiceDraftCommand` | `Invoices/UpdateInvoiceDraft` | sí | solo `Draft` |
| `IssueInvoiceCommand` | `Invoices/IssueInvoice` | sí | asigna número |
| `SendInvoiceCommand` | `Invoices/SendInvoice` | sí | orquesta PDF+PaymentLink+email |
| `RecordManualPaymentCommand` | `Invoices/RecordManualPayment` | sí | pago manual |
| `RecordOnlinePaymentCommand` | `Invoices/RecordOnlinePayment` | sí (por `PaymentReference`) | interno (consumer) |
| `RegisterPaymentFailureCommand` | `Invoices/RegisterPaymentFailure` | sí | interno |
| `RegisterRefundCommand` | `Invoices/RegisterRefund` | sí | interno |
| `VoidInvoiceCommand` | `Invoices/VoidInvoice` | sí | cancela cobro pendiente |
| `DeleteInvoiceDraftCommand` | `Invoices/DeleteInvoiceDraft` | sí | soft-delete |
| `ResendInvoiceCommand` | `Invoices/ResendInvoice` | sí | reenvío email |
| `ResendReceiptCommand` | `Receipts/ResendReceipt` | sí | reenvío recibo |
| `UpdateBillingSettingsCommand` | `Settings/UpdateBillingSettings` | sí | config tenant |

## Eventos de dominio (internos → outbox)

`InvoiceIssued`, `InvoiceSent`, `InvoicePaid`, `InvoicePartiallyPaid`, `InvoicePaymentFailed`, `InvoiceRefunded`, `InvoiceVoided`, `ReceiptIssued`, `ReceiptVoided`, `ReceiptRefunded`. Cada uno traduce a cero o más eventos de integración al drenarse.

## Eventos de integración PUBLICADOS por Billing

`BillingIntegrationEvents/…` — `Producer = Billing`.

| Nombre | EventType | Payload adicional | Consumidor previsto |
|---|---|---|---|
| `InvoiceIssuedIntegrationEvent` | `billing.invoice.issued` | `InvoiceId, InvoiceNumber, CustomerId, TotalAmountCents, Currency, DueDateUtc` | lectores/analytics |
| `InvoiceSentIntegrationEvent` | `billing.invoice.sent` | `InvoiceId, InvoiceNumber, CustomerId, CustomerEmail, PdfFileId, PayUrl?, PaymentMethod` | **Notification** (email al cliente) |
| `InvoicePaymentLinkCreatedIntegrationEvent` | `billing.invoice.payment_link_created` | `InvoiceId, PaymentSource, PaymentId, PayUrl, ExpiresAtUtc?` | lectores; auditoría |
| `InvoicePaidIntegrationEvent` | `billing.invoice.paid` | `InvoiceId, InvoiceNumber, CustomerId, AmountPaidCents, Currency, PaymentMethod, PaidAtUtc, ReceiptId, PaidPdfFileId?` | **Notification** (recibo por email) |
| `InvoicePartiallyPaidIntegrationEvent` | `billing.invoice.partially_paid` | `InvoiceId, AmountPaidCents, AmountDueCents, Currency, ReceiptId` | Notification |
| `InvoicePaymentFailedIntegrationEvent` | `billing.invoice.payment_failed` | `InvoiceId, PaymentSource, PaymentId, FailureCode` | Notification (aviso) |
| `InvoiceRefundedIntegrationEvent` | `billing.invoice.refunded` | `InvoiceId, RefundReference, RefundAmountCents, Currency, ReceiptId` | Notification |
| `InvoiceVoidedIntegrationEvent` | `billing.invoice.voided` | `InvoiceId, InvoiceNumber, Reason` | lectores |
| `ReceiptIssuedIntegrationEvent` | `billing.receipt.issued` | `ReceiptId, ReceiptNumber, InvoiceId, CustomerId, CustomerEmail, AmountPaidCents, Currency, PdfFileId, VerifyUrl` | **Notification** (email recibo) |

## Eventos de integración CONSUMIDOS por Billing

Cola `billing-events` (bind a `taxvision-events`, inbox durable). Contratos ya existentes en `src/BuildingBlocks/Messaging/PaymentIntegrationEvents/PaymentLifecycleIntegrationEvents.cs`.

| Nombre | EventType | Producer | Handler Billing | Correlación |
|---|---|---|---|---|
| `PaymentSucceededIntegrationEvent` | `payments.payment_succeeded` | PaymentClient | `PaymentSucceededConsumer` → `RecordOnlinePaymentCommand` | `(PaymentSource,PaymentId)` → `InvoicePaymentLink` (BDR-001) |
| `PaymentFailedIntegrationEvent` | `payments.payment_failed` | PaymentClient | `PaymentFailedConsumer` → `RegisterPaymentFailureCommand` | idem |
| `PaymentRefundedIntegrationEvent` | `payments.payment_refunded` | PaymentClient | `PaymentRefundedConsumer` → `RegisterRefundCommand` | idem |
| `PaymentCancelledIntegrationEvent` | `payments.payment_cancelled` | PaymentClient | `PaymentCancelledConsumer` → actualiza `InvoicePaymentLink` | idem |

**BDR-001**: el envelope de pago no trae `InvoiceId` ni `PurposeExternalReferenceId`. Billing correlaciona con su tabla local `InvoicePaymentLinks` (poblada al crear el cobro en UC-04). Si el evento no matchea ningún link (p.ej. cobro no originado por invoice), el consumer hace no-op idempotente. Mejora futura: PaymentClient echa `PurposeExternalReferenceId` en el envelope → correlación directa por `InvoiceId`.

## Mapa de adaptación de eventos (legado → nuevo)

| Legado (`CRMTAXPROBACKEND`) | Nuevo | Cambio |
|---|---|---|
| `LandingInvoicesSendEvent` (a PaymentService, crea token) | UC-04 llama PaymentClient M2M + `billing.invoice.payment_link_created` | de evento a llamada síncrona + evento de auditoría |
| `LandingInvoicesSendDirectEvent` (email sin link) | `billing.invoice.sent` (Notification) | unificado |
| `InvoicePaymentReadyEvent` (email con link) | `billing.invoice.sent` con `PayUrl` | unificado |
| `ServiceInvoiceUpdate` (PaymentService → CustomerService, "paid") | `payments.payment_succeeded` → UC-06 | contrato genérico de plataforma |
| `InvoicePaidEmailEvent` | `billing.invoice.paid` + `billing.receipt.issued` (Notification) | separado documento/recibo |
| `ManualPaymentCompletedEvent` (a PaymentService, registra Manual) | UC-05 aplica pago local; PaymentClient no necesita evento (el pago manual no pasa por provider) | simplificado |
| `PaymentCanceledEvent` (a PaymentService, void token) | UC-09 cancela el link vía PaymentClient M2M | de evento a llamada síncrona |
| `PaymentReceiptClientEmailEvent` (definido, sin uso) | `billing.receipt.issued` | reemplazado |
| `InvoicePaymentCompletedEvent` (referral, colgado) | fuera de MVP (gancho Growth futuro) | no portado |
