
# TaxVision — Subscription Service

**Implementado por:** Wagner Alcantara
**Fecha:** 01-07-2026

Documentacion del microservicio `TaxVision.Subscription` y todos los cambios realizados
durante su implementacion e integracion al repositorio principal.

---

## Indice

1. Descripcion del microservicio
2. Estructura de capas
3. Dominio
4. Application Layer
5. Infrastructure Layer
6. Api Layer
7. Modelo de facturacion sin prorrateo
8. Eventos de integracion
9. Base de datos y migraciones
10. Rutas YARP agregadas al Gateway
11. Variable de entorno agregada
12. Correcciones realizadas
13. Buenas practicas aplicadas
14. Endpoints
15. Coleccion Postman

---

## 1. Descripcion del microservicio

`TaxVision.Subscription` es el tercer microservicio del backend. Gestiona:

- planes comerciales con niveles de servicio (`Standard`, `Pro`);
- modulos funcionales y su asignacion a planes;
- suscripciones por tenant con modelo de asientos;
- enrollments previos al tenant (el cliente elige plan y paga antes de que exista el tenant);
- facturacion por asientos sin prorrateo — modelo Google Workspace;
- cambios de plan pendientes que aplican al inicio del siguiente ciclo;
- ciclo de vida de asientos: compra, renovacion, pago completado y pago fallido.

Se comunica con el resto del sistema exclusivamente por eventos de integracion:
nunca consulta las bases `TaxVision_Auth` ni `TaxVision_Tenants`.

---

## 2. Estructura de capas

```text
src/Services/Subscription/
├── TaxVision.Subscription.Domain/
│   ├── Enrollments/
│   │   ├── SubscriptionEnrollment.cs
│   │   └── EnrollmentStatus.cs
│   ├── Modules/
│   │   ├── Module.cs
│   │   └── PlanModule.cs
│   ├── Plans/
│   │   ├── Plan.cs
│   │   ├── PlanFeature.cs
│   │   ├── PlanVersion.cs
│   │   └── ServiceLevel.cs
│   ├── Subscriptions/
│   │   ├── Subscription.cs
│   │   ├── SeatSubscription.cs
│   │   ├── SubscriptionModule.cs
│   │   ├── SubscriptionStatus.cs
│   │   └── PendingSubscriptionChange.cs
│   └── ValueObjects/
│       └── Money.cs
│
├── TaxVision.Subscription.Application/
│   ├── Abstractions/
│   │   ├── IPlanRepository.cs
│   │   ├── IModuleRepository.cs
│   │   ├── ISubscriptionRepository.cs
│   │   ├── ISubscriptionModuleRepository.cs
│   │   ├── IPendingChangeRepository.cs
│   │   ├── IEnrollmentRepository.cs
│   │   ├── IPlanReadService.cs
│   │   ├── IModuleReadService.cs
│   │   └── ISubscriptionModuleReadService.cs
│   ├── Enrollments/Commands/CreateEnrollment.cs
│   ├── Enrollments/IntegrationEvents/
│   ├── Modules/Commands/
│   ├── Modules/Queries/
│   ├── Plans/Commands/
│   ├── Plans/Queries/
│   ├── SubscriptionModules/Commands/
│   ├── SubscriptionModules/Queries/
│   └── Subscriptions/
│       ├── Commands/
│       └── IntegrationEvents/
│
├── TaxVision.Subscription.Infrastructure/
│   ├── Persistence/
│   │   ├── Configurations/       (6 configuraciones EF Core)
│   │   ├── ReadServices/         (PlanReadService, ModuleReadService, SubscriptionModuleReadService)
│   │   ├── Repositories/         (6 repositorios)
│   │   ├── SubscriptionDbContext.cs
│   │   └── SubscriptionDbContextFactory.cs
│   ├── Migrations/               (3 migraciones)
│   └── DependencyInjection.cs
│
└── TaxVision.Subscription.Api/
    ├── Controllers/
    │   ├── EnrollmentsController.cs
    │   ├── ModulesController.cs
    │   ├── PlansController.cs
    │   ├── SubscriptionModulesController.cs
    │   └── SubscriptionsController.cs
    ├── Program.cs
    └── Dockerfile
```

