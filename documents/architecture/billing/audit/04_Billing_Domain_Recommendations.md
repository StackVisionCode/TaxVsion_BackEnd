# Billing — Recomendaciones de dominio

Auditoría: arquitecto principal (.NET/DDD/sistemas financieros). Fecha: 2026-07-22.
Base: `06_Invoices_Domain_Design.md`, `07`, `08`, `10` vs. convenciones reales del repo (`src/Services/Growth` como modelo DDD verificado).

## 1. `PaymentReceipt` dentro de `Invoice.RecordPayment` — decisión de arquitectura

**Problema (C-06):** `06:30` — `Invoice.RecordPayment()` **crea y devuelve** un `PaymentReceipt`, siendo ambos aggregate roots. En el repo, ningún aggregate crea otro aggregate root (verificado: `Growth` — `CodeReservation`, `CodeRedemption`, `ReferralRewardCase` se crean cada uno en su propio handler; la regla de la casa es *una raíz mutada por transacción*, ver `AggregateRoot` en `src/BuildingBlocks/Domain/`).

### Alternativas evaluadas

| Criterio | A. Invoice muta saldo + emite evento; **handler** crea Receipt | B. **Domain/Application service** coordina ambos en la txn local | C. Invoice crea el Receipt (diseño actual) |
|---|---|---|---|
| Consistencia | Cada raíz consistente por separado; el handler abre una txn local que persiste ambas | Igual que A, con la coordinación explícita en un servicio | Dos raíces en una sola mutación de aggregate — rompe el límite |
| Transaction boundary | Claro: 1 txn de aplicación, 2 `Add`/`Update` de raíces distintas (permitido en el mismo `SaveChanges`) | Claro | Difuso: el aggregate decide la persistencia de otro |
| Idempotencia | La clave vive en el handler (idempotency executor); replay devuelve el receipt persistido | Igual | La idempotencia queda dentro del aggregate, difícil de testear/aislar |
| Persistencia | El handler controla el orden y el outbox | Igual | El aggregate "sabe" de otro repositorio implícitamente |
| Testabilidad | Alta: `Invoice.RecordPayment` testeable sin crear receipts; el handler testeable con fakes | Media (un tipo más) | Baja: no se puede testear el pago sin materializar el receipt |
| Acoplamiento | Bajo: acoplados por evento/datos, no por referencia | Medio | Alto |

### Recomendación: **Alternativa A**

`Invoice.RecordPayment(...)` devuelve un `PaymentApplied` (VO con `amount, method, paymentReference, paymentDateUtc, resultingStatus`) y agrega un domain event `InvoicePaid`/`InvoicePartiallyPaid`. El **handler** (`RecordOnlinePaymentHandler`/`RecordManualPaymentHandler`), dentro de la misma transacción de Wolverine (`AutoApplyTransactions`), invoca `PaymentReceipt.Issue(...)` y lo persiste. Un mismo `SaveChanges` persiste ambas raíces y encola los eventos de integración al outbox. Esto respeta el límite de aggregate, mantiene la idempotencia en el executor (patrón `SqlBusinessIdempotencyExecutor` de Growth), y hace testeable el pago sin el recibo.

> Regla: *una mutación de escritura por aggregate root por transacción; múltiples raíces en la misma transacción de aplicación solo cuando la consistencia lo exige y las crea el handler, no otra raíz.*

## 2. Comportamiento exacto de pagos y reembolsos

Estados relevantes propuestos (corrige C-01/C-02): `InvoiceStatus { Draft, Issued, Sent, PartiallyPaid, Paid, PartiallyRefunded, Refunded, Voided }`. Campos: `Total`, `AmountPaid`, `AmountRefunded`, `AmountDue = Total - AmountPaid` (nunca se reabre por refund).

