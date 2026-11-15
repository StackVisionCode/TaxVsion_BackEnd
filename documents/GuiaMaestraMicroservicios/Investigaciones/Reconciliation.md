# Reconciliación de proyecciones (anti-entropía)

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\` salvo otra raíz indicada.
> Etiquetas: **[Código verificado]** · **[Documentación histórica]** · **[Recomendación]** · **[Decisión pendiente]**.

---

## 1. Estado actual

Los eventos son el camino principal; la reconciliación es el respaldo para (a) datos anteriores a que el servicio existiera, (b) eventos perdidos y (c) arranques en frío. Hay dos estilos en el repo:

- **Push (la fuente re-publica)**: Auth re-emite `UserRolesChangedIntegrationEvent`; Subscription re-encola recálculos de entitlements.
- **Pull (el consumidor pagina un endpoint interno M2M)**: zona horaria, asignaciones, directorios de clientes/empleados/oficinas, entitlements de FI.

### 1.1 Jobs push en la fuente [Código verificado]

| Job | Servicio | Qué hace | Frecuencia | Evidencia |
|---|---|---|---|---|
| `PermissionsBackfillService` | Auth | Re-publica `UserRolesChanged` para usuarios activos con `PermissionsBackfilledAt == null` (cursor persistido); no-op cuando llega a 0 | Al arrancar | `src/Services/Auth/Api/Bootstrap/PermissionsBackfillService.cs:12-47` |
| `SystemRolePermissionsSyncService` | Auth | Recalcula el set de cada rol de sistema contra el catálogo; si cambió, sube versión y publica | Al arrancar | `src/Services/Auth/Api/Bootstrap/SystemRolePermissionsSyncService.cs:12-40` |
| `PermissionsReconciliationService` | Auth | Re-publica `UserRolesChanged` de **todos** los activos, lotes de 50, 2 s entre lotes, sin subir versión | Cada 6 h (primer pase tras 6 h) | `src/Services/Auth/Api/Jobs/PermissionsReconciliationService.cs:12-58` |
| `EntitlementSnapshotReconciliationJob` | Subscription | Encola `RecalculateEntitlementsCommand(tenantId, OnlyIfChanged: true)` por tenant, con lock distribuido | Al arrancar y cada 24 h | `src/Services/Subscription/TaxVision.Subscription.Infrastructure/Scheduling/EntitlementSnapshotReconciliationJob.cs:15,21-27,75` |

`OnlyIfChanged` implica que si el snapshot de Subscription ya está al día **no se publica nada**: este job repara a Subscription, no a los consumidores.

### 1.2 Endpoints internos que alimentan las reconciliaciones pull [Código verificado]

| Endpoint | Servicio | Autorización | Paginación | Evidencia |
|---|---|---|---|---|
| `GET internal/tenants/directory?page&size` | Tenant | `ServiceOnly` + `[AllowActorTypes(Service)]` (**sin** chequeo de PlatformTenant) | `page ≥ 1`, `size` 1..200; devuelve lista sin total (fin = página corta) | `src/Services/Tenant/TaxVision.Tenant.Api/Controllers/InternalTenantDirectoryController.cs:27-47` |
| `GET internal/customers/reconciliation?status&page&size` | Customer | `ServiceOnly` + PlatformTenant | `PagedResult` con `TotalCount` | `src/Services/Customer/TaxVision.Customer.Api/Controllers/InternalCustomersController.cs:68-90` |
| `GET internal/customers/assignments/reconciliation?page&size` | Customer | `ServiceOnly` + PlatformTenant | `PagedResult`; fin = `page*size >= TotalCount` | `InternalCustomersController.cs:97-116`; cliente `src/BuildingBlocks/CustomerVisibility/CustomerAssignmentsReconciliationClient.cs:49,76` |
| `GET internal/users/{owners,staff,portal-accounts,directory}/reconciliation` | Auth | `ServiceOnly` + PlatformTenant | `PagedResult` (directory hasta 500) | `src/Services/Auth/Api/Controllers/InternalUserReconciliationController.cs:22-100` |
| `GET internal/tenants/{t}/users/{u}/permissions-snapshot` | Auth | `ServiceOnly` | — (uno a uno, bajo demanda) | `src/Services/Auth/Api/Controllers/InternalPermissionsSnapshotController.cs:18-35` |
| `GET internal/tenants/{tenantId}/entitlements` | Subscription | `ServiceOnly` | — (uno a uno) | `src/Services/Subscription/TaxVision.Subscription.Api/Controllers/Internal/InternalTenantEntitlementsController.cs:18-33` |
| `GET internal/plan-rate-limits` | Subscription | `ServiceOnly` | catálogo completo | `src/Services/Subscription/TaxVision.Subscription.Api/Controllers/Internal/InternalPlanRateLimitsController.cs:18` |

Todos son `[RateLimitExempt]` y el Gateway devuelve 404 a cualquier path con segmento `internal` (`src/Gateway/TaxVision.Gateway/Middleware/InternalSurfaceGuardMiddleware.cs:11-55`).

### 1.3 Jobs pull en los consumidores [Código verificado]

| Job | Dónde | Fuente | Intervalo | Reintento corto tras fallo | Evidencia |
|---|---|---|---|---|---|
| `TenantTimeZoneReconciliationJob` (compartido) | 12 servicios (Billing, Calendar, Campaigns, Correspondence, Customer, Documents, FinancialInsights, Notification, Scribe, Signature, Subscription, Tasks) | `internal/tenants/directory` (token PlatformTenant del servicio) | 12 h | **Sí** (`ReconciliationRetry`, 2 min) | `src/BuildingBlocks/TenantTimeZones/TenantTimeZoneReconciliationJob.cs:15-94` (`:38`) |
| `CustomerAssignmentReconciliationJob` (compartido) | 11 servicios (Billing, Calendar, Campaigns, CloudStorage, Correspondence, FinancialInsights, Notes, Notification, Signature, Sms, Tasks) | `internal/customers/assignments/reconciliation` | 12 h | **Sí** | `src/BuildingBlocks/CustomerVisibility/CustomerAssignmentReconciliationJob.cs:15-122` (`:38`) |
| `TenantCustomerFullReconciliationJob` | Calendar, Notes, Tasks | `internal/customers/reconciliation` | config | **Sí** | `src/Services/{Calendar,Notes,Tasks}/…/Jobs/TenantCustomerFullReconciliationJob.cs` |
| `TenantCustomerFullReconciliationJob` | FinancialInsights | `internal/customers/reconciliation` | 12 h | **No** (siempre `Task.Delay(interval)`) | `src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/Customers/CustomerDirectoryReconciliationJobs.cs:34-108` (`:59`) |
| `CustomerDirectoryReconciliationJob` (nombres) | FinancialInsights, Calendar, Notes, Tasks, Notification | Customer | 6 h (FI) / config | **No** | FI `CustomerDirectoryReconciliationJobs.cs:111-138`; Calendar `…/Jobs/CustomerDirectoryReconciliationJob.cs:40`; Notification `…/Directory/CustomerDirectoryReconciliationJob.cs:56` |
| `TenantDirectoryReconciliationJob` | FinancialInsights | `internal/tenants/directory` | 12 h | Sí, pero propio (5 min, no `ReconciliationRetry`) | `src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/Tenants/TenantDirectoryReconciliation.cs:148-189` (`:182`) |
| `TenantEntitlementsReconciliationJob` | FinancialInsights | `internal/tenants/{id}/entitlements` (uno por tenant conocido sin fila o con fila más vieja que el intervalo; máx. 500 por pasada) | 6 h, primer pase a los 60 s | **No** | `src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/Processing/TenantEntitlementsReconciliationJob.cs:11-116` (`:18-19,44,63`) |
| `EmployeeDirectoryReconciliationJob` | Customer | `internal/users/staff/reconciliation` | config | **No** | `src/Services/Customer/TaxVision.Customer.Infrastructure/Employees/EmployeeDirectoryReconciliationJob.cs:35,53` |
| `RecipientProjectionReconciliationJob` | Notification | `internal/users/{owners,portal-accounts}/reconciliation` | config | **No** | `src/Services/Notification/TaxVision.Notification.Infrastructure/Reconciliation/RecipientProjectionReconciliationJob.cs:42` |
| `CustomerProjectionReconciliationJob` | Correspondence, Signature | Customer | config | **No** | `…/Correspondence…/Scheduling/CustomerProjectionReconciliationJob.cs:58`; `…/Signature…/Scheduling/CustomerProjectionReconciliationJob.cs:49` |
| `CustomerContactPolicyReconciliationJob` | Sms | Customer | config | **No** | `src/Services/Sms/TaxVision.Sms.Infrastructure/Customers/CustomerContactPolicyReconciliationJob.cs:51` |

Patrón común de los compartidos: esperan `ApplicationStarted`, leen `Enabled/IntervalHours/PageSize`, un scope y una correlación nuevos por pasada, devuelven `bool completed`, y nunca tumban el servicio (`TenantTimeZoneReconciliationJob.cs:21-94`).

### 1.4 Reintento corto `ReconciliationRetry` [Código verificado]

```csharp
// src/BuildingBlocks/Common/ReconciliationRetry.cs:7-13
public static class ReconciliationRetry
{
    public static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(2);
    public static TimeSpan NextDelay(bool completed, TimeSpan interval) =>
        completed || interval <= AfterFailure ? interval : AfterFailure;
}
```

Contexto (del encargo de esta investigación; no encontré nota en memoria ni en docs): se añadió el 2026-11-15 tras un incidente en producción en el que los jobs corrieron antes de que Auth respondiera y quedaron esperando el intervalo completo (12 h). Lo usan **5 jobs**: `TenantTimeZoneReconciliationJob`, `CustomerAssignmentReconciliationJob` y `TenantCustomerFullReconciliationJob` de Calendar, Notes y Tasks. El test `ReconciliationRetryTests.Startup_reconciliation_jobs_retry_soon_after_a_failed_pass` fija exactamente esos 5 archivos (`deploy/tests/TaxVision.BuildingBlocks.Tests/Common/ReconciliationRetryTests.cs:29-41`).

### 1.5 URL de Tenant en compose y el respaldo a localhost [Código verificado]

- `TenantTimeZoneReconciliationOptions`: sección `TenantTimeZones:Reconciliation`, `TenantBaseUrl` vacío por defecto — `src/BuildingBlocks/TenantTimeZones/TenantTimeZoneReconciliationOptions.cs:9-25`.
- Resolución en `PostConfigure`: `TenantBaseUrl` → `PlatformSettings:TenantBaseUrl` → **`http://localhost:5217`** — `src/BuildingBlocks/TenantTimeZones/TenantTimeZonesServiceCollectionExtensions.cs:28-55` (`:37-42`). En un contenedor, `localhost` es el propio contenedor: la reconciliación nunca llega a Tenant y solo deja un warning por pasada.
- Compose: el ancla `x-platform-settings-env` define `PlatformSettings__TenantBaseUrl: http://tenant-api:8080` (`deploy/docker/docker-compose.yml:4-5`). Los 12 servicios con reconciliación de zona quedan cubiertos: 7 con `TenantTimeZones__Reconciliation__TenantBaseUrl: http://tenant-api:8080` explícito (customer, subscription, correspondence, campaigns, financial-insights, calendar, tasks) y 5 por el ancla (billing, documents, notification, scribe, signature).
- Mismo riesgo latente en asignaciones: `CustomerVisibilityReconciliationOptions.CustomerBaseUrl = "http://localhost:5263"` por defecto (`src/BuildingBlocks/CustomerVisibility/CustomerVisibilityReconciliationOptions.cs:12`). Los 11 consumidores tienen `CustomerVisibility__Reconciliation__CustomerBaseUrl` en compose o (FI) un `PostConfigure` desde `Services:Customer:BaseUrl` (`src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/DependencyInjection.cs:150-157`).
- Guardarraíles: `TenantTimeZoneReconciliationDeployTests.Every_service_that_reconciles_time_zones_gets_the_tenant_url_in_compose`, `ComposeInternalBaseUrlWiringTests.No_internal_base_url_env_points_to_localhost_in_compose`, `AssignmentVisibilityDeployWiringTests`.

