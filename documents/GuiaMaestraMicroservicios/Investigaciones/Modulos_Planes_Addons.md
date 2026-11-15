# Módulos, planes, add-ons, entitlements y gates — investigación

Fecha: 2026-11-15 · Trabajo de solo lectura · Repos: backend `TaxVsion_BackEnd` (rutas relativas a esa raíz salvo que se indique) y CRM `TaxVsion_Front`.

Leyenda: **[Código verificado]** leído hoy · **[Documentación histórica]** memoria/planes/guías · **[Recomendación]** propuesta de esta investigación · **[Decisión pendiente]** lo decide el dueño.

---

## 1. Estado actual (modelo)

### 1.1 Las piezas y quién es dueño de cada una

| Pieza | Dueño | Dónde (código) | Tipo |
|---|---|---|---|
| **Módulo comercial** (`customers`, `signatures`, `documents`, `planner`, `email`, `meetings`, `comms`, `campaigns`, `wallet`, `financial_insights`) | Subscription, tabla `Modules` | `src/Services/Subscription/TaxVision.Subscription.Domain/Catalog/Module.cs`; seed en `.../Infrastructure/Persistence/SubscriptionModuleCatalogSeeder.cs` | [Código verificado] |
| Categoría del módulo | `ModuleCategory { Core, Commercial }` | `.../Domain/Catalog/ModuleCategory.cs` | [Código verificado] |
| Precio de add-on de módulo | `Module.AddonPriceMonthlyUsd/YearlyUsd`; vendible si `Commercial` y ambos precios > 0 (`IsSellableAsAddon`, `Module.cs:17-18`); código de add-on `module-addon-{code}` | idem | [Código verificado] |
| **Plan** (`starter`, `pro`, `enterprise`) con Ids fijos | Subscription, agregado `SubscriptionPlan` (`TenantlessAggregateRoot`) | `.../Domain/Plans/PlanCatalog.cs`; seed en `SubscriptionPlanCatalogSeeder.cs` | [Código verificado] |
| **Edición/versión** del plan (Draft → Published → Superseded/Archived) con módulos, precios, asientos, almacenamiento, entitlements | `SubscriptionPlanVersion` | admin `PlansAdminController` (`admin/subscription/plans`) | [Código verificado] |
| **Revisión de catálogo** (cambio del catálogo que viaja en el código y se aplica una vez al desplegar) | `ICatalogRevision` + `CatalogRevisionCatalog.All` + `CatalogRevisionRunner` + tabla `CatalogRevisions` | `.../Application/Catalog/Revisions/ICatalogRevision.cs`, `.../Infrastructure/Catalog/CatalogRevisionRunner.cs` | [Código verificado] |
| **Entitlement** (clave → valor: `seats.max`, `storage.max_bytes`, `module.<code>`, `financial.documents_per_month`…) | Subscription calcula el snapshot por oficina | `EntitlementSnapshotBuilder`, `ContractedEntitlementPolicy`, `EntitlementKeyCatalog` | [Código verificado] |
| **Proyección de entitlements** en cada servicio | Cada servicio consumidor, desde `TenantEntitlementsChangedIntegrationEvent` | p. ej. `src/Services/FinancialInsights/.../Projections/Consumers/ProjectionConsumer.cs:76-110` | [Código verificado] |
| **Permiso** y su "carpeta" UI | Auth, `PermissionCatalog` | `src/Services/Auth/Domain/Roles/PermissionCatalog.cs` | [Código verificado] |
| **Permiso → módulo comercial** (para el gate) | BuildingBlocks, mapa por prefijo | `src/BuildingBlocks/Authorization/PermissionModuleMap.cs` | [Código verificado] |
| **Gate de módulo** en cada request | BuildingBlocks + configuración por servicio | `ModuleGateSettings.cs`, `ModuleGateRegistration.cs`, `TenantModuleEntitlementsSource.cs` (en `src/BuildingBlocks/BuildingBlocks.Web/ActorTypeAuthorization/`) | [Código verificado] |
| **Bundles de roles de sistema** (TenantAdmin, Employee, Customer Portal) | Auth, `SystemRoleDefaults` | `PermissionCatalog.cs:2276-…` | [Código verificado] |
| **Frontend** (menú, rutas, etiquetas) | CRM `features.ts` | `TaxVsion_Front/src/app/core/access/features.ts` | [Código verificado] |

**[Documentación histórica]** El ADR `TaxVsion_BackEnd/documents/architecture/catalog-model-adr.md` fijó: catálogo data-driven, módulos únicos, storage escalable; `Seats` y `WalletTopUp` son primitivas propias y **nunca** se unifican con add-ons (memoria `project_catalogo_planes_addons_modulos_reales.md`).

### 1.2 Catálogo de módulos vigente — [Código verificado]

