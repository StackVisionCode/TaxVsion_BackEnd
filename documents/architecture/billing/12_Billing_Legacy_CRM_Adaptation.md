# Billing — Adaptación del CRM legado (CRMTAXPROBACKEND → TaxVsion_BackEnd)

Fecha: 2026-07-22

Traza componente por componente cómo el módulo de invoices del CRM legado se adapta al CRM nuevo. Fuente legada verificada: `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND`.

## Principio de adaptación

El legado es **fuente de requisitos y de datos a reconciliar**, no un modelo a copiar. Cada pieza se re-expresa en las convenciones del CRM nuevo (DDD + Wolverine + EF + RabbitMQ, DB-per-service, `Money(long Cents)`, Result/Error, outbox/inbox, JWT). Los defectos conocidos del legado se corrigen explícitamente.

## Mapa de componentes

| Legado (ubicación) | Nuevo (Billing) | Tipo de cambio |
|---|---|---|
| `CustomerService/Domains/Invoice/InvoiceData.cs` | `Invoice` aggregate (`Domain/Invoices/`) | reescritura DDD (status enum, Money, snapshots limpios) |
| `Domains/Invoice/Item.cs` | `InvoiceLineItem` (entidad interna) | `decimal`→`Money`, tax en bps |
| `Domains/Invoice/Discount.cs` (owned) | `Discount` VO | `decimal`→bps/cents |
| `Domains/Invoice/InvoiceCompanyInfo.cs` (owned, logo base64) | `IssuerSnapshot` VO (logo `FileId`) | logo a CloudStorage ref |
| `Domains/Invoice/InvoicePDFSettings.cs` (DbSet `ConfigSetting`) | `TenantBillingSettings` aggregate | separa per-tenant de per-invoice |
| `Domains/Invoice/PaymentReceipt.cs` | `PaymentReceipt` aggregate | mantiene hash verificable; Result/Error |
| `Domains/Invoice/InvoiceAttachment.cs` (declarado, sin uso) | flag `ShowAttachments` + `FileId`s futuros | diferido |
| `Applications/Handlers/InvoicesHandles/*` (MediatR) | slices `Application/Invoices/*` (Wolverine) | inyección por método, Result |
| `Applications/Handlers/PaymentReceipts/*` | `Application/Receipts/*` | idem |
| `Controllers/Invoices/InvoiceController.cs` (sin `[Authorize]`) | `Api/Controllers/InvoicesController.cs` (`[Authorize]`+permiso) | seguridad corregida |
| `Controllers/PaymentReceipts/PaymentReceiptsController.cs` | `Api/Controllers/ReceiptsController.cs` | igual, `verify` anónimo |
| `Infrastructure/Services/InvoicePdfService.cs` (QuestPDF, 4 templates, in-proc) | Scribe (render) + CloudStorage (guardar) | delega presentación |
| `Infrastructure/Services/PaidInvoiceWatermarkService.cs` (disco) | watermark en el render de Scribe; PDF a CloudStorage | delega |
| `Infrastructure/Services/InvoiceAggregatorService.cs` (`.Result` bloqueante) | orquestación async en el handler `SendInvoice`/`RecordPayment` | corrige anti-patrón |
| `ICloudProShieldClient` (blob legado) | CloudStorage M2M | reemplazo de storage |
| `EmailServices/*` (consumía eventos de email) | Notification/Postmaster (consume `billing.*`) | reemplazo de notificación |
| `PaymentService` (IntelliPay token/callback) | PaymentClient (`PaymentPurposeKind.InvoicePayment`) | reemplazo de gateway |

## Corrección de defectos del legado (checklist)

| ID | Defecto legado | Evidencia legada | Corrección en Billing |
|---|---|---|---|
| LGC-01 | `InvoiceController` sin `[Authorize]`; tenant por `companyId` query | `Controllers/Invoices/InvoiceController.cs` | `[Authorize]` + `billing.view/manage`; tenant del JWT |
| LGC-02 | Número de invoice client-supplied con índice único (colisión/suplantación) | `InvoiceData.Number` + `HasIndex(...).IsUnique()` | `InvoiceNumberSequence` server-side |
| LGC-03 | `Status` string libre comparado ad hoc | `InvoiceData.Status` | `InvoiceStatus` enum + máquina de estados |
| LGC-04 | Punto flotante `decimal` | totales/`Item.Tax`/`Discount` | `Money(long Cents)` + bps |
| LGC-05 | `Customer.TaxId` usado para guardar el GUID del cliente | `Guid.Parse(invoice.Customer.TaxId)` | `Customer_CustomerId` explícito |
| LGC-06 | Logo en base64 inline en la fila | `InvoiceCompanyInfo.Logo` | `LogoFileId` (CloudStorage) |
| LGC-07 | `GetAllInvoicesHandler` calcula paginación pero no la aplica | `GetAllInvoiceQueries` | `Skip/Take` real (UC-12) |
| LGC-08 | `GET /SendEmail/{id}` (GET con efecto de escritura) | `InvoiceController` | `POST /{id}/send` |
| LGC-09 | `.Result` bloqueante en `InvoiceAggregatorService` | ese servicio | orquestación async |
| LGC-10 | Email enviado por acoplamiento de eventos ad hoc a EmailServices | `LandingInvoicesSend*`/`InvoicePaid*Event` | eventos `billing.*` → Notification |
| LGC-11 | Sin outbox/inbox: eventos publicados sin garantía transaccional | `IEventBus.Publish` directo | outbox durable (Wolverine) al `SaveChanges`; inbox durable al consumir |
| LGC-12 | Sin idempotencia formal en el consumo del "paid" | `UpdateInvoicePaidHandlers` chequeaba "already paid" a mano | `IBillingIdempotencyExecutor` + idempotencia por `PaymentReference` |
| LGC-13 | `PaymentReceiptClientEmailEvent`/`InvoicePaymentCompletedEvent` colgados | `SharedLibrary/DTOs/` | no portados; recibo vía `billing.receipt.issued` |

## Qué se preserva del legado (buenas decisiones)

- **Snapshot congelado** de cliente y emisor en la invoice (auditoría histórica correcta) — se mantiene, limpiando el abuso de `TaxId`.
- **Recibo con hash SHA-256 auto-verificable** y verificación pública anónima — se mantiene el concepto y el algoritmo (`ReceiptNumber|InvoiceId|PaymentReference|Amount|PaymentDate`).
- **Dos caminos de pago** (manual y online) — se mantienen: manual local (UC-05), online vía PaymentClink + evento (UC-04/UC-06).
- **Watermark "Paid"** sobre el PDF — se mantiene, ahora en el render de Scribe.
- **Config de PDF por tenant** (template/tamaño/orientación/logo/pie) — se mantiene en `TenantBillingSettings`.
- **Numeración de recibo server-side** (`RCP-…`) — ya era server-side en el legado; se mantiene.

## Datos a migrar (resumen; detalle en `14_Billing_Migration_Strategy.md`)

Tablas legadas fuente: `InvoiceData` (+ owned Customer/Company/Discount), `Items`, `InvoicePDFSettings`, `PaymentReceipts`. Conversión: `decimal`→cents (×100 con control de redondeo), `Status` string→enum, `Number`→cargar tal cual pero reservar la sequence a partir del máximo por tenant, `Customer.TaxId`(GUID)→`CustomerId`, `Logo`(base64)→subir a CloudStorage y guardar `FileId`, PDFs de `wwwroot/invoices` y blobs CloudProShield→re-subir a CloudStorage.
