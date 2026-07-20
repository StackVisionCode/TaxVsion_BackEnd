# Growth — Especificación de idempotencia

Formato recomendado: `{producer}:{operation}:v1:{scopeId:N}:{clientOperationId}`; máximo 200 caracteres, coherente con `PaymentApp.Domain/ValueObjects/IdempotencyKey.cs`.

| Operación | Scope/unique | Fingerprint | Replay igual | Replay distinto | Retención |
|---|---|---|---|---|---|
| CreateCode | tenant/platform + key | command canónico | 200/201 original | 409 | 2 años |
| IssueGift | CodeId + key | recipient+benefit | token no se reexpone; status original | 409 | vida+2 años |
| CreateQuote | tenant+subject+key | offer/rules/amount | quote original | 409 | 90 días |
| ReserveCode | tenant+key | QuoteId/payment/TTL | reservation original | 409 | 7 años if committed |
| CommitReservation | ReservationId+key | payment result | redemption original | 409 | 7 años |
| CancelReservation | ReservationId+key | reason/actor | estado original | 409 | 2 años |
| CompensateRedemption | RedemptionId+source event | delta/policy | compensation original | 409 | 7 años |
| CreateReferralAttribution | Program+referee+key | participants/window | original | 409 | policy+2 años |
| QualifyReferral | Attribution+event | payment facts | decision original | 409 | 7 años |
| Request/Confirm/ReverseReward | RewardCase+operation+key | grant/outcome | original | 409 | 7 años |
| ApplyGrant | GrantId | benefit/target/version | original | 409 | 7 años |

La respuesta serializada o sus datos de reconstrucción se guardan en `ProcessedBusinessMessages`. El inbox deduplica transporte; esta tabla deduplica efecto de negocio.

