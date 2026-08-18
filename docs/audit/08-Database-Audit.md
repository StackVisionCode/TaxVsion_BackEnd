# Auditoría de base de datos

Cada servicio usa DbContext/migraciones propias sobre SQL Server. Se observaron query filters de tenant, claves/índices únicos, owned collections, `rowversion`, soft-delete/timestamps y secuencias de invoice.

## Garantías relevantes

| Invariante | Garantía DB observada |
|---|---|
| una invoice por onboarding | unique filtered index `Invoices.OnboardingId` |
| invoice number por tenant | unique `(TenantId, InvoiceNumber)` |
| payment por onboarding | unique filtered index en `SaaSPayments.OnboardingId` |
| idempotency payment | unique `IdempotencyKey` |
| reservas/quotes Growth | índices tenant+idempotency y rowversion |

### DB-001 — checks financieros solo en código

**MEDIUM/P2/Medium.** `net=gross-discount`, suma de ajustes y coherencia payment/settlement viven en factory; columnas Money serializadas no tienen CHECK relacional útil. Escrituras/migraciones fuera del aggregate podrían violarlas.

### DB-002 — concurrencia necesita manejo explícito

**HIGH/P1/Medium.** `rowversion` detecta carreras, pero detectar no equivale a resolver. Los handlers críticos deben retry/reload o devolver conflicto de negocio probado.

### DB-003 — rehome pre-tenant

**MEDIUM/P2/Medium.** Cambiar TenantId de invoice requiere que líneas owned y filtros mantengan visibilidad; falta prueba DB que simule crash/retry durante backfill.