---

## 3. Dominio

Las entidades controlan su estado con setters privados y fabricas estaticas:

```csharp
// Plan creado con fabrica
var plan = Plan.Create(name, title, description,
    basePriceMonthly, basePriceAnnual,
    pricePerAdditionalSeat, includedSeats,
    currency, serviceLevel);

// Subscription
var sub = Subscription.Create(tenantId, plan, billingPeriod, startDate);

// SeatSubscription
var seat = SeatSubscription.Create(subscriptionId, quantity,
    pricePerSeat, currency, anchorDay);

// Enrollment
var enrollment = SubscriptionEnrollment.Create(planId, planName,
    billingPeriod, adminEmail, orgName, subdomain, timeZoneId, totalAmount);
```

`ServiceLevel` acepta `Standard = 1` y `Pro = 2`.
`BillingPeriod` acepta `Monthly` y `Annual`.
`EnrollmentStatus` recorre: `PendingPayment` -> `PaymentCompleted` -> `TenantProvisioned`.

---

## 4. Application Layer

### Interfaces declaradas (9 contratos)

| Interfaz | Proposito |
| --- | --- |
| `IPlanRepository` | CRUD de planes |
| `IModuleRepository` | CRUD de modulos |
| `ISubscriptionRepository` | CRUD de suscripciones |
| `ISubscriptionModuleRepository` | asignaciones de modulos a suscripciones |
| `IPendingChangeRepository` | cambios pendientes de plan |
| `IEnrollmentRepository` | enrollments previos al tenant |
| `IPlanReadService` | joins complejos de planes con features y modulos |
| `IModuleReadService` | join de modulos con filtros |
| `ISubscriptionModuleReadService` | join de modulos por suscripcion |

### Comandos implementados

Plans: `CreatePlan`, `UpdatePlan`, `DeletePlan`, `TogglePlanStatus`, `AssignModuleToPlan`, `RemoveModuleFromPlan`

Modules: `CreateModule`, `UpdateModule`, `DeleteModule`, `ToggleModuleStatus`

Subscriptions: `CreateSubscription`, `ChangePlan`, `ApplyPendingChange`, `RenewSubscription`, `UpdateSubscriptionPrice`, `AddSeat`, `CancelAtPeriodEnd`

Enrollments: `CreateEnrollment`

SubscriptionModules: `AssignSubscriptionModule`, `RemoveSubscriptionModule`

### Handlers de eventos de integracion consumidos

Todos los handlers viven en la capa Application (no en Infrastructure):

- `TenantCreatedConsumer` — sincroniza contexto de tenant desde `subscription-service-events`
- `EnrollmentPaymentCompletedHandler`
- `SeatPaymentCompletedHandler`, `SeatPaymentFailedHandler`
- `SeatRenewalDueHandler`, `SeatRenewalPaymentCompletedHandler`, `SeatRenewalPaymentFailedHandler`
- `SubscriptionRenewalDueHandler`, `SubscriptionRenewalPaymentCompletedHandler`, `SubscriptionRenewalPaymentFailedHandler`

---

## 5. Infrastructure Layer

### SubscriptionDbContext

Implementa `IUnitOfWork`. Convierte errores SQL 2601/2627 en `ConflictException`
igual que `AuthDbContext` y `TenantDbContext`.

### ReadService

Patron ReadService aplicado: la interfaz se declara en Application, la implementacion
vive en Infrastructure junto al DbContext. Aplica a `PlanReadService`,
`ModuleReadService` y `SubscriptionModuleReadService`.

```csharp
// Application/Abstractions
public interface IPlanReadService
{
    Task<List<PlanDto>> GetAllAsync(bool? isActive, CancellationToken ct = default);
    Task<PlanDto> GetByIdWithDetailsAsync(Guid planId, CancellationToken ct = default);
}
```