`SubscriptionModuleCatalogSeeder.Definitions`:

| Código | Categoría | Add-on mensual/anual |
|---|---|---|
| customers, signatures, documents, planner, comms, wallet | Core | — |
| email, meetings, campaigns | Commercial | $29 / $290 |
| financial_insights (`b2000000-…-00000000000a`) | Commercial | $29 / $290 |

`PermissionModuleMap.PrefixToModule` (`PermissionModuleMap.cs:48-69`): `customers.`→customers, `financial.`→financial_insights, `signature.`→signatures, `documents.`/`cloudstorage.`/`scribe.`→documents, `calendar.`/`reminders.`/`tasks.`/`notes.`→planner, `correspondence.`/`connectors.`/`postmaster.`/`email.`→email, `communication.meeting.`→meetings (**antes** de `communication.`), `communication.`/`comms.`→comms, `campaigns.`→campaigns, `wallet.`→wallet. Exentos (línea 94-99): `communication.notification.read`, `communication.support.open`, `communication.support.agent`. Sin módulo = **siempre efectivo** (auth, billing, subscription, tenant, notification, sms, growth, catalog, inventory, invoicing…).

`ModuleCatalogKnownModulesTests` (`deploy/tests/TaxVision.Subscription.Tests/Domain/ModuleCatalogKnownModulesTests.cs`) exige que los códigos del seeder sean exactamente `PermissionModuleMap.KnownModules`.

### 1.3 Planes y precios vigentes — [Código verificado]

- Seeder (solo base vacía): Pro $129, 10 asientos, 50 GB; Enterprise $299, 25 asientos, 200 GB; ambos con `financial_insights` (`SubscriptionPlanCatalogSeeder.cs:60-92`).
- Revisión R2 `2026-11-11-r2-single-enterprise` (`R2SingleEnterpriseRevision.cs:20-25`): **solo Enterprise a la venta**, $99/$999, 3 asientos, 15 GB; Pro $69/$699 (2 asientos, 10 GB) y Starter $39/$399 (1 asiento, 5 GB) definidos pero fuera de venta. En una base nueva corre seeder → R1…R5 al arrancar, así que manda R2.
- **[Documentación histórica]** Trial = `enterprise` por configuración (`DefaultPlanCode=enterprise`, D7). Add-ons de storage $3/$6/$10 (+2/+5/+10 GB, `MutexGroup = storage`, `MergeStrategy.Sum` sobre `storage.max_bytes`) y asiento extra $15 no cambian. Stripe usa importes dinámicos.

### 1.4 Revisiones de catálogo aplicadas en orden — [Código verificado]

`CatalogRevisionCatalog.All` (`ICatalogRevision.cs:45-55`, "Agregar al final; nunca reordenar ni borrar"):
1. `R1RebaselineRevision` — re-anclar a la publicada si los términos comerciales son iguales.
2. `R2SingleEnterpriseRevision` (`2026-11-11-r2-single-enterprise`).
3. `R3PlanDescriptionsRevision` (descripciones sin cifras).
4. `FinancialInsightsModuleRevision` (`2026-11-13-r4-financial-insights`).
5. `FinancialInsightsLegacyEditionsRevision` (`2026-11-13-financial-insights-legacy-editions`).

Ejecución (`Subscription.Api/Program.cs:272-287`): seeders (módulos, planes, add-ons, asientos) → `ICatalogRevisionRunner.RunAsync(CatalogRevisionCatalog.All)` **antes** de `app.Run()`. `CatalogRevisionRunner`: candado distribuido `bootstrap:catalog-revisions`; por revisión, scope + transacción + fila en `CatalogRevisions` en la misma transacción; una ya aplicada no vuelve a correr; una que falla no deja nada y **frena el arranque**. Como Wolverine todavía no arrancó, los avisos los publica `PublishCatalogRevisionFollowUpsHandler` por el outbox al arrancar (`CatalogRevisionFollowUpStarter`). Actor `Guid.Empty`, correlación `deploy:{id}`.

### 1.5 Entitlements: cálculo, viaje y proyección — [Código verificado]

