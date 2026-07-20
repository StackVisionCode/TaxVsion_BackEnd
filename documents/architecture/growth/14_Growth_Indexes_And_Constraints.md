# Growth — Índices y constraints

## Codes

| Tabla | Índice/constraint |
|---|---|
| CodeDefinitions | unique filtered `(TenantScopeId, CodeHash)` donde CodeHash no null; check fechas; check kind/status |
| CodeRules | unique `(CodeId, Version)`; check exactamente un benefit value; BasisPoints 1..10000 |
| CodeScopes | unique `(CodeId, ScopeType, ScopeId, IsExcluded)` |
| CodeQuotes | index `(TenantId, ExpiresAt)`; check Gross=Discount+Net y montos ≥0 |
| CodeReservations | unique `(TenantId, IdempotencyKey)`; unique `(PaymentSource, RelatedPaymentId)` donde payment no null; index `(Status, ExpiresAt)` |
| CodeRedemptions | unique `ReservationId`; unique `(PaymentSource, RelatedPaymentId, CodeId)` |
| CodeCompensations | unique `(RedemptionId, SourceEventId, CompensationType)` |

## Referrals

| Tabla | Índice/constraint |
|---|---|
| ReferralPrograms | unique `(ScopeType, TenantId, ProgramCode)`; check tenant required for tenant scope |
| ReferralCodes | unique `(ProgramId, CodeHash)`; unique active owner according to policy |
| ReferralAttributions | unique filtered `(ProgramId, RefereeType, RefereeId)` para estados activos; check referrer≠referee |
| ReferralQualifications | unique `(AttributionId, QualifyingEventId)` |
| ReferralRewardCases | unique `(QualificationId, BeneficiaryType, BeneficiaryId, RewardType)` |
| ReferralRewardAttempts | unique `(RewardCaseId, IdempotencyKey)` |
| ReferralFraudReviews | index `(Status, CreatedAt)` y `(TenantId, Status)` |

## Integration/audit

- Inbox unique `(ConsumerName, EventId)`.
- ProcessedBusinessMessages unique `(Operation, ScopeId, IdempotencyKey)`.
- Outbox index `(Status, NextAttemptAt)`.
- Audit index `(TenantId, OccurredAt)` y `(AggregateType, AggregateId, OccurredAt)`.

Todas las columnas `RowVersion` son concurrency tokens. No se usa `float`/`double`. El schema exacto y migración permanecen **IMPLEMENTATION_BLOCKER** hasta scaffolding autorizado.

