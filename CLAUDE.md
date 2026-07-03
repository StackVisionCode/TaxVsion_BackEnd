
# Guia de implementación de los test (Pendiente)
<a href="https://firebasestorage.googleapis.com/v0/b/c5iffaa-10025.firebasestorage.app/o/Guia_Implementacion_Pruebas_Automatizadas_TaxVision.pdf?alt=media&token=2ba4fb54-9e81-4812-b75c-03fe2b5e61d5"> Guía Implementación Test</a>

<a href="https://firebasestorage.googleapis.com/v0/b/c5iffaa-10025.firebasestorage.app/o/Guia_Implementacion_Customer_Subscription_TaxVision.pdf?alt=media&token=ae21b127-f034-49c0-a04b-d28c03097212">Customer y Subscription </br>
Guia de implementacion</a>

# TaxVision Backend

<img width="2400" height="1560" alt="taxvision_arch" src="https://github.com/user-attachments/assets/65b9f169-c0f1-4098-8533-f3d50e90c015" />


Backend multitenant de TaxVision construido con microservicios en .NET 10.

**Autor de las implementaciones documentadas:** Jorge Turbi

**Actualizado:** 02-07-2026 (Payment service — Stripe SaaS + adaptadores tenant)

**Licencia del codigo propio:** propietaria; consulte [LICENSE](LICENSE).

Esta documentacion describe el estado real del repositorio despues de incorporar
seguridad multitenant, mensajeria transaccional, administracion de tenants,
autenticacion exclusivamente por invitaciones, un tenant interno reservado para el
control plane, CorrelationId de extremo a extremo, cache con invalidacion, una
plataforma local de observabilidad con Grafana, Loki, Prometheus, Tempo y
OpenTelemetry, el microservicio de Subscription con facturacion por asientos
al estilo Google Workspace sin prorrateo, y el microservicio de Payment con
integracion Stripe para cobros de la plataforma SaaS y adaptadores pluggables
para pagos de los propios tenants.

## Indice

1. Introduccion y objetivo
2. Arquitectura general
3. Estructura del repositorio
4. Modulos y responsabilidades
5. Tecnologias, versiones y licencias
6. Patrones aplicados
7. Flujo general de una request
8. Configuracion y secretos
9. Middleware
10. CorrelationId
11. Errores y excepciones
12. Logging y observabilidad
13. Validaciones
14. Seguridad y autenticacion
15. Comunicacion entre servicios
16. Persistencia y migraciones
17. Inyeccion de dependencias
18. Buenas practicas
19. Endpoints y ejemplos
20. Ejecucion local y Docker
21. Depuracion
22. Pruebas automatizadas
23. Guia para nuevos microservicios y mejoras futuras
24. Guia de implementacion Customer y Subscription
25. Payment Service — Stripe SaaS y adaptadores tenant

## 1. Introduccion y objetivo

TaxVision es una base para una plataforma SaaS multitenant. El backend separa:

- el registro y ciclo de vida de tenants;
- identidad, credenciales y tokens;
- planes, modulos y suscripciones por asiento;
- enrutamiento y contexto confiable en el Gateway;
- contratos compartidos;
- transporte de eventos;
- cache;
- logs, metricas y trazas.

Los objetivos principales son:

- aislar cada usuario por `TenantId`;
- impedir usuarios asociados a tenants inexistentes o inactivos;
- permitir el mismo email en tenants diferentes;
- mantener bases independientes por microservicio;
- evitar llamadas directas entre bases de datos;
- propagar cambios por eventos durables;
- conservar atomicidad entre datos y outbox;
- tener trazabilidad HTTP y asincrona;
- aplicar facturacion por asientos sin prorrateo: los cambios de plan aplican en la siguiente renovacion;
- ofrecer una plantilla repetible para aproximadamente 25 microservicios futuros.

## 2. Arquitectura general

```text
Cliente / Postman
       |
       | HTTP :5047
       v
+----------------------+
| TaxVision.Gateway    |
| YARP + JWT + limits  |
+----------+-----------+
           |
     +-----+------------------+------------------+
     |                        |                  |
     v                        v                  v
+-------------------+  +-------------------+  +----------------------+
| Tenant API        |  | Auth API          |  | Subscription API     |
| puerto interno    |  | puerto interno    |  | puerto interno       |
+---------+---------+  +---------+---------+  +----------+-----------+
          |                      |                        |
          v                      v                        v
 TaxVision_Tenants        TaxVision_Auth          TaxVision_Subscription
          |
          | eventos transaccionales
          v
  RabbitMQ exchange: taxvision-events
          |
     +----+-----------------------------+
     |                                  |
     v                                  v
  queue: auth-tenant-events    queue: subscription-service-events    queue: payment-service-events
          |                                  |                                  |
          v                                  v                                  v
  Auth tenant registry            Subscription consumers              Payment consumers (Stripe)

Todos los procesos -> OTLP Collector
  logs    -> Loki
  metricas-> Prometheus
  trazas  -> Tempo
  consulta-> Grafana
```

### Limites de datos

| Servicio | Propietario de | Base |
| --- | --- | --- |
| Tenant | tenant canonico y estado | `TaxVision_Tenants` |
| Auth | usuarios, invitaciones, refresh tokens y proyeccion de tenants | `TaxVision_Auth` |
| Subscription | planes, modulos, suscripciones, asientos y enrollments | `TaxVision_Subscription` |
| Payment | cobros SaaS (Stripe), clientes Stripe, configuracion de pago de tenants y transacciones | `TaxVision_Payment` |
| Gateway | borde HTTP y propagacion de contexto | ninguna |

Auth nunca consulta directamente `TaxVision_Tenants`. Subscription nunca consulta
`TaxVision_Tenants` ni `TaxVision_Auth`. Cada servicio consume eventos y mantiene
proyecciones locales. Esto crea consistencia eventual y evita acoplamiento de bases.

## 3. Estructura del repositorio

```text
TaxVision/
|-- TaxVision.slnx
|-- global.json
|-- LICENSE
|-- .env.example
|-- README.md
|-- Postman_Collection/
|-- deploy/
|   |-- docker-compose.yml
|   |-- docker/
|   |   `-- docker-compose.yml
|   `-- observability/
|       |-- loki.yml
|       |-- tempo.yml
|       |-- prometheus.yml
|       |-- otel-collector.yml
|       `-- grafana/provisioning/
`-- src/
    |-- BuildingBlocks/
    |   |-- BuildingBlocks.csproj
    |   |-- BuildingBlocks.Infrastructure/
    |   `-- BuildingBlocks.Web/
    |-- Gateway/TaxVision.Gateway/
    `-- Services/
        |-- Auth/
        |   |-- Api/
        |   |-- Application/
        |   |-- Domain/
        |   `-- Infrastructure/
        |-- Tenant/
        |   |-- TaxVision.Tenant.Api/
        |   |-- TaxVision.Tenant.Application/
        |   |-- TaxVision.Tenant.Domain/
        |   `-- TaxVision.Tenant.Infrastructure/
        |-- Subscription/
        |   |-- TaxVision.Subscription.Api/
        |   |-- TaxVision.Subscription.Application/
        |   |-- TaxVision.Subscription.Domain/
        |   `-- TaxVision.Subscription.Infrastructure/
        `-- Payment/
            |-- TaxVision.Payment.Api/
            |-- TaxVision.Payment.Application/
            |-- TaxVision.Payment.Domain/
            `-- TaxVision.Payment.Infrastructure/
