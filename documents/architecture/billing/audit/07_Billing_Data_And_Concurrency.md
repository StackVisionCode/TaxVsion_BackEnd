# Billing — Datos, concurrencia y numeración

Auditoría: arquitecto principal (SQL Server / concurrencia). Fecha: 2026-07-22.
Evidencia del repo: `SqlBusinessIdempotencyExecutor` + `ProcessedBusinessMessage` (Growth), `SqlReferralRewardQuota` (patrón de contador concurrente), Wolverine outbox/inbox en schema `integration`.

## 1. Esquema (revisión)

El modelo de `10_Data_Model.md` es correcto en estructura; correcciones requeridas por la auditoría:

| Corrección | Motivo |
|---|---|
| `AmountCents` → `AmountMinorUnits` + exponente por moneda | ISO-4217 0/2/3 decimales (C-13) |
| Agregar `AmountRefunded` a `Invoices` | separar reembolso de saldo (C-01/C-02) |
| `InvoiceStatus` + `Refunded`, `PartiallyRefunded` | estado fiscal correcto (C-01) |
| `InvoicePaymentLinks`: agregar `TenantId`, `ExpectedAmountMinorUnits`, `Currency`, `PurposeKind`, `PaymentLinkId` (no solo `PaymentId`) | correlación segura + validación de monto/tenant (SEC-01, BDR-001) |
| Nueva tabla `InvoiceDeliveries` (estado técnico de la saga de envío) | separar entrega de `InvoiceStatus` (C-04) |
| `ProcessedBusinessMessages` (idempotencia) — replicar de Growth | idempotencia app-layer |
| `Invoices`: quitar `FileType.Invoice`; usar `OwnerType.Invoice`+`FolderType.Invoices`+`TaxYear` al almacenar | `FileType.Invoice` NO existe en CloudStorage |

Claves/índices confirmados como correctos: único filtrado `(TenantId, InvoiceNumber) WHERE InvoiceNumber IS NOT NULL`; único `(TenantId, ReceiptNumber)`; índice `(PaymentSource, PaymentId)` **+ nuevo** `(PaymentLinkId)` en `InvoicePaymentLinks`; único `(TenantId, PeriodKey)` en `InvoiceNumberSequences`; único `(TenantId, Operation, ScopeId, IdempotencyKey)` en `ProcessedBusinessMessages` (patrón exacto de Growth).

Owned types, `RowVersion` (`.IsRowVersion()`), soft-delete solo en `Draft`, `audit.AuditEntries` append-only, hashes `char(64)`: correctos.

## 2. Numeración `InvoiceNumberSequence` — comparación de opciones

Evidencia: **el repo NO usa `SQL SEQUENCE` en ningún lado** (`HasSequence`/`CREATE SEQUENCE`/`NEXT VALUE FOR` → NOT_FOUND). El patrón de la casa para contadores concurrentes es `UPDLOCK/HOLDLOCK` + upsert + increment (`SqlReferralRewardQuota.TryReserveAnnualSlotAsync`).

| Opción | Unicidad por tenant/período | Contención | "Quema" número si falla txn | Alineado al repo | Veredicto |
|---|---|---|---|---|---|
| RowVersion + retry (diseño actual) | sí (con índice único) | tormenta de reintentos bajo alta concurrencia | no (optimista) | parcial | aceptable pero subóptimo |
| **`UPDLOCK, HOLDLOCK` upsert+increment** | sí | serializa por `(TenantId, PeriodKey)`, sin reintentos | no (dentro de la misma txn) | **sí (patrón `SqlReferralRewardQuota`)** | **recomendado** |
| `UPDATE … OUTPUT INSERTED.Next` | sí | bueno; requiere fila preexistente | no | sí (variante del anterior) | recomendado (combinar con upsert) |
| SQL Server `SEQUENCE` | no por-tenant (una secuencia global o N objetos) | excelente | **sí** (quema valores) | **no** (sin precedente en el repo) | rechazado (gaps + no encaja multi-tenant/período) |
| Stored procedure | sí | bueno | depende | no (el repo no usa SPs para esto) | innecesario |
| Hi/Lo | sí (con gaps por bloque) | excelente | sí (gaps) | no | rechazado (gaps inaceptables en numeración fiscal) |

### Recomendación: `UPDLOCK, HOLDLOCK` upsert-then-increment, dentro de la transacción del `Issue`

Réplica del patrón verificado en `SqlReferralRewardQuota`:
```sql
-- 1) asegurar la fila del contador sin fantasmas
INSERT INTO billing.InvoiceNumberSequences (Id, TenantId, PeriodKey, Next, RowVersion)
SELECT @id, @tenant, @period, 0, DEFAULT
WHERE NOT EXISTS (
  SELECT 1 FROM billing.InvoiceNumberSequences WITH (UPDLOCK, HOLDLOCK)
  WHERE TenantId=@tenant AND PeriodKey=@period);
-- 2) reservar el siguiente número atómicamente
UPDATE billing.InvoiceNumberSequences WITH (UPDLOCK, ROWLOCK)
SET Next = Next + 1
OUTPUT INSERTED.Next
WHERE TenantId=@tenant AND PeriodKey=@period;
```
- Debe correr **dentro de una transacción ambiental** (como `SqlReferralRewardQuota` exige `CurrentTransaction is not null`), la misma que persiste el `Issue`. El índice único `(TenantId, InvoiceNumber)` es la red de seguridad final.
- El número se **formatea** a partir del `Next` devuelto (`{Prefix}-{yyyyMMdd|yyyy}-{Next:D4}`), no del `Next` crudo.