### 1.6 Cómo se evita que un snapshot viejo pise un evento nuevo [Código verificado]

| Reconciliación | Mecanismo | Evidencia |
|---|---|---|
| Asignaciones | El snapshot trae `Version` (DateTime de Customer); solo reemplaza si `item.Version > aplicado` (misma regla que el consumer) | `CustomerAssignmentReconciliationJob.cs:99-103`; consumer `CustomerAssignmentsProjectionConsumer.cs:26-31` |
| Zona horaria | El directorio no trae versión: `Reconcile(zone, readAtUtc)` corrige si la zona difiere y adelanta `SourceUpdatedAtUtc` a la hora de lectura; un evento posterior más viejo no lo deshace, uno más nuevo sí | `TenantTimeZoneReconciler.cs:15-59` (`:38-42`); `TenantTimeZoneProjection.cs:45-50` |
| Directorio de oficinas (FI) | Usa `Version` de Tenant cuando el directorio la expone; si no, `observedAtUtc` fijado **al inicio** de la pasada | `TenantDirectoryReconciliation.cs:98-145`; `AccessProjections.cs:198-219` |
| Directorio de clientes (FI) | `observedAtUtc` al inicio de la pasada; la fila solo cambia si es más nueva que la última aplicada | `CustomerDirectoryReconciliationJobs.cs:68-104`; `AccessProjections.cs:250-258` |
| Entitlements (FI) | `ApplyIfNewer` con `RevisionNumber` (aplica igual o más nueva) | `src/Services/FinancialInsights/TaxVision.FinancialInsights.Application/Imports/TenantEntitlementsResolver.cs` (`RefreshAsync`); `AccessProjections.cs:144-162` |
| Permisos (push de Auth) | El evento re-publicado lleva la versión actual; `ApplyIfNewer` re-aplica igual y descarta menor | `PermissionsReconciliationService.cs:12-24` |

