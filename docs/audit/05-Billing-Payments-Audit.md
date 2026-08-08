# Billing y Payments

## Invoice onboarding

`Invoice.CreateForOnboarding` exige owner/onboarding/plan, importes no negativos, descuento≤bruto, neto=bruto−descuento, suma de ajustes=descuento y coherencia settlement/payment. Crea una línea de plan, ajustes separados y nace `Issued/Paid` con `PaidAtUtc`. `InvoiceConfiguration` añade `rowversion`, índice único `OnboardingId` e invoice number por tenant.

### BIL-001 — carrera en secuencia/idempotencia

**HIGH/P1/Medium.** El consumer hace read-before-write (`GetByOnboardingId`) y asigna secuencia antes de `SaveChanges`. El índice único evita duplicado final, pero una entrega concurrente puede consumir/colisionar sequence y lanzar excepción; no se ve manejo local de conflicto. Verificar retry Wolverine y transacción del mismo DbContext.

### BIL-002 — settlement no monetario marcado como pago

**MEDIUM/P2/Small.** Invoice cero tiene `Status=Paid`, `PaymentId=null`, `PaymentMethod.Other`. El estado settled es correcto operacionalmente, pero “Paid/Other” pierde semántica. Añadir `Settled`/instrumentos y no inventar payment monetario.

### BIL-003 — eventos inválidos se descartan

**HIGH/P1/Small.** `OnboardingInvoiceRequestedConsumer` registra warning y `return` si settlement/adjustment no parsea o factory falla. Esto confirma el mensaje como procesado sin invoice. Lanzar error no transitorio/DLQ con alerta o persistir rechazo auditable.

## PaymentApp

`SaaSPayment` modela Pending/Processing/RequiresAction/Succeeded/Failed/Cancelled y refunds/chargebacks, con `rowversion` e índices únicos por idempotency/onboarding. Stripe es el proveedor del checkout inicial.

### PAY-001 — efecto externo antes de persistencia

**HIGH/P1/Large.** `CreateHostedCheckoutSessionAsync` ocurre antes de `payments.AddAsync/SaveChanges`. Si validación de referencia o DB falla, Stripe ya tiene sesión. El propio código lo reconoce. Persistir intent primero y reconciliar por Stripe idempotency key/webhook.

### PAY-002 — trust boundary del neto

**HIGH/P1/Medium.** Se consulta gross a Subscription, pero el neto se acepta del servicio Auth con validación de rango. Los demás campos de descuento no autorizan el precio. Usar quote firmado/consultar Growth.

## Invoice vs payment

En A/B/D invoice se crea después del payment exitoso. En C no hay payment. No existe `if total==0 skip invoice`; lo contrario está implementado explícitamente.