- Snapshot desde la **edición contratada** (no la publicada) — `ContractedEntitlementPolicy`; un `module.*` vale `true` solo si la base da acceso (`grantsAccess && enabled`, `ContractedEntitlementPolicy.cs:101-106`). **[Documentación histórica]** acceso = {Trialing, Active, PastDue, GracePeriod}; un add-on cuenta solo si él está Active y la base da acceso (`Guia_Facturacion_AddOns.md` §7).
- `EntitlementKeyCatalog.PlatformDefaults` (`.../Domain/Plans/EntitlementKeyCatalog.cs:10-16`) rellena claves de plataforma que a una edición le faltan (comunicación, storage, firma, `FinancialInsightsEntitlementCatalog.Defaults`); nunca pisa una clave que la edición ya define.
- `financial.documents_per_month` = 200 por defecto (`FinancialInsightsEntitlementCatalog.cs:12-17`).
- Viaja en `TenantEntitlementsChangedIntegrationEvent` (`src/BuildingBlocks/Messaging/SubscriptionIntegrationEvents/TenantEntitlementsChangedIntegrationEvent.cs`): `RevisionNumber`, `ChangedKeys`, `PlanCode`, `SubscriptionStatus`, asientos, `MaxStaffUsers?`, `CurrentPeriodStartUtc?`, `EntitlementValues` (snapshot completo). Identidad: nombre CLR completo. Exchange `taxvision-events`.
- Cada servicio proyecta (`ApplyIfNewer` por `RevisionNumber`), invalida su caché (`ITenantPlanCodeCacheInvalidator`) y lee los módulos con `evt.ExtractEnabledModules()`. FinancialInsights además parsea su cuota (`FinancialEntitlements.ParseDocumentsPerMonth`).
- Reconciliación por M2M: Subscription `InternalTenantEntitlementsController` (`internal/tenants/{id}/entitlements`) [Código verificado: el controlador existe]; **[Documentación histórica]** FinancialInsights lo usa con `TenantEntitlementsReconciliationJob`.

### 1.6 Gate de módulo — [Código verificado]

- Configuración `Authorization:ModuleGate` (`ModuleGateSettings.cs:31-47`): `Enforce` ausente/false → solo registra; `Enforce: true` + `EnforcedModules` → deniega esos; `Enforce: true` sin lista → deniega todos.
- `ModuleGateRegistration.AddModuleGate(services, configuration)` registra `TenantModuleEntitlementsSource`, **valida** los nombres contra `PermissionModuleMap.KnownModules` (revienta en enforce con un nombre desconocido) y escribe en el log "Module gate registered: ENFORCING (modules: …)" o "log-only".
- Semántica de la fuente (`TenantModuleEntitlementsSource.cs:31-45`): **sin fila de proyección = no sé → pasa (fail-open)**; **lista vacía `[]` = sé que no tiene módulos → deniega** (caso de suscripción `Expired`).
- Despliegue (`deploy/docker/docker-compose.yml`): 14 servicios con `Authorization__ModuleGate__Enforce: ${<SVC>_MODULE_GATE_ENFORCE:-false}` y la lista **literal** (`EnforcedModules__0: <módulo>`); `deploy.yml:498-512` pasa los secrets. **FinancialInsights es distinto:** `Enforce: "true"` literal + `financial_insights` (`docker-compose.yml:1659-1660`) → en producción **deniega desde el primer día**; el resto sigue en log-only salvo que su secret esté en `true`.
- Communication (Node): `COMMUNICATION_MODULE_GATE_ENFORCE` (booleano), espejo de exenciones vigilado por `The_node_exemption_list_mirrors_the_dotnet_one`.

### 1.7 Permisos y bundles — [Código verificado]

- `PermissionDefinition(Id, Code, Module, Description, IsCustomerPortal, IsAssignableByTenant = true, PlatformOnly = false, AllowedActorTypes = null, IsDangerous = false, IsReserved = false, ExcludeFromAdminBundle = false)` (`PermissionCatalog.cs:386-415`). `Module` aquí es la **carpeta UI** (para FinancialInsights coincide: `"financial_insights"`); el módulo del gate lo da `PermissionModuleMap`.
- Bundle TenantAdmin (`PermissionCatalog.cs:2290-2303`): todo lo que **no** sea portal, `PlatformOnly`, `IsDangerous`, `IsReserved` ni `ExcludeFromAdminBundle`, y admita `TenantAdmin` → un permiso nuevo normal **entra solo** al admin.
- Bundle Employee: lista explícita; FinancialInsights añade `FinancialImport` y `FinancialView` ("El módulo financial_insights lo filtra el gate por plan o add-on").
- Bundle Customer Portal: lista explícita; incluye `FinancialPortalImport`.
- Permisos de FinancialInsights: `financial.import` (`a1000000-…-0001a0`), `financial.view` (`…-0001a1`), `financial.portal.import` (`…-0001a2`, `IsCustomerPortal: true`) — constantes en `src/BuildingBlocks/Authorization/FinancialInsightsPermissions.cs`; migraciones Auth `20261113201902_AddFinancialInsightsPermissions`, `20261113235202_AddFinancialPortalImportPermission`, `20261114011323_FinancialInsightsPermissionDescriptions`.
- Catálogo de planes: solo plataforma. `SubscriptionPermissions.CatalogManage = "subscription.catalog.manage"`, `AdminCrossTenant = "subscription.admin.cross_tenant"`, `AddOnsManage = "addons.manage"` (`src/BuildingBlocks/Authorization/SubscriptionPermissions.cs:15-21`). `ModulesAdminController` (`admin/subscription/modules`, GET/PUT/POST) con `[HasPermission(CatalogManage)]` y `[AllowActorTypes(ActorType.PlatformAdmin)]`.

