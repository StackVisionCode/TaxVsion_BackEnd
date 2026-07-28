# Billing — Backlog priorizado de mejoras

Auditoría: arquitecto principal. Fecha: 2026-07-22.
Prioridad: P0 (corrupción financiera/cross-tenant o impide scaffolding) · P1 (impide integración/producción segura) · P2 (operabilidad/mantenibilidad) · P3 (futuro).
Servicio afectado: B=Billing, PC=PaymentClient, SC=Scribe, CS=CloudStorage, CU=Customer, NO=Notification, AU=Auth.

## P0 — antes de implementar el dominio / no negociable

| # | Ítem | Servicio | Esfuerzo | Dependencia | Criterio de aceptación |
|---|---|---|---|---|---|
| P0-1 | Añadir `Refunded`/`PartiallyRefunded` a `InvoiceStatus`; separar `AmountRefunded` de `AmountDue` | B | M | — | Reembolso total → `Refunded`; parcial → `PartiallyRefunded`; `AmountDue` no reabre (T-14) |
| P0-2 | Definir pago-tras-void (refund automático + `payment_after_void` + alerta) | B | M | P0-1 | Test T-15 verde |
| P0-3 | Definir sobrepago (`amount > AmountDue` → rechazo/alerta) | B | S | — | Test T-13 verde |
| P0-4 | Recálculo server-side de subtotal/impuesto/total desde componentes; ignorar totales del caller | B | L | — | Test T-17 verde; caller no puede fijar `Total` |
| P0-5 | Validar `TenantId`+monto+moneda en el consumer de pago | B | S | P1-1 | Test T-20/T-21 verde |
| P0-6 | PaymentClient publica `payments.*` (succeeded/failed/refunded/cancelled/chargeback) | PC | L | — | PC-ISSUE-01; consumido por Billing y Growth |
| P0-7 | Motor HTML→PDF tras `IInvoiceDocumentService` (Scribe no hace PDF) | B (+SC) | L | — | Factura/recibo renderizados a PDF y almacenados; test E2E |

## P1 — antes de integración / producción segura

| # | Ítem | Servicio | Esfuerzo | Dependencia | Criterio de aceptación |
|---|---|---|---|---|---|
| P1-1 | `IInvoiceDocumentService` (interfaz en App) + impl Scribe(HTML)+HTML→PDF+CloudStorage | B | L | P0-7 | Billing.Application no ve bytes; guarda solo `FileId`/estado/versión |
| P1-2 | Cliente MinIO + `SaveFileRequestedIntegrationEvent` + IAM temp-bucket `billing/*` | B (+CS) | M | P1-1 | PDF almacenado con `OwnerType.Invoice`+`FolderType.Invoices`+`TaxYear` |
| P1-3 | Endpoint M2M en PaymentClient para crear/revocar PaymentLink (service-scope) | PC | M | — | PC-ISSUE-04; Billing crea/revoca con su token de servicio |
| P1-4 | Registrar cliente M2M outbound de Billing en Auth (`ServiceAuth__Clients__N`) + token acquirer | B (+AU) | M | — | Billing obtiene token para Scribe/CloudStorage/Customer/PaymentClient |
| P1-5 | Endpoint M2M `GET customers/internal/{id}` con tax-id + dirección (o evento enriquecido) | CU | M | — | `CustomerSnapshot` completo; sin exponer a humanos indebidamente |
| P1-6 | `SendInvoice` como saga con `InvoiceDeliveries` + pasos idempotentes + compensación (revoke) | B | L | P1-1,P1-3 | Test T-10/T-11 verde; sin doble link ni estado inconsistente |
| P1-7 | Correlación por `PaymentLinkId` + `InvoicePaymentLinks` enriquecido (Tenant/ExpectedAmount/Currency) | B | M | P0-6/P1-3 | Test T-19/T-20/T-21 verde |
| P1-8 | `BillingAuthorizationPolicyProvider` + `[HasPermission("billing.*")]` en todos los endpoints | B | S | — | Test de autorización; SEC-05 cerrado |
| P1-9 | `Money`→`MinorUnits` + exponente ISO-4217 | B | M | — | Test T-18 (JPY/BHD) verde |
| P1-10 | `RecordPayment` (Alternativa A): handler crea el recibo; clave de idempotencia canónica | B | M | P0-1 | Test T-03/T-04/T-05 verde |
| P1-11 | Consumer de Notification para `billing.*` + plantilla/EventKey de invoice en Scribe | NO (+SC) | M | P0-7 | Email al cliente con link de descarga; `NotificationCategory.Billing` |
| P1-12 | Endurecer `verify` público: token opaco + rate limit + respuesta mínima + tiempo constante | B | M | — | Test T-23/T-09 verde |
| P1-13 | Global query filter por tenant (fail-closed) + ownership-404 en todos los repos | B | S | — | Test T-07/T-08 verde |
| P1-14 | Migración: rechazar import con inconsistencia monetaria; conservar hashes | B | M | — | Test T-22 verde |
| P1-15 | Purpose/InvoiceId + ProviderEventId/AttemptId en el envelope de pago | PC | M | P0-6 | PC-ISSUE-02/03; correlación directa por `InvoiceId` |
| P1-16 | Migración EF inicial de Billing (`InitialBilling`) | B | M | P0-1,P1-9 | `dotnet ef database update` crea el esquema; apply-script aplica |

