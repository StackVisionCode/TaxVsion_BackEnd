# TaxVision.Billing — Resumen ejecutivo

Fecha: 2026-07-22
Estado final de diseño: `READY_FOR_SCAFFOLDING` (dominio y contratos especificados; integración con PaymentClient/Scribe/CloudStorage/Notification documentada, no implementada).

## Resultado

Se crea un microservicio nuevo **`TaxVision.Billing`** que formaliza la **facturación del tenant hacia sus taxpayers/clientes** (el tenant emite facturas a sus clientes y cobra por ellas), adaptando y modernizando el módulo de invoices que en el CRM legado `CRMTAXPROBACKEND` vivía disperso dentro de `CustomerService` + `PaymentService` + `EmailServices`.

Billing es el par natural de **PaymentClient** (que ejecuta los cobros tenant→taxpayer) y del futuro **Catalog** (que resolverá precios de los ítems que el tenant vende). **NO** absorbe la facturación del SaaS (planes/seats/add-ons plataforma→tenant), que permanece en **Subscription** + **PaymentApp**.

```
src/Services/Billing/
  TaxVision.Billing.Domain/           # Invoice + PaymentReceipt + TenantBillingSettings + InvoiceNumberSequence
  TaxVision.Billing.Application/       # slices de caso de uso (Wolverine handlers) + Abstractions
  TaxVision.Billing.Infrastructure/    # EF Core, outbox/inbox, idempotencia, clientes M2M (PaymentClient/Scribe/CloudStorage)
  TaxVision.Billing.Api/               # controllers públicos + Internal M2M + consumers de eventos de pago
deploy/tests/TaxVision.Billing.Tests/  # xUnit
```

Un solo bounded context (`Invoices`) con dos aggregate roots (`Invoice`, `PaymentReceipt`) más dos aggregates de soporte (`TenantBillingSettings`, `InvoiceNumberSequence`). No se justifica multi-contexto para el MVP.

## Coordenadas de plataforma (todas verificadas libres)

| Recurso | Valor | Evidencia |
|---|---|---|
| Puerto dev | `5440` | `5410` ocupado por Growth (`gateway/appsettings.json`); siguiente bloque libre |
| Ruta Gateway | `/billing/{**catch-all}` → cluster `billing` | patrón de `growth` en `gateway/appsettings.json` |
| Contenedor | `billing-api`, interno `:8080` | patrón compose `growth-api` |
| Base de datos | `TaxVision_Billing` (`BILLING_DB_CONNECTION`) | patrón `GROWTH_DB_CONNECTION` en `.env` |
| Audience M2M | `taxvision-billing` (`Jwt__ValidAudiences__1`) | patrón `taxvision-growth` |
| Cola RabbitMQ | `billing-events` (bind a `taxvision-events`) | patrón `growth-events` |
| Permisos humanos | `billing.view` / `billing.manage` (YA existen y seeded en Auth) | `PermissionCatalog.cs:20-21`; migración `20260702073548_AddSecurityRbacMfaSessionsAndPlanLimits.cs:557-566` |

## Evidencia y confianza

| Área | Resultado | Evidencia (path) | Clasificación | Confianza |
|---|---|---|---|---|
| Modelo de invoices legado | Mapeado completo (aggregate `InvoiceData`, `Item`, `Discount`, `PaymentReceipt`, `InvoiceCompanyInfo`, `InvoicePDFSettings`) | `CRMTAXPROBACKEND/CustomerService/Domains/Invoice/` | VERIFIED | 95% |
| Eventos legados | 8 eventos catalogados (send / send-direct / ready / update / paid-email / manual-completed / canceled / referral) | `CRMTAXPROBACKEND/SharedLibrary/DTOs/` | VERIFIED | 95% |
| Gancho de pago en el CRM nuevo | `PaymentPurposeKind.InvoicePayment` + `PurposeExternalReferenceId` opaco | `PaymentClient/…/ValueObjects/PaymentPurpose.cs` | VERIFIED | 99% |
| Permisos Billing en Auth | `billing.view`/`billing.manage` ya existen y están seeded | `PermissionCatalog.cs:20-21` | VERIFIED | 99% |
| Convención Money | VO por servicio `Money(long AmountCents, string Currency)` | `PaymentClient/…/ValueObjects/Money.cs` | VERIFIED | 99% |
| Contrato de eventos de pago a consumir | `PaymentSucceeded/Failed/Refunded/Cancelled` sin `InvoiceId` en el envelope | `BuildingBlocks/Messaging/PaymentIntegrationEvents/PaymentLifecycleIntegrationEvents.cs` | VERIFIED | 95% |
| Render PDF en el CRM nuevo | Scribe (render) + CloudStorage (`FileType.Invoice`/`FolderType.Invoices`) existen | `src/Services/Scribe/`, `CloudStorage` enums | PARTIAL | 80% |

## DESIGN_BLOCKER

- **BDR-001 (HIGH)** — El envelope `PaymentLifecycleIntegrationEvent` **no lleva `InvoiceId` ni el `PurposeExternalReferenceId`**. Billing no puede saber qué invoice se pagó solo con el evento. Resolución MVP: Billing persiste el mapeo `(PaymentSource, PaymentId) → InvoiceId` cuando **él mismo** inicia el cobro/link vía PaymentClient. Mejora futura: pedir a PaymentClient que haga eco de `PurposeExternalReferenceId` en el envelope. Ver `13_Billing_Payment_Prerequisite_Issues.md`. (Espejo del gap Growth `RCL-027/028`.)
- **BDR-002 (MEDIUM)** — Autoridad de precio de los ítems facturados. El CRM legado recibía `TotalTax`/`Total`/`Item.Tax` **precalculados desde el cliente** (sin cálculo server-side). El CRM nuevo prevé un `Catalog` (aún inexistente) como autoridad de precio. Decisión MVP: Billing acepta montos precalculados por el caller (paridad con legado) pero los **congela como snapshot** en la invoice; el resolver server-side de precio queda como gancho futuro (`Catalog.ResolvePrice`). Ver `05_Billing_Ownership_Matrix.md`.

## Alcance MVP

Incluido: emisión de invoices (borrador → emitida → enviada → pagada/anulada), numeración server-side por tenant, snapshots de cliente/emisor, líneas de detalle, descuento e impuestos como montos congelados, generación de PDF (Scribe→CloudStorage), recibo de pago verificable por hash, correlación con cobros de PaymentClient (`PurposeKind=InvoicePayment`), y publicación de eventos de integración (`billing.invoice.*`, `billing.receipt.issued`).

Fuera de alcance MVP (ganchos dejados listos): cálculo server-side de impuestos/precio (Catalog), recurrencia/suscripción de facturas, notas de crédito formales, multi-moneda por línea, reconciliación con referrals/Codes (Growth), portal público de pago (lo sirve el frontend + PaymentClient link).

## Riesgos actuales

- Sin PDF render server-side confirmado en Scribe para el layout de invoice (Scribe existe pero el template de invoice no) → BDR/plan lo trata como fase con fallback.
- Correlación pago↔invoice depende del mapeo local hasta que PaymentClient eche el eco (BDR-001).
- El legado no calculaba impuestos; portar “tal cual” arrastra la deuda — se documenta explícitamente como decisión abierta (`16_Billing_Open_Questions.md`).

## Fuentes no disponibles

- No hay especificación previa de Billing en el repo (`src/Services/Billing/` no existe). Esta serie es la primera.
- El template de PDF de invoice en Scribe no existe todavía; el diseño asume su creación en la fase de implementación.
