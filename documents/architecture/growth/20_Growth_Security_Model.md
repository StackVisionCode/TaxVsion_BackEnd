# Growth — Modelo de seguridad

## Autorización

Evaluación acumulativa: JWT válido + role contextual + permiso explícito + TenantId + ownership + audience/scope M2M. Ni TenantAdmin ni PlatformAdmin tienen bypass general.

## Permisos

Se conserva convención real `module.resource.action` observada en `PaymentAppPermissions.cs`:

- `codes.code.read/manage/issue/activate/revoke`
- `codes.audit.read`
- `codes.redemption.read`
- `codes.compensation.manage`
- `referrals.own.read`
- `referrals.program.read/manage`
- `referrals.attribution.read`
- `referrals.fraud.read/manage`
- `referrals.reward.read/manage`
- `referrals.audit.read`

Los nombres están dados de alta en `GrowthPermissions` y `PermissionCatalog`. La
migración/validación de seed de Auth sigue siendo **IMPLEMENTATION_BLOCKER** hasta
que la migración y sus pruebas queden verdes.

## Tenant isolation

Se eligen **ambos**: global query filters como defensa por defecto + repositorios tenant-scoped explícitos. Operaciones platform-global usan un contexto elevado específico, permiso cross-tenant y audit entry; nunca desactivan filtros implícitamente.

## M2M

GDR-008 aprobó client credentials con audience `taxvision-growth`, scopes por
operación, tokens cortos, rotación y endpoints internos fuera de Gateway. Auth ya
emite audience/scopes configurables y Growth aplica políticas acumulativas; la
prueba E2E con secretos rotados queda como **IMPLEMENTATION_BLOCKER**.

## Gift codes

RNG criptográfico ≥128 bits, base32/base64url sin caracteres ambiguos, almacenamiento HMAC/SHA-256 con pepper protegido cuando no se necesita recuperación, prefix+last four, expiración/receptor/rate limit. Token completo solo en respuesta de emisión y canal seguro; nunca log/evento.

## Pruebas negativas obligatorias

Tenant A lee/modifica B; TenantAdmin accede global; usuario sin audit permission; M2M scope/audience incorrectos; recurso global filtrado erróneamente; IDOR por body/path; code enumeration/rate limiting.

## Evidencia/riesgo real

`src/Services/PaymentApp/TaxVision.PaymentApp.Api/Common/ClaimsPrincipalExtensions.cs:36` permite `PlatformAdmin` por rol; Growth no copia ese bypass. Clasificación: **VERIFIED** defecto de patrón existente, severidad HIGH para reutilización.