## P2 — operabilidad, resiliencia, mantenibilidad

| # | Ítem | Servicio | Esfuerzo | Criterio |
|---|---|---|---|---|
| P2-1 | Numeración `UPDLOCK/HOLDLOCK` upsert+increment | B | M | Test T-01/T-02 verde |
| P2-2 | Métricas completas (`BillingMetrics`) + dashboards + alertas | B | M | Métricas del prompt emitidas |
| P2-3 | `InvoiceDeliveryStatus` separado de `InvoiceStatus` | B | S | Máquina comercial sin estados técnicos |
| P2-4 | `mark-as-paid`→`payments/manual`; versionado `/v1`; ETag/If-Match | B | S | API consistente |
| P2-5 | Job de reconciliación (links usados sin pago, pagos a factura void) | B | M | `05` §6 implementado |
| P2-6 | Corregir docs 06/07/08/09/10/11/01 (contradicciones) | B | S | `02_Contradictions` resuelto |
| P2-7 | Reemplazar `FileType.Invoice` por `OwnerType.Invoice`+`FolderType.Invoices`+`TaxYear` en docs | B | S | Docs alineados al código |
| P2-8 | Suite de tests completa (matriz `08`) | B | L | Cobertura + casos críticos verdes |

## P3 — futuro

| # | Ítem | Servicio | Criterio |
|---|---|---|---|
| P3-1 | `DocumentService` M2M (Alternativa C) sustituye la impl de `IInvoiceDocumentService` | nuevo | Sin tocar `Billing.Application` |
| P3-2 | Nota de crédito como aggregate propio | B | Correcciones fiscales formales |
| P3-3 | Recurrencia de facturas (integración con PaymentClient recurring) | B (+PC) | Facturación periódica |
| P3-4 | `Catalog.ResolvePrice` como autoridad de precio server-side | nuevo (+B) | Billing solo snapshotea precio |
| P3-5 | Integración descuentos Growth (Codes) sobre invoices | B (+Growth) | Cupón/referido en factura |
| P3-6 | Adjuntar PDF al email (cuando Notification Phase-4 lo soporte) | NO (+B) | Adjunto real en vez de link |
| P3-7 | Refund iniciado por Billing (PC-ISSUE-05) | PC (+B) | Billing solicita reembolsos |

## Ruta crítica

`P0-6 (PaymentClient publica) → P1-3 (M2M link) → P1-7 (correlación)` habilita el ciclo de pago.
`P0-7 (HTML→PDF) → P1-1/P1-2 (IInvoiceDocumentService+CloudStorage) → P1-11 (Notification)` habilita la entrega.
`P0-1..P0-5 + P1-9/P1-10` cierran el dominio financiero. `P1-16` habilita la persistencia real.