```

### BuildingBlocks separados

`BuildingBlocks` contiene contratos sin dependencias web:

- entidades base y tenancy;
- `Result`, `Error` y `ConflictException`;
- contratos de persistencia;
- contratos de cache;
- eventos de integracion;
- `ICorrelationContext`.

`BuildingBlocks.Infrastructure` contiene implementaciones tecnicas:

- Redis mediante `IDistributedCache`;
- registro `AddRedisCache`;
- serializacion y TTL.

`BuildingBlocks.Web` contiene preocupaciones del host:

- middleware;
- autenticacion JWT comun;
- rate limiting;
- Serilog;
- OpenTelemetry;
- health checks;
- mapeo de errores HTTP.

Esta separacion permite que los dominios no dependan de ASP.NET, Redis, Serilog u
OpenTelemetry.

## 4. Modulos y responsabilidades

### Gateway

- expone `http://localhost:5047`;
- enruta `/auth/*`, `/tenants/*`, `/enrollments/*`, `/subscriptions/*`, `/api/plans/*`, `/api/modules/*`, `/api/subscription-modules/*`, `/payments/*` y `/webhooks/*`;
- valida JWT en rutas protegidas; `enrollments`, lectura de planes y modulos, y webhooks son anonimos;
- elimina `X-Tenant-Id` del cliente;
- reconstruye `X-Tenant-Id` desde el claim firmado `tenant_id`;
- genera o valida `X-Correlation-Id`;
- limita endpoints sensibles;
- expone health checks agregados.

### Tenant Service

- crea tenants;
- valida `AdminEmail`;
- exige una zona horaria predeterminada mediante un identificador IANA;
- genera invitacion segura para el administrador;
- lista tenants con paginacion y cache;
- cambia estado Active/Suspended/Closed;
- publica eventos de creacion y estado;
- usa outbox transaccional EF Core + Wolverine.

### Auth Service

- proyecta tenants desde RabbitMQ;
- crea y acepta invitaciones con tokens hasheados;
- elimina el registro publico de usuarios;
- soporta `TenantEmployee`, `CustomerPortal`, `TenantAdmin` y `PlatformAdmin`;
- activa el primer `TenantAdmin` mediante la invitacion del onboarding;
- provisiona el primer `PlatformAdmin` mediante bootstrap secreto y temporal;
- permite email repetido en tenants distintos;
- login con tenant, email y password;
- JWT con rol, `tenant_id` y zona horaria efectiva en `zoneinfo`;
- refresh token rotatorio y revocacion;
- bloquea login/refresh si el tenant esta inactivo;
- consume eventos con inbox durable.

### Subscription Service

- gestiona planes comerciales con niveles de servicio (`Standard`, `Pro`);
- gestiona modulos funcionales y su asignacion a planes;
- crea y administra suscripciones por tenant con modelo de asientos;
- procesa el enrollment previo al tenant: el cliente elige plan y paga antes de que exista el tenant;
- factura sin prorrateo: los cambios de plan aplican en la siguiente renovacion;
- controla ciclo de vida de asientos: compra, renovacion, pago completado y pago fallido;
- registra cambios de plan pendientes que se aplican tras confirmacion de pago;
- consume `TenantCreatedIntegrationEvent` para sincronizar el contexto del tenant;
- publica siete eventos de integracion hacia `taxvision-events`;
- usa outbox transaccional EF Core + Wolverine;
- consume eventos de Payment mediante inbox durable.

### Payment Service

Dos contextos de pago independientes:

**SaaS / Control Plane** (TaxVision cobra a los tenants):
- recibe eventos de pago de Subscription via `payment-service-events`;
- obtiene o crea un `StripeCustomer` por `TenantId`;
- crea y confirma `PaymentIntent` en Stripe;
- publica eventos `*PaymentCompleted` o `*PaymentFailed` hacia `taxvision-events`;
- recibe webhooks de Stripe en `/webhooks/stripe` y actualiza el estado del pago;
- registra cada cobro en `SaaSPayments`;
- usa outbox transaccional EF Core + Wolverine;
- consume eventos mediante inbox durable.

**Tenant / Aplicacion del cliente** (el tenant cobra a sus propios clientes):
- almacena la configuracion de proveedor del tenant en `TenantPaymentConfigs`;
- claves del proveedor almacenadas cifradas; nunca se exponen en respuestas HTTP;
- adaptor `IPaymentAdapter` seleccionado por `IPaymentAdapterFactory` segun proveedor;
- soporta Stripe (implementado), PayPal (stub extensible), Square, MercadoPago, Manual;
- registra cada transaccion en `TenantTransactions`;
- no usa Redis; no publica eventos internos.

## 5. Tecnologias, versiones y licencias

Las versiones se fijan en `.csproj`, `global.json` y Dockerfiles.

| Tecnologia | Version | Uso | Licencia |
| --- | --- | --- | --- |
| .NET SDK | 10.0.300 | build | MIT |
| ASP.NET runtime | 10.0.9 | APIs Docker | MIT |
| ASP.NET Core | 10.0.9 | HTTP, JWT, health | MIT |
| EF Core SQL Server | 10.0.9 | ORM y migraciones | MIT |
| SQL Server | externa | persistencia | comercial Microsoft |
| WolverineFx | 6.14.0 | CQRS, RabbitMQ, outbox/inbox | MIT |
| RabbitMQ | 4.3.2-management | broker AMQP | MPL-2.0 |
| Redis | 7.2.12-alpine | cache | BSD-3-Clause |
| YARP | 2.3.0 | reverse proxy | MIT |
| Serilog | 10.0.0 | logging estructurado | Apache-2.0 |
| Serilog OTLP sink | 4.2.0 | logs remotos | Apache-2.0 |
| OpenTelemetry | 1.16.0 / instrumentaciones 1.15.x | metricas y trazas | Apache-2.0 |
| OTel Collector contrib | 0.153.0 | pipeline OTLP | Apache-2.0 |
| Grafana | 13.0.3 | exploracion | AGPL-3.0 |
| Loki | 3.7.2 | logs | AGPL-3.0 |
| Tempo | 2.8.2 | trazas | AGPL-3.0 |
| Prometheus | 3.5.3 LTS | metricas | Apache-2.0 |
| Swashbuckle | 10.2.2 | Swagger | MIT |
| Stripe.net | 47.x | procesador de pagos SaaS | Apache-2.0 |

Mapster y `Serilog.Sinks.MSSqlServer` fueron eliminados porque no tenian uso real.

Fuentes:

