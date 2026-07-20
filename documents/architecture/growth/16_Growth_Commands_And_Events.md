# Growth — Commands y eventos

## Taxonomía

- **Domain event:** hecho interno de un aggregate; no sale automáticamente.
- **Integration event:** hecho estable requerido por otro servicio.
- **Command:** solicitud dirigida a un owner; puede rechazarse.
- **Internal notification:** coordinación dentro del deployment sin contrato público.
- **Metric:** observación agregada; no gobierna estado.

## Envelope de integración

Todo contrato contiene `EventId`, `EventType`, `EventVersion`, `OccurredAt`, `CorrelationId`, `CausationId`, `TraceId`, `TenantId`, `AggregateId`, `AggregateVersion`. El `IntegrationEvent` actual solo cubre parte; ampliar contratos nuevos sin romper los existentes es **IMPLEMENTATION_BLOCKER**. Evidencia: localizar clase base bajo `src/BuildingBlocks/Messaging/`.

## Contratos mínimos

| Nombre | Tipo | Producer → Consumer | Payload adicional |
|---|---|---|---|
| CodeReservationCommitted | hecho | Codes → Payment/Referrals | ReservationId, RedemptionId, payment ref, amounts |
| CodeRedemptionCompensated | hecho | Codes → Referrals | RedemptionId, policy/action, amounts |
| PaymentSucceeded | hecho existente/evolución | Payment → Growth | source/id, amounts, snapshot refs |
| PaymentRefunded | hecho | Payment → Growth | cumulative + delta, currency |
| PaymentChargebackChanged | hecho | Payment → Growth | dispute id/status/amount/version |
| ReferralQualified | hecho | Referrals → interesados | AttributionId, QualificationId |
| GrantReferralReward | command | Referrals → Subscription | GrantId, benefit, tenant, expiry |
| ReferralRewardGranted | confirmación | Subscription → Referrals | GrantId, materialized ref |
| ReferralRewardRejected | confirmación | Subscription → Referrals | GrantId, reason code |
| TrialOrFeatureGrantConfirmed | hecho | Subscription → Codes | GrantId, effective dates |

No se publican token de gift, reglas internas, señales de fraude crudas ni PII.