Guardado por página y `ChangeTracker.Clear()` entre páginas (FI `TenantDirectoryReconciliation.cs:132-133`); escritura idempotente (sin diferencias no escribe — `TenantTimeZoneReconciler.cs:39-56`). Sin lock distribuido en los jobs de consumidor (FI lo justifica por idempotencia: `TenantEntitlementsReconciliationJob.cs:23-28`); Subscription sí usa lock.

---

## 2. Evidencia documental

- Memoria `project_tenant_timezones_auditoria.md`: `TenantTimeZones__Reconciliation__TenantBaseUrl` añadido a 5 servicios con permiso del dueño; verificación en vivo 22/22 oficinas.
- Memoria `project_financial_insights_local.md`: "Directorio de oficinas vacío, arreglado 2026-11-14" (job al arrancar y cada 12 h, reintento a 5 min).
- `src/BuildingBlocks/TenantTimeZones/TenantTimeZoneReconciliationOptions.cs:5-7` documenta el respaldo a la URL de PlatformSettings.
- No hallé en los vaults (`RABC`, `RateLimit`, `CommunicationCorrecciones`) decisiones sobre `ReconciliationRetry`.

---

## 3. Problemas detectados

1. **El reintento corto no está en todos los jobs de arranque.** Al menos 10 reconciliaciones siguen con `Task.Delay(interval)` tras un fallo (tabla §1.3): las de FI (clientes completos, nombres, entitlements), Customer (empleados), Notification (destinatarios y directorio de clientes), Correspondence/Signature (clientes), Sms (política de contacto), Calendar (nombres). Si corren antes que Auth/Customer al desplegar, el incidente de las 12 h se repite para ellas. El test solo vigila 5 archivos.
2. **Política de reintento heterogénea**: 2 min (`ReconciliationRetry`), 5 min propio (FI tenant directory), 60 s de demora inicial + 6 h (FI entitlements), ninguna (resto).
3. **Respaldo a localhost en código de producción** (`LocalTenantBaseUrl`, `CustomerBaseUrl` por defecto): un servicio nuevo que olvide la variable en compose arranca "bien" y nunca reconcilia; solo la fitness de compose lo detecta. **[Decisión pendiente]**: si el default debe seguir existiendo fuera de Development.
4. **Carrera posible en la reconciliación de zona** (inferida del código, no reproducida): `TenantTimeZoneReconciler` lee la página remota y después, por oficina, `FindAsync` + `Reconcile` sin versión. Si entre ambas lecturas se aplicó un `TenantUpdated` más nuevo, `Reconcile` reescribe la zona con el dato leído antes; el siguiente evento con la misma versión se ignoraría (`IsOlderThan` compara versiones) y el valor viejo duraría hasta la próxima pasada (12 h). Ventana de milisegundos.
5. **Subscription no repara a los consumidores de entitlements** (`OnlyIfChanged`); solo FI tiene pull de entitlements. Un servicio nuevo con gate de módulo depende de que el plan de cada tenant cambie para recibir su fila.
6. **Endpoints cross-tenant con criterios distintos**: el directorio de Tenant no exige PlatformTenant; Customer y Auth sí.
7. **El directorio de Tenant no trae total** (fin por página corta) y el de Customer sí (`TotalCount`): cada cliente implementa su propio criterio de fin.