### 1.8 Publicación, compra, activación y desactivación

- **Admin de plataforma** [Código verificado, rutas de `PlansAdminController`]: `PUT {planId}/modules`, `PUT {planId}/versions/{versionId}/draft`, `POST {planId}/versions`, `POST {planId}/versions/{versionId}/publish`, `deprecate`/`archive`/`off-sale` con `/preview`, `on-sale`, `rank`, `featured`, `prices`. **[Documentación histórica]** retirar/deprecar/archivar exige vista previa + token (hash del estado) + motivo; todo deja `CatalogAuditEntry`; no hay front de plataforma (cliente = Postman).
- **Migración de clientes** [Documentación histórica]: `SubscriberMigration` — inmediata solo si es "pura mejora"; si no, en la renovación con aviso de 30–365 días (`subscription.version_change_scheduled`).
- **Compra de add-on por la oficina** [Código verificado: `AddOnsController` con `POST addons`, `POST addons/checkout`, `POST addons/checkout/{intentId}/cancel`, `POST addons/{id}/cancel`]. **[Documentación histórica]** (`Subscriptions/Guia_Facturacion_AddOns.md`): add-on dependiente de la base, co-terminado al aniversario, prorrateo solo del período inicial, absorbido sin doble cobro al subir a un plan que lo incluye, Subscription publica `AddOnRenewalDue` y PaymentApp cobra; eventos `AddOnActivated`, `AddOnCancelled`, `TenantEntitlementsChanged`.
- **Apagado**: estados fuera del acceso (Draft/Suspended/Cancelled/Expired) → `module.*` en false → proyecciones con `[]` → el gate deniega (cuando está en enforce).

### 1.9 Frontend — [Código verificado]

- CRM `src/app/core/access/features.ts`: `FeatureDefinition { id, label, route, module, anyOf }`; FinancialInsights = `{ id: 'financial', label: 'Financial Insights', route: 'financial', module: 'financial_insights', anyOf: ['financial.import', 'financial.view'] }` (líneas 234-240). `MODULE_LABELS` (`features.ts:345-356`) con `financial_insights: 'Financial Insights'`; un código desconocido se muestra crudo.
- Ruta con `canMatch: [accessCanMatch]` y `data: { feature: 'financial' }` (`src/app/app.routes.ts:243-246`).
- Sidebar: `{ label: 'Financial Insights', icon: 'stats-chart-outline', route: '/financial', featureId: 'financial', section: 'Business' }` (`src/app/layout/sidebar/sidebar.component.ts:192`) con specs en `sidebar.component.spec.ts:107-119`.
- Otras etiquetas: `plan-picker-modal.component.ts:43`, `user-management/ui/edit-access-drawer/access-view.ts:110`, `roles-panel/role-visuals.ts:40`.
- **[Documentación histórica]** Portal: `portal-financial.model.ts`; landing (repo `landing`): `module-labels.ts` (`MODULE_LABELS`, `MODULE_ICONS`, `OFFERED_MODULES`) y `MODULE_DESCRIPTIONS`. Bootstrap del front: `/auth/me/effective-access` (`AuthController.cs:282`).

## 2. Evidencia del caso FinancialInsights (módulo vendible nuevo)

**[Documentación histórica]** `Implementaciones/FinancialInsights/Revision_y_Plan_FinancialInsights.md` (2026-11-13) encontró que el servicio entró **sin ser vendible**: sin plan, sin precio de add-on, permisos con `ExcludeFromAdminBundle: true` (nadie veía el menú), módulo insertado con SQL crudo sin precio, sin codegen Static, perfil compose `financial` que no se desplegaba, cuota fija de 10 documentos en appsettings, landing sin tocar, gate del CI rojo por contratos de offboarding. Decisiones D1–D7 del dueño.

**[Código verificado]** Cómo quedó:

