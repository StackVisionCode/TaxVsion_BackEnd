# Referrals — Catálogo de casos de uso

| Caso | Actor | Permiso/scope | Idempotencia | Resultado |
|---|---|---|---|---|
| CreateProgram | Platform/Tenant admin | `referrals.program.manage` | requerida | Draft |
| ActivateProgram | autorizado | `referrals.program.manage` | requerida | Active |
| CreateReferralCode | participante/system | `referrals.read-own` o interno | requerida | referral code |
| CreateReferralAttribution | público autenticado/M2M | resource scope | requerida | attributed |
| QualifyReferral | Payment event consumer | interno | `EventId` + payment | qualified/rejected |
| RequestReward | system | interno | requerida | RewardCase Pending |
| ConfirmReward | Subscription | M2M confirm | `GrantId` | Granted |
| ReverseReward | Payment/Subscription | interno | requerida | Reversed/clawback |
| OpenFraudReview | system/admin | `referrals.fraud.manage` | requerida | Open |
| ResolveFraudReview | admin | `referrals.fraud.manage` | requerida | Approved/Rejected |
| ReadOwnReferrals | participante | `referrals.read-own` | n/a | solo propios |
| ReadAudit | auditor | `referrals.audit.read` | n/a | vista redacted |

Taxpayer-to-taxpayer permanece `DEFERRED`; sus casos no se exponen en producción MVP.