### DependencyInjection

`AddSubscriptionInfrastructure` registra:

- `SubscriptionDbContext` como `IUnitOfWork`;
- 6 repositorios;
- 3 ReadServices.

---

## 6. Api Layer

### Middleware aplicado (mismo orden que los otros servicios)

```csharp
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
```

### Wolverine — discovery de handlers

En `Program.cs` se usa `IncludeAssembly` para registrar handlers de Application:

```csharp
opts.Discovery.IncludeAssembly(
    typeof(TaxVision.Subscription.Application.AssemblyReference).Assembly);
```

---

## 7. Modelo de facturacion sin prorrateo

TaxVision implementa el modelo Google Workspace. Ningun cambio de plan genera
cargos parciales ni creditos retroactivos.

- los cambios de plan o periodo de facturacion se registran como `PendingSubscriptionChange`;
- `PendingSubscriptionChange` almacena: `SubscriptionId`, `NewPlanId`, `NewBillingPeriod`, `RequestedAtUtc`;
- el cambio se aplica mediante `POST /subscriptions/pending-changes/{id}/apply` (solo `PlatformAdmin`);
- al aplicar se actualiza el plan activo, el precio base y los modulos incluidos;
- se publica `TenantEntitlementsChangedIntegrationEvent` para notificar a otros servicios.

La migracion `RemoveProrationAndRenameToSubscriptionModules` elimino los campos
`ProrationAmount`, `ProrationCredits` y cualquier logica de prorrateo del esquema
original, ademas de renombrar la tabla de modulos de suscripcion a `SubscriptionModules`.

---

## 8. Eventos de integracion

### Publicados (exchange `taxvision-events`)

| Evento | Disparado por |
| --- | --- |
| `EnrollmentPaymentRequestedIntegrationEvent` | `CreateEnrollment` |
| `TenantProvisioningRequestedIntegrationEvent` | `EnrollmentPaymentCompletedHandler` |
| `SeatPurchaseRequestedIntegrationEvent` | `AddSeat` |
| `SeatRenewalPaymentRequestedIntegrationEvent` | `SeatRenewalDueHandler` |
| `SubscriptionRenewalPaymentRequestedIntegrationEvent` | `SubscriptionRenewalDueHandler` |
| `SubscriptionRenewalDueIntegrationEvent` | `RenewSubscription` |
| `TenantEntitlementsChangedIntegrationEvent` | `ApplyPendingChange` |

### Consumidos (cola `subscription-service-events`)

| Evento | Handler |
| --- | --- |
| `TenantCreatedIntegrationEvent` | `TenantCreatedConsumer` — upsert de contexto |
| `EnrollmentPaymentCompletedIntegrationEvent` | `EnrollmentPaymentCompletedHandler` |
| `SeatPaymentCompletedIntegrationEvent` | `SeatPaymentCompletedHandler` |
| `SeatPaymentFailedIntegrationEvent` | `SeatPaymentFailedHandler` |
| `SeatRenewalDueIntegrationEvent` | `SeatRenewalDueHandler` |
| `SeatRenewalPaymentCompletedIntegrationEvent` | `SeatRenewalPaymentCompletedHandler` |
| `SeatRenewalPaymentFailedIntegrationEvent` | `SeatRenewalPaymentFailedHandler` |
| `SubscriptionRenewalPaymentCompletedIntegrationEvent` | `SubscriptionRenewalPaymentCompletedHandler` |
| `SubscriptionRenewalPaymentFailedIntegrationEvent` | `SubscriptionRenewalPaymentFailedHandler` |

Outbox transaccional configurado igual que Auth y Tenant:

```csharp
options.PersistMessagesWithSqlServer(sqlConn);
options.Policies.UseDurableOutboxOnAllSendingEndpoints();
options.UseEntityFrameworkCoreTransactions()
    .WithDbContextAbstraction<IUnitOfWork, SubscriptionDbContext>();
options.Policies.AutoApplyTransactions();
```

---