| Escenario | Regla | Estado resultante | Efecto |
|---|---|---|---|
| Pago menor que el saldo (`0 < amount < AmountDue`) | aceptar | `PartiallyPaid` | `AmountPaid += amount`; emitir recibo; `InvoicePartiallyPaid` |
| Pago igual al saldo (`amount == AmountDue`) | aceptar | `Paid` | `AmountPaid = Total`; `AmountDue = 0`; recibo; `InvoicePaid` |
| Pago mayor que el saldo (`amount > AmountDue`) | **rechazar** MVP | sin cambio | error `Billing.Invoice.Overpayment` + alerta; (futuro: registrar crédito) |
| Pago duplicado (mismo `PaymentId`/`Idempotency-Key`) | idempotente | sin cambio | devolver el recibo existente; métrica `duplicate_payment` |
| Reembolso parcial (`0 < r ≤ AmountPaid`) | aceptar | `PartiallyRefunded` | `AmountRefunded += r`; el recibo referido → `Refunded`/parcial; `InvoiceRefunded`; NO tocar `AmountDue` |
| Reembolso total (`AmountRefunded == AmountPaid`) | aceptar | `Refunded` | recibo(s) → `Refunded`; `InvoiceRefunded` |
| Varios recibos | permitido | — | cada pago parcial genera un recibo; la invoice referencia N recibos |
| Reembolso asociado a un recibo específico | requerido | — | `RegisterRefund(receiptId, amount, refundReference)` — el refund apunta al recibo/pago original (el evento `payments.payment_refunded` trae `PaymentId` → link → recibo) |
| Pago recibido después de anular (`Voided`) | **no descartar** | `Voided` (sin cambio de estado comercial) | registrar el hecho; disparar `RegisterRefund` automático; `billing.invoice.payment_after_void`; alerta (C-09) |
| Evento de pago fuera de orden | tolerar | según hecho | usar `ProviderEventId`/versión/`OccurredAt`; ignorar un `failed` posterior a un `succeeded` ya aplicado (patrón de Growth `PaymentReservationCancellation`: no revertir un commit por un failed tardío) |

## 3. Invariantes (corregidas)

1. `AmountDue = Total - AmountPaid ≥ 0`. Un reembolso **no** modifica `AmountDue` (afecta `AmountRefunded`).
2. `Paid ⇔ AmountDue == 0 ∧ AmountRefunded == 0`.
3. `Refunded ⇔ AmountRefunded == AmountPaid ∧ AmountPaid > 0`.
4. Totales congelados en `Issue`; snapshots congelados en `Issue`; edición solo en `Draft`.
5. `RecordPayment` idempotente por la clave canónica (online `(PaymentSource,PaymentId)`, manual `Idempotency-Key`).
6. Un `PaymentReceipt` sella su contenido con `VerificationHash` y no se reescribe; `Void`/`Refunded` son metadata.
7. `Void` prohibido sobre `Paid`; `Void` de una factura con `AmountPaid>0` exige reembolso previo/automático (C-11).
8. Una moneda por invoice; la aritmética respeta el exponente ISO-4217 de esa moneda (C-13).

## 4. Numeración (`InvoiceNumberSequence`) — resumen (detalle en `07_Data_And_Concurrency`)

El diseño actual (`06`: `Allocate` con `RowVersion` + retry) es correcto pero subóptimo bajo contención. Recomendación: **SQL Server `SEQUENCE` por `(TenantId, PeriodKey)`** o **`UPDATE … SET Next = Next + 1 OUTPUT INSERTED.Next` con `UPDLOCK, HOLDLOCK`** (upsert atómico), evitando la tormenta de reintentos del `RowVersion` optimista bajo emisión concurrente. El índice único `(TenantId, InvoiceNumber)` en `Invoices` es la red de seguridad final. La asignación del número debe ocurrir **dentro** de la misma transacción que persiste el `Issue` (si la txn falla, el número no se "quema" — con SEQUENCE sí se quema un valor, aceptable; con UPDLOCK no).

## 5. Impuestos, descuentos y dinero (resumen; detalle en `07`)

- Billing recibe **componentes** (cantidad, precio unitario, tasa de impuesto en bps, descuento) y **recalcula server-side** (C-07). Nunca confía en `Total`/`TotalTax` del caller.
- Orden de operaciones definido y determinista (línea → descuento → impuesto → totales), con reglas de redondeo explícitas (banker's rounding o half-up, decidir y fijar).
- Descuento por línea y global; impuesto añadido vs incluido como bandera por línea.
- Moneda con exponente ISO-4217 (0/2/3 decimales), no `Cents` fijo.

## 6. Inmutabilidad fiscal

Una factura `Issued` o posterior es un documento histórico: se prohíbe editar líneas/totales/snapshots y el borrado físico (soft-delete solo en `Draft`). Las correcciones se hacen con nota de crédito (fuera de MVP, gancho futuro) o refund, nunca reescribiendo la factura.
