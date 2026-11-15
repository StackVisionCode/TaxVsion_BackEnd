# Proyecciones locales (read-models alimentados por eventos)

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\` salvo otra raíz indicada.
> Etiquetas: **[Código verificado]** · **[Documentación histórica]** · **[Recomendación]** · **[Decisión pendiente]**.

---

## 1. Estado actual

### 1.1 Mecánica común [Código verificado]

- **Publicación**: el dueño declara la ruta `options.PublishMessage<TEvento>().ToRabbitExchange("taxvision-events")` (sin esa línea Wolverine descarta el evento). Ej.: Auth `src/Services/Auth/Api/Program.cs:534-545` (`UserRegistered`, `UserDeactivated`, `UserReactivated`, `UserOffboarded`, `UserRolesChanged`, `RolePermissionsChanged`…), Subscription `src/Services/Subscription/TaxVision.Subscription.Api/Program.cs:191` (`TenantEntitlementsChanged`), Tenant `src/Services/Tenant/TaxVision.Tenant.Api/Program.cs:171-174` (`TenantCreated`, `TenantStatusChanged`, `TenantUpdated`), Customer `src/Services/Customer/TaxVision.Customer.Api/Program.cs:196` (`CustomerAssignmentsChanged`).
- **Transporte**: `options.UseTaxVisionRabbitMq(uri)` con publisher confirms (un rechazo del broker vuelve como excepción y el outbox reintenta) — `src/BuildingBlocks/BuildingBlocks.Web/Hosting/RabbitMqTransportExtensions.cs:30-49`. Outbox durable en todos los endpoints de envío y transacción EF automática (FI `Program.cs:99-102`).
- **Consumo**: una cola por servicio enlazada al exchange fanout con routing key vacía y inbox durable: FI `ListenToRabbitQueue("financial-insights-events", q => q.BindExchange("taxvision-events", string.Empty)).UseDurableInbox()` — `src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/Program.cs:120-122`. Cada servicio recibe **todos** los eventos y Wolverine solo invoca los que tienen handler.
- **Consumers compartidos** (viven fuera del `.Application` del servicio y se incluyen a mano): `options.Discovery.IncludeType(typeof(CustomerAssignmentsProjectionConsumer))` y `typeof(TenantTimeZoneProjectionConsumer)` — FI `Program.cs:87-90`.
- **Tenant del scope**: `IntegrationEventTenantMiddleware` sella el tenant desde `evt.TenantId` y falla si es `Guid.Empty` — `src/BuildingBlocks/BuildingBlocks.Web/Tenancy/IntegrationEventTenantMiddleware.cs:38-45`. Las tiendas de proyección usan `IgnoreQueryFilters()` + `TenantId` explícito (`src/BuildingBlocks/CustomerVisibility/CustomerAssignmentProjectionStore.cs:51-54`).
- **Contrato**: `IntegrationEvent { EventId, TenantId, OccurredOn, CorrelationId }` — `src/BuildingBlocks/Messaging/IIntegrationEvent.cs:3-20`; identidad estable con `[MessageIdentity("…")]` (p. ej. `UserRolesChangedIntegrationEvent.cs:9`, `TenantTimeZoneChangedIntegrationEvent` = `tenant.time_zone_changed.v1`).
- **Correlación**: cada consumer hace `correlation.Push(evt.CorrelationId ?? EventId)` (FI `ProjectionConsumer.cs:29,315-316`).

### 1.2 Reglas de orden e idempotencia por tipo de proyección [Código verificado]

| Proyección | Campo de versión | Regla | Evidencia |
|---|---|---|---|
| UserPermissions | `PermissionsVersion` (int, por usuario, lo sube Auth) | descarta `version < actual`; igual se re-aplica | `src/Services/FinancialInsights/TaxVision.FinancialInsights.Domain/Projections/AccessProjections.cs:54-63` |
| UserPermissions (ciclo de vida) | `LifecycleUpdatedAtUtc` | descarta `observedAt < actual` | `AccessProjections.cs:65-72` |
| RolePermissions | `PermissionsVersion` del rol | igual que arriba; **no** se une a los permisos del usuario | `AccessProjections.cs:102-110`; `ProjectionConsumer.cs:48-74` |
| TenantPlanCode / TenantEntitlements | `RevisionNumber` (long, Subscription) | descarta `revision < actual` | `src/BuildingBlocks/RateLimiting/TenantPlanCode/ITenantPlanCodeProjection.cs:24-34`; FI `AccessProjections.cs:144-162` |
| TenantTimeZone | `SourceVersion` (versión de la oficina en Tenant) o `SourceUpdatedAtUtc` si falta | aplica solo si es más nuevo (`IsOlderThan`); igual o menor → no-op | `src/BuildingBlocks/TenantTimeZones/TenantTimeZoneProjection.cs:14-40`; consumer `TenantTimeZoneProjectionConsumer.cs:62-71` |
| CustomerAssignment | `Version` (DateTime = `UpdatedAtUtc` del cliente) | `evt.Version <= aplicado` → ignora; si no, borra e inserta el set | `src/BuildingBlocks/CustomerVisibility/CustomerAssignmentsProjectionConsumer.cs:26-31`; `CustomerAssignmentProjectionStore.cs:105-121` |
| TenantDirectory (FI) | `TenantVersion` o `observedAtUtc` | versión mayor manda; sin versión, la hora | FI `AccessProjections.cs:198-219` |
| CustomerDirectory (FI) | `UpdatedAtUtc` (`OccurredOn`/fecha del evento) | descarta lo más viejo; `name=null` no borra el nombre | FI `AccessProjections.cs:250-258` |

### 1.3 Matriz de proyecciones [Código verificado salvo que se marque]

| Proyección (entidad) | Dueño (fuente) | Evento(s) | Consumidores (servicios) | Consumer / upsert | Versión / orden | Eliminación / baja | Reparación |
|---|---|---|---|---|---|---|---|
| `UserPermissionsProjection` (+ `AuthzUserPermissionsProjection` en Billing, Documents, Notification, Signature) | Auth | `UserRolesChangedIntegrationEvent` (códigos efectivos) + `UserDeactivated/Offboarded/Reactivated` | Calendar, Campaigns, Catalog, CloudStorage, Connectors, Correspondence, Customer, FinancialInsights, Growth, Inventory, Notes, PaymentApp, PaymentClient, Postmaster, Reminder, Scribe, Sms, Subscription, Tasks, Tenant, Wallet + los 4 `Authz*`; Communication (Prisma, Node) | FI `ProjectionConsumer.Handle(UserRolesChanged…)` `:21-46`; `UserLifecyclePermissionsProjectionConsumer` `:14-100` | `PermissionsVersion`; ciclo de vida por fecha | `IsActive=false` (fila se conserva); lector filtra `IsActive && HasPermissionsSnapshot` (`FinancialProjectionReaders.cs:18-25`) | Pull-recovery bajo demanda (`ProjectionPermissionsSource.cs:79-95`); Auth re-publica: backfill de arranque, sync de roles de sistema, cada 6 h (`PermissionsReconciliationService.cs:12-58`) |
| `RolePermissionsProjection` (+ `AuthzRolePermissionsProjection`) | Auth | `RolePermissionsChangedIntegrationEvent` | Los mismos | FI `ProjectionConsumer.cs:49-74` | `PermissionsVersion` del rol | — | Re-publicación de Auth al cambiar roles de sistema |
| `TenantPlanCodeProjection` (PlanCode + `EnabledModules`) | Subscription | `TenantEntitlementsChangedIntegrationEvent` | Auth, Billing, Calendar, Campaigns, Catalog, CloudStorage, Connectors, Correspondence, Customer, Documents, Growth, Inventory, Notes, Notification, PaymentApp, PaymentClient, Postmaster, Reminder, Scribe, Signature, Sms, Subscription, Tasks, Tenant, Wallet | Handler compartido `TenantPlanCodeProjectionHandler.Handle<T>` (`src/BuildingBlocks/Messaging/RateLimiting/TenantPlanCodeProjectionHandler.cs:24-70`) + wrapper de 1 línea (ej. `src/Services/Notes/TaxVision.Notes.Application/RateLimiting/Consumers/TenantPlanCodeProjectionConsumer.cs:13-34`); invalida cachés tras guardar | `RevisionNumber` | Suscripción vencida → `EnabledModules = []` (no se borra la fila) | Subscription re-encola recálculo cada 24 h **solo si cambió** (`EntitlementSnapshotReconciliationJob`, `OnlyIfChanged`) |
| `TenantEntitlementsProjection` (FI: PlanCode, módulos, `DocumentsPerMonth`, ancla del período) | Subscription | `TenantEntitlementsChangedIntegrationEvent` | FinancialInsights | `ProjectionConsumer.cs:76-112` + `cache.InvalidateAsync` | `RevisionNumber` | `[]` = sin módulos | Pull bajo demanda (`TenantEntitlementsResolver.GetOrPullAsync`) + job `TenantEntitlementsReconciliationJob` (6 h) contra `internal/tenants/{id}/entitlements` |
| `TenantTimeZoneProjection` (tabla compartida) | Tenant | `TenantCreatedIntegrationEvent`, `TenantUpdatedIntegrationEvent` | Billing, Calendar, Campaigns, Correspondence, Customer, Documents, FinancialInsights, Notification, Scribe, Signature, Subscription, Tasks | `TenantTimeZoneProjectionConsumer` (`src/BuildingBlocks/TenantTimeZones/TenantTimeZoneProjectionConsumer.cs:14-74`) + `resolver.Forget(tenantId)` | `SourceVersion` / fecha | Zona inválida → no cambia (warning) | `TenantTimeZoneReconciliationJob` (12 h, retry 2 min) contra `internal/tenants/directory` |
| `CustomerAssignmentProjection` (fila por cliente+usuario, compartida) | Customer | `CustomerAssignmentsChangedIntegrationEvent` (snapshot completo del set) | Billing, Calendar, Campaigns, CloudStorage, Correspondence, FinancialInsights, Notes, Notification, Signature, Sms, Tasks | `CustomerAssignmentsProjectionConsumer` (`src/BuildingBlocks/CustomerVisibility/CustomerAssignmentsProjectionConsumer.cs:10-33`) | `Version` (DateTime) | Set vacío = sin asignados (borra filas); retiro de empleado lo limpia Customer vía nuevo snapshot (`CustomerAssignmentProjection.cs:14-20`) | `CustomerAssignmentReconciliationJob` (12 h, retry 2 min) contra `internal/customers/assignments/reconciliation` |
| `TenantDirectoryProjection` (FI) / `TenantDirectoryEntry` (Postmaster) | Tenant | FI: `TenantCreated`, `TenantUpdated`, `TenantStatusChanged`; Postmaster: `TenantCreated`, `TenantUpdated`, `TenantProfileChanged`, `TenantSubdomainChanged` | FinancialInsights, Postmaster | FI `ProjectionConsumer.cs:114-179` (Updated no crea fila: "una actualización no prueba que la oficina esté activa") | `TenantVersion` / fecha | `IsActive=false` por estado | FI `TenantDirectoryReconciliationJob` (12 h, retry propio de 5 min); Postmaster backfill de arranque (según comentario de `InternalTenantDirectoryController.cs:12-15`) |
| `CustomerDirectoryProjection` (FI) / `CustomerDirectoryEntry` (Calendar, Notes, Tasks) | Customer | `CustomerCreated/Updated/Activated/Reactivated/Deactivated/Archived`, `CustomersBulkImported` (sin PII) | FinancialInsights, Calendar, Notes, Tasks | FI `ProjectionConsumer.cs:181-313` | Fecha del evento | `IsActive=false` | `TenantCustomerFullReconciliationJob` + `CustomerDirectoryReconciliationJob` (nombres) contra `internal/customers/reconciliation` |
| `TenantEmployeeDirectoryEntry` | Auth | `UserRegistered`, `UserDeactivated`, `UserReactivated`, `UserOffboarded` | Customer | `AuthUserDirectoryConsumer`, `CustomerUserOffboardedConsumer` (`src/Services/Customer/TaxVision.Customer.Application/Employees/Consumers/`) | no verificado en detalle | baja/retiro | `EmployeeDirectoryReconciliationJob` contra `internal/users/staff/reconciliation` (PlatformTenant) |
| `CustomerContactPolicyProjection` | Customer | `CustomerCreated`, `CustomerUpdated`, `CustomersBulkImported` | Notification, Sms | no verificado en detalle | no verificado | — | Sms `CustomerContactPolicyReconciliationJob` |
| Destinatarios de Notification (`NotificationRecipientPermissionsProjection`, `NotificationRecipientRolePermissionsProjection`, `TenantOwnerProjection`, `CustomerPortalAccountProjection`, `UserEmailDirectoryEntry`) | Auth | `UserRolesChanged`, `RolePermissionsChanged`, `UserRegistered/Deactivated/Reactivated/Offboarded`, `SecurityAlert` | Notification | no verificado en detalle | no verificado | baja | `RecipientProjectionReconciliationJob` contra `internal/users/{owners,portal-accounts}/reconciliation` |
| `CustomerEmailProjection` | Customer | `CustomerCreated/Updated/Activated/Reactivated/Deactivated/Archived` | Signature | no verificado en detalle | no verificado | — | `CustomerProjectionReconciliationJob` (Signature) |
| `OfficeScheduleProjection` | Tenant | `TenantProfileChanged`, `TenantUpdated` | Calendar | no verificado en detalle | — | — | — |
| `TenantBillingPreferenceProjection` | Subscription | `TenantBillingPreferenceChanged` (+ eventos de renovación) | PaymentApp | no verificado en detalle | — | — | — |
| `OffboardedStaffProjection` | Auth | `UserOffboarded` | Notes | no verificado en detalle | — | — | — |
| `TenantSeatSlotProjection` | Subscription/Auth (seats) | no verificado | Auth | no verificado | — | — | — |
| `UserDirectoryEntry` (Communication, Node) | Auth | `UserProfileUpdated` y otros | Communication | Prisma; ver memoria `project_communication_upsert_pisa_permisos.md` [Documentación histórica] | — | — | `internal/users/directory/reconciliation` (`InternalUserReconciliationController.cs:79-100`) |

### 1.4 Configuración de una proyección compartida [Código verificado]

- `services.AddTenantTimeZoneProjection<TDbContext>()` + `modelBuilder.ApplyTenantTimeZoneProjection()` + migración + `IncludeType(typeof(TenantTimeZoneProjectionConsumer))` — `src/BuildingBlocks/TenantTimeZones/TenantTimeZonesServiceCollectionExtensions.cs:15-26`.
- `services.AddCustomerVisibilityProjection<TDbContext>()` + `modelBuilder.ApplyCustomerAssignmentProjection()` (índice único `(TenantId, CustomerId, UserId)`) + migración + `IncludeType(typeof(CustomerAssignmentsProjectionConsumer))` — `src/BuildingBlocks/CustomerVisibility/CustomerVisibilityServiceCollectionExtensions.cs:13-20`, `CustomerVisibilityModelBuilderExtensions.cs:9-28`.
- FI mapea ambas en `FinancialInsightsDbContext.cs:131-133` y las registra en `DependencyInjection.cs:59-60,148-149`.

---

## 2. Evidencia documental

- `documents/RBAC_Endurecimiento_Defensa_Senior.md` §6.4-6.5 (cómo construir `UserPermissionsProjection`) [Documentación histórica, parcialmente desfasada — ver Problemas 1 y 2].
- `documents/architecture/Guia_Cablear_Modulo_Plan_Microservicio.md` §3.1-3.2 (`TenantPlanCodeProjection` + invalidador doble).
- Memoria: `feedback_no_conceder_permisos_sistema_por_migracion.md`, `project_communication_upsert_pisa_permisos.md`, `feedback_consumer_runtime_un_handler_por_evento_proyecciones.md` (Node: un handler por evento; proyecciones extra con `registerProjection`), índice `index_feedback_wolverine.md` ("evento sin PublishMessage se descarta", "guardar antes de publicar").

---

## 3. Problemas detectados

1. **Doc y comentario de código piden recomponer la unión en `RolePermissionsChanged`** (senior §6.5 paso 3; cabecera de `src/Services/CloudStorage/TaxVision.CloudStorage.Application/Permissions/Consumers/PermissionsProjectionConsumers.cs:10-15`), pero el código vigente no lo hace y no debe (`RolePermissionsFanOut.cs:15-19`). Riesgo de que un servicio nuevo copie la doc.
2. **Senior §6.5 dice "no-op si version <= actual"**; el código re-aplica versiones iguales (`version < PermissionsVersion → return`). Ambas son idempotentes, pero la guía debe fijar una sola regla.
3. **La reconciliación de entitlements de Subscription no repara consumidores**: `EntitlementSnapshotReconciliationJob` encola con `OnlyIfChanged` (`src/Services/Subscription/TaxVision.Subscription.Infrastructure/Scheduling/EntitlementSnapshotReconciliationJob.cs:21-27,75`): si el snapshot de Subscription está al día no publica nada. Solo FinancialInsights tiene pull M2M de entitlements (`internal/tenants/{id}/entitlements` lo consume únicamente FI). Un servicio nuevo con `TenantPlanCodeProjection` no tendrá fila para los tenants existentes hasta que su plan cambie → gate fail-open y cuota base.
4. **`TenantTimeZoneChangedIntegrationEvent` no lo procesa el consumer compartido** (solo `TenantCreated`/`TenantUpdated`); es aditivo por diseño (`TenantTimeZoneChangedIntegrationEvent.cs` doc), pero conviene decirlo en la guía para que nadie lo use como fuente de la proyección.
5. **Validación de `IsActive` delegada al lector de cada servicio** (`ProjectionPermissionsSource` no la conoce). Verificado en FI; no verificado en los demás. **[Decisión pendiente]**.
6. **Proyecciones Node** (Communication) duplican lógica y tuvieron bugs de mapeo (`ActorType` ausente, casing; memoria `project_communication_permissions_projection_casing_bug_fixed.md`). El evento ahora exige `ActorType` (`UserRolesChangedIntegrationEvent.cs:29-38`).
7. **Sin borrado físico de tenants** en ninguna proyección revisada: un tenant eliminado queda como fila `IsActive=false` o con módulos `[]`. No es un fallo, pero la guía debe decirlo.

---

## 4. Buenas prácticas observadas

- Event-carried state transfer: los eventos llevan el estado completo (códigos efectivos, set de asignados, snapshot de entitlements); el consumidor no llama a la fuente en el hot path.
- Upsert idempotente con versión monotónica definida por el dueño; reentregas y desorden no deshacen cambios.
- Handlers compartidos en BuildingBlocks (`TenantPlanCodeProjectionHandler`, `CustomerAssignmentsProjectionConsumer`, `TenantTimeZoneProjectionConsumer`): la forma se comparte, la tabla es de cada servicio.
- Invalidación de caché **después** de guardar (`TenantPlanCodeProjectionHandler.cs:60-61`; FI `ProjectionConsumer.cs:109-110`).
- Eventos masivos sin PII (`CustomersBulkImported`): se insertan ids y la reconciliación completa nombres (`ProjectionConsumer.cs:263-288`).
- `[UserReference(UserReferenceKind.Projection, HandledBy = …)]` documenta quién limpia la referencia al retirar un empleado (`AccessProjections.cs:12`; `CustomerAssignmentProjection.cs:15-19`).

---

## 5. Recomendación

- Toda proyección nueva: (a) dueño único, (b) evento con estado completo y versión monotónica del dueño, (c) consumer idempotente `ApplyIfNewer`, (d) `IgnoreQueryFilters()` + tenant del evento, (e) **reparación** (pull bajo demanda y/o reconciliación periódica), (f) test de orden inverso y duplicado.
- No derivar permisos de usuario desde `RolePermissionsChanged`.
- Para el plan: si el servicio enforza el gate o depende de cuotas, añadir reparación pull de entitlements (patrón FI `TenantEntitlementsResolver`) o **[Decisión pendiente]** que Subscription ofrezca re-publicación forzada.

---

## 6. Riesgos

| Riesgo | Efecto |
|---|---|
| Falta `PublishMessage<T>()` en el dueño | El evento se descarta en silencio; la proyección nunca se llena |
| Falta `IncludeType(...)` del consumer compartido | El servicio recibe el evento y no lo procesa |
| Falta la migración de la tabla compartida | El consumer lanza al guardar; Wolverine reintenta / DLQ |
| Versión mal comparada | Un evento viejo pisa uno nuevo (permisos revocados vuelven) |
| Servicio nacido después de los datos | Proyección vacía para todo lo anterior (403, gate abierto, nombres vacíos) hasta reconciliar |
| Recomponer permisos desde roles | Denies resucitan |

---

## 7. Pruebas

**Existentes [Código verificado]**
- FI `deploy/tests/TaxVision.FinancialInsights.Tests/Application/ProjectionConsumerTests.cs`: `User_deny_event_removes_permission_and_role_event_cannot_restore_it`, `Tenant_module_projection_keeps_newest_revision_and_empty_means_disabled`, `Entitlements_store_the_monthly_document_quota_when_the_plan_sends_it`, `Entitlements_keep_the_subscription_period_anchor`, `Customer_status_projection_is_tenant_scoped_and_update_does_not_reactivate`, `Bulk_import_adds_missing_customers_without_touching_known_ones`, `Tenant_status_does_not_roll_back_on_older_event`.
- `deploy/tests/TaxVision.BuildingBlocks.Tests/TenantTimeZones/TenantTimeZoneProjectionTests.cs`, `TenantTimeZonesDependencyTests.cs`.
- `…/RateLimit/TenantPlanCodeProjectionDefaultMethodTests.cs`, `CachedTenantEntitlementModulesReaderTests.cs`, `CachedTenantPlanCodeReaderTests.cs`.
- `…/ActorTypeAuthorization/ModuleGateWiringFitnessTests.cs` (`Every_gated_service_invalidates_the_modules_cache_on_a_plan_change`, `No_service_registers_the_modules_reader_without_its_cache`).
- `…/Architecture/SaveBeforePublishTests.cs`, `DomainEventHandlerConventionTests.cs`.
- `deploy/tests/TaxVision.Auth.Tests/Application/RolePermissionsFanOutTests.cs`, `Infrastructure/PermissionsReconciliationServiceTests.cs`.

**Que debe tener un servicio nuevo [Recomendación]**
1. Por cada consumer: crea si no existe; aplica versión mayor; ignora versión menor; duplicado no cambia nada.
2. Evento con `TenantId` vacío → excepción (middleware).
3. Tenant A no ve filas del tenant B en la proyección.
4. Ciclo de vida: desactivar → lector null; reactivar sin fila → no crea fila vacía.
5. Fitness: el `Program.cs` incluye los consumers compartidos que usa y declara `PublishMessage` de todo evento que publica.

---

## 8. Dependencias

RabbitMQ (`taxvision-events`), Wolverine (outbox/inbox durable SQL Server), EF Core por servicio, dueños (Auth, Subscription, Tenant, Customer), endpoints de reconciliación (`Reconciliation.md`), Redis (cachés de plan y módulos).

---

## 9. Por qué un servicio funcional puede quedar inaccesible

- Proyección de permisos vacía → 403 en todo `[HasPermission]` para humanos (salvo pull-recovery).
- Proyección de asignaciones vacía → empleados sin `customers.view_all` no ven ningún cliente (la lista sale vacía, no da error).
- Proyección de módulos con `[]` (tenant vencido) → 403 `Authz.ModuleUnavailable` con el gate en enforce; sin fila → acceso abierto.
- Proyección de zona horaria ausente → fechas en UTC (no bloquea, pero los vencimientos civiles salen corridos).

---

## 10. Material para la guía (servicio de referencia: FinancialInsights)

### 10.1 Entidad con `ApplyIfNewer`
`src/Services/FinancialInsights/TaxVision.FinancialInsights.Domain/Projections/AccessProjections.cs:8-76` (extracto):
```csharp
public sealed class UserPermissionsProjection : TenantEntity
{
    [UserReference(UserReferenceKind.Projection, HandledBy = "UserLifecyclePermissionsProjectionConsumer")]
    public Guid UserId { get; private set; }
    public int PermissionsVersion { get; private set; }
    public string PermissionCodesJson { get; private set; } = "[]";
    public string RoleIdsJson { get; private set; } = "[]";
    public bool IsActive { get; private set; }
    public bool HasPermissionsSnapshot { get; private set; }
    public DateTime LifecycleUpdatedAtUtc { get; private set; }