---

## 4. Buenas prácticas observadas

- Jobs que no tumban el servicio, esperan `ApplicationStarted`, crean scope y correlación por pasada.
- Idempotencia y versión en el upsert de la reconciliación igual que en el consumer.
- `observedAtUtc` fijado al inicio de la pasada: conservador frente a eventos concurrentes.
- Resultado explícito (`TenantReachable`, `Applied`/`Inserted`/`Refreshed`) y logs con conteos.
- Fitness de despliegue para URL internas y `ReconciliationRetry`.
- Push de permisos centralizado en Auth: un job en vez de 23 (`PermissionsReconciliationService.cs:19-24`).

---

## 5. Recomendación

- Toda reconciliación de arranque: devolver `bool completed` y esperar `ReconciliationRetry.NextDelay(completed, interval)`; añadir el archivo al `[InlineData]` de `ReconciliationRetryTests`.
- URL interna siempre explícita en compose (`http://<servicio>-api:8080`), nunca depender del default.
- Token de PlatformTenant para endpoints cross-tenant.
- Comparar versiones del dueño (o `observedAtUtc` de inicio de pasada) antes de escribir.
- **[Decisión pendiente]**: unificar la semántica de paginación (`PagedResult` con total) y la exigencia de PlatformTenant en endpoints de reconciliación.

