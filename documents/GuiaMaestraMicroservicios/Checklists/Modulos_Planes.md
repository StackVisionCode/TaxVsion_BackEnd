# Checklist — Módulos, planes, add-ons, entitlements y cuotas

> Propósito: decidir si una capacidad se vende y, si se vende, llevarla con sus tres capas a la vez (permisos + gate, catálogo + revisión, frontends), sin confundir gate de módulo, rate limit y cuota de negocio.
> Un módulo nuevo **siempre** es código: el `POST` de módulos del admin no basta sin el prefijo en `PermissionModuleMap`.

**Cuándo aplica:** servicio o feature que una oficina debe tener incluida en su plan o comprar como add-on, o que limita unidades por plan.

**Formato:** *Verificación* = comando, prueba o archivo; *Origen* = investigación. Condicionales entre corchetes.

---

## 0. Decidir si se vende

- [ ] **Clasificado según la tabla "Cuándo NO comercializar"**:
  - Transversal que necesita toda oficina → prefijo **sin** módulo en `PermissionModuleMap`; `module: null` en `features.ts`.
  - De plataforma → permiso `PlatformOnly`, `[AllowActorTypes(ActorType.PlatformAdmin)]`, sin módulo.
  - Servicio interno sin endpoints de oficina → sin módulo ni gate.
  - Básico para todos → módulo `Core` (no vendible como add-on).
  - Se cobra por consumo → primitiva propia (wallet, asientos), no add-on.
  - Permiso sin endpoint → `IsReserved: true`.
  - Verificación: decisión escrita en el PR con la fila aplicada.
  - Origen: [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §5 y §10 paso 0.

## 1. Módulo vendible: los 10 pasos reales (ejemplo FinancialInsights)

- [ ] **1. Permisos** en `src/BuildingBlocks/Authorization/<Svc>Permissions.cs` y `[HasPermission(...)]` en cada endpoint (un endpoint sin permiso no pasa por el gate).
  - Verificación: `<Svc>ArchitectureTests`; revisión. · Origen: [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §10 paso 1; [RateLimit_Cuotas_Gates](../Investigaciones/RateLimit_Cuotas_Gates.md) §1.4.
- [ ] **2. Prefijo → módulo** en `src/BuildingBlocks/Authorization/PermissionModuleMap.cs`, más específico primero (`communication.meeting.` antes de `communication.`).
  - Verificación: prueba del mapa "un permiso del prefijo devuelve el módulo; ningún prefijo existente lo tapa" (`PermissionModuleMapTests`). · Origen: paso 2 y §7.
- [ ] **3. Catálogo de Auth** (`PermissionCatalog.cs`, GUID fijo, módulo) + migración de Auth solo `InsertData`; Employee/Portal en `SystemRoleDefaults` por lista explícita; admin automático (sin `ExcludeFromAdminBundle` salvo motivo); permisos peligrosos con `IsDangerous`/`PlatformOnly`.
  - Verificación: `GatedModuleFitnessTests` (Auth.Tests); prueba "el admin lo recibe; Employee/Portal según decisión". · Origen: paso 3, §4 y §8.
- [ ] **4. Módulo en Subscription**: fila en `SubscriptionModuleCatalogSeeder.Definitions` (bases nuevas) y migración idempotente `IF NOT EXISTS ... INSERT INTO [Modules]` (bases existentes).
  - Verificación: `ModuleCatalogKnownModulesTests.Subscription_modules_match_the_permission_gate`; `scripts/migrate-local.ps1 -Apply -Service Subscription`. · Origen: paso 4.
- [ ] **5. Revisión de catálogo** (`ICatalogRevision`) con Id `AAAA-MM-DD-<nombre>` nunca reutilizado, agregada **al final** de `CatalogRevisionCatalog.All`; precio por la ruta de dominio (`MarkAsCommercial` + `SetAsAddon`), planes que lo incluyen y re-anclaje solo a quien gana. Incluirlo también en `SubscriptionPlanCatalogSeeder`.
  - Verificación: `CatalogRevisionsTests` (idempotente, precio puesto, incluido en los planes previstos, salta a quien perdería algo); tras arrancar Subscription, fila en `CatalogRevisions`. · Origen: paso 5 y §4.
- [ ] [si hay oficinas en ediciones viejas] Revisión de ediciones heredadas (patrón `FinancialInsightsLegacyEditionsRevision`).
  - Verificación: `CatalogRevisionsTests`. · Origen: paso 5.
- [ ] **6. [si limita unidades por plan] Cuota como entitlement** `<prefijo>.<clave>` con default en `EntitlementKeyCatalog.PlatformDefaults` (no en `appsettings`).
  - Verificación: `EntitlementKeyCatalogTests`. · Origen: paso 6 y §4.
- [ ] **7. Servicio**: consumer de `TenantEntitlementsChangedIntegrationEvent` con `ApplyIfNewer` + invalidador; reconciliación M2M al arrancar; `PermissionPolicyProvider` + `ModuleGateRegistration.AddModuleGate(...)` + `AddUserPermissionsSource(...)`; lector de módulos registrado con `AddCachedTenantEntitlementModulesReader<T>()`.
  - Verificación: prueba del consumer de entitlements e invalidador; `ModuleGateWiringFitnessTests` (`Every_invalidator_that_clears_the_modules_cache_has_it_registered`, `No_service_registers_the_modules_reader_without_its_cache`). · Origen: paso 7; [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §1.8.
- [ ] **8. Compose del gate**: `Authorization__ModuleGate__Enforce` y `Authorization__ModuleGate__EnforcedModules__0: <modulo>` (lista **literal**, nunca variable). Modo inicial: ver decisión pendiente.
  - Verificación: `ModuleGateDeployWiringTests.The_staged_module_list_is_never_a_variable`; errata en la lista revienta el arranque (`ModuleGateSettingsTests`). · Origen: paso 8 y §7.
- [ ] **9. Frontends**: CRM `features.ts` (`FEATURES` + `MODULE_LABELS`), `app.routes.ts` (`canMatch: [accessCanMatch]`, `data: { feature }`), sidebar con `featureId`, `plan-picker-modal`, `access-view`, `role-visuals`; portal y landing (`module-labels.ts`, `MODULE_DESCRIPTIONS`). Mismo nombre visible en los tres.
  - Verificación: `features.spec.ts`, `module-label.spec.ts`, `sidebar.component.spec.ts` (con y sin módulo, con y sin permiso). · Origen: paso 9 y §8.
- [ ] **10. Verificar**: migraciones aplicadas en local; revisión aplicada; log `Module gate registered: ENFORCING (modules: <modulo>)` (o `log-only`); oficina con el módulo lo ve en menú y en /account ("Included in your plan"); oficina sin él recibe 403 `Authz.ModuleUnavailable` y ve la oferta de add-on.
  - Verificación: log de arranque y prueba manual en vivo. · Origen: paso 10.

## 2. Gate de módulo: semántica que el servicio debe respetar

- [ ] **`null` (sin fila) no gatea; `[]` deniega (suscripción vencida); módulo presente permite.** PlatformAdmin y Service siempre habilitados.
  - Verificación: prueba del gate del servicio con `Enforce=true` (patrón `TenantModuleEntitlementsSourceTests`: `An_empty_projection_denies`, `A_missing_projection_is_NOT_the_same_as_an_empty_one`). · Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §1.8 y §7.
- [ ] **Proyección de entitlements poblada para tenants existentes** (si no, fail-open en la API): reconciliación/pull al arrancar.
  - Verificación: ver [Proyecciones_Reconciliation](Proyecciones_Reconciliation.md) §3. · Origen: [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §7; [Proyecciones](../Investigaciones/Proyecciones.md) §3.3.
- [ ] **Contadores de las fitness del gate actualizados** (`The_fourteen_gated_services_are_still_wired` cuenta 15 hoy) y lista de `ModuleGateDeployWiringTests` si usa secret.
  - Verificación: gate. · Origen: [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §8 punto 6.
- [ ] **Sin lógica por nombre de plan** en código.
  - Verificación: `NoPlanNameLogicFitnessTests` (BB). · Origen: [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §7.

## 3. No confundir los mecanismos

| Mecanismo | Responde | Respuesta |
|---|---|---|
| Rate limit pre-auth (Gateway) | ¿Esta IP martilla login/registro? | 429 |
| Load shedding (Gateway) | ¿Flota saturada? | 503 |
| Rate limit tiered (servicio) | ¿Este usuario/tenant excede su ritmo? | 429 (fail-open) |
| Cuota por plan | ¿Cuánto ritmo le toca a su plan? | ajusta el número |
| Gate de módulo | ¿Su plan incluye el módulo? | 403 |
| Cuota de negocio | ¿Le quedan unidades este mes? | `Result` de dominio |
| Gate de asientos | ¿Usuario dentro del cupo pagado? | 4xx en el Gateway |

Origen: [RateLimit_Cuotas_Gates](../Investigaciones/RateLimit_Cuotas_Gates.md) §1.

- [ ] **Límite de unidades = entitlement numérico** leído de la proyección y aplicado en el dominio con `Result`, nunca con el rate limiter; período anclado al inicio de la suscripción en la zona de la oficina.
  - Verificación: T-DOM "N+1 en el período → error de negocio". · Origen: [RateLimit_Cuotas_Gates](../Investigaciones/RateLimit_Cuotas_Gates.md) §5, §7 y §10.6.
- [ ] [si escala rate limit por plan] `RateLimit:EnforceTierQuotas=true` solo cuando existen la proyección de plan y el acquirer M2M; lectores reales (`ScopedTenantPlanCodeReader`, `ScopedPlanRateLimitReader`) registrados **antes** de `AddTieredRateLimiting()`; `SubscriptionClient__BaseUrl: http://subscription-api:8080` en compose.
  - Verificación: `Program.cs` (patrón FI `:61-72`); compose. · Origen: [RateLimit_Cuotas_Gates](../Investigaciones/RateLimit_Cuotas_Gates.md) §1.3, §5 y §10.

## 4. Nunca

- [ ] Editar a mano una edición publicada ni bajar términos a clientes actuales (se cambia con revisión o con el admin: vista previa + token + motivo).
  - Verificación: revisión del PR. · Origen: [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §4.
- [ ] Re-publicar entitlements o estados sin revisar los consumidores que mandan correos.
  - Verificación: búsqueda de consumers del evento. · Origen: [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §4.
- [ ] Tomar el seeder de planes como fuente de precios (siembra precios viejos que la revisión corrige) ni seguir `Guia_Cablear_Modulo_Plan_Microservicio.md` (superada: `PlanModuleCatalog`, `MinPlanTier`).
  - Verificación: precios en la revisión vigente. · Origen: [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §3 y §6.

## Decisiones pendientes

- Modo inicial del gate para un módulo nuevo: `"true"` literal (FinancialInsights) o `${<SVC>_MODULE_GATE_ENFORCE:-false}` como los otros 14 ([Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §3.4; [Auth_RBAC](../Investigaciones/Auth_RBAC.md) problema 4).
- `wallet` módulo (backend) vs transversal (CRM) ([Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §3.5).
- Oficinas sin suscripción con el gate en enforce ([Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §3.7).