## 9. Base de datos y migraciones

**Base:** `TaxVision_Subscription`

### Tablas

| Tabla | Contenido |
| --- | --- |
| `Plans` | planes comerciales |
| `PlanFeatures` | features incluidas por plan |
| `PlanVersions` | historial de versiones de precio |
| `Modules` | modulos funcionales |
| `PlanModules` | asignacion de modulos a planes |
| `Subscriptions` | suscripcion activa por tenant |
| `SeatSubscriptions` | asientos adicionales |
| `SubscriptionModules` | modulos activos en la suscripcion |
| `PendingSubscriptionChanges` | cambios de plan pendientes |
| `SubscriptionEnrollments` | enrollments previos al tenant |
| `wolverine_*` | outbox/inbox durable Wolverine |

### Indices

| Columna(s) | Tipo |
| --- | --- |
| `Plans.Code` | unico |
| `PlanFeatures.(PlanId, FeatureCode)` | unico compuesto |
| `Modules.Name` | unico |
| `Subscriptions.TenantId` | unico |
| `Subscriptions.(TenantId, Status)` | compuesto |
| `SeatSubscriptions.SubscriptionId` | indice |
| `SeatSubscriptions.PeriodEndUtc` | indice |
| `SubscriptionModules.(SubscriptionId, ModuleId)` | unico compuesto |

### Migraciones aplicadas

```
20260628222519_InitialSubscription
20260629012401_UpdateSubscriptionSchema
20260701011951_RemoveProrationAndRenameToSubscriptionModules
```

### Comando para aplicar

```powershell
dotnet ef database update `
  --project src\Services\Subscription\TaxVision.Subscription.Infrastructure\TaxVision.Subscription.Infrastructure.csproj `
  --startup-project src\Services\Subscription\TaxVision.Subscription.Api\TaxVision.Subscription.Api.csproj `
  --connection "Server=localhost,1433;Database=TaxVision_Subscription;User Id=sa;Password=<SA_PASSWORD>;TrustServerCertificate=true"
