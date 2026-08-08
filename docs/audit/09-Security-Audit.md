# Auditoría de seguridad

## Controles observados

JWT, actor types, RBAC/scopes M2M, audience, tenant middleware, rate limiting, service token cache, cifrado AES-GCM, denylist, tests de tenant isolation e internal controllers. Auth emite tokens de servicio para onboarding con PlatformTenant y scopes específicos en Growth; PaymentApp usa policy ServiceOnly.

### SEC-001 — PaymentApp confía en cualquier service actor

**HIGH/P1/Medium.** El comentario de `PaymentAppOnboardingClient` indica que `client_id` no se valida y ServiceOnly exige solo `actor_type=Service`. Un token de otro servicio con audiencia aceptada podría invocar checkout interno si routing lo permite. Exigir scope/audience/client allowlist específicos.

### SEC-002 — price override entre servicios

**HIGH/P1/Medium.** `NetAmountCents` es input M2M confiado. Firmar quote o consultar Growth en PaymentApp.

### SEC-003 — secretos operacionales

**HIGH/P0/Small.** Existe `.env` local no tracked y `.env.zip` tracked. No se imprimieron valores. El artefacto zip debe inspeccionarse/retirarse del historial si contiene secretos y rotarlos. `dev-keys` solo trackea `.gitignore`.

### SEC-004 — tenant plataforma como ámbito privilegiado

**MEDIUM/P1/Large.** Una falla de filtro/rehome puede exponer activos pre-tenant cross-prospect. Aplicar subject authorization además de TenantId.

## Threat model prioritario

Actores: usuario anónimo, tenant user, admin, service token robado, webhook falso. Activos: PII onboarding, códigos/gift balances, invoices, payments, tokens. Límites: gateway→service, Auth→servicios, provider→webhook, RabbitMQ, DB por servicio. Pruebas obligatorias: IDOR OnboardingId, replay webhook, scope confusion, tenant ID tampering y SSRF en URLs/provider callbacks.