| Paso | Evidencia |
|---|---|
| Prefijo de permiso → módulo | `PermissionModuleMap.cs:51` `("financial.", "financial_insights")` |
| Constantes de permiso compartidas | `BuildingBlocks/Authorization/FinancialInsightsPermissions.cs` |
| Permisos en Auth con Guid nuevo, carpeta UI y descripción | `PermissionCatalog.cs:538-559` + 3 migraciones de Auth |
| Admin los recibe (sin `ExcludeFromAdminBundle`), Employee import+view, Portal portal.import | `PermissionCatalog.cs:2290-2303`, `:2471-2474`, `:2500` |
| Módulo en `Modules` (bases existentes) | migración Subscription `20261113201921_AddFinancialInsightsModule` (insert idempotente `IF NOT EXISTS`, Commercial, sin precio) |
| Módulo en el seeder (bases nuevas) con precio | `SubscriptionModuleCatalogSeeder.cs:72-79` |
| Incluido en Pro y Enterprise (bases nuevas) | `SubscriptionPlanCatalogSeeder.cs:73,91` |
| Precio de add-on + inclusión en Enterprise/Pro + re-anclaje "solo mejora" (bases existentes) | `FinancialInsightsModuleRevision.cs` (`MarkAsCommercial`, `SetAsAddon(29, 290)`, `ReviseModules`, `RebaselineVersion`, auditoría) |
| Oficinas en ediciones viejas con otros términos | `FinancialInsightsLegacyEditionsRevision.cs` (edición heredada Superseded con el módulo y los mismos términos; `SubscriptionPlan.AddLegacyEditionWithModules` según memoria) |
| Entitlement de cuota | `FinancialInsightsEntitlementCatalog.cs` + `EntitlementKeyCatalog.cs:15` |
| Proyección y cuota en el servicio | `ProjectionConsumer.cs:76-110`, `FinancialEntitlements.cs:13-17` |
| Gate registrado y en enforce | `FinancialInsights.Api/Program.cs:57-58`; `docker-compose.yml:1659-1660` |
| Front | `features.ts:234-240`, `MODULE_LABELS`, ruta y sidebar |

**[Documentación histórica]** Verificado en local: 13 oficinas Enterprise/Pro con el módulo, CRM (sidebar + /financial) y landing /account ("Included in your plan"); secrets nuevos van al environment `production-extra` (`ProductionExtraSecretsDeployTests`).

## 3. Problemas y contradicciones

1. **[Documentación histórica desactualizada]** `TaxVsion_BackEnd/documents/architecture/Guia_Cablear_Modulo_Plan_Microservicio.md` sigue describiendo `PlanModuleCatalog`, `ModuleAddOnCatalog` y `MinPlanTier` (líneas 32-33, 65-70). Hoy manda la tabla `Modules` de Subscription; `MinPlanTier` se quitó (F7, migración `DropPermissionMinPlanTier`); la tabla `PlanModules` de Auth se borró (`20261016164318_RemoveDuplicatePlanModulesCatalog`). La memoria `project_catalogo_planes_addons_modulos_reales.md` (F1) también describe el `PlanModule` de Auth como vigente.
2. **[Documentación histórica desactualizada]** `Subscriptions/Guia_Facturacion_AddOns.md` §9 lista Starter $49 / Pro $129 / Enterprise $299 y módulos `reports`, `marketing`, `builder`, `irs`, `miles` con add-ons de $49. Hoy: R2 ($39/$69/$99), 10 módulos, ninguno de esos cinco.
3. **[Código verificado]** El seeder de planes siembra precios viejos (Pro $129, Enterprise $299) que R2 corrige en el mismo arranque. Funciona, pero la guía no debe tomar el seeder como fuente de precios.
4. **[Código verificado]** El gate de FinancialInsights es **enforce literal** en compose, mientras los otros 14 dependen de un secret `${…:-false}`. `ModuleGateDeployWiringTests.ModuleGateEnvVars` sigue diciendo "14 .NET + Communication" y no cubre FinancialInsights; `ModuleGateWiringFitnessTests.The_fourteen_gated_services_are_still_wired` ya cuenta 15. **[Decisión pendiente]** si un módulo nuevo nace en enforce (como FinancialInsights) o sigue el escalón con secret.
5. **[Código verificado]** `wallet` es módulo en el backend (`PermissionModuleMap.cs:68`, gate de Wallet en compose) pero el CRM lo trata como transversal (`features.ts`: "El monedero se cobra por consumo… transversal", `module: null`). Hoy no rompe nada porque `wallet` es Core (todos los planes), pero las dos fuentes dicen cosas distintas.
6. **[Código verificado]** El catálogo es "dinámico" solo a medias: `ModulesAdminController` permite `POST` de un módulo, pero sin el prefijo en `PermissionModuleMap` (código + deploy en los 25 servicios) el gate no lo conoce, y `ModuleCatalogKnownModulesTests` exige que seeder y mapa coincidan. Un módulo nuevo **siempre** requiere código.
7. **[Documentación histórica]** Fail-open sin proyección: un tenant real sin suscripción (`codigoprogramar-tax`, Kind=Customer) pasaría el gate en enforce. Dato de onboarding sin decidir (memoria A6).
8. **[Documentación histórica]** A6 "faltan casos 2/3/8 del Anexo C" (verificación en vivo con un tenant Starter) y decisión de producto sobre `call.record`/`meeting.record` fuera de los bundles de Employee y Portal.
9. **[Código verificado]** `PermissionDefinition.Module` (carpeta UI) y el módulo del gate son dos conceptos con nombre parecido; en FinancialInsights coinciden, en otros no (memoria F1: "no son dos vocabularios de la misma cosa").
10. **[Documentación histórica]** `TenantAdminCommunicationExtras` sigue como excepción en el bundle del admin (F6 diferido, issue 233).