```

---

## 10. Rutas YARP agregadas al Gateway

Se agregaron tres rutas nuevas en `Gateway/TaxVision.Gateway/appsettings.json`:

```json
"plans": {
  "ClusterId": "subscription",
  "Match": { "Path": "/api/plans/{**catch-all}" },
  "AuthorizationPolicy": "anonymous"
},
"modules": {
  "ClusterId": "subscription",
  "Match": { "Path": "/api/modules/{**catch-all}" },
  "AuthorizationPolicy": "anonymous"
},
"subscription-modules": {
  "ClusterId": "subscription",
  "Match": { "Path": "/api/subscription-modules/{**catch-all}" }
}
```

El cluster `subscription` apunta a `subscription-api:8080` dentro de
`taxvision-network`. El servicio usa `expose` (no `ports`), por lo que el puerto
interno 8080 no es accesible desde el host; todo el trafico pasa por el Gateway en
`localhost:5047`.

---

## 11. Variable de entorno agregada

Se agrego `SUBSCRIPTION_DB_CONNECTION` al `.env.example` y al `docker-compose.yml`
del stack completo (`deploy/docker/docker-compose.yml`):

```env
SUBSCRIPTION_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Subscription;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
```

Subscription no usa Redis. Solo requiere la connection string de SQL Server y la URI
de RabbitMQ. Ningun secreto aparece en `appsettings.json`; todos vienen del `.env`.

---

## 12. Correcciones realizadas

### Handlers movidos de Infrastructure a Application (Refactor Task #14)

Los handlers de Wolverine estaban en la capa Infrastructure (violacion de Clean
Architecture). Se movieron todos a la capa Application. Los controllers fueron
actualizados para referenciar los namespaces correctos:

**Antes (incorrecto):**
```csharp
using TaxVision.Subscription.Infrastructure.Handlers.Plans;
using TaxVision.Subscription.Infrastructure.Handlers.Modules;
using TaxVision.Subscription.Infrastructure.Handlers.Subscriptions;
using TaxVision.Subscription.Infrastructure.Handlers.SubscriptionModules;
```

**Despues (correcto):**
```csharp
using TaxVision.Subscription.Application.Plans.Commands;
using TaxVision.Subscription.Application.Plans.Queries;
using TaxVision.Subscription.Application.Modules.Commands;
using TaxVision.Subscription.Application.Modules.Queries;
using TaxVision.Subscription.Application.Subscriptions.Commands;
using TaxVision.Subscription.Application.SubscriptionModules.Commands;
using TaxVision.Subscription.Application.SubscriptionModules.Queries;
```

### Roles corregidos en controllers

Todos los endpoints usaban `[Authorize(Roles = "Developer")]`, un rol inexistente en
TaxVision. Corregidos a los actores reales del sistema:

| Operacion | Antes | Despues |
| --- | --- | --- |
| CRUD planes/modulos/subscriptions | `Developer` | `PlatformAdmin` |
| Seats, cancel, change-plan | `Developer` | `TenantAdmin` |
| Renew | `Developer` | `PlatformAdmin,TenantAdmin` |
| GET planes/modulos | `Developer` | `[AllowAnonymous]` |

---

## 13. Buenas practicas aplicadas

Verificacion de cumplimiento de todas las practicas del proyecto:

| Practica | Cumplimiento en Subscription |
| --- | --- |
| Database per service | `TaxVision_Subscription` propia, sin acceso a otras bases |
| Secrets fuera de Git | connection string via `.env` / User Secrets, nunca en `appsettings.json` |
| Atomic outbox | `PersistMessagesWithSqlServer` + `AutoApplyTransactions` |
| Durable inbox | cola `subscription-service-events` con inbox durable Wolverine |
| Idempotencia | `TenantCreatedConsumer` usa upsert |
| Correlation completo | `CorrelationIdMiddleware` + propagacion en eventos |
| Handlers en Application | ningun handler en Infrastructure; solo repositorios alli |
| ReadService | `IPlanReadService`, `IModuleReadService`, `ISubscriptionModuleReadService` |
| Error mapping | `Plan.NotFound`, `Module.NotFound`, `Subscription.NotFound`, `Enrollment.NotFound`, `Subscription.AlreadyExists`, `Plan.NameConflict`, `Module.NameConflict` |
| Race protection | indices unicos + `ConflictException` via SQL 2601/2627 |
| Sin prorrateo | `PendingSubscriptionChange` + aplica en renovacion |
| Imagenes reproducibles | Dockerfile con `mcr.microsoft.com/dotnet/aspnet:10.0.9` |
| Observabilidad central | `AddTaxVisionOpenTelemetry` desde BuildingBlocks.Web |
| Health checks | `AddHealthChecks().AddSqlServer(...).AddRabbitMQ(...)` |
| CQRS | comandos y queries via `bus.InvokeAsync<T>` |
| Clean Architecture | Domain sin dependencias de ASP.NET/EF; Application sin dependencias de SQL |
| Registro cerrado | endpoints de escritura protegidos por JWT + rol; no hay endpoints publicos de escritura sin control |
| Tenant header confiable | `TenantResolutionMiddleware` reconstruye `TenantContext` desde el claim JWT |

---

## 14. Endpoints

Base: `http://localhost:5047`

### Plans

| Metodo | Ruta | Acceso |
| --- | --- | --- |
| `GET` | `/api/plans` | Anonimo |
| `GET` | `/api/plans/{id}` | Anonimo |
| `POST` | `/api/plans` | PlatformAdmin |
| `PUT` | `/api/plans/{id}` | PlatformAdmin |
| `DELETE` | `/api/plans/{id}` | PlatformAdmin |
| `PATCH` | `/api/plans/{id}/status` | PlatformAdmin |
| `POST` | `/api/plans/{id}/modules/{moduleId}` | PlatformAdmin |
| `DELETE` | `/api/plans/{id}/modules/{moduleId}` | PlatformAdmin |

### Modules

