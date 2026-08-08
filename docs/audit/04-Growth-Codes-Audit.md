# Auditoría Growth / Codes / Gift / Referral

## Modelo real

Growth posee `CodeDefinition`, `CodeRuleVersion`, `CodeQuote`, `CodeReservation`, `CodeRedemption`, compensations, usage counters, referrals/attributions/qualifications/rewards. Quote usa subject `Anonymous(OnboardingId)` y owner PlatformTenant para pre-tenant. Referral attribution y redemption son conceptos distintos.

## Hallazgos

### GRO-001 — stack no atómico

**HIGH/P1/Medium.** `OnboardingCodeReserver.ReserveAsync` realiza quote+reserve por código en bucle HTTP. Si el segundo falla, el primero ya quedó reservado y Auth retorna error sin cancelarlo. Impacto: capacidad/balance bloqueado hasta expiry (24h), experiencia inconsistente. Solución: endpoint batch transaccional o compensación inmediata durable.

### GRO-002 — idempotency key por snapshot insuficiente para códigos repetidos

**MEDIUM/P2/Small.** Key `onb-reserve:{onboarding}:{snapshot}` no incluye definición/código; dos entradas diferentes aplicadas al mismo residual en retries/variaciones pueden colisionar. Incluir quote/reservation identity y fingerprint del payload.

### GRO-003 — orden comercial rígido

**MEDIUM/P2/Small.** El código ordena Referral→Promo→Gift aunque el request D enumera Gift→Promotion. Esto cambia qué instrumento se consume, especialmente gift balance y promociones porcentuales. Formalizar política y mostrarla al usuario.

### GRO-004 — concurrencia requiere prueba DB real

**HIGH/P1/Medium.** Migraciones muestran `rowversion`, índices de idempotencia y counters, pero las pruebas localizadas son mayormente fakes/domain. Debe probarse disputa `RemainingUses=1` contra SQL Server con retry de `DbUpdateConcurrencyException`.

## GiftCard

El modelo trata gift como `CodeKind.BenefitGift`, no como wallet/ledger de saldo general. La auditoría no encontró en el flujo onboarding evidencia de refund automático que restaure gift tras refund monetario, ni cancelación inmediata de reservas previas si falla el stack. Expiry se aplica en quote/reservation; currency es transportada y validada en dominios, pero requiere contratos E2E.

