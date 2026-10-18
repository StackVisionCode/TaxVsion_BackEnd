# Backfill — SaaSPayment.ReceiptFileId para onboardings históricos

**Cuándo**: ejecutar DESPUÉS del redeploy de PaymentApp con `OnboardingReceiptReadyConsumer`.
Antes del deploy no sirve: los pagos nuevos ya los cubre el consumer; esto solo recoge los históricos
cuyo `OnboardingReceiptReadyIntegrationEvent` ya se perdió (ya se consumió una vez sin handler).

**Por qué 2 pasos**: `SaaSPayments` vive en la DB de PaymentApp y `TenantOnboardings` en la DB de
Auth. No se puede hacer JOIN directo entre bases.

---

## Paso 1 — En la DB de Auth: extraer los pares (PaymentId, ReceiptFileId)

```sql
SELECT PaymentId, ReceiptFileId
FROM TenantOnboardings
WHERE PaymentId IS NOT NULL
  AND ReceiptFileId IS NOT NULL;
```

Guardá el resultado. Cada fila es un pago de onboarding que ya tiene PDF generado y attacheado
al TenantOnboarding, pero cuyo SaaSPayment aún no apunta a ese PDF.

---

## Paso 2 — En la DB de PaymentApp: UPDATE por cada fila del Paso 1

Opción A (una fila a la vez — más seguro, podés auditar):

```sql
UPDATE SaaSPayments
SET ReceiptFileId = '<ReceiptFileId del Paso 1>'
WHERE Id = '<PaymentId del Paso 1>'
  AND ReceiptFileId IS NULL;
```

Opción B (lote con VALUES — más rápido si hay muchos):

```sql
;WITH Pairs(PaymentId, ReceiptFileId) AS (
    VALUES
        ('<guid-pago-1>', '<guid-file-1>'),
        ('<guid-pago-2>', '<guid-file-2>')
        -- ... pegar los pares del Paso 1
)
UPDATE p
SET p.ReceiptFileId = pr.ReceiptFileId
FROM SaaSPayments p
INNER JOIN Pairs pr ON pr.PaymentId = p.Id
WHERE p.ReceiptFileId IS NULL;
```

El guard `ReceiptFileId IS NULL` hace la operación idempotente: si se corre dos veces, no sobrescribe.

---

## Verificación post-backfill

```sql
-- En Auth DB
SELECT COUNT(*) AS onboardings_con_receipt
FROM TenantOnboardings
WHERE PaymentId IS NOT NULL AND ReceiptFileId IS NOT NULL;

-- En PaymentApp DB
SELECT COUNT(*) AS pagos_onboarding_con_receipt
FROM SaaSPayments
WHERE Type = 8  -- OnboardingInitial
  AND ReceiptFileId IS NOT NULL;
```

Ambos conteos deben coincidir.
