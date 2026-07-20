# Growth — Modelo de datos

Convenciones: `uniqueidentifier`, fechas `datetime2(7)` UTC, strings `nvarchar`, dinero `bigint AmountCents + char(3) Currency`, porcentaje `int BasisPoints`, RowVersion `rowversion`, IDs asignados por aplicación.

| Tabla | PK | FK internas | Datos principales | Sensible/retención |
|---|---|---|---|---|
| codes.CodeDefinitions | Id | — | TenantScopeId?, Kind, Status, CodeHash?, Prefix?, LastFour?, Starts/Expires, RowVersion | token hash; definición + 7 años audit |
| codes.CodeRules | Id | CodeId | Version, benefit type/value, stacking, published | inmutable; vida code + 7 años |
| codes.CodeScopes | Id | CodeId | ScopeType, ScopeId, include/exclude | no PII |
| codes.CodeQuotes | Id | CodeId, RuleId | campos mínimos de quote, SnapshotHash | subject pseudónimo; TTL + 90 días |
| codes.CodeReservations | Id | QuoteId, CodeId | campos mínimos, fingerprint, state, RowVersion | 7 años si committed; 1 año otherwise |
| codes.CodeRedemptions | Id | ReservationId | payment refs, amounts, committed | financiero indirecto; 7 años |
| codes.CodeCompensations | Id | RedemptionId | type, amounts, reason, source event | 7 años |
| referrals.ReferralPrograms | Id | — | scope, policy JSON versionado, state, RowVersion | no PII |
| referrals.ReferralCodes | Id | ProgramId | hash/display, owner pseudónimo, expiry | hash; lifecycle + 1 año |
| referrals.ReferralAttributions | Id | ProgramId | referrer/referee refs, window, state, RowVersion | PII indirecta; policy aprobada |
| referrals.ReferralQualifications | Id | AttributionId | qualifying event/payment, decision | 7 años |
| referrals.ReferralRewardCases | Id | QualificationId | beneficiary, reward/grant refs, state, RowVersion | 7 años |
| referrals.ReferralRewardAttempts | Id | RewardCaseId | key/fingerprint/outcome | 7 años |
| referrals.ReferralFraudReviews | Id | AttributionId? RewardCaseId? | signals redacted, decision | acceso restringido; policy legal |
| integration.OutboxMessages | Id | — | envelope, payload, attempts | según Wolverine/operación |
| integration.InboxMessages | Id | — | EventId, consumer, received/status | dedupe retention ≥ replay window |
| integration.ProcessedBusinessMessages | Id | — | operation, key, fingerprint, response | por retención de operación |
| audit.AuditEntries | Id | — | actor, action, tenant, before/after redacted | append-only, ≥7 años |

No hay FK entre schemas `codes` y `referrals`. Las referencias cross-context son GUID opacos.

