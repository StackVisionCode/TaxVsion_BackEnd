# Billing — Preguntas abiertas

Fecha: 2026-07-22

Decisiones pendientes de confirmar con el owner del producto antes de (o durante) la implementación. Cada una tiene una recomendación por defecto para no bloquear el scaffolding.

| ID | Pregunta | Opciones | Recomendación por defecto |
|---|---|---|---|
| OQ-01 | ¿Billing calcula impuestos/precio o los recibe precalculados? | (a) recibir del caller (paridad legado) · (b) resolver server-side vía Catalog | (a) para MVP; congelar snapshot; (b) cuando exista Catalog (BDR-002) |
| OQ-02 | ¿Correlación pago↔invoice por mapeo local o eco de PaymentClient? | (a) tabla `InvoicePaymentLinks` local · (b) pedir a PaymentClient que eche `PurposeExternalReferenceId` | (a) para MVP; abrir issue en PaymentClient para (b) (BDR-001) |
| OQ-03 | ¿PDF en fase 1 o diferido a fase 2? | (a) requerir Scribe template desde B2 · (b) emitir sin PDF, PDF en B5 | (b): invoice válida sin PDF; PDF en B5 (BDR-003) |
| OQ-04 | ¿Soporta pago parcial en MVP o solo full? | (a) full only (paridad legado, `AmountDue→0`) · (b) parcial con `PartiallyPaid` | (b) modelado, pero puede lanzarse restringido a full en B3 |
| OQ-05 | ¿Nota de crédito como aggregate propio? | (a) fuera de MVP, reembolso reduce `AmountPaid` · (b) `CreditNote` aggregate | (a) para MVP |
| OQ-06 | ¿Numeración por período (reset anual/mensual) o global por tenant? | (a) global `ALL` · (b) `Yearly`/`Monthly` configurable | (b) configurable en `TenantBillingSettings.ResetPolicy`, default `Yearly` |
| OQ-07 | ¿Multi-moneda por invoice o una moneda por tenant? | (a) una moneda por invoice (todas las líneas igual) · (b) multi-moneda por línea | (a) para MVP (invariante 4 del dominio) |
| OQ-08 | ¿Integración con Growth (cupón/referido) sobre invoices? | (a) fuera de MVP · (b) descuento de invoice vía Codes | (a); gancho futuro (el legado tenía el evento colgado) |
| OQ-09 | ¿Portal de pago público lo sirve el frontend + PaymentClient link, o Billing expone algo? | (a) frontend + PaymentClink URL · (b) endpoint público en Billing | (a): Billing solo expone `receipts/verify` anónimo |
| OQ-10 | ¿Retención/borrado de invoices (política fiscal)? | (a) soft-delete solo en Draft, resto inmutable · (b) política de purga configurable | (a) para MVP; purga fuera de scope |

## Decisiones ya tomadas (registro)

- Servicio nuevo `TaxVision.Billing`, single bounded context `Invoices`, 4 proyectos (ADR-BILLING-001).
- Alcance tenant→taxpayer; el SaaS billing se queda en Subscription/PaymentApp (confirmado por el usuario).
- Entregable de esta fase: documentación + scaffold compilable (confirmado por el usuario).
- `Money(long Cents)`, status enum, numeración server-side, PDF vía Scribe/CloudStorage, email vía Notification, correlación vía PaymentClient `PurposeKind`.
- Reutiliza `billing.view`/`billing.manage` ya seeded en Auth (sin migración de Auth nueva).