---

## 6. Riesgos

| Riesgo | Efecto |
|---|---|
| Job sin reintento corto que arranca antes que su fuente | Proyección vacía/desfasada hasta 6-12 h tras cada despliegue |
| URL ausente en compose | Reconciliación contra `localhost` del contenedor: nunca repara, solo warnings |
| Token M2M ausente | Reconciliación aborta en la página 1 cada pasada |
| Snapshot sin versión pisando evento nuevo | Dato viejo hasta la siguiente pasada |
| Endpoint cross-tenant sin PlatformTenant | Cualquier cliente M2M enumera datos de todos los tenants |

---

## 7. Pruebas

**Existentes [Código verificado]**
- `deploy/tests/TaxVision.BuildingBlocks.Tests/Common/ReconciliationRetryTests.cs`: `A_pass_that_could_not_finish_retries_in_minutes_not_hours`, `A_short_interval_is_never_stretched_by_the_retry`, `Startup_reconciliation_jobs_retry_soon_after_a_failed_pass` (5 archivos).
- `…/TenantTimeZones/TenantTimeZoneReconcilerTests.cs`, `TenantTimeZoneReconciliationOptionsTests.cs`, `TenantTimeZonesDependencyTests.cs`.
- `…/Deploy/TenantTimeZoneReconciliationDeployTests.cs`, `ComposeInternalBaseUrlWiringTests.cs` (`No_internal_base_url_env_points_to_localhost_in_compose`, `Services_set_internal_base_urls_to_container_dns`), `AssignmentVisibilityDeployWiringTests.cs`.
- FI: `deploy/tests/TaxVision.FinancialInsights.Tests/Infrastructure/TenantDirectoryReconciliationTests.cs` (`Reconciler_inserts_missing_offices_with_their_status_across_pages`, `Reconciler_refreshes_stale_rows_and_keeps_newer_ones`, `Reconciler_aborts_when_tenant_does_not_answer`…), `TenantEntitlementsReconciliationTests.cs` (`Refresh_creates_a_missing_row_and_updates_a_stale_one`, `Job_targets_known_tenants_without_a_row_or_with_a_stale_one`, `Portal_import_heals_a_missing_plan_row_instead_of_failing`).
- Auth: `deploy/tests/TaxVision.Auth.Tests/Infrastructure/PermissionsReconciliationServiceTests.cs`, `Api/InternalUserDirectoryReconciliationEndpointTests.cs`.

**Que debe tener un servicio nuevo [Recomendación]**
1. Reconciliador: inserta faltantes en varias páginas; corrige desfasados; no pisa una fila más nueva; sin diferencias no escribe.
2. Fuente caída o sin token → `completed=false` y no lanza.
3. Job: el archivo aparece en `ReconciliationRetryTests`.
4. Compose: su URL interna está en `ComposeInternalBaseUrlWiringTests` / `TenantTimeZoneReconciliationDeployTests` cuando aplique.

---

## 8. Dependencias

Tenant (`internal/tenants/directory`), Customer (`internal/customers/*reconciliation`), Auth (`internal/users/*/reconciliation`, snapshot de permisos, push de permisos), Subscription (`internal/tenants/{id}/entitlements`, `internal/plan-rate-limits`), M2M (`M2M_ServiceAuth.md`), compose (`x-platform-settings-env`), `BuildingBlocks.Common.ReconciliationRetry`.

---

## 9. Por qué un servicio funcional puede quedar inaccesible

