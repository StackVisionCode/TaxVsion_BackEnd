# Wallet/Ledger — Security

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-10-06
- **Estado:** DISEÑO — no implementado
- **Coherente con:** `00_Plan_And_Architecture.md §7/§8`, `ADR.md` (ADR-WAL-011/012), `Security`/`RateLimit` guías de la suite.
- Convenciones: RBAC acumulativo (JWT + actor-type + `[HasPermission]` + tenant + ownership + M2M audience/scope), sin bypass; multi-tenant fail-closed; `[RateLimit]`/`[RateLimitExempt]` en todo endpoint (`TaxVsion_BackEnd/CLAUDE.md`, guías `RateLimit/`, `Guia_IgnoreQueryFilters...`).

> **Modelo prepago (ADR-WAL-005) + acceso `wallet.*` (ADR-WAL-012).** Hay dos superficies: (1) endpoints de **tenant** (`/wallet`, `/wallet/top-up`, `/wallet/estimate`, `/wallet/pricing` lectura) con JWT + `wallet.view`/`wallet.manage`; (2) interno **event-driven** (el cobro del run por el PEP, sin token de usuario). Editar precios = **PlatformAdmin**.

---

## 1. Modelo de acceso

### 1.1 Endpoints de tenant (JWT + permiso)

| Endpoint | Permiso / actor | Nota |
|---|---|---|
| `GET /wallet` | `wallet.view` | saldo del tenant del JWT |
| `GET /wallet/ledger` | `wallet.view` | auditoría paginada |
| `POST /wallet/top-up` | `wallet.manage` | inicia recarga (cobra PaymentApp) |
| `GET /wallet/pricing` | `wallet.view` | tarifas vigentes |
| `POST /wallet/estimate` | `wallet.view` | cálculo sin efectos |
| `PUT /wallet/pricing/{channel}` | **PlatformAdmin** | no es permiso de tenant |

`WalletPermissions` (`View="wallet.view"`, `Manage="wallet.manage"`) + módulo `wallet` en `PermissionModuleMap`/`PlanModuleCatalog` (ADR-WAL-012). Gate de módulo en el gateway.

### 1.2 Cobro del run (interno, event-driven)

El `Debit` de un run **no tiene endpoint** y **no usa token de usuario**: lo dispara el consumer de `CampaignRunPendingAuthorizationIntegrationEvent` (PEP, ADR-WAL-007). Los jobs/consumers usan M2M client-credentials (audience `taxvision-wallet` + scopes) cuando llamen a otros servicios.

## 2. Tenant enforcement (fail-closed)

- El `TenantId` sale del **JWT**, nunca del body — un usuario del tenant A no puede operar sobre B (403).
- En el cobro por evento, el `TenantId` viaja en el evento y se valida contra el wallet destino; defensa en profundidad como en `SqlBusinessIdempotencyExecutor.cs:39-49` (rechaza tenant vacío/mismatch).
- Query filter global por `TenantId` en el DbContext; toda lectura de wallet/ledger es tenant-scoped. Cross-tenant solo vía `.IgnoreQueryFilters()` + tenant explícito en jobs/consumers dentro de scope Wolverine (`Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`). El catálogo `ChannelPrice` es global de plataforma (no tenant-scoped; solo PlatformAdmin lo muta).

## 3. Rate limiting

Todo endpoint lleva `[RateLimit(<categoría>)]`. `top-up` en categoría estricta (money-IN); `get`/`estimate`/`pricing` categoría de lectura. Sin excepciones sin `[RateLimitExempt]` justificado (`RateLimit/Guia_Nuevos_Servicios_Endpoints.md`).

## 4. Anti-patrones de seguridad del legado corregidos

| Legado | Evidencia | Corrección en Wallet |
|---|---|---|
| **JWT de usuario persistido en BD** para refunds asíncronos | `CreateCampaignCommandHandler.cs:67` (`BackgroundAuthToken`); `WalletServiceClient.cs:179-180` | **Nunca** se persiste JWT. El cobro es event-driven sin token de usuario (ADR-WAL-011). |
| Débito autorizado por token de usuario reenviado entre servicios | `WalletServiceClient.cs:38-41` | Cobro por evento PEP; el autorizador decide por saldo+catálogo, no por el token del usuario. |
| Sin idempotencia → replays cobran doble | `WalletServiceClient.cs:101` | `OperationKey` + `UNIQUE(TenantId, OperationKey)`. |

## 5. Integridad financiera

- **Montos server-side:** el frontend nunca fija el costo. Campaigns publica **conteos** de unidades; Wallet calcula el costo con el catálogo `ChannelPrice` server-side (ADR-WAL-006). El `amountCents` de `/wallet/top-up` es el **monto a recargar** (lo cobra PaymentApp), no un costo de campaña.
- **Ledger inmutable con grants revocados** (UPDATE/DELETE denegados a nivel BD, `Data_Model.md §2`): ni un bug ni un actor con acceso a la app pueden alterar la historia; solo append. Correcciones = `Adjustment`.
- **Catálogo de precios solo PlatformAdmin:** un tenant no puede abaratarse el envío; los cambios de tarifa quedan auditados.
- **Freeze** (opcional) como respuesta a fraude/dispute sin destruir datos.

## 6. Secretos

Wallet **no** tiene secretos de proveedor (el charge del top-up lo hace PaymentApp contra Stripe; el débito de campaña es interno). Solo credenciales M2M y connection string, gestionadas por el mecanismo de secretos de la plataforma (no en BD, no en texto plano).

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado persiste JWT para refund (a eliminar) | `CreateCampaignCommandHandler.cs:67`; `WalletServiceClient.cs:179-180` | VERIFIED | 95% |
| Legado reenvía token de usuario entre servicios | `WalletServiceClient.cs:38-41` | VERIFIED | 92% |
| Validación tenant explícito + mismatch fail-closed | `SqlBusinessIdempotencyExecutor.cs:39-49` | VERIFIED | 95% |
| Acceso `wallet.view`/`wallet.manage` + módulo (espejo campaigns) | `CampaignsPermissions.cs:12-21`; `PermissionModuleMap.cs:36-56` | VERIFIED | 92% |
| Costo server-side por catálogo; conteos desde Campaigns | `00_Plan §4/§5` (diseño) | NEW | n/a |
