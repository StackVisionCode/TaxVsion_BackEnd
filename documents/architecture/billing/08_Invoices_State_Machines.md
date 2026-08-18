# Invoices — Máquinas de estado

Fecha: 2026-07-22

Reemplaza el status string libre del legado (`"Draft"/"sent"/"paid"/"canceled"`, comparado case-insensitive ad hoc) por enums con transiciones explícitas y guardas en el dominio.

## `Invoice.Status : InvoiceStatus`

```
                    UpdateDraft (self)
                       ┌────┐
                       ▼    │
   CreateDraft ──►  [Draft] ─────────────── SoftDelete ──► (borrado lógico)
                       │
                       │ Issue (asigna InvoiceNumber, congela snapshots+totales)
                       ▼
                   [Issued] ──────────────── Void ──► [Voided]
                       │
                       │ MarkSent (render PDF + email + PaymentLink opcional)
                       ▼
                    [Sent] ───────────────── Void ──► [Voided]
                       │  │
        RecordPayment  │  │ RecordPayment (parcial)
        (total)        │  ▼
                       │ [PartiallyPaid] ──── Void ──► [Voided]
                       │  │   │
                       │  │   │ RecordPayment (completa el saldo)
                       ▼  ▼   ▼
                     [Paid]
                       │
                       │ RegisterRefund (evento payments.payment_refunded)
                       ▼
                 [PartiallyPaid]  (saldo reabierto por reembolso parcial)
                   o recibo Refunded (reembolso total → invoice queda Paid con AmountPaid reducido)
```

### Tabla de transiciones

| Desde | Evento/acción | Guarda | Hacia | Efectos |
|---|---|---|---|---|
| — | `CreateDraft` | ≥1 línea, moneda consistente, `dueDate ≥ issueDate` | `Draft` | calcula totales provisionales |
| `Draft` | `UpdateDraft` | — | `Draft` | recompone líneas/descuento/totales |
| `Draft` | `SoftDelete` | — | (DeletedAtUtc) | no elimina físico |
| `Draft` | `Issue` | número disponible en la sequence | `Issued` | asigna `InvoiceNumber`, congela snapshots+totales, `AmountDue=Total` |
| `Issued` | `MarkSent` | — | `Sent` | `SentAtUtc`; dispara PDF/email/PaymentLink |
| `Issued`/`Sent`/`PartiallyPaid` | `Void` | Status ≠ `Paid` | `Voided` | `AmountDue=0`; cancela cobro pendiente en PaymentClient |
| `Sent`/`PartiallyPaid` | `RecordPayment` (total) | `amount == AmountDue` | `Paid` | genera `PaymentReceipt`, `PaidAtUtc`, PDF watermark |
| `Sent`/`PartiallyPaid` | `RecordPayment` (parcial) | `0 < amount < AmountDue` | `PartiallyPaid` | genera `PaymentReceipt`, reduce `AmountDue` |
| `Sent` | `MarkPaymentFailed` | — | `Sent` (sin cambio) | actualiza `InvoicePaymentLink`; evento `payment_failed` |
| `Paid`/`PartiallyPaid` | `RegisterRefund` | monto ≤ `AmountPaid` | `PartiallyPaid` (o recibo `Refunded`) | reduce `AmountPaid`; marca recibo |

### Guardas clave (equivalentes limpias de los `if` legados)

- No se puede `Issue` dos veces (solo desde `Draft`).
- No se puede `Void` una `Paid` → error `Billing.Invoice.CannotVoidPaid` (legado: "process a refund instead").
- No se puede `RecordPayment` sobre `Draft/Issued/Voided` → `Billing.Invoice.NotPayable`.
- `RecordPayment` idempotente por `PaymentReference`: replay ⇒ devuelve el recibo existente (legado `UpdateInvoicePaidHandlers` chequeaba "already paid").
- `SoftDelete` bloqueado fuera de `Draft` (legado bloqueaba delete de `sent`/`paid`).

### Estados derivados (no persistidos como enum)

- **Overdue**: `Status ∈ {Issued, Sent, PartiallyPaid}` y `nowUtc > DueDateUtc`. Se calcula en query, no es un estado del aggregate.

## `PaymentReceipt.Status : ReceiptStatus`

```
   Issue ──► [Active] ── Void ──► [Void]
                 │
                 └── MarkRefunded ──► [Refunded]
```

| Desde | Acción | Hacia | Nota |
|---|---|---|---|
| — | `Issue` | `Active` | computa `VerificationHash` |
| `Active` | `Void` | `Void` | anulación administrativa |
| `Active` | `MarkRefunded` | `Refunded` | disparado por `payments.payment_refunded` |

El `VerificationHash` se calcula una vez en `Issue` y **no** se recalcula en `Void`/`Refunded` (el hash sella el hecho de pago original; el estado es metadata sobre ese hecho).

## Concurrencia

- `Invoice` y `PaymentReceipt` llevan `RowVersion` (`IsRowVersion()`); las transiciones fallan con `Billing.*.Concurrency` (→ HTTP 409) ante escritura concurrente.
- `InvoiceNumberSequence.Allocate` es la sección crítica de la numeración: `RowVersion` + índice único `(TenantId, PeriodKey)`; ante colisión se reintenta la asignación. El índice único `(TenantId, InvoiceNumber)` en `Invoice` es la red de seguridad final.