- Sin reconciliación de asignaciones (o con la URL en localhost), los empleados sin `customers.view_all` ven listas vacías aunque Customer tenga asignaciones.
- Sin reconciliación de directorio de oficinas (FI), las consultas que exigen oficina activa no encuentran el tenant.
- Sin pull de entitlements, el worker de FI dejaba documentos esperando plan (cerrado con `Portal_import_heals_a_missing_plan_row_instead_of_failing`).
- Sin re-publicación de permisos ni pull-recovery, usuarios anteriores al servicio reciben 403 hasta 6 h.

---

## 10. Material para la guía (servicio de referencia: FinancialInsights + kits compartidos)

### 10.1 Kits compartidos (una línea cada uno)
`src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/DependencyInjection.cs:59-60,148-157`:
```csharp
services.AddTenantTimeZoneProjection<FinancialInsightsDbContext>();
services.AddTenantTimeZoneReconciliation<FinancialTenantTimeZoneTokenProvider>(configuration);
services.AddCustomerVisibilityProjection<FinancialInsightsDbContext>();
services.AddCustomerVisibilityReconciliation<FinancialPlatformTokenProvider>(configuration);
var customerUrl = configuration["Services:Customer:BaseUrl"];
if (string.IsNullOrWhiteSpace(configuration[$"{CustomerVisibilityReconciliationOptions.SectionName}:CustomerBaseUrl"])
    && !string.IsNullOrWhiteSpace(customerUrl))
    services.PostConfigure<CustomerVisibilityReconciliationOptions>(o => o.CustomerBaseUrl = customerUrl);
```

### 10.2 Proveedor de token PlatformTenant
```csharp
// Tenants/FinancialTenantTimeZoneTokenProvider.cs:8-13
internal sealed class FinancialTenantTimeZoneTokenProvider(IServiceTokenAcquirer tokenAcquirer)
    : ITenantTimeZoneTokenProvider
{
    public Task<string?> GetServiceTokenAsync(CancellationToken ct = default) =>
        tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
}
```

### 10.3 Bucle del job con reintento corto (patrón compartido)
`src/BuildingBlocks/TenantTimeZones/TenantTimeZoneReconciliationJob.cs:21-45`:
```csharp
await WaitForStartedAsync(serviceProvider.GetRequiredService<IHostApplicationLifetime>(), stoppingToken);
var options = serviceProvider.GetRequiredService<IOptions<TenantTimeZoneReconciliationOptions>>().Value;
if (!options.Enabled) return;
var interval = TimeSpan.FromHours(Math.Max(1, options.IntervalHours));
while (!stoppingToken.IsCancellationRequested)
{
    var completed = await RunOnceSafeAsync(Math.Clamp(options.PageSize, 1, 200), stoppingToken);
    try { await Task.Delay(ReconciliationRetry.NextDelay(completed, interval), stoppingToken); }
    catch (TaskCanceledException) { break; }
}
```

### 10.4 Compose del servicio (URLs internas explícitas)
```yaml
# deploy/docker/docker-compose.yml (financial-insights-api)
Services__Auth__BaseUrl: http://auth-api:8080/
Services__Customer__BaseUrl: http://customer-api:8080/
Services__Tenant__BaseUrl: http://tenant-api:8080/
TenantTimeZones__Reconciliation__TenantBaseUrl: http://tenant-api:8080
SubscriptionClient__BaseUrl: http://subscription-api:8080
# Para servicios con el kit de asignaciones sin PostConfigure propio:
# CustomerVisibility__Reconciliation__CustomerBaseUrl: http://customer-api:8080
```
O heredar `<<: *platform-settings-env` (aporta `PlatformSettings__TenantBaseUrl`).

### 10.5 Opciones (appsettings, FI `:37-41`)
```json
"FinancialInsights": {
  "CustomerReconciliation": { "Enabled": true, "IntervalHours": 12, "PageSize": 200, "NamesIntervalHours": 6 },
  "TenantReconciliation": { "Enabled": true, "IntervalHours": 12, "PageSize": 200 }
}
```

### 10.6 Checklist
- [ ] Endpoint fuente identificado y su exigencia de PlatformTenant.
- [ ] Token M2M de PlatformTenant disponible (cliente registrado en Auth).
- [ ] URL interna explícita en compose; nada apunta a `localhost`.
- [ ] Reconciliador idempotente y versionado; guarda por página.
- [ ] Job con `ReconciliationRetry.NextDelay` y en `ReconciliationRetryTests`.
- [ ] Logs con conteos y warning cuando la fuente no responde.