## 4. Buenas prácticas

- **Un módulo nuevo es código, no solo dato:** prefijo en `PermissionModuleMap`, fila en `SubscriptionModuleCatalogSeeder`, migración idempotente para bases existentes y revisión de catálogo para precios/inclusión.
- **Nunca editar a mano** una edición publicada ni bajar términos a clientes actuales: se cambia con una revisión (deploy) o con el admin (vista previa + token + motivo) y, si quita algo, con migración agendada.
- **Revisiones:** Id `AAAA-MM-DD-<nombre>` nunca reutilizado, agregar al final de `CatalogRevisionCatalog.All`, idempotentes, todo por los agregados, con auditoría y re-anclaje solo cuando el cliente "solo gana".
- **Precio de add-on por la ruta de dominio** (`MarkAsCommercial` + `SetAsAddon`), no con SQL crudo.
- **Cuotas por plan como entitlement** (`<prefijo>.<clave>`) con default en `EntitlementKeyCatalog`, no en `appsettings`.
- **Permisos:** Guid nuevo, constante en `BuildingBlocks/Authorization/<Svc>Permissions.cs`, `[HasPermission]` en cada endpoint; dejar que el admin lo reciba por el filtro (sin `ExcludeFromAdminBundle` salvo motivo), Employee/Portal por lista explícita.
- **Gate:** `ModuleGateRegistration.AddModuleGate(...)` (valida y anuncia), proyección + invalidador de caché del mismo servicio, lista literal en compose.
- **Front:** `FEATURES` + `MODULE_LABELS` + ruta con `accessCanMatch` + sidebar con `featureId`; mismo nombre visible en CRM, portal y landing.
- **Antes de re-publicar entitlements** o estados, revisar consumidores que mandan correos.

## 5. Cuándo NO comercializar

| Caso | Qué hacer | Evidencia |
|---|---|---|
| Función transversal que necesita toda oficina (notificaciones, soporte, usuarios, facturación propia, SMS por consumo, catálogo/inventario) | Prefijo **sin** módulo en `PermissionModuleMap` (siempre efectivo); `module: null` en `features.ts` | `PermissionModuleMap.cs:8-12`, exenciones `:94-99`; Employee bundle "transversales (sin módulo): siempre efectivos" |
| Algo de plataforma (PlatformAdmin) | Permiso `PlatformOnly`, `[AllowActorTypes(ActorType.PlatformAdmin)]`, sin módulo | `subscription.catalog.manage`, `ModulesAdminController` |
| Servicio interno sin endpoints de oficina (workers, ContentScanner, Gateway, Recorder/TranscriptWorker) | Sin módulo, sin gate | no aparecen en `PermissionModuleMap` |
| Parte básica incluida en todos los planes | Módulo `Core` (no vendible como add-on) | `ModuleCategory.Core`; customers, documents, planner, comms, signatures, wallet |
| Se cobra por consumo | Primitiva propia (wallet top-up, asientos), no add-on | ADR: Seats y WalletTopUp nunca se unifican con add-ons |
| Permiso declarado sin endpoint | `IsReserved: true` (no se concede ni se ofrece) | `PermissionCatalog.cs:405-407`; caso `reports.view` (memoria A6) |

## 6. Recomendación

- **[Recomendación]** La guía debe tener un procedimiento único "módulo vendible nuevo" (sección 10) y marcar como **superada** `Guia_Cablear_Modulo_Plan_Microservicio.md`.
- **[Recomendación]** Pedir que el módulo nuevo llegue con las tres capas a la vez (permisos+gate, catálogo+revisión, front en CRM/portal/landing); FinancialInsights mostró que entrar solo con el servicio deja algo que nadie puede comprar ni ver.
- **[Decisión pendiente]** Modo inicial del gate para módulos nuevos (punto 3.4); `wallet` transversal o módulo en el front (3.5); qué hacer con oficinas sin suscripción en enforce (3.7).

## 7. Riesgos

