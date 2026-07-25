# Invoices — Diseño de dominio

Fecha: 2026-07-22

Bounded context único `Invoices`. Cuatro aggregate roots: `Invoice` y `PaymentReceipt` (centrales), `TenantBillingSettings` e `InvoiceNumberSequence` (soporte). Base classes de `src/BuildingBlocks/Domain/`: `BaseEntity` (`Guid Id`) → `TenantEntity : BaseEntity, ITenantOwned` (`Guid TenantId`) → `AggregateRoot : TenantEntity` (acumula `IDomainEvent`).

## Aggregate roots

### `Invoice : AggregateRoot`
Documento factura tenant→taxpayer. Dueño de su estado, totales, líneas y enlaces de pago.

Propiedades (todas `private set`):
- Identidad: `Id`, `TenantId`, `InvoiceNumber` (VO, `null` en `Draft`, asignado en `Issue`).
- Estado: `InvoiceStatus Status` (`Draft` inicial).
- Fechas: `DateTime IssueDateUtc`, `DateTime DueDateUtc`, `DateTime? SentAtUtc`, `DateTime? PaidAtUtc`, `DateTime CreatedAtUtc`, `DateTime UpdatedAtUtc`.
- Partes: `CustomerSnapshot Customer`, `IssuerSnapshot Issuer` (ambos congelados en `Issue`).
- Detalle: `IReadOnlyCollection<InvoiceLineItem> Lines`, `Discount? Discount`.
- Totales (congelados en `Issue`): `Money Subtotal`, `Money TaxTotal`, `Money DiscountTotal`, `Money Total`, `Money AmountPaid`, `Money AmountDue`.
- Comercial: `string? PoNumber`, `string? Summary`, `string? Notes`, `string Currency`.
- Pago: `PaymentMethod? PaymentMethod`, `IReadOnlyCollection<InvoicePaymentLink> PaymentLinks`.
- Presentación: `Guid? PdfFileId`, `Guid? PaidPdfFileId` (referencias CloudStorage).
- Auditoría/concurrencia: `Guid CreatedBy`, `Guid? LastModifiedBy`, `byte[] RowVersion`, `DateTime? DeletedAtUtc` (soft-delete solo en `Draft`).

Fábrica y comportamiento (todo devuelve `Result`/`Result<T>`, nunca `throw` de flujo):
- `static Result<Invoice> CreateDraft(tenantId, customer, issuer, lines, discount?, dueDateUtc, currency, poNumber?, summary?, notes?, actorUserId, nowUtc)` — valida ≥1 línea, moneda consistente, `dueDate ≥ issueDate`; calcula totales provisionales; `Status=Draft`.
- `Result UpdateDraft(...)` — solo en `Draft`; recompone líneas/descuento/totales.
- `Result Issue(InvoiceNumber number, IssuerSnapshot issuer, actorUserId, nowUtc)` — `Draft → Issued`; asigna número, congela snapshots y totales; recalcula `AmountDue = Total`, `AmountPaid = 0`.
- `Result MarkSent(actorUserId, nowUtc)` — `Issued → Sent`; set `SentAtUtc`; emite `InvoiceSent`.
- `Result AttachPaymentLink(InvoicePaymentLink link)` — registra el `(PaymentSource,PaymentId,PayUrl,Status)` devuelto por PaymentClient (para correlación BDR-001).
- `Result<PaymentReceipt> RecordPayment(Money amount, PaymentMethod method, string paymentReference, DateTime paymentDateUtc, ReceiptNumber receiptNumber, actorUserId, nowUtc)` — `Sent/PartiallyPaid → Paid|PartiallyPaid`; suma `AmountPaid`, recalcula `AmountDue`; si `AmountDue==0 → Paid`, si `>0 → PartiallyPaid`; **crea y devuelve** el `PaymentReceipt` correspondiente; emite `InvoicePaid`/`InvoicePartiallyPaid` (+ `ReceiptIssued`). Idempotente por `paymentReference`.
- `Result MarkPaymentFailed(string paymentReference, string failureCode)` — no cambia el estado del documento (sigue `Sent`); actualiza el `InvoicePaymentLink`; emite `InvoicePaymentFailed`.
- `Result RegisterRefund(Money amount, string refundReference, DateTime nowUtc)` — solo desde `Paid/PartiallyPaid`; reduce `AmountPaid`; marca recibo `Refunded` (vía el aggregate recibo); emite `InvoiceRefunded`.
- `Result Void(string reason, actorUserId, nowUtc)` — `Draft/Issued/Sent/PartiallyPaid → Voided`; set `AmountDue = 0`, append `reason` a `Notes`; emite `InvoiceVoided` (el orquestador cancela el cobro pendiente en PaymentClient si existe).
- `Result SetPdf(Guid pdfFileId)` / `Result SetPaidPdf(Guid paidPdfFileId)` — guarda referencias de CloudStorage.
- `Result SoftDelete(actorUserId, nowUtc)` — solo en `Draft`.

Lo que `Invoice` **NO** debe contener: lógica de cobro/provider, envío de email, render de PDF, cálculo de impuestos server-side, ni datos maestros de cliente/emisor (solo snapshots). No conoce Stripe/IntelliPay ni el bus (los eventos se drenan en `SaveChanges`).

### `PaymentReceipt : AggregateRoot`
Comprobante verificable de un pago aplicado a una invoice.

Propiedades: `Id`, `TenantId`, `ReceiptNumber` (VO), `Guid InvoiceId`, `string InvoiceNumber` (copia legible), `CustomerSnapshot Customer` (subconjunto), `Money AmountPaid`, `PaymentMethod PaymentMethod`, `string PaymentReference`, `DateTime PaymentDateUtc`, `DateTime IssuedDateUtc`, `VerificationHash VerificationHash`, `ReceiptStatus Status` (`Active`/`Void`/`Refunded`), `string? Notes`, `Guid? PdfFileId`, `Guid? ProcessedByUserId`, auditoría + `RowVersion`.

