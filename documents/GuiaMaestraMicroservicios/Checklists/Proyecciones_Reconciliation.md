# Checklist — Proyecciones locales y reconciliación

> Propósito: que cada read-model local (permisos, plan, zona horaria, asignaciones, directorios) se llene por eventos, no retroceda ante duplicados o desorden, y se repare solo cuando el servicio nace después de los datos o pierde eventos.
> Regla de oro del código: en el camino de la request se lee la proyección local; M2M solo para reconciliar o para una consulta que no admite retraso.

**Cuándo aplica:** el servicio guarda datos de otro dueño (Auth, Subscription, Tenant, Customer), usa `[HasPermission]`, gate de módulo, cuotas por plan, zona de la oficina o visibilidad por cliente asignado.

**Formato:** *Verificación* = comando, prueba o archivo; *Origen* = investigación. Condicionales entre corchetes.

---

## 1. Diseño de cada proyección

- [ ] **Dueño único y evento con estado completo** (códigos efectivos, set de asignados, snapshot de entitlements) y versión monotónica definida por el dueño.
  - Verificación: revisar el contrato en `src/BuildingBlocks/Messaging/<Dueño>IntegrationEvents/` y la tabla "Reglas de orden" de la investigación.
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §1.2 y §5.
- [ ] **Tabla + índice único + migración aplicada en local** (p. ej. `(TenantId, UserId)` para permisos).
  - Verificación: `scripts/migrate-local.ps1` sin pendientes; configuración EF con el índice.
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §10.1 y §10.6.
- [ ] **Upsert idempotente `ApplyIfNewer`**: descarta versión menor; igual se re-aplica. Ciclo de vida por fecha (`SetActive(active, observedAtUtc)`).
  - Verificación: prueba por consumer "crea si no existe; aplica mayor; ignora menor; duplicado no cambia nada".
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §7 y §10.1.
- [ ] **Escritura con `IgnoreQueryFilters()` + `TenantId` del evento.**
  - Verificación: prueba "tenant A no ve filas del tenant B en la proyección".
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §1.1 y §7.
- [ ] **`correlation.Push(evt.CorrelationId ?? EventId)`** en la primera línea del consumer (G15).
  - Verificación: revisión del consumer (no hay fitness para el `Push` del consumer).
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §1.1; [Guardrails](../Referencias/Guardrails.md) #15.
- [ ] [si hay caché sobre la proyección] **Invalidar la caché después de guardar** (plan, módulos).
  - Verificación: `ModuleGateWiringFitnessTests.Every_gated_service_invalidates_the_modules_cache_on_a_plan_change` y `No_service_registers_the_modules_reader_without_its_cache`.
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §4 y §10.4.
- [ ] **Referencia a empleado anotada** con `[UserReference(UserReferenceKind.Projection, HandledBy = ...)]`.
  - Verificación: `UserReferenceContractTests` (BB) en el gate.
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §4.
- [ ] **No derivar permisos de usuario desde `RolePermissionsChanged`**: ese consumer solo guarda la fila del rol.
  - Verificación: prueba "`RolePermissionsChanged` no altera los códigos efectivos de un usuario".
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §3.1 y §5; [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §3.1.

## 2. Cableado

- [ ] **El dueño declara la ruta** `PublishMessage<TEvento>().ToRabbitExchange("taxvision-events")`; sin ella el evento se descarta y la proyección nunca se llena.
  - Verificación: `EventRoutingContractTests` (BB); en el log del emisor, ninguna línea "No routes can be determined".
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §1.1 y §6.
- [ ] **Cola del servicio** `<svc>-events` enlazada a `taxvision-events` con routing key vacía y `UseDurableInbox()`.
  - Verificación: `Program.cs` (patrón FI `ListenToRabbitQueue("financial-insights-events", ...)`).
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §1.1 y §10.5.
- [ ] [si usa consumers compartidos] `options.Discovery.IncludeType(typeof(CustomerAssignmentsProjectionConsumer))` y/o `typeof(TenantTimeZoneProjectionConsumer)`. Sin la línea, el evento llega y no se procesa.
  - Verificación: `Program.cs`; regenerar codegen (el `IncludeType` cambia el código generado).
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §1.4, §6 y §10.5.
- [ ] [si calcula fechas en la zona de la oficina] Kit TenantTimeZones: `AddTenantTimeZoneProjection<TDbContext>()`, `AddTenantTimeZoneReconciliation<TTokenProvider>(config)`, `modelBuilder.ApplyTenantTimeZoneProjection()` y migración propia. Fuente: `TenantCreated`/`TenantUpdated` (no `TenantTimeZoneChanged`).
  - Verificación: `TenantTimeZonesDependencyTests` (BB) como patrón; migración aplicada.
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §1.4 y problema 4; [BuildingBlocks](../Investigaciones/BuildingBlocks.md) matriz.
- [ ] [si filtra datos por cliente asignado] Kit CustomerVisibility: `AddCustomerVisibilityProjection<TDbContext>()`, `AddCustomerVisibilityReconciliation<TTokenProvider>(config)`, `modelBuilder.ApplyCustomerAssignmentProjection()` y migración.
  - Verificación: patrón `Billing.Tests/Customers/CustomerAssignmentsProjectionConsumerTests.cs`; `AssignmentVisibilityDeployWiringTests`.
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §1.4; [BuildingBlocks](../Investigaciones/BuildingBlocks.md) matriz.
- [ ] [si usa el plan del tenant] `TenantPlanCodeProjectionConsumer` como wrapper de 1 línea sobre `TenantPlanCodeProjectionHandler.Handle` + invalidador de las dos cachés.
  - Verificación: patrón `Notes.Application/RateLimiting/Consumers/TenantPlanCodeProjectionConsumer.cs`; prueba `TenantPlanCodeProjectionTests` del servicio.
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §10.4.
- [ ] [si usa permisos] Lector y escritor de pull-recovery en una sola clase registrada como `IUserPermissionsProjectionReader` e `IUserPermissionsProjectionWriter`.
  - Verificación: `DependencyInjection.cs` (patrón FI `:68-74`).
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §10.3.

## 3. Reparación (reconciliación)

- [ ] **Toda proyección tiene reparación**: pull bajo demanda y/o reconciliación periódica. Un servicio nacido después de los datos queda vacío para todo lo anterior hasta reconciliar.
  - Verificación: identificar el job o el pull en `Infrastructure/`; prueba del reconciliador.
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §5 y §6; [Reconciliation](../Investigaciones/Reconciliation.md) §1.
- [ ] **Endpoint fuente identificado** y su exigencia de PlatformTenant (Tenant `internal/tenants/directory` no la exige; Customer y Auth sí).
  - Verificación: tabla §1.2 de Reconciliation.
  - Origen: [Reconciliation](../Investigaciones/Reconciliation.md) §1.2 y §10.6.
- [ ] **Token M2M de PlatformTenant** disponible (proveedor `GetServiceTokenAsync` → `tokenAcquirer.GetTokenAsync(PlatformTenant.Id)` y cliente registrado en Auth).
  - Verificación: prueba "fuente caída o sin token → `completed=false` y no lanza".
  - Origen: [Reconciliation](../Investigaciones/Reconciliation.md) §7 y §10.2.
- [ ] **Job con reintento corto**: devuelve `bool completed` y espera `ReconciliationRetry.NextDelay(completed, interval)`; el archivo se agrega al `[InlineData]` de `ReconciliationRetryTests`.
  - Verificación: `deploy/tests/TaxVision.BuildingBlocks.Tests/Common/ReconciliationRetryTests.cs` (`Startup_reconciliation_jobs_retry_soon_after_a_failed_pass`) en el gate.
  - Origen: [Reconciliation](../Investigaciones/Reconciliation.md) §1.4, §5 y §10.3.
- [ ] **El job espera `ApplicationStarted`**, crea scope y correlación por pasada, guarda por página (`ChangeTracker.Clear()` entre páginas) y nunca tumba el servicio.
  - Verificación: revisión del job contra `TenantTimeZoneReconciliationJob.cs:21-94`.
  - Origen: [Reconciliation](../Investigaciones/Reconciliation.md) §1.3, §1.6 y §4.
- [ ] **Versionado en la reconciliación**: compara la versión del dueño o `observedAtUtc` fijado al inicio de la pasada; sin diferencias no escribe.
  - Verificación: pruebas "inserta faltantes en varias páginas; corrige desfasados; no pisa una fila más nueva; sin diferencias no escribe".
  - Origen: [Reconciliation](../Investigaciones/Reconciliation.md) §1.6 y §7.
- [ ] **URL interna explícita en compose** (`TenantTimeZones__Reconciliation__TenantBaseUrl: http://tenant-api:8080` o el ancla `<<: *platform-settings-env`; `CustomerVisibility__Reconciliation__CustomerBaseUrl: http://customer-api:8080`). El default de código es `localhost` y en un contenedor nunca reconcilia.
  - Verificación: `TenantTimeZoneReconciliationDeployTests.Every_service_that_reconciles_time_zones_gets_the_tenant_url_in_compose`, `ComposeInternalBaseUrlWiringTests.No_internal_base_url_env_points_to_localhost_in_compose`, `AssignmentVisibilityDeployWiringTests`.
  - Origen: [Reconciliation](../Investigaciones/Reconciliation.md) §1.5 y §10.4.
- [ ] [si el servicio enforza el gate o depende de cuotas por plan] Reparación pull de entitlements (patrón FI `TenantEntitlementsResolver` + `TenantEntitlementsReconciliationJob`): la reconciliación de Subscription (`OnlyIfChanged`) no repara a los consumidores.
  - Verificación: prueba "refresh crea la fila faltante y actualiza una desfasada" (patrón `TenantEntitlementsReconciliationTests`).
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §3.3 y §5; [Reconciliation](../Investigaciones/Reconciliation.md) §3.5.
- [ ] **Logs con conteos** y warning cuando la fuente no responde.
  - Verificación: log del servicio tras arrancar en local (`logs\<Svc>.log`).
  - Origen: [Reconciliation](../Investigaciones/Reconciliation.md) §4 y §10.6.

## 4. Migración, reconciliación y reposición no son lo mismo

- [ ] **Esquema y catálogo determinista** → migración (contenedor `migrations` en el deploy; local `scripts/migrate-local.ps1`). **Datos de proyección** → reconciliación en runtime. **Reposición de datos de negocio** → la decide el dueño.
  - Verificación: revisar que la reparación no esté escondida en una migración.
  - Origen: [Migraciones](../Investigaciones/Migraciones.md) "Migración vs reconciliation".
- [ ] **Antes de re-publicar un evento** (reconciliación push, backfill) revisar todos sus consumidores, sobre todo los que mandan correos.
  - Verificación: búsqueda de `Handle(<Evento>` en `src/Services`.
  - Origen: [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §2 y §4; [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §4.

## 5. Verificación tras desplegar

- [ ] **Proyecciones pobladas en todas las bases.** Ejemplo real de consulta de solo lectura: `Implementaciones/CommunicationCorrecciones/Proyectos/TenantTimeZones/Evidencias/Despliegue_Proyecciones_Zona.sql` (columna `faltan` = 0 en todas las bases; `en_utc` revela oficinas sin zona).
  - Verificación: correr el SQL (solo `SELECT`) y adaptar el nombre de tabla para otras proyecciones.
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §9; ver también [Produccion](Produccion.md).

---

## Pruebas mínimas (resumen)

| Escenario | Origen |
|---|---|
| Crea / aplica mayor / ignora menor / duplicado no cambia | [Proyecciones](../Investigaciones/Proyecciones.md) §7 |
| Evento con `TenantId` vacío → excepción | [Proyecciones](../Investigaciones/Proyecciones.md) §7 |
| Tenant A no ve filas de B | [Proyecciones](../Investigaciones/Proyecciones.md) §7 |
| Desactivar → lector null; reactivar sin fila no crea fila vacía | [Proyecciones](../Investigaciones/Proyecciones.md) §7 |
| Reconciliador: faltantes, desfasados, fila más nueva intacta, sin escrituras inútiles | [Reconciliation](../Investigaciones/Reconciliation.md) §7 |
| Fuente caída → `completed=false` | [Reconciliation](../Investigaciones/Reconciliation.md) §7 |

## Decisiones pendientes

- Re-publicación forzada de entitlements por Subscription vs pull por servicio ([Proyecciones](../Investigaciones/Proyecciones.md) §5).
- Paginación unificada (`PagedResult` con total) y exigencia de PlatformTenant en todos los endpoints de reconciliación ([Reconciliation](../Investigaciones/Reconciliation.md) §5).
- Si el default `localhost` debe existir fuera de Development ([Reconciliation](../Investigaciones/Reconciliation.md) problema 3).