| Riesgo | Efecto | Vigilancia |
|---|---|---|
| Prefijo nuevo no mapeado | El gate nunca lo bloquea aunque el plan lo cobre | `ModuleCatalogKnownModulesTests`, `GatedModuleFitnessTests` (Auth) |
| Orden de prefijos (`communication.meeting.` después de `communication.`) | Meetings regalado en todos los planes | test del mapa (memoria) |
| Errata en `EnforcedModules` | Módulo en log-only creyendo que deniega | `ModuleGateSettings.ValidateOrThrow` al arrancar; `ModuleGateSettingsTests` |
| Lista de módulos como variable en compose | Secret vacío → enforce de todos | `ModuleGateDeployWiringTests.The_staged_module_list_is_never_a_variable` |
| Servicio con gate sin invalidar caché al cambiar de plan | 403 viejos hasta TTL (1 min) | `ModuleGateWiringFitnessTests` |
| Revisión que baja términos | Clientes pierden asientos/GB/módulos | re-anclaje solo-mejora + `CatalogRevisionsTests` |
| Revisión que falla | Subscription no arranca | diseño del runner (preferible a quedar a medias) |
| Lógica por nombre de plan en código | Se rompe al cambiar el catálogo | `NoPlanNameLogicFitnessTests` |
| Permiso nuevo peligroso entra al admin | Escalada | marcar `IsDangerous`/`PlatformOnly` |
| Proyección vacía en servicio nuevo | Fail-open en la API, rechazo en worker (caso FI) | reconciliación M2M al arrancar |

## 8. Pruebas

Existentes:
- `deploy/tests/TaxVision.Subscription.Tests/Domain/ModuleCatalogKnownModulesTests.cs` — `Subscription_modules_match_the_permission_gate`.
- `deploy/tests/TaxVision.Subscription.Tests/Application/CatalogRevisionsTests.cs` (incluye FinancialInsights).
- `deploy/tests/TaxVision.Subscription.Tests/Domain/EntitlementKeyCatalogTests.cs`, `NoReportsReferencesInCatalogTests.cs`.
- `deploy/tests/TaxVision.Auth.Tests/Domain/GatedModuleFitnessTests.cs`.
- `deploy/tests/TaxVision.BuildingBlocks.Tests/ActorTypeAuthorization/ModuleGateSettingsTests.cs`, `ModuleGateWiringFitnessTests.cs` (`Every_invalidator_that_clears_the_modules_cache_has_it_registered`, `Every_gated_service_invalidates_the_modules_cache_on_a_plan_change`, `No_service_registers_the_modules_reader_without_its_cache`, `The_fourteen_gated_services_are_still_wired` = 15, `The_node_exemption_list_mirrors_the_dotnet_one`).
- `deploy/tests/TaxVision.BuildingBlocks.Tests/Deploy/ModuleGateDeployWiringTests.cs`, `ProductionExtraSecretsDeployTests.cs`.
- `deploy/tests/TaxVision.BuildingBlocks.Tests/Architecture/NoPlanNameLogicFitnessTests.cs`.
- `deploy/tests/TaxVision.BuildingBlocks.Tests/Offboarding/UserReferenceContractTests.cs`, `OffboardingParticipantsContractTests.cs` (las que dejaron rojo el gate al entrar FinancialInsights).
- FinancialInsights: `deploy/tests/TaxVision.FinancialInsights.Tests/Application/ProjectionConsumerTests.cs`.
- **[Documentación histórica]** Integración del gate en enforce contra el host real de Signature (403 con `code`, `reason`, `module` RFC 9457); `TenantModuleEntitlementsSourceTests` (null vs `[]`).
- CRM: `src/app/core/access/features.spec.ts`, `module-label.spec.ts`, `src/app/layout/sidebar/sidebar.component.spec.ts`.

Las que debe tener un módulo vendible nuevo:
1. `ModuleCatalogKnownModulesTests` en verde (seeder ↔ mapa).
2. Prueba del mapa: un permiso del prefijo devuelve el módulo; ningún prefijo existente lo tapa.
3. Prueba de la revisión de catálogo: idempotente, precio puesto, incluido en los planes previstos, re-ancla solo a quien gana y salta a quien perdería algo.
4. Prueba del consumer de entitlements del servicio (`ApplyIfNewer`, módulos, cuota) y del invalidador.
5. Prueba del gate del servicio con `Enforce=true` (403 sin módulo, 200 con módulo, `[]` deniega, sin fila pasa).
6. Actualizar los contadores de las fitness del gate y la lista de `ModuleGateDeployWiringTests` si usa secret.
7. Bundles: el admin lo recibe; Employee/Portal según decisión.
8. Front: spec del sidebar/feature (con y sin módulo, con y sin permiso) y de `moduleLabel`.

## 9. Dependencias

- Subscription (catálogo, ediciones, revisiones, snapshot, evento), Auth (permisos, bundles, techo por módulo, `/auth/me/effective-access`), BuildingBlocks (`PermissionModuleMap`, gate, caché), cada servicio (proyección + gate), PaymentApp (cobro), Communication Node (espejo de exenciones y `catalog.changed`), CRM/portal/landing.
- Redis (caché de módulos, candado de revisiones), RabbitMQ (`taxvision-events`, `taxvision-catalog`).

