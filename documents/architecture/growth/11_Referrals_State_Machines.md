# Referrals — Máquinas de estado

## Attribution

```text
Pending ──validate──► Active ──qualifying event──► Qualified
   ├──reject──► Rejected
Active ──window ends──► Expired
Qualified ──fraud signal──► UnderReview ──approve──► Qualified
                                      └──reject──► Rejected
```

Una atribución solo califica una vez por programa/referee/qualifying event. Tenant-to-tenant y taxpayer-to-taxpayer usan policies distintas.

## RewardCase

```text
Requested ─► PendingGrant ─► Granted ─► Vested
    │              │             └──refund/dispute/fraud──► ClawbackPending
    │              └──reject──► Failed                    ├──confirm──► Reversed
    └──cancel──► Cancelled                                └──fail──► ManualReview
```

## FraudReview

`Open → Investigating → Approved | Rejected | Escalated`. Toda resolución exige actor, permiso, reason y evidencia redacted. Un review no modifica balances; emite comandos al owner del beneficio.

## Concurrencia

- unique `(ProgramId, RefereeType, RefereeId)` para atribución activa según policy;
- unique `(AttributionId, QualifyingEventId)`;
- unique `(RewardCaseId, RewardType, BeneficiaryId)`;
- RowVersion en Program, Attribution, RewardCase y FraudReview;
- refund durante grant deja `ClawbackPending` mediante guard monotónico.

