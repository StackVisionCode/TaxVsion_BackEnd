# Billing — Modelo de datos

Fecha: 2026-07-22

Convenciones: PKs `uniqueidentifier` app-assigned; timestamps `datetime2(7)` en UTC; texto `nvarchar`; dinero `bigint AmountCents` + `char(3) Currency`; porcentaje `int BasisPoints`; hashes `char(64)`; concurrencia `rowversion`; enums a `nvarchar` vía `HasConversion<string>()`. Esquemas: `billing` (dominio), `integration` (outbox/inbox Wolverine), `audit`. Sin FKs cross-schema ni cross-service.

DbContext: `TaxVision.Billing.Infrastructure/Persistence/BillingDbContext.cs`; factory `BillingDbContextFactory.cs`; esquemas en `BillingSchemas.cs`.

## Tablas

| Tabla | PK | FK internas | Datos principales | Sensible / retención |
|---|---|---|---|---|
| `billing.Invoices` | `Id` | — (Lines/PaymentLinks por owned/child) | `TenantId`, `InvoiceNumber` (null en Draft), `Status`, `IssueDateUtc`, `DueDateUtc`, `SentAtUtc?`, `PaidAtUtc?`, totales (`SubtotalCents`,`TaxTotalCents`,`DiscountTotalCents`,`TotalCents`,`AmountPaidCents`,`AmountDueCents`,`Currency`), `PoNumber?`, `Summary?`, `Notes?`, `PaymentMethod?`, `PdfFileId?`, `PaidPdfFileId?`, snapshots Customer/Issuer (owned, columnas inline), `Discount` (owned), auditoría, `RowVersion`, `DeletedAtUtc?` | Datos de cliente snapshot (PII); retención según política fiscal del tenant |
| `billing.InvoiceLineItems` | `Id` | `InvoiceId` (cascade) | `Description`, `Quantity`, `UnitAmountCents`, `TaxBasisPoints`, `TaxAmountCents`, `LineTotalCents`, `Currency` | — |
| `billing.InvoicePaymentLinks` | `Id` | `InvoiceId` (cascade) | `PaymentSource`, `PaymentId`, `Status`, `PayUrl?`, `CreatedAtUtc` | correlación BDR-001; índice `(PaymentSource, PaymentId)` |
| `billing.PaymentReceipts` | `Id` | — (referencia lógica a `InvoiceId`) | `TenantId`, `ReceiptNumber`, `InvoiceId`, `InvoiceNumber`, snapshot cliente (owned), `AmountPaidCents`, `Currency`, `PaymentMethod`, `PaymentReference`, `PaymentDateUtc`, `IssuedDateUtc`, `VerificationHash` (char 64), `Status`, `Notes?`, `PdfFileId?`, `ProcessedByUserId?`, auditoría, `RowVersion` | hash de verificación pública; PII snapshot |
| `billing.TenantBillingSettings` | `Id` | — | `TenantId` (único), `IssuerSnapshot` default (owned), `Template`, `PageSize`, `Orientation`, `ShowLogo`, `ShowFooter`, `ShowAttachments`, `NumberPrefix`, `ResetPolicy`, auditoría, `RowVersion` | — |
| `billing.InvoiceNumberSequences` | `Id` | — | `TenantId`, `PeriodKey`, `Next` (bigint), `RowVersion` | índice único `(TenantId, PeriodKey)` |
| `billing.ProcessedBusinessMessages` | `Id` | — | idempotencia app-layer: `(TenantId, Operation, IdempotencyKey)` único, `PayloadFingerprint` (char 64), `Status`, `ResponseContentType`, `ResponseJson`, `CreatedAtUtc`, `CompletedAtUtc?` | patrón `SqlBusinessIdempotencyExecutor` de Growth |
| `integration.*` | — | — | outbox/inbox durable de Wolverine (`PersistMessagesWithSqlServer(..., BillingSchemas.Integration)`) | gestionado por Wolverine |
| `audit.AuditEntries` | `Id` | — | rastro de acciones (invoice issued/paid/voided, settings changed) | append-only |

## Índices y unicidad (detalle en `14_…` si se numera aparte; resumen aquí)

- `billing.Invoices`: único `(TenantId, InvoiceNumber)` (filtrado `WHERE InvoiceNumber IS NOT NULL`); índice `(TenantId, Status)`; índice `(TenantId, CustomerId)` (columna owned del snapshot); índice `(TenantId, DueDateUtc)` para overdue.
- `billing.InvoicePaymentLinks`: índice `(PaymentSource, PaymentId)` (correlación de eventos de pago).
- `billing.PaymentReceipts`: único `(TenantId, ReceiptNumber)`; índice `(TenantId, InvoiceId)`; índice `VerificationHash` (verificación pública).
- `billing.InvoiceNumberSequences`: único `(TenantId, PeriodKey)`.
- `billing.ProcessedBusinessMessages`: único `(TenantId, Operation, IdempotencyKey)`.

## Owned types (snapshots congelados, columnas inline)

- `Invoice.Customer` → columnas `Customer_CustomerId`, `Customer_Name`, `Customer_Email`, `Customer_Phone`, `Customer_TaxId`, `Customer_AddrLine1/Line2/City/State/Zip/Country`.
- `Invoice.Issuer` → `Issuer_Name`, `Issuer_AddrLine1/…`, `Issuer_Phone`, `Issuer_Email`, `Issuer_Website`, `Issuer_LogoFileId`.
- `Invoice.Discount` → `Discount_Type`, `Discount_Value` (bps o cents), `Discount_AmountCents`.
- `PaymentReceipt.Customer` → subconjunto (`CustomerId`, `Name`, `Email`).
- `TenantBillingSettings.DefaultIssuer` → mismas columnas que `Issuer_*`.

Precisión monetaria: **todo en `bigint` cents** — se elimina el uso de `decimal(18,2)`/`decimal(18,4)` del legado (y su migración de "fix precision"). Porcentajes en `int` basis points (elimina `decimal` de descuentos).

## Diferencias con el modelo legado (`CustomerService`)

| Legado | Nuevo | Motivo |
|---|---|---|
| `InvoiceData.Number` client-supplied, único | `InvoiceNumber` server-assigned vía `InvoiceNumberSequences` | evita colisión y suplantación |
| `Status` string libre | `InvoiceStatus` enum a `nvarchar` | máquina de estados verificable |
| `decimal` (Subtotal/Total/Tax/Discount) | `bigint` cents + `char(3)` currency | precisión, convención de plataforma |
| `Customer.TaxId` guardando el GUID del cliente | `Customer_CustomerId` (uniqueidentifier) + `Customer_TaxId` real | corrige el abuso |
| `InvoiceCompanyInfo.Logo` base64 inline | `Issuer_LogoFileId` (ref CloudStorage) | no inflar la fila |
| PDF en `wwwroot/invoices` / base64 en eventos | `PdfFileId`/`PaidPdfFileId` (CloudStorage) | almacenamiento centralizado |
| `InvoicePDFSettings` doblando como per-invoice y per-company | per-invoice hereda de `TenantBillingSettings` (per-tenant) | separación clara |
