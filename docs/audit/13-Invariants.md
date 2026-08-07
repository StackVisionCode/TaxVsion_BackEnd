# Matriz de invariantes

| Invariante | DB | Código | Estado |
|---|---|---|---|
| Invoice.Total ≥ 0 | no CHECK visible | `CreateForOnboarding`/Money | CODE |
| net = gross − discount | no | factory | CODE |
| suma adjustments = discount | no | factory | CODE |
| net 0 ⇒ PaymentId null | no CHECK | factory/finalizer | CODE |
| net > 0 ⇒ PaymentId no null | no CHECK | factory | CODE |
| una invoice/onboarding | unique index | precheck | DB+CODE |
| un payment/onboarding | unique index | repository replay | DB+CODE |
| payment amount = invoice total | cross-DB imposible | payload/flow | NOT ENFORCED end-to-end |
| Gift balance ≥ 0 | counters/rowversion, verificar CHECK | aggregate | CODE/concurrency token |
| una redención por subject/regla | índices según entidad | domain | requiere prueba específica |
| onboarding Completed ⇒ TenantId | no evidencia DB cross-state | aggregate/saga | CODE |
| provisioning solo tras éxito comercial | no cross-service | orchestration | CODE/eventual |
| invoice creada antes de onboarding usable | no | no | NOT ENFORCED |
| event processed once | Wolverine inbox | durable inbox | DB+INFRA |
| request effect once | índices parciales | keys variables | PARTIAL |

Las invariantes cross-service son las más débiles: no pueden imponerse con FK y necesitan saga/reconciliation.

