# Wallet/Ledger — Security

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- Convenciones: RBAC acumulativo (JWT + actor-type + `[HasPermission]` + tenant + ownership + M2M audience/scope), sin bypass; multi-tenant fail-closed; `[RateLimit]`/`[RateLimitExempt]` en todo endpoint (ver `TaxVsion_BackEnd/CLAUDE.md`, guías `RateLimit/` y `Guia_IgnoreQueryFilters...`).

---

## 1. Modelo de acceso: solo M2M interno

Wallet **no** tiene endpoints de usuario final. Solo lo llaman servicios (Campaigns, SMS, WhatsApp, Admin/Platform) por **client-credentials M2M** con:

- **Audience:** `taxvision-wallet` (rechaza tokens con otra audience).
- **Scopes por operación** (principio de mínimo privilegio):

| Scope | Permite | Quién lo tiene |
|---|---|---|
| `wallet:reserve` | Reserve | Campaigns, SMS, WhatsApp |
| `wallet:consume` | Consume | Campaigns, SMS, WhatsApp |
| `wallet:refund` | Release/Refund | Campaigns, SMS, WhatsApp |
| `wallet:read` | GetBalance, Ledger | Campaigns, SMS, Admin, UI-gateway (readonly) |
| `wallet:adjust` | Adjust | Admin/Platform únicamente |
| `wallet:admin` | Freeze/Unfreeze | Admin/Platform únicamente |

Un cliente de canal **no** tiene `wallet:adjust`/`wallet:admin` (no puede crear saldo). Recharge no es scope M2M: solo lo dispara el consumer interno del evento de PaymentApp (`Commands_And_Events.md §2.1`).

## 2. Tenant enforcement (fail-closed)

- El `TenantId` viaja en el request pero **se valida** contra el token M2M: un cliente autorizado para el tenant A no puede reservar sobre el tenant B (403). Patrón: `tenantId` explícito comparado con el scope del actor, defensa en profundidad como en `SqlBusinessIdempotencyExecutor.cs:39-49` (rechaza tenant vacío y mismatch contra el contexto ambiental).
- Query filter global por `TenantId` en el DbContext; toda lectura del balance/ledger/reservations es tenant-scoped. Cross-tenant solo vía `.IgnoreQueryFilters()` + tenant explícito en jobs/consumers, dentro de scope Wolverine con `TenantId` seteado (`Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`).

## 3. Rate limiting

Todo endpoint lleva `[RateLimit(<categoría interna>)]`. Las operaciones de dinero (reserve/consume/refund/adjust) en categoría estricta por-cliente-M2M; `get-balance`/`ledger` categoría de lectura. Sin excepciones sin `[RateLimitExempt]` justificado (`RateLimit/Guia_Nuevos_Servicios_Endpoints.md`).

## 4. Anti-patrones de seguridad del legado corregidos

| Legado | Evidencia | Corrección en Wallet |
|---|---|---|
| **JWT de usuario persistido en BD** para hacer refunds asíncronos | `CreateCampaignCommandHandler.cs:67` (`BackgroundAuthToken`); `WalletServiceClient.cs:179-180` (bearer con token guardado) | **Nunca** se persiste JWT. Refund = M2M client-credentials propio de Wallet, sin token de usuario. |
| Débito autorizado por token de usuario reenviado entre servicios | `WalletServiceClient.cs:38-41` (auth handler reenvía token) | M2M audience/scope explícitos; el consumidor prueba autorización por su propio token, no el del usuario. |
| Sin idempotencia → replays cobran doble | `WalletServiceClient.cs:101` | `Idempotency-Key` obligatorio + business-inbox. |

## 5. Integridad financiera

- **Montos server-side:** el frontend nunca envía `amountCents`; los consumidores M2M calculan el costo (precio por canal × destinatarios) server-side. Wallet confía en el consumidor autenticado, no en el navegador (regla de suite: "nunca montos confiados por el frontend").
- **Ledger inmutable con grants revocados** (UPDATE/DELETE denegados a nivel BD, `Data_Model.md §2`): ni un bug ni un actor con acceso a la app pueden alterar la historia; solo append.
- **Freeze** como respuesta a fraude/dispute sin destruir datos.

## 6. Secretos

Wallet **no** tiene secretos de proveedor (no integra proveedores externos; esos viven cifrados en cada ejecutor de canal, `02_Context_Map §Fronteras`). Solo credenciales M2M y cadena de conexión, gestionadas por el mecanismo de secretos de la plataforma (no en BD, no en texto plano).

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado persiste JWT para refund (a eliminar) | `CreateCampaignCommandHandler.cs:67`; `WalletServiceClient.cs:179-180` | VERIFIED | 95% |
| Legado reenvía token de usuario entre servicios | `WalletServiceClient.cs:38-41` | VERIFIED | 92% |
| Validación tenant explícito + mismatch fail-closed | `SqlBusinessIdempotencyExecutor.cs:39-49` | VERIFIED | 95% |
| M2M audience/scope, RBAC sin bypass | `CLAUDE.md`/`00_Overview:48,50` | DOCUMENTED_ONLY | 88% |
| Scopes por operación de Wallet | diseño | NEW | n/a |