    public void ApplyIfNewer(int version, IReadOnlyCollection<string> codes, IReadOnlyCollection<Guid> roleIds)
    {
        if (version < PermissionsVersion)
            return;
        PermissionsVersion = version;
        PermissionCodesJson = JsonSerializer.Serialize(codes.Distinct(StringComparer.Ordinal).ToArray());
        RoleIdsJson = JsonSerializer.Serialize(roleIds.Distinct().ToArray());
        HasPermissionsSnapshot = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetActive(bool active, DateTime observedAtUtc)
    {
        if (observedAtUtc < LifecycleUpdatedAtUtc)
            return;
        IsActive = active;
        LifecycleUpdatedAtUtc = observedAtUtc;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
```
Índice único `(TenantId, UserId)` (`FinancialInsightsDbContext.cs:96`).

### 10.2 Consumer
`src/Services/FinancialInsights/TaxVision.FinancialInsights.Application/Projections/Consumers/ProjectionConsumer.cs:21-46`:
```csharp
public static async Task Handle(UserRolesChangedIntegrationEvent evt, IFinancialProjectionRepository projections,
    IUnitOfWork unitOfWork, ICorrelationContext correlation, CancellationToken ct)
{
    using (correlation.Push(CorrelationOf(evt)))
    {
        var row = await projections.FindUserPermissionsAsync(evt.TenantId, evt.UserId, ct);
        if (row is null)
            projections.AddUserPermissions(UserPermissionsProjection.Create(
                evt.TenantId, evt.UserId, evt.PermissionsVersion, evt.PermissionCodes, evt.RoleIds));
        else
            row.ApplyIfNewer(evt.PermissionsVersion, evt.PermissionCodes, evt.RoleIds);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
```

### 10.3 Lector + escritor (pull-recovery) con una sola clase
`src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/Persistence/FinancialProjectionReaders.cs:8-63` — `GetSnapshotAsync` con `IgnoreQueryFilters().AsNoTracking()` y filtro `IsActive && HasPermissionsSnapshot`; `PersistSnapshotAsync` con `ApplyIfNewer` y captura de violación de unicidad (2601/2627) para primeras peticiones concurrentes. Registro (`DependencyInjection.cs:68-74`):
```csharp
services.AddScoped<FinancialUserPermissionsReader>();
services.AddScoped<IUserPermissionsProjectionReader>(sp => sp.GetRequiredService<FinancialUserPermissionsReader>());
services.AddScoped<IUserPermissionsProjectionWriter>(sp => sp.GetRequiredService<FinancialUserPermissionsReader>());
```

### 10.4 Plan del tenant (handler compartido, servicios con `TenantPlanCodeProjection`)
```csharp
public static class TenantPlanCodeProjectionConsumer
{
    public static Task Handle(TenantEntitlementsChangedIntegrationEvent evt, ITenantPlanCodeProjectionRepository repository,
        ITenantPlanCodeCacheInvalidator planCodeCache, IUnitOfWork unitOfWork, ICorrelationContext correlation,
        ILogger<TenantPlanCodeProjection> logger, CancellationToken ct) =>
        TenantPlanCodeProjectionHandler.Handle(evt, repository, planCodeCache, unitOfWork, correlation, logger,
            TenantPlanCodeProjection.Create, ct);
}
```
(`src/Services/Notes/TaxVision.Notes.Application/RateLimiting/Consumers/TenantPlanCodeProjectionConsumer.cs:13-34`) + invalidador de las dos cachés (FI `src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/RateLimiting/TenantPlanCodeCacheInvalidator.cs:10-20`).

### 10.5 Proyecciones compartidas en `Program.cs` y DI
```csharp
// Program.cs (Wolverine)
options.Discovery.IncludeType(typeof(CustomerAssignmentsProjectionConsumer));
options.Discovery.IncludeType(typeof(BuildingBlocks.TenantTimeZones.TenantTimeZoneProjectionConsumer));
options.ListenToRabbitQueue("financial-insights-events", q => q.BindExchange("taxvision-events", string.Empty))
       .UseDurableInbox();
// Infrastructure/DependencyInjection.cs
services.AddTenantTimeZoneProjection<FinancialInsightsDbContext>();
services.AddTenantTimeZoneReconciliation<FinancialTenantTimeZoneTokenProvider>(configuration);
services.AddCustomerVisibilityProjection<FinancialInsightsDbContext>();
services.AddCustomerVisibilityReconciliation<FinancialPlatformTokenProvider>(configuration);
// DbContext.OnModelCreating
modelBuilder.ApplyCustomerAssignmentProjection();
modelBuilder.ApplyTenantTimeZoneProjection();
```

### 10.6 Checklist
- [ ] Tabla + índice único + migración (aplicada en local).
- [ ] Consumer idempotente con versión del dueño.
- [ ] `IncludeType` de los consumers compartidos.
- [ ] Reparación: pull bajo demanda y/o reconciliación con `ReconciliationRetry`.
- [ ] Invalidación de caché tras guardar (si hay caché).
- [ ] Tests de orden, duplicado y aislamiento por tenant.
