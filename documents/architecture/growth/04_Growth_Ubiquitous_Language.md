# Growth — Lenguaje ubicuo

## Codes

| Término | Definición |
|---|---|
| CodeDefinition | Aggregate que define una credencial promocional y su lifecycle |
| CodeRuleVersion | Reglas inmutables versionadas usadas por quote |
| Scope | Oferta/plan/producto/tenant/sujeto donde aplica |
| Quote | Evaluación inmutable, expirable y no consumible |
| Reservation | Retención provisional y atómica de disponibilidad |
| Redemption | Hecho único de consumo confirmado |
| Compensation | Ajuste posterior por reversa financiera |
| Benefit Gift | Descuento, trial, período gratuito o feature unlock |
| Monetary Gift | Saldo reutilizable/parcial; requiere Ledger y queda fuera del MVP |
| Prelaunch Grant | Beneficio validado por Codes y materializado por Subscription |

## Referrals

| Término | Definición |
|---|---|
| ReferralProgram | Aggregate de política de un programa concreto |
| ReferralCode | Identificador de invitación; no es un CodeDefinition promocional |
| Attribution | Asociación temporal referrer→referee dentro de un scope |
| Qualification | Decisión de que un evento cumple reglas del programa |
| RewardCase | Lifecycle del beneficio debido al referrer/referee |
| RewardAttempt | Intento idempotente de materializar/revertir el reward |
| Vesting | Espera antes de que el reward sea irrevocable según política |
| Clawback | Reversión del reward por refund, chargeback o fraude |
| FraudReview | Caso de revisión, no decisión automática de culpabilidad |

## Términos prohibidos o ambiguos

- `Apply` sin especificar quote, reserve o grant.
- `Gift` sin distinguir benefit/monetary.
- `Referral code` como sinónimo automático de código promocional.
- `price` sin owner, moneda, oferta y versión.
- “admin bypass”; todo acceso exige permiso y scope.

