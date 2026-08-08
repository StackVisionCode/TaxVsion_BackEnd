# Eventos, outbox/inbox y sagas

## Infraestructura

Auth, Growth, PaymentApp y Billing registran Wolverine con durable outbox para endpoints de salida y durable inbox en listeners RabbitMQ. Los eventos heredan `IntegrationEvent` con EventId/TenantId/correlation según contrato.

## Cadena crítica

`OnboardingPaymentSucceededIntegrationEvent` → Auth consumer → completion/finalize → `OnboardingInvoiceRequestedIntegrationEvent` → Billing consumer → `GenerateInvoicePdfCommand` → Documents → completion/backfill. C usa la misma finalización sin evento PaymentSucceeded.

### EVT-001 — atomicidad del publish en servicios

**MEDIUM/P1/Medium.** La política durable existe, pero debe verificarse que cada `PublishAsync` crítico participe del mismo DbContext/transaction que el cambio agregado; algunas publicaciones ocurren después de HTTP externos y otras después de `SaveChanges`.

### EVT-002 — poison message confirmado silenciosamente

**HIGH/P1/Small.** Billing retorna ante payload inválido. Debe fallar/mandar DLQ con métrica, no producir éxito sin invoice.

### SAG-001 — saga comercial implícita

**HIGH/P1/Large.** No hay un único estado `Reserved→Paid/Covered→CodesCommitted→InvoiceCreated→Provisioned→Completed`. Los hitos están repartidos y las compensaciones no cubren todas las permutaciones.

## Estados imposibles posibles

- Onboarding registration-ready con invoice ausente.
- Stripe session existente con SaaSPayment ausente.
- Uno de varios códigos reservado y onboarding sin referencias persistidas.
- Payment succeeded pero commit Growth fallido durante retry.
- Invoice Paid y posterior refund sin compensación equivalente en Growth.

