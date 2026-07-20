# Referrals — Diseño de dominio

## Aggregate roots

- `ReferralProgram`: scope, participantes, ventana, evento calificador, límites, waiting period y políticas.
- `ReferralAttribution`: relación inmutable referrer/referee y expiración.
- `ReferralQualification`: decisión idempotente por evento financiero.
- `ReferralRewardCase`: requested→pending→granted→vested/reversed/failed.
- `ReferralFraudReview`: señales, estado y decisión auditada.

## Programa A — tenant-to-tenant

| Regla | Diseño |
|---|---|
| Referrer/referee | Tenant → Tenant |
| Scope | Plataforma |
| Qualifying event | Primer PaymentApp exitoso elegible |
| Minimum payment | 1 minor unit por default MVP; parametrizable y versionado |
| Attribution window | 90 días por default MVP; parametrizable |
| Waiting period | 30 días desde el primer pago exitoso |
| Reward | descuento futuro, trial extension o feature grant |
| Reward owner | Subscription |
| Fraude | self-referral, shared owner/payment method/domain/device, cycles |
| Refund/chargeback | pending no viste; granted inicia clawback |
| Maximum | 10 rewards por referrer/año calendario |
| Privacy | TenantId y actor auditado; mínima PII |

## Programa B — taxpayer-to-taxpayer

| Regla | Diseño |
|---|---|
| Referrer/referee | Taxpayer → Taxpayer |
| Scope | Un TenantId |
| Qualifying event | PaymentClient exitoso sobre oferta server-side |
| Reward | benefit gift no monetario |
| Owner | sistema comercial/Subscription según beneficio; nunca Referrals balance |
| Fraude | self-referral, household/device/payment fingerprint, velocity |
| Privacy | IDs pseudónimos, cifrado y retención aprobada |

Diseñado pero **DEFERRED** de producción hasta resolver PII, antifraude, Catalog y autoridad de precio.

## Separación respecto de Codes

ReferralCode identifica atribución; CodeDefinition autoriza beneficio. Una calificación puede emitir `GrantReferralReward` hacia Subscription o solicitar a Codes una emisión. No hay FK ni aggregate compartido.