## 3. Escenarios de numeración (definidos)

| Escenario | Comportamiento definido |
|---|---|
| Dos solicitudes emiten simultáneamente | `UPDLOCK/HOLDLOCK` serializa; una obtiene N, la otra N+1; 0 duplicados |
| Se reserva un número y la transacción falla | El `UPDATE` está en la misma txn del `Issue`; si el `Issue` falla, el increment se revierte (no se quema el número). *(Con SEQUENCE se quemaría — otra razón para rechazarla.)* |
| Cambia el año durante una emisión | `PeriodKey` se calcula al inicio de la txn desde `nowUtc`; una emisión en el borde usa el `PeriodKey` de su `nowUtc`; sin números mezclados |
| Se importa una factura histórica | La migración **no** usa la sequence para el número (conserva el legado); pero **avanza** `Next` al máximo importado por `(tenant, period)` para no re-emitir colisiones |
| Cambia el prefijo (`TenantBillingSettings.NumberPrefix`) | El prefijo es presentación; el contador es por `(TenantId, PeriodKey)` independiente del prefijo. Cambiar prefijo no reinicia el contador (salvo que se combine con `ResetPolicy`) — documentar |
| Existe un número manual legado | La unicidad `(TenantId, InvoiceNumber)` lo protege; si un import trae un número que colisiona con uno futuro de la sequence, la migración lo detecta y avanza/aparta (ver `14_Migration`) |

## 4. Idempotencia (replicar `SqlBusinessIdempotencyExecutor`)

Billing necesita `SqlBillingIdempotencyExecutor` + `ProcessedBillingMessage` idénticos al patrón de Growth:
- Insert-first: `Begin(...)` inserta el claim (`(TenantId, Operation, ScopeId, IdempotencyKey)` único); colisión → replay del `ResponseJson` si el `RequestFingerprint` (SHA-256) coincide; si no coincide → `FingerprintConflict` (→ HTTP 409, C-04/T-04).
- Estados `Processing/Completed/Failed`; `Processing` concurrente → `OperationInProgress`.
- Corre dentro de la txn de Wolverine con savepoint si ya hay txn ambiental (para `RecordPayment` que además crea el recibo — Alternativa A, un `SaveChanges`, dos raíces).
- Retención/cleanup por índice `(Status, ExpiresAtUtc)`.

## 5. Concurrencia por aggregate

- `Invoice`, `PaymentReceipt`: `RowVersion` optimista → conflicto = `Billing.*.Concurrency` (HTTP 409).
- `RecordPayment` (Alternativa A): el handler abre la txn; `Invoice` (update) + `PaymentReceipt` (insert) + claim de idempotencia en el mismo `SaveChanges`; los eventos de integración se encolan al outbox en esa txn.
- Eventos de pago concurrentes/duplicados: dedupe por `PaymentLinkId`/`EventId`/`ProviderEventId` **antes** de mutar (patrón Growth `event:{EventId:N}`).

## 6. Retención, PII, cifrado, archivado

- **Retención fiscal**: facturas/recibos son documentos con retención larga (definir por jurisdicción; los `FolderType.Invoices/Receipts` de CloudStorage ya exigen `TaxYear`, útil para políticas por año).
- **PII**: `CustomerSnapshot`/`IssuerSnapshot` (email/tel/taxid/dirección) en reposo; evaluar cifrado de columna para TaxId/email según política; `ToString()` redacted en records.
- **Soft-delete**: solo `Draft`; facturas emitidas nunca se borran físicamente (inmutabilidad fiscal).
- **Archivado**: los PDFs viven en CloudStorage (no en la BD de Billing); Billing guarda solo `FileId`+versión+estado.
- **Outbox/Inbox**: tablas Wolverine (`wolverine_incoming_envelopes`/`wolverine_outgoing_envelopes`/`wolverine_dead_letters`) en schema `integration`, **gestionadas por Wolverine** (no por migraciones EF); cleanup por su propia política de retención.

## 7. Estado del scaffold vs. este diseño

- `InvoiceNumberSequence` scaffoldeado como `TenantEntity (TenantId, PeriodKey, Next, RowVersion)` — correcto; falta implementar `Allocate` con `UPDLOCK/HOLDLOCK` (B2).
- **No hay migración EF de Billing todavía** (`apply-migrations.sh` ya lista Billing pero hará no-op hasta la migración inicial). Es esperado en B1; bloquea B2.
- `BillingDbContext` sin DbSets de dominio aún (B2) — correcto para B1.
