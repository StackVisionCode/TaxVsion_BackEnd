# Growth — Contratos API conceptuales

No se implementan endpoints en esta fase.

## Públicos/autenticados por Gateway

| Método/ruta | Permiso | Scope |
|---|---|---|
| POST `/growth/codes` | `codes.manage` | tenant propio o plataforma explícita |
| POST `/growth/codes/{id}/activate` | `codes.activate` | owner |
| POST `/growth/codes/{id}/revoke` | `codes.revoke` | owner |
| GET `/growth/codes/{id}` | `codes.read` | owner |
| GET `/growth/referrals/me` | `referrals.read-own` | actor propio |
| POST `/growth/referral-programs` | `referrals.program.manage` | tenant propio/plataforma |
| GET `/growth/referral-programs/{id}` | `referrals.program.read` | owner |

## Internos, no Gateway

| Método/ruta | Audience/scope conceptual |
|---|---|
| POST `/internal/codes/quotes` | `taxvision-growth` / `growth.codes.quote` |
| POST `/internal/codes/reservations` | `growth.codes.reserve` |
| POST `/internal/codes/reservations/{id}/commit` | `growth.codes.commit` |
| POST `/internal/codes/reservations/{id}/cancel` | `growth.codes.cancel` |
| POST `/internal/codes/redemptions/{id}/compensate` | `growth.codes.compensate` |
| POST `/internal/referrals/qualifications` | `growth.referrals.qualify` |
| POST `/internal/referrals/rewards/{id}/confirm` | `growth.referrals.reward.confirm` |

## Errores

`400 Validation`, `401`, `403`, `404` para ownership sin revelar existencia, `409 IdempotencyConflict/ConcurrencyConflict/InvalidState`, `410 QuoteExpired`, `422 RuleNotApplicable`, `503 DependencyUnavailable`. Responses incluyen correlation ID, no token completo ni PII.

## Quote response

Incluye todos los campos mínimos del encargo y `PromotionSnapshotId`. Reservation response añade status, TTL y RowVersion opaque ETag. Payment debe validar hash/montos server-side.

