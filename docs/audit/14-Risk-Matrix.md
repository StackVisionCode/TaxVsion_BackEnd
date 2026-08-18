# Matriz de riesgos

| ID | Severidad | Prob. | Impacto | Componentes | Prioridad |
|---|---|---:|---:|---|---|
| PAY-001 | HIGH | media | financiero/operativo | PaymentApp/Stripe | P1 |
| REL-001 | HIGH | media | trazabilidad | Auth/Billing | P1 |
| GRO-001 | HIGH | media | saldo/capacidad | Auth/Growth | P1 |
| PAY-002 | HIGH | baja-media | financiero | Auth/PaymentApp/Growth | P1 |
| BIL-003 | HIGH | baja | invoice ausente permanente | Billing/Messaging | P1 |
| TST-001 | HIGH | alta | defectos no detectados | todos | P1 |
| SEC-001 | HIGH | baja-media | cobro no autorizado | Auth/PaymentApp | P1 |
| SEC-003 | HIGH | desconocida | credenciales | repositorio/deploy | P0 investigar |
| TST-002 | HIGH | confirmada | ausencia de señal CI | Auth/tests | P0 |
| ARCH-001 | MEDIUM | media | aislamiento/recovery | pre-tenant | P1 |
| ONB-003 | MEDIUM | media | checkout fallido tardío | Auth/Growth | P2 |

No se confirmó una vulnerabilidad CRITICAL explotable ni doble cobro efectivo mediante ejecución. Los HIGH pueden escalar a CRITICAL si pruebas concurrentes confirman pérdida financiera o autorización M2M indebida.

## Catálogo normalizado de hallazgos

Cada entrada indica `ID — título — severidad — componentes — archivos/clases/métodos — escenario/impacto — solución — complejidad/prioridad`.

- **PAY-001 — sesión externa antes de persistencia — HIGH — PaymentApp/Stripe —** `CreateOnboardingCheckoutHandler.Handle`, `RecordSession`, `PersistAndAuditAsync`: Stripe confirma y la validación/DB falla; queda sesión no reconciliada. Persistir intención y reconciliar por key. **Large/P1**.
- **PAY-002 — neto confiado entre servicios — HIGH — Auth/PaymentApp/Growth —** `StartOnboardingCheckoutHandler`, `CreateOnboardingCheckoutHandler.PrepareNewPayment`: caller envía neto arbitrario dentro del rango; posible undercharge. Quote firmado o validación Growth. **Medium/P1**.
- **GRO-001 — stack no atómico — HIGH — Auth/Growth —** `OnboardingCodeReserver.ReserveAsync`: segunda reserva falla y primera queda bloqueada. Batch transaccional/compensación durable. **Medium/P1**.
- **REL-001 — onboarding sin invoice materializada — HIGH — Auth/Billing —** `OnboardingSuccessCompleter`, `OnboardingFinalizer`, `OnboardingInvoiceRequestedConsumer`: Billing caído/poison mientras registro avanza; pérdida temporal o permanente de trazabilidad. ACK/saga/reconciler. **Large/P1**.
- **BIL-001 — carrera de secuencia/alta — HIGH — Billing —** `OnboardingInvoiceRequestedConsumer.Handle`, `InvoiceNumberSequence.Allocate`: entregas concurrentes pasan precheck; conflicto/hueco. Transacción, locking y retry probado. **Medium/P1**.
- **BIL-003 — payload inválido descartado — HIGH — Billing/Messaging —** `OnboardingInvoiceRequestedConsumer.Handle`: parse/factory falla y retorna; no invoice. Lanzar poison/DLQ y alertar. **Small/P1**.
- **SEC-001 — autorización M2M genérica — HIGH — Auth/PaymentApp —** `PaymentAppOnboardingClient`, internal checkout controller/policy: otro service actor invoca checkout. Scope/audience/client allowlist. **Medium/P1**.
- **SEC-003 — archivo de entorno versionado — HIGH — Deploy/repo —** `.env.zip`: posible secreto histórico. Inspección segura, purge/rotation. **Small/P0**.
- **TST-001 — A–D sin E2E — HIGH — todos los dominios comerciales —** suites en `deploy/tests`: defectos distribuidos pasan inadvertidos. Harness real con fault injection. **Large/P1**.
- **TST-002 — test contract obsoleto — HIGH — Auth/tests —** `OnboardingPaymentSucceededConsumerTests.cs:90`: compilación `CS1061`; CI sin cobertura. Actualizar test/contrato. **Small/P0**.
