# Billing — Estrategia de migración

Fecha: 2026-07-22

## Principio

El CRM legado es fuente para inventario y reconciliación de facturas históricas, **no** un modelo a copiar. La migración de datos es un `PRODUCTION_BLOCKER` separado de la readiness de diseño/MVP: se puede construir y probar Billing sin datos legados; importarlos es una fase aparte con su propio criterio.

## Fuentes legadas

Tablas en la BD de `CustomerService` (`ApplicationDbContext`): `InvoiceData` (+ columnas owned de Customer/Company/Discount), `Items`, `InvoicePDFSettings` (DbSet `ConfigSetting`), `PaymentReceipts`. Archivos: PDFs en `wwwroot/invoices` y blobs `Receipts/{year}/{MM}` en CloudProShield.

## Fases

1. **Inventario**: contar invoices por tenant (`CompanyId`), por estado, con/ sin recibo, con/sin PDF. Detectar números duplicados, montos inconsistentes (`Subtotal+TotalTax-Discount ≠ Total`), `Customer.TaxId` no-GUID.
2. **Clasificación**: separar borradores (descartar o migrar como `Draft`), emitidas/enviadas (migrar con estado), pagadas (migrar + recibo), canceladas (migrar como `Voided`).
3. **Mapeo de campos**:
   - `decimal`→cents (×100 con validación de redondeo; rechazar si pierde precisión inesperada).
   - `Status` string→`InvoiceStatus` (`"Draft"→Draft`, `"sent"→Sent`, `"paid"→Paid`, `"canceled"→Voided`).
   - `Number`→`InvoiceNumber` tal cual; **reservar la sequence** por tenant a partir de `max(seq)` extraído del número para no re-emitir colisiones.
   - `Customer.TaxId`(GUID)→`Customer_CustomerId`; `Customer.TaxId` real→null si no había uno legítimo.
   - `Company.Logo`(base64)→subir a CloudStorage, guardar `Issuer_LogoFileId`.
   - PDFs `wwwroot/invoices` + CloudProShield→re-subir a CloudStorage, guardar `PdfFileId`/`PaidPdfFileId`.
   - `PaymentReceipt`→`billing.PaymentReceipts` conservando `VerificationHash` (¡no recalcular! el hash legado debe seguir validando).
4. **Shadow / validación**: importar a una BD Billing de staging; correr verificación de hashes de recibos, cuadre de totales, unicidad de números por tenant.
5. **Import**: carga idempotente por `(TenantId, legacy InvoiceId)`; mapear identidades a las del CRM nuevo (CustomerId real vía Customer service).
6. **Cutover**: congelar escritura en el legado, importar delta, apuntar el frontend a `/billing/*`.

## Exclusiones

- Eventos colgados legados (`PaymentReceiptClientEmailEvent`, `InvoicePaymentCompletedEvent`) no se migran.
- Adjuntos de invoice (`InvoiceAttachment`) — no estaban poblados; no se migran.
- Tokens/estado de IntelliPay en PaymentService — no se migran (PaymentClient es el nuevo owner de cobros; los cobros históricos ya cerrados quedan en el recibo).

## Criterios de salida

- 100% de invoices legadas por tenant importadas o explícitamente descartadas con motivo.
- Todos los `VerificationHash` de recibos migrados validan (`ValidateHash()==true`).
- Suma de totales por tenant cuadra legado vs. Billing dentro de tolerancia 0.
- La sequence por tenant queda por encima del máximo número histórico (sin riesgo de re-emisión).

## Nota

Mientras no se ejecute la migración, Billing opera solo con facturas nuevas — perfectamente válido para lanzar la capacidad a tenants nuevos o como opt-in. La existencia de datos legados es `PRODUCTION_BLOCKER` solo para tenants que exigen continuidad histórica.
