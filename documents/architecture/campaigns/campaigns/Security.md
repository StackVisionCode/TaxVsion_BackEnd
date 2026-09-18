# Campaigns — Security

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-09-16 (revisión: **sin balance/monto; acceso ya cableado**)
- **Estado:** DISEÑO — no implementado

Cumple las convenciones vinculantes de `TaxVsion_BackEnd/CLAUDE.md`: multi-tenant fail-closed, RBAC acumulativo (JWT + actor-type + `[HasPermission]` + tenant + ownership + M2M audience/scope), rate-limit obligatorio, sin secretos ni JWT en claro. Corrige los anti-patrones de seguridad del legado (#5): secretos y JWT de usuario persistidos en texto plano.

> **Regla dura:** Campaign no verifica ni consume saldo. **No hay gate de balance, ni error `402`, ni "gasta dinero", ni montos.** Las verificaciones son: RBAC (`campaigns.manage`) + entitlement de feature (`module.campaigns`) + tenant + ownership. Nada de dinero.

---

## 1. Multi-tenancy fail-closed

- **Query filter global** por `TenantId` en todas las entidades (`campaign`, `campaign_run`, `campaign_recipient`, `contact`, `contact_list`, `sender_profile`, `processed_business_message`). Una consulta sin tenant no ve nada (fail-closed), corrige el legado que filtraba con `.Where(c => c.Status == ...)` **sin** tenant (`CampaignSchedulerBackgroundService.cs:54-59`) y por `CompanyId` manual en otros paths.
- **Repos tenant-scoped:** el tenant viene del `ITenantContext` (JWT en API; envelope en Wolverine), nunca de un parámetro del frontend.
- **Escrituras/lecturas cross-tenant** (jobs, sweeper) solo vía `.IgnoreQueryFilters()` + tenant explícito y auditado, según `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`. En el scope Wolverine se setea el tenant del envelope antes de tocar el DbContext.

---

## 2. RBAC — acceso YA cableado

El acceso **ya existe centralmente** (no se re-agrega, ADR-CAMP-001 D4):

- **Permiso:** `campaigns.manage` (`PermissionCatalog.cs:39,474-482`; módulo `campaigns`, MinPlanTier Pro; TenantAdmin lo recibe por defecto). Cubre crear/editar/archivar campañas, contactos, remitentes, y schedule/trigger/cancel. Todo endpoint de escritura lleva `[HasPermission("campaigns.manage")]`; los de lectura, el mismo permiso (o uno de lectura si se decide granular más adelante).
- **Actor-type:** endpoints de staff exigen `TenantEmployee`/`TenantAdmin`/`PlatformAdmin` (`[AllowActorTypes(...)]`). El **consumo de dispatch** por los ejecutores es M2M (`ActorType.Service`), no toca estos endpoints.
- **Ownership:** además del permiso, el query filter garantiza tenant-scoping; endpoints por id verifican existencia dentro del tenant → `404`, no `403`, para no filtrar existencia cross-tenant.
- **Gate ortogonal (dos verificaciones, ninguna de dinero):** `campaigns.manage` (RBAC, "¿este usuario puede?") es distinto de `module.campaigns` (entitlement Subscription, "¿el tenant tiene la feature?", `SubscriptionPlanCatalogSeeder.cs`). Sin entitlement → `403 feature_not_enabled`. **No hay una tercera verificación de balance.**
- **Enforce vs log-only (fix #17, verificado):** el gate de módulo/entitlement está **inline** en `PermissionPolicyProvider` (`BuildingBlocks.Web.ActorTypeAuthorization`, `PermissionPolicyProvider.cs:50-88`) + `PermissionModuleMap` — **no** hay una clase `ModuleGate` dedicada. Lee el flag `Authorization:ModuleGate:Enforce` (`:68-71`), cuyo **default es `false` (LOG-ONLY)**: hoy solo loguea "would 403". **Producción de Campaigns debe poner el flag en `true`** para que el entitlement se **aplique** (`403 feature_not_enabled`); el log-only es solo etapa de rollout. Si el tenant **pierde** el entitlement entre agendar y ejecutar, el run se **rechaza** en el guard `Created→Dispatching` (`Rejected(gate)`); una recurrencia revalida en cada disparo.

---

## 3. M2M (client-credentials) y autorización en el BUS (fix #16)

- Campaigns→Subscription y Campaigns→Customer usan **client-credentials** con audience/scope propios. **Nunca** se persiste ni reenvía el JWT del usuario final (corrige `Campaign.BackgroundAuthToken`, `Campaign.cs:87`).
- **El canal real de dispatch/result es mensajería (AMQP), no HTTP.** Las reglas HTTP (`ActorType.Service`, audience) **no** prueban por sí solas que un publicador AMQP esté autorizado a fabricar un result o elegir cualquier `TenantId`. Por eso:
  - **Credenciales y permisos del broker** por servicio (quién puede publicar en `campaign.dispatch.result.*` y consumir `campaign.dispatch.<channel>`); un consumer de un canal no puede publicar results de otro.
  - **Validación al aplicar un result:** el `TenantId` del envelope debe coincidir con el del run, y debe existir correspondencia **`DispatchId ↔ RunId ↔ CampaignId ↔ channel`**; un result con tenant/identidad que no casan se **rechaza y audita** (no muta la unidad).
  - **Reutilizar un consumer existente no implica abrir su controller HTTP:** permitir `ActorType.Service` en *todo* un controller público no es consecuencia automática de agregar un consumer interno; se autoriza en el **punto exacto** del handler.

---

## 4. Rate limiting

Todo endpoint público lleva `[RateLimit(categoría)]` (categorías `read`/`write`) o `[RateLimitExempt]` justificado. Ver `documents/RateLimit/Guia_Nuevos_Servicios_Endpoints.md`. El `trigger`/`schedule` van en categoría `write` estricta (efecto de fan-out masivo), aunque **no** por costo de dinero — por volumen de mensajería.

---

## 5. Datos: PII y secretos

- **PII mínima:** `campaign_recipient` (y `contact`) guardan solo el destino que el canal necesita. Retención acotada: purga/anonimización tras N días de estado terminal (los contadores agregados no-PII sobreviven). Ver `Data_Model.md §4`.
- **PII más allá de la fila del recipient (fix #24):** hay copias de PII en `audience_ref` manual, snapshots del run, `Payload`/variables, **outbox/inbox, DLQ, backups** y en el `raw_payload` de webhooks del proveedor. Purgar solo `campaign_recipient` **no** las elimina. Política: definir **retención por superficie**, **redactar/cifrar** el crudo de webhook antes de persistir (puede traer destinatario, asunto y datos del emisor), acotar acceso, y auditar. Una baja (opt-out) debe conservarse aunque se purgue el resto.
- **Sin secretos de proveedor** en Campaigns: SMTP/SMS/WhatsApp/push API keys viven en cada **ejecutor** (Notification, TaxVision.Sms, …), cifrados. Campaigns no integra proveedores (frontera de `../02_Context_Map.md`). Corrige `SmtpProviderConfig.ApiKey` en texto plano del legado.
- **El `SenderRef` no es un secreto:** Campaign referencia el remitente (from/dominio, número, WABA id) como id opaco; el secreto/credencial que autoriza usarlo vive en el ejecutor.

---

## 6. Superficie de abuso

| Vector | Mitigación |
|---|---|
| Disparar campañas masivas de otro tenant | tenant-scoping fail-closed + `campaigns.manage` + `module.campaigns` |
| Enumerar recipients/runs/contactos de otro tenant | query filter fail-closed + `404` uniforme |
| Falsificar results | results por M2M autenticado del ejecutor (`ActorType.Service`, audience `campaigns.api`) |
| Replay de dispatch | idempotencia por `dispatch_id` + `UNIQUE(run_id, dispatch_id)` |
| Inyección vía variables de plantilla | render en Scribe (en el ejecutor) con escaping; Campaigns pasa variables como datos, no ejecuta |
| Import de contactos sin consentimiento | opt-out obligatorio + marca de consentimiento por `Contact`; ver `Domain_Design.md §Contactos` |

---

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Permiso `campaigns.manage` ya existe (módulo, MinPlanTier Pro) | `PermissionCatalog.cs:39,474-482`; `PermissionModuleMap.cs:45` | VERIFIED | 96% |
| Entitlement `module.campaigns` sembrado en Pro/Enterprise | `SubscriptionPlanCatalogSeeder.cs` | VERIFIED | 93% |
| Ejecutores permiten M2M (`ActorType.Service`) | `Sms/.../MessagesController.cs:21` | VERIFIED | 96% |
| Legado persiste JWT de usuario en BD | `Campaign.cs:87`; uso `CampaignSendService.cs:112-127` | VERIFIED | 97% |
| Legado filtra sin tenant (query global sin TenantId) | `CampaignSchedulerBackgroundService.cs:54-59` | VERIFIED | 93% |
| Sin gate/verificación de balance (Campaign solo campaña) | ADR-CAMP-001 D1 (decisión del usuario) | DECISION | 99% |
| Convenciones RBAC/RateLimit/tenant fail-closed | CLAUDE.md, `Guia_IgnoreQueryFilters`, guía RateLimit | DOCUMENTED_ONLY | 90% |
