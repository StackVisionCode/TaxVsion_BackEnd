# Campaigns — Security

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Cumple las convenciones vinculantes de `TaxVsion_BackEnd/CLAUDE.md`: multi-tenant fail-closed, RBAC acumulativo (JWT + actor-type + `[HasPermission]` + tenant + ownership + M2M audience/scope), rate-limit obligatorio, minor units, sin secretos ni JWT en claro. Corrige los anti-patrones de seguridad del legado (#5): secretos y JWT de usuario persistidos en texto plano.

---

## 1. Multi-tenancy fail-closed

- **Query filter global** por `TenantId` en todas las entidades (`campaign`, `campaign_run`, `campaign_recipient`, `processed_business_message`). Una consulta sin tenant no ve nada (fail-closed), corrige el legado que filtraba con `.Where(c => c.Status == ...)` **sin** tenant (`CampaignSchedulerBackgroundService.cs:54-59`) y por `CompanyId` manual en otros paths.
- **Repos tenant-scoped:** el tenant viene del `ITenantContext` (JWT en API; envelope en Wolverine), nunca de un parámetro del frontend.
- **Escrituras/lecturas cross-tenant** (jobs, reconciliación) solo vía `.IgnoreQueryFilters()` + tenant explícito y auditado, según `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`. En el scope Wolverine se setea el tenant del envelope antes de tocar el DbContext.

---

## 2. RBAC

Permisos (acumulativos, `[HasPermission]`):

| Permiso | Cubre |
|---|---|
| `campaigns:read` | listar/ver campañas, runs, recipients, stats |
| `campaigns:write` | crear/editar/archivar definición |
| `campaigns:send` | schedule/trigger/cancel (gasta dinero) |

- **Ownership:** además del permiso, se valida que el recurso pertenezca al tenant del actor (el query filter lo garantiza; endpoints por id verifican existencia dentro del tenant → `404`, no `403`, para no filtrar existencia cross-tenant).
- **Actor-type:** endpoints públicos exigen `tenant-user`. Los webhooks de tracking son anónimos pero atados a token firmado (§4).
- **Gate ortogonal:** `campaigns:send` (RBAC, "¿este usuario puede?") es distinto de `module.campaigns` (entitlement Subscription, "¿el tenant tiene la feature?") y del **balance** ("¿cuánto puede enviar?"). Los tres se evalúan por separado (ADR-CAMP-000 §Decisiones/#5). Un usuario con permiso pero tenant sin entitlement → `403 feature_not_enabled`; con entitlement pero sin saldo → run `Rejected(insufficient)` / `402` en estimate.

---

## 3. M2M (client-credentials)

- Campaigns→Wallet, Campaigns→Subscription, Campaigns→Customer usan **client-credentials** con audience/scope propios (`campaigns.api` como emisor; consume `wallet.api`, `subscription.api`, `customer.api`). **Nunca** se persiste ni reenvía el JWT del usuario final.
- Corrige el anti-patrón legado más grave: `Campaign.BackgroundAuthToken` (`Campaign.cs:87`) guardaba el **JWT del usuario** en la BD para poder hacer el refund en background (`CampaignSendService.cs:112-127`). Aquí el refund es una operación M2M autenticada por el propio servicio; no hay token de usuario que persistir ni que expire.

---

## 4. Tokens de tracking (open/click)

- Endpoints `/v1/t/o/{token}` y `/v1/t/c/{token}` son anónimos pero el `token` es **HMAC firmado** que resuelve `(runId, recipientId)` server-side.
- **Nunca** ids crudos ni PII en la URL (regla de privacidad). El token es opaco, de un solo propósito, y validado (firma + expiración + existencia del recipient) antes de cualquier efecto.
- El click redirige (302) solo a la `DestinationUrl` de la propia campaña (allowlist server-side), no a una URL del token → evita open-redirect.
- Efecto idempotente set-once (ver `Idempotency_Spec.md §5`).

---

## 5. Rate limiting

Todo endpoint público lleva `[RateLimit(categoría)]` (categorías `read`/`write`/`tracking`) o `[RateLimitExempt]` justificado (solo el pixel de open, que igual está protegido por token + dedupe). Ver `documents/RateLimit/Guia_Nuevos_Servicios_Endpoints.md`. El `trigger`/`schedule` (gastan dinero) van en categoría `write` estricta.

---

## 6. Datos: PII y secretos

- **PII mínima por run:** `campaign_recipient` guarda solo el destino que el canal necesita (email O phone O pushTokenRef). Retención acotada: purga/anonimización tras N días de `Completed` (los contadores agregados no-PII sobreviven). Ver `Data_Model.md §5`.
- **Sin secretos de proveedor** en Campaigns: SMTP2GO/SMS/WhatsApp API keys viven en cada ejecutor, cifrados. Campaigns no integra proveedores (frontera de `../02_Context_Map.md`). Corrige `SmtpProviderConfig.ApiKey` en texto plano del legado.
- **Sin montos confiados por el frontend:** el precio-por-mensaje y el costo se calculan server-side y se congelan en el run. El frontend nunca envía `estimatedCost` (corrige `CreateCampaignCommandHandler.cs:219`).

---

## 7. Superficie de abuso

| Vector | Mitigación |
|---|---|
| Disparar campañas masivas para agotar saldo ajeno | tenant-scoping + `campaigns:send` + gasto contra el propio Wallet del tenant |
| Enumerar recipients/runs de otro tenant | query filter fail-closed + `404` uniforme |
| Falsificar results/tracking | results por M2M autenticado del ejecutor; tracking por token HMAC |
| Replay de dispatch para re-cobrar | idempotencia por `dispatch_idempotency_key` + reserve/consume por `runId` |
| Inyección vía template variables | render en Scribe con escaping; Campaigns pasa variables como datos, no ejecuta |

---

## 8. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado persiste JWT de usuario en BD | `Campaign.cs:87`; uso `CampaignSendService.cs:112-127` | VERIFIED | 97% |
| Legado filtra sin tenant (query global sin TenantId) | `CampaignSchedulerBackgroundService.cs:54-59` | VERIFIED | 93% |
| Legado confía costo del frontend/local | `CreateCampaignCommandHandler.cs:219` | VERIFIED | 95% |
| Gate `module.campaigns` ortogonal al balance | ADR-CAMP-000 §Decisiones/#5; seeder tiers | VERIFIED | 92% |
| Convenciones RBAC/RateLimit/tenant fail-closed | CLAUDE.md, `Guia_IgnoreQueryFilters`, guía RateLimit | DOCUMENTED_ONLY | 90% |
| Tokens de tracking HMAC + allowlist redirect | diseño (este doc §4) | NEW | 85% |
