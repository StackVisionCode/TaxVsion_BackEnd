# Billing — Prerrequisitos e issues de pago

Fecha: 2026-07-22

Igual que Growth documentó los prerrequisitos que Payment debía cumplir (`27_Payment_Prerequisite_Issues.md`, audit `RCL-027/028`), Billing depende de PaymentClient para el ciclo de cobro tenant→taxpayer y hay gaps a resolver antes de la integración productiva.

## BDR-001 — El envelope de pago no identifica la invoice

**Afirmación**: `PaymentLifecycleIntegrationEvent` (base de `payments.payment_succeeded/failed/refunded/cancelled`) NO lleva `InvoiceId` ni `PurposeExternalReferenceId`.
**Evidencia**: `src/BuildingBlocks/Messaging/PaymentIntegrationEvents/PaymentLifecycleIntegrationEvents.cs` — el envelope tiene `PaymentSource`, `PaymentId`, montos, `CodeReservationId?`, `ReferralAttributionId?`, `PromotionSnapshotId?`, pero ningún campo de propósito/invoice.
**Clasificación**: VERIFIED. **Severidad**: HIGH.

Resolución MVP (sin tocar PaymentClient): cuando Billing crea el cobro (UC-04, `PaymentPurpose(InvoicePayment, InvoiceId)`), PaymentClient devuelve el `PaymentId`. Billing persiste `billing.InvoicePaymentLinks (PaymentSource, PaymentId, InvoiceId, Status)` con índice `(PaymentSource, PaymentId)`. Al llegar `payments.payment_succeeded`, el consumer busca el link por `(PaymentSource, PaymentId)` y resuelve el `InvoiceId`. Si no hay link (cobro no originado por invoice), no-op idempotente.

Mejora futura (requiere cambio en PaymentClient, fuera de scope Billing): PaymentClient echa `PurposeKind` + `PurposeExternalReferenceId` en el envelope de pago → correlación directa por `InvoiceId`, sin tabla de mapeo. Recomendado como issue separado en PaymentClient.

## BDR-002 — Autoridad de precio / cálculo de impuestos

**Afirmación**: el CRM nuevo prevé un `Catalog` como autoridad de precio, pero no existe. El legado recibía `Total/TotalTax/Item.Tax` precalculados desde el cliente (sin cálculo server-side).
**Evidencia**: no hay servicio `Catalog` en `src/Services/` (solo `Customer.Domain/Catalogs/` y `Subscription.Domain/Plans/PlanCatalog.cs`); memoria de diseño previa lo confirma como diferido.
**Clasificación**: PARTIAL. **Severidad**: MEDIUM.

Resolución MVP: Billing acepta montos precalculados por el caller (paridad con legado) y los **congela** como snapshot en la invoice. Se documenta como decisión abierta (`16_…`). Gancho futuro: `Catalog.ResolvePrice(item) → PriceSnapshot` invocado en UC-01/UC-03; Billing pasaría a solo snapshotear la respuesta del resolver.

## BDR-003 — Render de PDF en Scribe

**Afirmación**: el CRM nuevo tiene Scribe (render) y CloudStorage (`FileType.Invoice`/`FolderType.Invoices`), pero no existe un template de invoice en Scribe.
**Evidencia**: `src/Services/Scribe/` existe; enums de CloudStorage tienen `Invoice`. No hay template de invoice verificado.
**Clasificación**: PARTIAL. **Severidad**: MEDIUM.

Resolución: fase de implementación crea el template de invoice/recibo en Scribe (portando los 4 layouts QuestPDF del legado: classic/modern/minimal/professional + watermark "Paid"). Fallback: si Scribe no está listo, la fase 1 puede emitir invoices sin PDF (estado válido; `PdfFileId` null) y diferir PDF a fase 2.

## BDR-004 — Cancelación del cobro al anular (UC-09)

**Afirmación**: al hacer `Void` de una invoice con link de pago pendiente, hay que cancelar el cobro en PaymentClient (el legado publicaba `PaymentCanceledEvent`).
**Evidencia**: `CancelInvoiceHandler` legado publicaba a PaymentService solo si `PaymentMethod=="IntelliPay"`.
**Clasificación**: DOCUMENTED_ONLY. **Severidad**: LOW.

Resolución: UC-09 llama PaymentClient M2M `cancel` para el `PaymentId` del link activo (best-effort, no bloquea la anulación de la invoice; si falla, se registra y reintenta). Requiere que PaymentClient exponga cancelación de link — verificar en implementación.

## Matriz de bloqueo

| ID | ¿Bloquea scaffolding? | ¿Bloquea integración? | ¿Bloquea producción? |
|---|---|---|---|
| BDR-001 | No | Sí (correlación) | Sí |
| BDR-002 | No | No | Parcial (deuda fiscal) |
| BDR-003 | No | No | Sí (sin PDF no hay factura entregable) |
| BDR-004 | No | Sí (limpieza de cobros) | Sí |

Ninguno bloquea el scaffolding compilable (tarea actual). BDR-001 y BDR-004 se resuelven en la fase de integración con PaymentClient; BDR-003 en la fase de PDF.