## 10. Material para la guía — procedimiento real "módulo vendible nuevo" (ejemplo FinancialInsights)

**Paso 0 — Decidir si se vende** (tabla de la sección 5). Si es Core o transversal, no hay add-on.

**Paso 1 — Permisos** (`src/BuildingBlocks/Authorization/FinancialInsightsPermissions.cs`):
```csharp
public static class FinancialInsightsPermissions
{
    public const string Import = "financial.import";
    public const string View = "financial.view";
    public const string PortalImport = "financial.portal.import";
}
```

**Paso 2 — Prefijo → módulo** (`PermissionModuleMap.cs:51`), respetando el orden "más específico primero":
```csharp
("financial.", "financial_insights"),
```

**Paso 3 — Catálogo de Auth** (`PermissionCatalog.cs:538-559`) + migración de Auth:
```csharp
new(new Guid("a1000000-0000-0000-0000-0000000001a0"), FinancialImport, "financial_insights",
    "Import client bank statements into Financial Insights", false),
```
Employee/Portal: agregar la constante a su lista en `SystemRoleDefaults`. Admin: automático.

**Paso 4 — Módulo en Subscription:** fila en `SubscriptionModuleCatalogSeeder.Definitions` (`Commercial(…, 29m, 290m)`) para bases nuevas y migración idempotente para existentes:
```sql
IF NOT EXISTS (SELECT 1 FROM [Modules] WHERE [Code] = 'financial_insights')
    INSERT INTO [Modules] ([Id], [Code], [Name], [Description], [Category], [AddonPriceMonthlyUsd], [AddonPriceYearlyUsd])
    VALUES ('b2000000-0000-0000-0000-00000000000a', 'financial_insights', 'Financial Insights',
            'Bank statement imports and financial analysis.', 'Commercial', NULL, NULL);
```

**Paso 5 — Revisión de catálogo** (precio + planes que lo incluyen + re-anclaje solo-mejora), agregada al final de `CatalogRevisionCatalog.All`:
```csharp
public sealed class FinancialInsightsModuleRevision : ICatalogRevision
{
    public const string ModuleCode = "financial_insights";
    public const decimal AddonMonthlyUsd = 29m;
    public const decimal AddonYearlyUsd = 290m;
    private static readonly string[] IncludedIn = ["enterprise", "pro"];
    public string Id => "2026-11-13-r4-financial-insights";
    // ApplyAsync: module.MarkAsCommercial(); module.SetAsAddon(29, 290);
    //             plan.ReviseModules(...); subscription.RebaselineVersion(...) solo si no pierde módulos
}
```
Si hay oficinas en ediciones viejas con otros términos: revisión de ediciones heredadas (`FinancialInsightsLegacyEditionsRevision`). Incluirlo también en `SubscriptionPlanCatalogSeeder` para bases nuevas.

**Paso 6 — Cuota como entitlement** (`FinancialInsightsEntitlementCatalog.cs`) y sumarla a `EntitlementKeyCatalog.PlatformDefaults`:
```csharp
public const string DocumentsPerMonth = "financial.documents_per_month";
public static IReadOnlyCollection<EntitlementSeed> Defaults { get; } =
    [new(DocumentsPerMonth, EntitlementValueType.Int, 200.ToString(CultureInfo.InvariantCulture))];
```

**Paso 7 — Servicio:** consumer de `TenantEntitlementsChangedIntegrationEvent` con `ApplyIfNewer` + invalidador; reconciliación M2M al arrancar; en `Program.cs`:
```csharp
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
ModuleGateRegistration.AddModuleGate(builder.Services, builder.Configuration);
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());
```
y `[HasPermission(...)]` en cada endpoint.

**Paso 8 — Compose** (literal; ver decisión pendiente 3.4):
```yaml
Authorization__ModuleGate__Enforce: "true"
Authorization__ModuleGate__EnforcedModules__0: financial_insights
```

**Paso 9 — Frontends:** CRM `features.ts` (entrada `FEATURES` + `MODULE_LABELS`), `app.routes.ts` (`canMatch: [accessCanMatch]`, `data: { feature }`), sidebar con `featureId`, `plan-picker-modal`, `access-view`, `role-visuals`; portal y landing (`module-labels.ts`, `MODULE_DESCRIPTIONS`).

**Paso 10 — Verificar:** migraciones aplicadas en local; arranque de Subscription aplica la revisión (fila en `CatalogRevisions`); log "Module gate registered: ENFORCING (modules: financial_insights)"; oficina de un plan que lo incluye lo ve en el menú y en /account ("Included in your plan"); oficina sin el módulo recibe 403 y ve la oferta de add-on.