Comportamiento:
- `static Result<PaymentReceipt> Issue(tenantId, invoiceId, invoiceNumber, customer, amount, method, paymentReference, paymentDateUtc, receiptNumber, processedByUserId, nowUtc)` — computa `VerificationHash` sobre `ReceiptNumber|InvoiceId|PaymentReference|AmountCents|PaymentDateUtc(O)`; `Status=Active`; emite `ReceiptIssued`.
- `bool ValidateHash()` — recomputa y compara (verificación pública anti-manipulación).
- `Result Void(string reason, nowUtc)` — `Active → Void`.
- `Result MarkRefunded(string refundReference, nowUtc)` — `Active → Refunded`.
- `Result SetPdf(Guid pdfFileId)`.

### `TenantBillingSettings : TenantEntity`
Configuración por tenant (una fila por `TenantId`). Emisor por defecto (`IssuerSnapshot` base), ajustes de PDF (`Template`, `PageSize`, `Orientation`, `ShowLogo`, `ShowFooter`, `ShowAttachments`), y política de numeración (`NumberPrefix` default `"INV"`, `ResetPolicy` `None|Yearly|Monthly`). Fábrica `CreateDefault(tenantId, actorUserId, nowUtc)` + `Update(...)`.

### `InvoiceNumberSequence : TenantEntity`
Contador monótono server-side. Clave `(TenantId, PeriodKey)` donde `PeriodKey` = `"ALL"` | `yyyy` | `yyyyMM` según `ResetPolicy`. Propiedad `long Next`. `Result<InvoiceNumber> Allocate(prefix, periodKey, nowUtc)` incrementa y formatea `"{prefix}-{yyyyMMdd}-{Next:D3}"`; concurrencia por `RowVersion` + índice único `(TenantId, PeriodKey)` y índice único `(TenantId, InvoiceNumber)` en `Invoice`.

## Value objects

- `InvoiceNumber(string Value)` — formato validado, ≤ 40 chars. `Create` / `TryParse`.
- `Money(long AmountCents, string Currency)` — centavos + ISO-4217; `Add`/`Subtract`/`Zero` (mismo patrón que `PaymentClient/…/Money.cs`).
- `Discount(DiscountType Type, int Value, Money Amount)` — `Type ∈ {Percentage, Fixed}`; `Value` = basis points (Percentage) o cents (Fixed); `Amount` = monto aplicado congelado.
- `InvoiceLineItem(string Description, int Quantity, Money UnitAmount, int TaxBasisPoints, Money TaxAmount, Money LineTotal)` — entidad interna; `LineTotal = UnitAmount*Quantity - descuentos + TaxAmount` (congelado).
- `CustomerSnapshot(Guid CustomerId, string Name, string? Email, string? Phone, string? TaxId, Address? Billing)` — id real (no se abusa `TaxId`).
- `IssuerSnapshot(string Name, Address Address, string? Phone, string? Email, string? Website, Guid? LogoFileId)` — logo por `FileId`, no base64.
- `Address(string Line1, string? Line2, string City, string State, string Zip, string Country)`.
- `PaymentPurposeReference(string ExternalReferenceId)` — el `InvoiceId` que Billing pasa a PaymentClient.
- `InvoicePaymentLink(string PaymentSource, Guid PaymentId, string Status, string? PayUrl)` — entidad interna para correlación.
- `VerificationHash(string Value)` — SHA-256 hex (64).
- `ReceiptNumber(string Value)` — `"RCP-{yyyy}-{seq}"`.

## Enums

- `InvoiceStatus { Draft, Issued, Sent, PartiallyPaid, Paid, Voided }`
- `ReceiptStatus { Active, Void, Refunded }`
- `PaymentMethod { Online, Card, Cash, Check, BankTransfer, Other }`
- `DiscountType { Percentage, Fixed }`
- `NumberResetPolicy { None, Yearly, Monthly }`

## Invariantes

1. Una `Invoice` en `Draft` no tiene `InvoiceNumber`; al `Issue` se le asigna uno único por tenant y ya no cambia.
2. Los snapshots (`Customer`, `Issuer`) y los totales quedan **congelados en `Issue`**: editar solo es posible en `Draft`.
3. `AmountDue = Total - AmountPaid` siempre ≥ 0; `Paid ⇔ AmountDue == 0`.
4. Toda `Money` de una invoice comparte la misma `Currency`.
5. Un `PaymentReceipt` referencia exactamente una `Invoice` y su `VerificationHash` sella su contenido; no se reescribe.
6. `RecordPayment` es idempotente por `PaymentReference` (protege contra redelivery del evento de pago).
7. `Void` no aplica a `Paid` (para revertir dinero se usa `RegisterRefund`, disparado por `payments.payment_refunded`).
8. Soft-delete solo en `Draft`; una invoice emitida jamás se borra físicamente (documento histórico).

## Tipos MVP-in / out-of-scope

MVP-in: `Invoice`, `PaymentReceipt`, `TenantBillingSettings`, `InvoiceNumberSequence`, pago full o parcial, descuento único a nivel invoice, impuesto por línea como monto congelado, PDF vía Scribe/CloudStorage, correlación PaymentClient.

Out-of-scope (ganchos): nota de crédito formal como aggregate propio, recurrencia de facturas, impuesto calculado server-side (Catalog), descuento por cupón/referido (Growth), multi-moneda por línea, adjuntos de invoice (solo flag).
