# Growth — Modelo de observabilidad

## Logs

Structured logs con `TraceId`, `CorrelationId`, `CausationId`, `TenantId`, bounded context, aggregate type/id/version, operation, outcome y duration. Redactar token, email, taxpayer ID, payload de fraude y fingerprints. Prefix/last-four solo cuando sea necesario.

## Métricas

| Métrica | Tipo | Labels de baja cardinalidad |
|---|---|---|
| `growth.codes.quotes_total` | counter | outcome, kind |
| `growth.codes.reservations_total` | counter | outcome, source |
| `growth.codes.reservations_active` | gauge | source |
| `growth.codes.reconciliation_lag_seconds` | histogram | outcome |
| `growth.codes.compensations_total` | counter | type |
| `growth.referrals.attributions_total` | counter | program_type, outcome |
| `growth.referrals.qualifications_total` | counter | program_type, outcome |
| `growth.referrals.rewards_total` | counter | type, state |
| `growth.referrals.fraud_reviews_open` | gauge | program_type |
| `growth.integration.inbox_duplicates_total` | counter | consumer |
| `growth.integration.outbox_lag_seconds` | histogram | destination |

No label TenantId, CodeId, email ni payment ID en métricas.

## Traces

Spans: quote, reserve SQL, payment handoff, commit/cancel, reconcile lookup, compensate, qualify, grant/reverse. Propagar W3C trace context en RabbitMQ y HTTP.

## Alertas

Outbox/inbox lag, reservas expiradas con payment unknown, compensation failures, reward stuck, concurrency spikes, cross-tenant denials, code enumeration, reconciliation backlog y DLQ.

## SLO propuestos

Quote p95 <250 ms; reserve/commit p95 <400 ms sin dependencia Payment; reconciliación 99% <5 min; cero acceso cross-tenant confirmado. SLO requieren medición de carga: **UNVERIFIED**, no blocker de scaffolding.

