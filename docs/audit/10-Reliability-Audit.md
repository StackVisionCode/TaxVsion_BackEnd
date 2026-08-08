# Confiabilidad, fallos y observabilidad

La solución incluye circuit breakers, durable messaging, retries/jobs, health checks y stack OTEL/Tempo/Loki/Prometheus/Grafana. CorrelationId, OnboardingId, TenantId, PaymentId, InvoiceId y reservation IDs aparecen en contratos/logs.

## Fault analysis

| Falla | Estado actual probable | Recuperación |
|---|---|---|
| Growth down antes de reserva | checkout no inicia | retry cliente; reservas previas del stack pueden quedar |
| PaymentApp/Stripe down | no checkout | circuit breaker/retry seguro solo con misma key |
| DB falla después de Stripe | sesión externa sin registro | webhook/reconciliación no demostrada para este hueco |
| RabbitMQ down | outbox retiene mensajes si enlistado correctamente | Wolverine durable |
| Billing down | onboarding puede seguir; invoice pendiente | outbox retry, salvo poison payload |
| Documents/MinIO down | invoice DB existe, PDF pendiente | command retry |
| SMTP down | entrega se reintenta/loguea; no debe decidir pago | revisar DLQ/alertas |
| Redis down | rate limiting/cache degradado según componente | circuit/fallback; riesgo fail-open variable |

### REL-003 — trazabilidad no equivale a reconciliación

**MEDIUM/P2/Medium.** IDs permiten buscar logs, pero falta vista operacional que pruebe cardinalidad 1:1:1 entre onboarding-payment-invoice y N reservations, y alerte divergencias.

### REL-004 — retries HTTP sobre efectos externos

**HIGH/P1/Medium.** Todo retry de POST debe conservar idempotency key y payload fingerprint. Stripe/Growth tienen keys, pero deben probarse timeouts después de commit remoto.