- [.NET support](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [RabbitMQ](https://www.rabbitmq.com/)
- [Redis licenses](https://redis.io/legal/licenses/)
- [Wolverine EF Core outbox](https://wolverinefx.net/guide/durability/efcore/outbox-and-inbox)
- [Loki con OTLP](https://grafana.com/docs/loki/latest/send-data/otel/)

## 6. Patrones aplicados

### Clean Architecture

```text
API -> Application -> Domain
 |          ^
 v          |
Infrastructure
```

Domain no conoce HTTP ni EF Core. Application declara contratos. Infrastructure los
implementa. API compone el proceso.

### DDD pragmatico

Las entidades controlan su estado mediante setters privados y fabricas:

```csharp
var result = User.Register(
    tenantId,
    name,
    lastName,
    email,
    passwordHash,
    actorType,
    customerId);
```

```csharp
var sub = Subscription.Create(tenantId, plan, billingPeriod, startDate);
```

Los antiguos eventos de dominio que se acumulaban sin dispatcher fueron eliminados.
Los cambios entre servicios usan eventos de integracion explicitos.

### CQRS

Controllers envian comandos y queries mediante Wolverine:

```csharp
var result = await bus.InvokeAsync<Result<UserResponse>>(command, ct);
```

Es CQRS logico: lectura y escritura pueden tener handlers diferentes, aunque cada
servicio usa una sola base.

### Repository y Unit of Work

Application depende de interfaces declaradas en la capa Application:
`IUserRepository`, `ITenantRepository`, `ITenantRegistry`, `IPlanRepository`,
`IModuleRepository`, `ISubscriptionRepository`, `ISubscriptionModuleRepository`,
`IPendingChangeRepository`, `IEnrollmentRepository` e `IUnitOfWork`. Los `DbContext`
implementan Unit of Work.

### ReadService

Para consultas que requieren joins multiples que no encajan en un repositorio simple,
se usa el patron ReadService: la interfaz se declara en Application y la
implementacion vive en Infrastructure junto al DbContext.

```csharp
// Application/Abstractions
public interface IPlanReadService
{
    Task<List<PlanDto>> GetAllAsync(bool? isActive, CancellationToken ct = default);
    Task<PlanDto> GetByIdWithDetailsAsync(Guid planId, CancellationToken ct = default);
}

// Infrastructure/Persistence/ReadServices
public sealed class PlanReadService(SubscriptionDbContext db) : IPlanReadService { ... }
```

Esto aplica tambien a `IModuleReadService` y `ISubscriptionModuleReadService`.

### Result pattern

Fallos esperados no usan excepciones:

```csharp
return Result.Failure<LoginResponse>(
    new Error("Auth.Invalid", "Invalid credentials."));
```

Excepciones se reservan para fallos inesperados o conflictos detectados por SQL.

### EDA, outbox e inbox

Tenant publica:

- `TenantCreatedIntegrationEvent`;
- `TenantStatusChangedIntegrationEvent`.

Auth consume ambos desde `auth-tenant-events`.

Subscription publica desde `taxvision-events`:

- `EnrollmentPaymentRequestedIntegrationEvent`;
- `TenantProvisioningRequestedIntegrationEvent`;
- `SeatPurchaseRequestedIntegrationEvent`;
- `SeatRenewalPaymentRequestedIntegrationEvent`;
- `SubscriptionRenewalPaymentRequestedIntegrationEvent`;
- `SubscriptionRenewalDueIntegrationEvent`;
- `TenantEntitlementsChangedIntegrationEvent`.

Subscription consume desde `subscription-service-events`:

- `TenantCreatedIntegrationEvent` (sincroniza contexto de tenant);
- `EnrollmentPaymentCompletedIntegrationEvent`;
- `SeatPaymentCompletedIntegrationEvent`;
- `SeatPaymentFailedIntegrationEvent`;
- `SeatRenewalDueIntegrationEvent`;
- `SeatRenewalPaymentCompletedIntegrationEvent`;
- `SeatRenewalPaymentFailedIntegrationEvent`;
- `SubscriptionRenewalPaymentCompletedIntegrationEvent`;
- `SubscriptionRenewalPaymentFailedIntegrationEvent`.

Cada API configura:

```csharp
options.PersistMessagesWithSqlServer(sqlConn);
options.Policies.UseDurableOutboxOnAllSendingEndpoints();
options.UseEntityFrameworkCoreTransactions()
    .WithDbContextAbstraction<IUnitOfWork, ServiceDbContext>();
options.Policies.AutoApplyTransactions();
```

La transaccion EF conserva conjuntamente cambios de negocio y envelopes salientes.

### Facturacion sin prorrateo

TaxVision aplica el modelo Google Workspace: ningun cambio genera cargos parciales.
Los cambios de plan o periodo de facturacion se registran como `PendingSubscriptionChange`
y se aplican al inicio del siguiente ciclo de renovacion. La actualizacion de precio
tampoco genera retroactivos.

## 7. Flujo general de una request

### Crear tenant y activar administrador

```text
POST /tenants
 -> valida Name, SubDomain, AdminEmail y DefaultTimeZoneId
 -> genera activation token
 -> guarda tenant
 -> guarda evento en outbox, dentro de la transaccion
 -> devuelve token plano una sola vez
 -> RabbitMQ entrega evento a Auth
 -> Auth crea Invitation(TenantAdmin) con el hash y la expiracion
 -> POST /auth/invitations/accept
 -> Auth compara hash en tiempo constante
 -> crea TenantAdmin
 -> repeticion devuelve el mismo usuario
```

La password nunca viaja por RabbitMQ. El token plano no se almacena.

### Invitar y registrar un actor

```text
POST /auth/invitations (TenantAdmin o PlatformAdmin)
 -> valida la matriz fija de invitaciones
 -> genera token aleatorio y guarda solo SHA-256
 -> devuelve el token plano una sola vez
POST /auth/invitations/accept
 -> valida token, estado, expiracion, tenant y email
 -> fija password PBKDF2
 -> crea el User con un unico ActorType
 -> marca la invitacion Accepted en la misma transaccion
 -> publica UserRegisteredIntegrationEvent
```

No existe un endpoint de registro publico.

### Login

```text
POST /auth/login
 -> confirma tenant activo
 -> busca por TenantId + Email
 -> verifica PBKDF2
 -> genera JWT con zoneinfo usando la zona predeterminada del tenant
 -> genera y almacena hash del refresh token
```

### Suspender tenant

```text
PATCH /tenants/{id}/status
 -> exige PlatformAdmin
 -> cambia estado
 -> publica TenantStatusChangedIntegrationEvent
 -> Auth actualiza IsActive
 -> login y refresh quedan bloqueados
```

### Enrollment (onboarding previo al tenant)

```text
POST /enrollments
 -> anonimo, rate limited
 -> busca el plan por nombre (planCode = nombre del plan)
 -> calcula precio segun BillingPeriod
 -> crea SubscriptionEnrollment en estado PendingPayment
 -> publica EnrollmentPaymentRequestedIntegrationEvent
 -> Payment Service procesa el pago
 -> EnrollmentPaymentCompletedIntegrationEvent llega por inbox
 -> Subscription publica TenantProvisioningRequestedIntegrationEvent
 -> Tenant Service crea el tenant y Auth crea la invitacion del admin
```

### Crear suscripcion y cambiar plan

```text
POST /subscriptions
 -> PlatformAdmin crea la suscripcion para un tenant
 -> busca el plan por ServiceLevel
 -> calcula precio base segun BillingPeriod
 -> crea Subscription en estado Active

POST /subscriptions/{id}/change-plan
 -> TenantAdmin solicita cambio de plan o periodo
 -> crea PendingSubscriptionChange (sin prorrateo)
 -> el cambio aplica en la proxima renovacion

POST /subscriptions/pending-changes/{id}/apply
 -> PlatformAdmin aplica el cambio despues de confirmacion de pago
 -> actualiza plan, modulos incluidos y precio
 -> publica TenantEntitlementsChangedIntegrationEvent
```

## 8. Configuracion y secretos

Copie `.env.example` a `.env` y cambie todos los placeholders:

```powershell
Copy-Item .env.example .env
```

Estructura:

```env
JWT_SECRET=replace-with-a-random-secret-of-at-least-32-bytes
AUTH_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Auth;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
TENANT_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Tenants;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
SUBSCRIPTION_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Subscription;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
PAYMENT_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Payment;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
STRIPE_SECRET_KEY=sk_test_replace-with-your-stripe-secret-key
STRIPE_WEBHOOK_SECRET=whsec_replace-with-your-webhook-secret
RABBITMQ_USER=taxvision
RABBITMQ_PASSWORD=replace-with-a-strong-rabbitmq-password
RABBITMQ_CONNECTION=amqp://taxvision:replace-with-url-encoded-password@rabbitmq:5672
GRAFANA_ADMIN_USER=admin
GRAFANA_ADMIN_PASSWORD=replace-with-a-strong-grafana-password
SA_PASSWORD=replace-with-sa-password
```

Si la password RabbitMQ contiene caracteres reservados, codifiquela para URI.

### User Secrets

Auth:

```powershell
dotnet user-secrets set "ConnectionStrings:Default" "<AUTH_CONNECTION>" `
  --project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" `
  --project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
dotnet user-secrets set "RabbitMq:Uri" "amqp://user:password@localhost:5672" `
  --project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
dotnet user-secrets set "Jwt:Secret" "<SAME_SECRET>" `
  --project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
```

Tenant requiere los mismos tipos de claves y el mismo JWT secret. Subscription
requiere `ConnectionStrings:Default` (sin Redis), `RabbitMq:Uri` y `Jwt:Secret`.
Payment requiere `ConnectionStrings:Default`, `RabbitMq:Uri`, `Jwt:Secret`,
`Stripe:SecretKey` y `Stripe:WebhookSecret`.
Gateway requiere `Jwt:Secret`.

### Bootstrap del primer PlatformAdmin

El tenant interno se crea mediante migraciones con el identificador fijo:

```text
8f58a521-4c25-4d91-9f4e-7ad5df14c001
```

No representa una suscripcion comercial. En Docker, configure las variables de
entorno en `docker-compose.yml` bajo `auth-api` temporalmente:

```yaml
PlatformBootstrap__Enabled: "true"
PlatformBootstrap__Email: "admin@taxvision.com"
PlatformBootstrap__InvitationToken: "<RANDOM-SECRET-32+>"
```

O en desarrollo local mediante secretos:

```powershell
dotnet user-secrets set "PlatformBootstrap:Enabled" "true" `
  --project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
dotnet user-secrets set "PlatformBootstrap:Email" "admin@taxvision.com" `
  --project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
dotnet user-secrets set "PlatformBootstrap:InvitationToken" "<RANDOM-SECRET-32+>" `
  --project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
```

El servicio guarda solamente SHA-256 del token configurado y nunca lo escribe en
logs. Despues de aceptar la invitacion, deshabilite y elimine estos secretos. Los
`PlatformAdmin` posteriores se crean mediante invitaciones emitidas por otro
`PlatformAdmin`.

## 9. Middleware

### Gateway

```csharp
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantPropagationMiddleware>();
```

### Auth

```csharp
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
```

### Tenant

Agrega `TenantResolutionMiddleware` para reconstruir `TenantContext`.

### Subscription

```csharp
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
```

El orden importa: correlation debe envolver logging y excepciones; autenticacion debe
ejecutarse antes de leer claims.

## 10. CorrelationId

### Que es

`CorrelationId` permite localizar una operacion en Gateway, servicios, eventos y
consumidores. TaxVision usa:

```http
X-Correlation-Id: 7c89fd90f55045b7b61735586536cc29
```

### Generacion y validacion

El cliente puede enviarlo. Si falta, supera 128 caracteres o contiene caracteres
fuera de `A-Z`, `a-z`, `0-9`, `.`, `_` y `-`, el middleware genera:

```csharp
Guid.NewGuid().ToString("N")
```

### Recorrido

1. Gateway lee o crea el ID.
2. Lo guarda en header, `CorrelationContext`, Serilog y Activity.
3. YARP reenvia el header.
4. Auth/Tenant/Subscription reutilizan el mismo ID.
5. La respuesta devuelve `X-Correlation-Id`.
6. Los handlers lo asignan a `IntegrationEvent.CorrelationId`.
7. El consumidor restaura un scope de correlation.

```csharp
using (correlation.Push(evt.CorrelationId))
{
    await unitOfWork.SaveChangesAsync(ct);
}
```

OpenTelemetry agrega `taxvision.correlation_id` como tag y baggage de la Activity.

### Probar

```powershell
curl.exe -i `
  -H "X-Correlation-Id: taxvision-check-001" `
  http://localhost:5047/health/live
```

En Grafana:

```logql
{service_name="gateway"} | CorrelationId = "taxvision-check-001"
```

## 11. Errores y excepciones

`ErrorHttpMapping` asigna:

| Codigo | HTTP |
| --- | --- |
| `Auth.Invalid` | 401 |
| `Auth.InvalidInvitation` | 401 |
| `Auth.InvalidRefreshToken` | 401 |
| `Tenant.NotFound` | 404 |
| `Invitation.NotFound` | 404 |
| `Plan.NotFound` | 404 |
| `Module.NotFound` | 404 |
| `Subscription.NotFound` | 404 |
| `Enrollment.NotFound` | 404 |
| `Tenant.Inactive` | 403 |
| `Invitation.Forbidden` | 403 |
| `User.EmailConflict` | 409 |
| `Invitation.PendingConflict` | 409 |
| `Tenant.SubdomainConflict` | 409 |
| `Subscription.AlreadyExists` | 409 |
| `Plan.NameConflict` | 409 |
| `Module.NameConflict` | 409 |

`AuthDbContext`, `TenantDbContext` y `SubscriptionDbContext` convierten errores SQL
2601/2627 en `ConflictException`, cubriendo condiciones de carrera.

`ExceptionHandlingMiddleware` devuelve `ProblemDetails`, codigo y correlation sin
exponer stack trace.

Wolverine reintenta fallos asincronos a 1, 5 y 15 segundos.

## 12. Logging y observabilidad

### Pipeline

```text
Serilog OTLP -----------+
OpenTelemetry metrics --+--> OTel Collector
OpenTelemetry traces ---+
                              |--> Loki
                              |--> Prometheus
                              `--> Tempo

Grafana consulta los tres backends.
```

### Servicios

| Componente | URL | Uso |
| --- | --- | --- |
| Grafana | `http://localhost:3000` | interfaz principal |
| Prometheus | `http://localhost:9090` | consultas PromQL |
| Loki | interno `loki:3100` | logs |
| Tempo | interno `tempo:3200` | trazas |
| OTel Collector | `4317`, `4318` | recepcion OTLP |

### Grafana

Los datasources se aprovisionan automaticamente:

- Prometheus, datasource predeterminado;
- Loki;
- Tempo, enlazado con Loki y Prometheus.

Uso:

1. Abra `http://localhost:3000`.
2. Ingrese credenciales `GRAFANA_ADMIN_*`.
3. Abra **Explore**.
4. Seleccione Loki para logs.
5. Seleccione Prometheus para metricas.
6. Seleccione Tempo para buscar trazas.

Consultas utiles:

```logql
{service_name="auth-service"}
{service_name="tenant-service"} |= "CorrelationId"
{service_name="subscription-service"}
{service_name="subscription-service"} |= "Enrollment"
```

```promql
up{job="taxvision-otel-collector"}
dotnet_process_cpu_count{exported_job="gateway"}
```

Ademas del sink remoto, cada API conserva archivos JSON en el volumen
`taxvision-logs`.

## 13. Validaciones

Tenant:

- Name obligatorio;
- SubDomain de 3 a 40, minusculas, numeros y guion;
- subdominio unico;
- AdminEmail valido;
- `DefaultTimeZoneId` obligatorio, resoluble y expresado como identificador IANA;
- estados validos;
- un tenant Closed no se reactiva.

Auth:

- tenant existente y activo;
- TenantId obligatorio;
- nombre y apellido;
- email normalizado;
- password minima de 12;
- `(TenantId, Email)` unico;
- registro solo mediante invitacion;
- token de invitacion aleatorio; solo SHA-256 persiste;
- expiracion predeterminada de siete dias;
- `CustomerId` obligatorio solo para `CustomerPortal`;
- `PlatformAdmin` permitido solo en el tenant interno;
- refresh token activo.

Subscription:

- `PlanCode` debe corresponder al nombre de un plan activo;
- `BillingPeriod` acepta `Monthly` o `Annual`;
- `ServiceLevel` acepta `Standard` o `Pro`;
- un tenant solo puede tener una suscripcion activa;
- `TenantId` obligatorio para crear suscripcion;
- cambio de plan requiere suscripcion existente;
- precio no puede ser negativo;
- `Quantity` de asientos debe ser mayor que cero;
- nombre de plan unico; nombre de modulo unico;
- `(SubscriptionId, ModuleId)` unico en SubscriptionModules.

Paginacion:

- `page >= 1`;
- `1 <= size <= 100`.

## 14. Seguridad y autenticacion

### Password

PBKDF2 usa:

- salt aleatorio de 16 bytes;
- HMAC-SHA256;
- 100,000 iteraciones;
- salida de 32 bytes;
- comparacion en tiempo constante.

### JWT

Incluye:

- `sub`;
- `email`;
- `tenant_id`;
- `actor_type`;
- `customer_id`, solamente para `CustomerPortal`;
- `zoneinfo`;
- un rol fijo derivado del actor.

Se firma con HMAC-SHA256. Auth, Tenant, Subscription y Gateway comparten secret,
issuer y audience.

`zoneinfo` contiene actualmente `Tenant.DefaultTimeZoneId`. Se vuelve a calcular en
cada login y renovacion del access token usando la proyeccion local de Auth. Cuando
se incorpore `UserProfile.TimeZoneId`, la resolucion sera:

```text
UserProfile.TimeZoneId ?? Tenant.DefaultTimeZoneId ?? Etc/UTC
```

La preferencia personal no se ha agregado todavia. Las fechas de negocio deben
persistirse en UTC; `zoneinfo` se usa para presentacion y reglas que necesiten la
hora local. No se almacenan offsets fijos como `UTC-4`, porque no representan reglas
historicas o de horario de verano.

### Actores e invitaciones

Auth usa cuatro actores cerrados:

| Actor | Alcance | Identificador adicional |
| --- | --- | --- |
| `TenantEmployee` | un tenant comercial | ninguno |
| `CustomerPortal` | un tenant y un customer | `CustomerId` |
| `TenantAdmin` | administracion de un tenant | ninguno |
| `PlatformAdmin` | control plane del SaaS | tenant interno reservado |

No se aceptan nombres de roles arbitrarios desde el cliente. `ActorType` determina el
unico rol persistido y emitido en JWT.

Matriz autorizada:

| Invitador | Puede invitar |
| --- | --- |
| `PlatformAdmin` | `PlatformAdmin` dentro del tenant interno; `TenantAdmin` dentro de un tenant comercial |
| `TenantAdmin` | `TenantAdmin`, `TenantEmployee` y `CustomerPortal` dentro de su propio tenant |
| `TenantEmployee` | nadie |
| `CustomerPortal` | nadie |

Una invitacion conserva `TenantId`, email normalizado, actor, `CustomerId` opcional,
hash del token, creador, estado, expiracion y auditoria de aceptacion/cancelacion.
El token plano solo aparece en la respuesta de creacion. La aceptacion es idempotente:
si ya fue aceptada devuelve el usuario vinculado.

### Tenant interno reservado

`TaxVision Platform` tiene `Kind = Platform`, subdominio
`platform-internal`, zona `Etc/UTC` y GUID fijo. Se siembra de forma explicita en las
bases Tenant y Auth porque es una raiz de confianza del control plane, no un tenant
adquirido por suscripcion.

- no aparece en el listado comercial;
- no puede suspenderse mediante el endpoint de estado;
- no debe recibir Customer, Subscription o Billing;
- solo admite usuarios `PlatformAdmin`;
- un `PlatformAdmin` administra otros tenants mediante endpoints explicitos, sin
  cambiar su `tenant_id` ni suplantar silenciosamente a otro tenant.

### Refresh token

- 64 bytes aleatorios;
- solo SHA-256 se almacena;
- refresh rota el token;
- revoke es idempotente;
- token anterior no se reutiliza;
- tenant inactivo no renueva.

### Autorizacion por endpoint

| Endpoint | Acceso |
| --- | --- |
| `POST /tenants` | onboarding publico, rate limited |
| `GET /tenants` | rol `PlatformAdmin` |
| `PATCH /tenants/{id}/status` | rol `PlatformAdmin` |
| `POST /auth/invitations` | `TenantAdmin` o `PlatformAdmin`, sujeto a la matriz |
| `POST /auth/invitations/accept` | anonimo con token valido |
| `POST /auth/invitations/{id}/cancel` | `TenantAdmin` o `PlatformAdmin` |
| `POST /enrollments` | anonimo, rate limited |
| `GET /api/plans` | anonimo |
| `GET /api/plans/{id}` | anonimo |
| `POST /api/plans` | rol `PlatformAdmin` |
| `PUT /api/plans/{id}` | rol `PlatformAdmin` |
| `DELETE /api/plans/{id}` | rol `PlatformAdmin` |
| `PATCH /api/plans/{id}/status` | rol `PlatformAdmin` |
| `POST /api/plans/{id}/modules/{moduleId}` | rol `PlatformAdmin` |
| `DELETE /api/plans/{id}/modules/{moduleId}` | rol `PlatformAdmin` |
| `GET /api/modules` | anonimo |
| `GET /api/modules/{id}` | autenticado |
| `POST /api/modules` | rol `PlatformAdmin` |
| `PUT /api/modules/{id}` | rol `PlatformAdmin` |
| `DELETE /api/modules/{id}` | rol `PlatformAdmin` |
| `PATCH /api/modules/{id}/status` | rol `PlatformAdmin` |
| `POST /subscriptions` | rol `PlatformAdmin` |
| `POST /subscriptions/current/seats` | rol `TenantAdmin` |
| `POST /subscriptions/current/cancel` | rol `TenantAdmin` |
| `POST /subscriptions/current/renew` | rol `PlatformAdmin` o `TenantAdmin` |
| `POST /subscriptions/{id}/change-plan` | rol `TenantAdmin` |
| `POST /subscriptions/pending-changes/{id}/apply` | rol `PlatformAdmin` |
| `PATCH /subscriptions/{id}/price` | rol `PlatformAdmin` |
| `GET /api/subscription-modules/subscription/{id}` | autenticado |
| `POST /api/subscription-modules` | rol `PlatformAdmin` |
| `DELETE /api/subscription-modules/{id}` | rol `PlatformAdmin` |

La creacion del primer `PlatformAdmin` se resuelve mediante el bootstrap secreto. No
existe un endpoint publico que otorgue ese rol.

### Rate limiting

Gateway limita por IP y path:

- login;
- refresh;
- crear invitacion;
- aceptar invitacion;
- crear tenant;
- crear enrollment.

El limite actual es 10 requests por minuto, sin cola.

## 15. Comunicacion entre servicios

### YARP

```text
/auth/{**catch-all}                    -> auth-api:8080
/tenants/{**catch-all}                 -> tenant-api:8080
/enrollments/{**catch-all}             -> subscription-api:8080  (anonimo)
/subscriptions/{**catch-all}           -> subscription-api:8080
/api/plans/{**catch-all}               -> subscription-api:8080  (GET anonimo)
/api/modules/{**catch-all}             -> subscription-api:8080  (GET anonimo)
/api/subscription-modules/{**catch-all}-> subscription-api:8080
/payments/{**catch-all}               -> payment-api:8080
/webhooks/{**catch-all}               -> payment-api:8080  (anonimo, verificacion Stripe-Signature)

Solo Gateway publica API al host. Los servicios se exponen internamente en el
puerto 8080 dentro de `taxvision-network`.

## 25. Payment Service — Stripe SaaS y adaptadores tenant

El microservicio de Payment encapsula **dos contextos de cobro completamente independientes**
dentro de un solo servicio. En el futuro estos contextos seran separados en dos microservicios
distintos (`TaxVision.PaymentApp` y `TaxVision.PaymentClient`). El plan de separacion
arquitectonica se documentara antes de implementar.

### Contexto 1: SaaS / Control Plane

TaxVision cobra a los tenants por sus suscripciones usando Stripe.

```text
Subscription publica evento de pago
    |
    | taxvision-events → payment-service-events
    v
Payment Service consume evento (inbox durable Wolverine)
    |
    | GetOrCreateStripeCustomer (busca por TenantId en Stripe API)
    | CreatePaymentIntent (Stripe API)
    | ConfirmPaymentIntent (Stripe API)  ← dev: pm_card_visa; prod: token del frontend
    v
SaaSPayment.Status = Processing
    |
    | Stripe webhook → POST /webhooks/stripe
    | Verifica Stripe-Signature
    v
ProcessSaaSPaymentCommand → ProcessSaaSPaymentHandler
    |
    | GetByStripePaymentIntentIdAsync → SaaSPayment
    | MarkCompleted() o MarkFailed()
    v
Publica EnrollmentPaymentCompletedIntegrationEvent (u otro segun tipo)
    → Subscription consume y completa el ciclo
```

Tablas: `SaaSPayments`, `StripeCustomers`, `wolverine_*`.

Variables de entorno requeridas:

```env
STRIPE_SECRET_KEY=sk_test_...      # clave secreta Stripe (nunca en appsettings.json)
STRIPE_WEBHOOK_SECRET=whsec_...    # secret para verificar firma del webhook
```

Eventos consumidos (`payment-service-events`):

| Evento | Handler |
| --- | --- |
| `EnrollmentPaymentRequestedIntegrationEvent` | `EnrollmentPaymentRequestedHandler` |
| `SeatPurchaseRequestedIntegrationEvent` | `SeatPurchaseRequestedHandler` |
| `SeatRenewalPaymentRequestedIntegrationEvent` | `SeatRenewalPaymentRequestedHandler` |
| `SubscriptionRenewalPaymentRequestedIntegrationEvent` | `SubscriptionRenewalPaymentRequestedHandler` |

Eventos publicados (`taxvision-events`):

- `EnrollmentPaymentCompletedIntegrationEvent` / `EnrollmentPaymentFailedIntegrationEvent`
- `SeatPaymentCompletedIntegrationEvent` / `SeatPaymentFailedIntegrationEvent`
- `SeatRenewalPaymentCompletedIntegrationEvent` / `SeatRenewalPaymentFailedIntegrationEvent`
- `SubscriptionRenewalPaymentCompletedIntegrationEvent` / `SubscriptionRenewalPaymentFailedIntegrationEvent`

### Contexto 2: Tenant / Aplicacion del cliente

Los tenants cobran a sus propios clientes usando su propio proveedor de pago.

```text
TenantAdmin configura su proveedor
    POST /payments/tenant/config
    → TenantPaymentConfig (SecretKeyEncrypted en AES — pendiente cifrado real)

TenantAdmin inicia un cobro
    POST /payments/tenant/charge
    → ProcessTenantPaymentHandler
    → IPaymentAdapterFactory.GetAdapter(provider)
    → IPaymentAdapter.ChargeAsync(...)
    → TenantTransaction.MarkCompleted() o MarkFailed()
```

Tablas: `TenantPaymentConfigs`, `TenantTransactions`.

Proveedores soportados:

| Proveedor | Estado |
| --- | --- |
| Stripe | Implementado (`StripePaymentAdapter`) |
| PayPal | Stub extensible (`PayPalPaymentAdapter`) |
| Square, MercadoPago, Manual | Enum declarado; adaptor pendiente |

### Endpoints

| Metodo | Ruta | Rol | Descripcion |
| --- | --- | --- | --- |
| `GET` | `/payments/saas/{id}` | `PlatformAdmin` | Consultar un cobro SaaS por ID |
| `GET` | `/payments/saas` | `PlatformAdmin` | Listar cobros SaaS del tenant en contexto |
| `GET` | `/payments/tenant/config` | `TenantAdmin` | Ver config del proveedor del tenant |
| `POST` | `/payments/tenant/config` | `TenantAdmin` | Crear o actualizar config del proveedor |
| `POST` | `/payments/tenant/charge` | `TenantAdmin` | Cobrar a un cliente del tenant |
| `GET` | `/payments/tenant/transactions` | `TenantAdmin` | Listar transacciones del tenant |
| `POST` | `/webhooks/stripe` | Anonimo | Receptor de webhooks Stripe (verifica firma) |

### Pendientes de produccion

- Implementar cifrado AES real para `SecretKeyEncrypted` y `WebhookSecretEncrypted`
  en `TenantPaymentConfig`; actualmente se almacenan como texto.
- Reemplazar `pm_card_visa` en `StripeGateway.ConfirmPaymentIntentAsync` con el
  `paymentMethodId` real proveniente del frontend (Stripe Elements / Stripe.js).
- Implementar PayPal REST Orders API en `PayPalPaymentAdapter`.
- Mover confirmacion del PaymentIntent a flujo asincrono via webhooks en produccion.

`AddTenantInfrastructure` registra repositorios y lecturas.

`AddSubscriptionInfrastructure` registra:

- `SubscriptionDbContext` como `IUnitOfWork`;
- `IPlanRepository`, `IModuleRepository`, `IEnrollmentRepository`, `ISubscriptionRepository`, `ISubscriptionModuleRepository`, `IPendingChangeRepository`;
- `IPlanReadService`, `IModuleReadService`, `ISubscriptionModuleReadService`.

`AddPaymentInfrastructure` registra:

- `PaymentDbContext` como `IUnitOfWork`;
- `ISaaSPaymentRepository`, `IStripeCustomerRepository`, `ITenantPaymentConfigRepository`, `ITenantTransactionRepository`;
- `IStripeGateway` (implementado con Stripe.net);
- `IPaymentAdapter` adaptadores (`StripePaymentAdapter`, `PayPalPaymentAdapter`);
- `IPaymentAdapterFactory`.

`AddTaxVisionJwtAuthentication`, `AddTaxVisionOpenTelemetry` y
`AddTaxVisionGatewayRateLimiting` son extensiones reutilizables para nuevos hosts.

## 18. Buenas practicas

| Practica | Implementacion |
| --- | --- |
| Database per service | bases Auth, Tenant, Subscription y Payment |
| Identidad multitenant | filtro e indice `(TenantId, Email)` |
| Registro cerrado | solo invitaciones con actor y alcance validados |
| Secrets fuera de Git | `.env`, User Secrets |
| Password hashing | PBKDF2 |
| Token storage seguro | hash SHA-256 |
| Tenant header confiable | derivado del JWT |
| Error mapping | status HTTP por codigo |
| Race protection | indices + `ConflictException` |
| Atomic outbox | middleware EF Core Wolverine |
| Durable inbox | queues Auth, Subscription y Payment |
| Idempotencia | upsert e invitacion reentrante |
| Control plane aislado | tenant interno no comercial |
| Correlation completo | HTTP, eventos, logs y traces |
| Cache responsable | solo lectura + invalidacion + fallback |
| Health checks | SQL, Redis, Rabbit y downstream |
| Imagenes reproducibles | tags exactos |
| Observabilidad central | OTLP, Loki, Prometheus y Tempo |
| Capas compartidas | base, infrastructure y web |
| Sin prorrateo | cambios de plan aplican en siguiente renovacion |
| ReadService | joins complejos en Infrastructure, interfaz en Application |
| Handlers en Application | ningun handler en Infrastructure; solo repositorios e implementaciones tecnicas |
| Stripe secrets en env | `Stripe:SecretKey` y `Stripe:WebhookSecret` nunca en appsettings.json |
| Claves de tenant cifradas | `SecretKeyEncrypted` en `TenantPaymentConfigs`; nunca expuestas en HTTP |
| Adaptadores pluggables | `IPaymentAdapter` / `IPaymentAdapterFactory` para pagos de tenants |
| Webhook verificado | signature Stripe validada antes de procesar eventos |

## 19. Endpoints y ejemplos

Base:

```text
http://localhost:5047
```

### Crear tenant

```http
POST /tenants
```

```json
{
  "name": "Empresa Demo",
  "subdomain": "empresa-demo",
  "adminEmail": "admin@empresa-demo.com",
  "defaultTimeZoneId": "America/Santo_Domingo"
}
```

### Enrollment (onboarding publico)

```http
POST /enrollments
```

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

### Login

```http
POST /auth/login
```

```json
{
  "tenantId": "tenant-guid",
  "email": "ana@example.com",
  "password": "Use-A-Strong-Password-123!"
}
```

### Cambiar estado de tenant

```http
PATCH /tenants/{tenantId}/status
Authorization: Bearer <platform-admin-jwt>
```

```json
{ "status": "Suspended" }
```

### Crear plan

```http
POST /api/plans
Authorization: Bearer <platform-admin-jwt>
```

```json
{
  "name": "Starter",
  "title": "Plan Starter",
  "description": "Plan basico",
  "basePriceMonthly": 29.00,
  "basePriceAnnual": 290.00,
  "pricePerAdditionalSeat": 9.00,
  "includedSeats": 3,
  "currency": "USD",
  "isActive": true,
  "serviceLevel": "Standard",
  "features": ["Acceso basico", "Soporte por email"]
}
```

### Crear suscripcion

```http
POST /subscriptions
Authorization: Bearer <platform-admin-jwt>
```

```json
{
  "tenantId": "tenant-guid",
  "serviceLevel": "Standard",
  "billingPeriod": "Monthly",
  "isActive": true
}
```

### Comprar asientos adicionales

```http
POST /subscriptions/current/seats
Authorization: Bearer <tenant-admin-jwt>
```

```json
{ "quantity": 2 }
```

### Consultar pago SaaS

```http
GET /payments/saas/{paymentId}
Authorization: Bearer <platform-admin-jwt>
```

Respuesta:

```json
{
  "id": "guid",
  "tenantId": "tenant-guid",
  "paymentType": "Enrollment",
  "status": "Completed",
  "amountCents": 2900,
  "currency": "USD",
  "stripePaymentIntentId": "pi_...",
  "referenceId": "enrollment-guid",
  "createdAtUtc": "2026-07-01T00:00:00Z",
  "failureReason": null
}
```

### Configurar proveedor de pago del tenant

```http
POST /payments/tenant/config
Authorization: Bearer <tenant-admin-jwt>
```

```json
{
  "provider": "Stripe",
  "publicKey": "pk_live_...",
  "secretKeyEncrypted": "<AES-encrypted-sk_live_...>",
  "webhookSecretEncrypted": "<AES-encrypted-whsec_...>"
}
```

### Procesar pago del tenant

```http
POST /payments/tenant/charge
Authorization: Bearer <tenant-admin-jwt>
```

```json
{
  "customerId": "customer-guid",
  "amountCents": 5000,
  "currency": "USD",
  "description": "Factura #INV-001"
}
```

### Webhook Stripe

```http
POST /webhooks/stripe
Stripe-Signature: t=...,v1=...
```

Recibe `payment_intent.succeeded` o `payment_intent.payment_failed` y actualiza el
estado del `SaaSPayment` correspondiente.

## 20. Ejecucion local y Docker

### Requisitos

- .NET SDK 10.0.300;
- Docker Engine/Desktop;
- SQL Server;
- `dotnet-ef` 10.0.9.

```powershell
dotnet tool update --global dotnet-ef --version 10.0.9
dotnet restore
dotnet build
```

### Stack completo

```powershell
docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  up -d --build
```

El archivo canonico del stack completo es
`deploy/docker/docker-compose.yml`. RabbitMQ crea el usuario de `RABBITMQ_USER`
unicamente al inicializar un volumen nuevo.

Estado:

```powershell
docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  ps
```

Actualizar un servicio:

```powershell
docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  up -d --build --force-recreate payment-api
```

Detener sin eliminar datos:

```powershell
docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  down
```

No use `down -v` salvo que quiera eliminar los volumenes.

## 21. Depuracion

### Health

```powershell
curl.exe -i http://localhost:5047/health/live
curl.exe -i http://localhost:5047/health/ready
```

### Logs Docker

```powershell
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f gateway
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f auth-api
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f tenant-api
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f subscription-api
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f payment-api
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f otel-collector
```

### RabbitMQ

Abra `http://localhost:15672` con `RABBITMQ_USER` y `RABBITMQ_PASSWORD`.

Revise:

- `taxvision-events`;
- `auth-tenant-events`;
- `subscription-service-events`;
- `payment-service-events`;
- consumidores;
- ready/unacked.

### Wolverine SQL

```sql
SELECT * FROM dbo.wolverine_outgoing_envelopes;
SELECT * FROM dbo.wolverine_incoming_envelopes;
SELECT * FROM dbo.wolverine_dead_letters;
```

Ejecutar en la base correspondiente (`TaxVision_Auth`, `TaxVision_Tenants`,
`TaxVision_Subscription` o `TaxVision_Payment`) segun el servicio que se depura.

### Grafana — queries utiles

```logql
{service_name="payment-service"}
{service_name="payment-service"} |= "Stripe"
{service_name="payment-service"} |= "Enrollment"
```

## 22. Pruebas automatizadas

Por solicitud, no se crearon proyectos ni pruebas automatizadas.

La guia completa esta en:

```text
D:\TaxVision Docs\Guia_Implementacion_Pruebas_Automatizadas_TaxVision.pdf
```

La guia cubre unitarias, integracion, arquitectura, E2E, outbox/inbox, multitenancy,
cache, correlation y observabilidad para los 25 microservicios planeados.

## 23. Guia para nuevos microservicios y mejoras futuras

### Plantilla obligatoria

Cada microservicio nuevo debe tener:

```text
Service.Domain
Service.Application
Service.Infrastructure
Service.Api
```

Use BuildingBlocks segun necesidad:

- base para contratos y resultados;
- Infrastructure para Redis;
- Web para middleware, JWT, health y observabilidad.

Checklist:

1. Base de datos propia.
2. Migracion inicial.
3. `IUnitOfWork`.
4. Filtro de TenantId.
5. Correlation HTTP y eventos.
6. Outbox si publica.
7. Inbox e idempotencia si consume.
8. Health live/ready.
9. JWT y autorizacion.
10. OTLP.
11. Dockerfile con version exacta.
12. Servicio en `taxvision-network`.
13. Ruta YARP en `appsettings.json` del Gateway.
14. Pruebas siguiendo la guia PDF.
15. Handlers en Application layer; ningun handler en Infrastructure.
16. ReadService para consultas con joins multiples.

### Pendientes reales

- reemplazar credenciales locales por un secret manager en produccion;
- mover el bootstrap de `PlatformAdmin` a un secret manager/Job de provisioning en produccion;
- habilitar TLS externo e interno segun el entorno;
- agregar CI/CD, SBOM y escaneo de secretos;
- implementar las pruebas de la guia;
- definir retencion y almacenamiento object storage para observabilidad productiva;
- crear el microservicio de Customer;
- crear `UserProfile` y aplicar la sobrescritura personal de `TimeZoneId`;
- versionar contratos de eventos antes de incorporar mas consumidores;
- agregar cache en Subscription para lectura de planes y modulos;
- agregar ruta YARP para health checks de subscription-api en el Gateway agregado;
- implementar cifrado real para `SecretKeyEncrypted` y `WebhookSecretEncrypted` en Payment;
- implementar PayPal SDK real en `PayPalPaymentAdapter`;
- mover confirmacion de PaymentIntent a webhooks asincronos en produccion (actualmente sincrono para desarrollo).

## 24. Guia de implementacion Customer y Subscription

La guia detallada se encuentra en:

```text
output/pdf/Guia_Implementacion_Customer_Subscription_TaxVision.pdf
```

Incluye modelo final del aggregate `Customer`, `CustomerRelation`,
`CustomerFiscalProfile`, separacion de bounded contexts, implementacion por capas,
tablas, indices, endpoints, eventos y pruebas.

Nota: la seccion de prorrateo de la guia original queda obsoleta. TaxVision aplica
el modelo Google Workspace: sin cargos parciales, los cambios de plan aplican al
inicio del siguiente ciclo de renovacion.ange fanout.

| Cola | Consumidor |
| --- | --- |
| `auth-tenant-events` | Auth Service |
| `subscription-service-events` | Subscription Service |
| `payment-service-events` | Payment Service |

El consumidor de creacion de tenant en Subscription usa upsert. Los tres servicios usan
inbox durable y correlation.

`TenantCreatedIntegrationEvent` incluye `DefaultTimeZoneId`. Auth lo conserva en su
tabla `Tenants`, por lo que login y refresh no consultan la base de Tenant ni realizan
una llamada HTTP entre microservicios.

El mismo evento lleva el hash y la expiracion de la invitacion inicial. Auth crea una
fila `Invitation` de tipo `TenantAdmin`; las columnas especiales de invitacion que
antes estaban dentro de su proyeccion `Tenant` fueron eliminadas.

### Redis

Solo el listado de tenants usa cache. La estrategia es:

1. obtener `tenants:list:v2:version`;
2. incluir version, page y size en la clave;
3. guardar pagina durante 5 minutos;
4. crear tenant cambia la version;
5. claves antiguas expiran;
6. si Redis falla, la lectura usa SQL Server.

No se cachean credenciales, tokens ni operaciones de escritura. Subscription no usa
Redis en su estado actual.

## 16. Persistencia y migraciones

### Auth

Tablas:

- `Tenants`;
- `Users`;
- `Invitations`;
- `RefreshTokens`;
- `wolverine_*`.

La migracion `AddAuthTenantDefaultTimeZone` agrega `DefaultTimeZoneId` a la
proyeccion local de tenants. Los registros anteriores reciben `Etc/UTC`.

La migracion `AddInvitationActorsAndPlatformTenant`:

- crea `Invitations`;
- agrega `ActorType` y `CustomerId` a `Users`;
- convierte roles existentes a los cuatro actores fijos;
- migra invitaciones iniciales desde las columnas antiguas de `Tenants`;
- elimina esas columnas despues de preservar sus datos;
- agrega `Kind` y siembra `TaxVision Platform`.

### Tenant

- `Tenants`;
- `wolverine_*`.

La migracion `AddTenantDefaultTimeZone` agrega `DefaultTimeZoneId`; los tenants
anteriores reciben `Etc/UTC`.

La migracion `AddTenantKindAndPlatformTenant` agrega `Kind`, clasifica los registros
anteriores como `Customer` y siembra el tenant interno. Las lecturas comerciales
filtran `Kind = Customer`.

### Subscription

Tablas:

- `Plans`;
- `PlanFeatures`;
- `PlanVersions`;
- `Modules`;
- `PlanModules`;
- `Subscriptions`;
- `SeatSubscriptions`;
- `SubscriptionModules`;
- `PendingSubscriptionChanges`;
- `SubscriptionEnrollments`;
- `wolverine_*`.

Migraciones aplicadas:

-