| Metodo | Ruta | Acceso |
| --- | --- | --- |
| `GET` | `/api/modules` | Anonimo |
| `GET` | `/api/modules/{id}` | Autenticado |
| `POST` | `/api/modules` | PlatformAdmin |
| `PUT` | `/api/modules/{id}` | PlatformAdmin |
| `DELETE` | `/api/modules/{id}` | PlatformAdmin |
| `PATCH` | `/api/modules/{id}/status` | PlatformAdmin |

### Enrollments

| Metodo | Ruta | Acceso |
| --- | --- | --- |
| `POST` | `/enrollments` | Anonimo, rate limited |

Body:

```json
{
  "planCode": "Starter",
  "billingPeriod": "Monthly",
  "adminEmail": "admin@empresa.com",
  "orgName": "Mi Empresa",
  "subdomain": "mi-empresa",
  "timeZoneId": "America/Santo_Domingo"
}
```

Nota: `planCode` es el **nombre** del plan, no un codigo separado.

Respuesta `202 Accepted`:

```json
{
  "enrollmentId": "guid",
  "status": "PendingPayment",
  "totalAmount": 29.00
}
```

### Subscriptions

| Metodo | Ruta | Acceso |
| --- | --- | --- |
| `POST` | `/subscriptions` | PlatformAdmin |
| `POST` | `/subscriptions/current/seats` | TenantAdmin |
| `POST` | `/subscriptions/current/cancel` | TenantAdmin |
| `POST` | `/subscriptions/current/renew` | PlatformAdmin, TenantAdmin |
| `POST` | `/subscriptions/{id}/change-plan` | TenantAdmin |
| `POST` | `/subscriptions/pending-changes/{id}/apply` | PlatformAdmin |
| `PATCH` | `/subscriptions/{id}/price` | PlatformAdmin |

Ejemplo crear suscripcion:

```json
{
  "tenantId": "tenant-guid",
  "serviceLevel": "Standard",
  "billingPeriod": "Monthly",
  "isActive": true
}
```

### SubscriptionModules

| Metodo | Ruta | Acceso |
| --- | --- | --- |
| `GET` | `/api/subscription-modules/subscription/{id}` | Autenticado |
| `POST` | `/api/subscription-modules` | PlatformAdmin |
| `DELETE` | `/api/subscription-modules/{id}` | PlatformAdmin |

---

## 15. Coleccion Postman

`Postman_Collection/TaxVision_Backend.postman_collection.json` fue reorganizada en
seis carpetas:

- **Tenant** — crear, listar, cambiar estado
- **Auth** — login PlatformAdmin/TenantAdmin, refresh, revoke, invitaciones
- **Subscription / Plans** — CRUD completo + asignar/quitar modulos del plan
- **Subscription / Modules** — CRUD completo
- **Subscription / Enrollments** — enrollment publico
- **Subscription / Subscriptions** — crear, seats, cancel, renew, change-plan, apply, price
- **Subscription / SubscriptionModules** — get, assign, remove

Los scripts de test capturan automaticamente en variables de entorno:
`planId`, `moduleId`, `subscriptionId`, `pendingChangeId`, `subscriptionModuleId`,
`enrollmentId`.

Variables de entorno requeridas en Postman:

| Variable | Descripcion |
| --- | --- |
| `UrlBase` | `http://localhost:5047` |
| `accessToken` | se captura automaticamente en Login |
| `refreshToken` | se captura automaticamente en Login |
| `tenantId` | se captura automaticamente en Registro_Tenant |
| `invitationToken` | se captura automaticamente en Registro_Tenant o Invite |
| `planId` | se captura automaticamente en Create_Plan |
| `moduleId` | se captura automaticamente en Create_Module |
| `subscriptionId` | se captura automaticamente en Create_Subscription |
| `pendingChangeId` | se captura automaticamente en Change_Plan |
| `subscriptionModuleId` | se captura automaticamente en Assign_Module_To_Subscription |
| `enrollmentId` | se captura automaticamente en Create_Enrollme