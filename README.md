# TaxVision Backend

Backend multitenant de TaxVision construido con microservicios en .NET 10.

**Autor de las implementaciones documentadas:** Jorge Turbi

**Estado de esta documentacion:** refleja el codigo del repositorio al 27-06-2026.

> Esta documentacion describe el comportamiento actual. Cuando una capacidad esta
> preparada pero no se usa todavia, o existe una brecha entre el diseno y el codigo,
> se indica expresamente.

## Indice

1. [Introduccion general](#1-introduccion-general)
2. [Objetivo del proyecto](#2-objetivo-del-proyecto)
3. [Arquitectura general](#3-arquitectura-general)
4. [Estructura de carpetas y proyectos](#4-estructura-de-carpetas-y-proyectos)
5. [Tecnologias y licenciamiento](#5-tecnologias-y-licenciamiento)
6. [Patrones de diseno y arquitectura](#6-patrones-de-diseno-y-arquitectura)
7. [Flujo general de una request](#7-flujo-general-de-una-request)
8. [Configuracion](#8-configuracion)
9. [Middleware](#9-middleware)
10. [CorrelationId](#10-correlationid)
11. [Errores y excepciones](#11-errores-y-excepciones)
12. [Logging y observabilidad](#12-logging-y-observabilidad)
13. [Validaciones](#13-validaciones)
14. [Seguridad y autenticacion](#14-seguridad-y-autenticacion)
15. [Comunicacion entre servicios](#15-comunicacion-entre-servicios)
16. [Acceso a datos y persistencia](#16-acceso-a-datos-y-persistencia)
17. [Inyeccion de dependencias](#17-inyeccion-de-dependencias)
18. [Buenas practicas aplicadas](#18-buenas-practicas-aplicadas)
19. [Ejemplos de codigo explicados](#19-ejemplos-de-codigo-explicados)
20. [Ejecucion local y Docker](#20-ejecucion-local-y-docker)
21. [Depuracion](#21-depuracion)
22. [Recomendaciones para nuevos desarrolladores](#22-recomendaciones-para-nuevos-desarrolladores)
23. [Mejoras futuras y brechas conocidas](#23-mejoras-futuras-y-brechas-conocidas)

## 1. Introduccion general

TaxVision Backend es una base para una plataforma SaaS multitenant. Separa la
administracion de tenants, la identidad de usuarios y el acceso externo en procesos
independientes:

- **Gateway:** unico punto de entrada HTTP cuando se ejecuta con Docker.
- **Tenant Service:** fuente canonica de tenants.
- **Auth Service:** registro, login, JWT, refresh tokens y proyeccion local de tenants.
- **BuildingBlocks:** contratos y componentes compartidos.
- **RabbitMQ:** transporte de eventos entre servicios.
- **Redis:** infraestructura de cache distribuida.
- **SQL Server:** persistencia independiente para Auth y Tenant.

La solucion combina llamadas HTTP sincronas con comunicacion asincrona. Por ejemplo,
crear un tenant es una operacion HTTP en Tenant Service, pero su disponibilidad en
Auth se propaga mediante `TenantCreatedIntegrationEvent`.

### Vista de alto nivel

```text
Cliente / Postman
       |
       | HTTP :5047
       v
+---------------------+
| TaxVision.Gateway   |
| YARP + JWT          |
+----------+----------+
           |
       +---+-----------------------+
       |                           |
       v                           v
+-------------------+       +-------------------+
| Tenant API        |       | Auth API          |
| :8080 interno     |       | :8080 interno     |
+---------+---------+       +---------+---------+
          |                           |
          v                           v
 TaxVision_Tenants              TaxVision_Auth
          |
          | TenantCreatedIntegrationEvent
          v
    RabbitMQ exchange
    "taxvision-events"
          |
          v
 queue "auth-tenant-events" ---> Auth tenant registry

Redis es compartido como cache distribuida, aunque los flujos de negocio actuales
todavia no consumen `ICacheService`.
```

## 2. Objetivo del proyecto

El objetivo es ofrecer una base mantenible para evolucionar TaxVision como sistema
SaaS:

- aislar responsabilidades por microservicio;
- mantener datos separados por contexto;
- identificar a cada usuario dentro de su tenant;
- evitar registrar usuarios para tenants inexistentes;
- autenticar mediante tokens firmados;
- publicar cambios relevantes mediante eventos;
- soportar entrega durable de mensajes;
- exponer APIs solo mediante el Gateway en Docker;
- proporcionar trazabilidad mediante `CorrelationId`;
- ejecutar el entorno de desarrollo de forma repetible con Docker Compose.

## 3. Arquitectura general

### 3.1 Microservicios

Cada servicio posee su propio modelo, API y base de datos:

| Servicio | Responsabilidad | Base de datos |
| --- | --- | --- |
| Tenant | Ciclo de vida y consulta de tenants | `TaxVision_Tenants` |
| Auth | Usuarios, credenciales, tokens y registry local de tenants | `TaxVision_Auth` |
| Gateway | Enrutamiento, autenticacion de tokens y propagacion de contexto | No aplica |

Auth no consulta directamente la base de datos de Tenant. Recibe eventos y mantiene
su propia tabla `Tenants`. Esto reduce el acoplamiento, pero introduce consistencia
eventual: puede existir un intervalo corto entre crear el tenant y poder registrar
un usuario.

### 3.2 Capas internas

Tenant y Auth siguen una separacion inspirada en Clean Architecture:

```text
API -> Application -> Domain
 |          ^
 v          |
Infrastructure
```

- **Domain:** entidades, invariantes y eventos de dominio.
- **Application:** comandos, queries, handlers, DTOs y contratos.
- **Infrastructure:** EF Core, repositorios, SQL Server y seguridad concreta.
- **API:** controllers, composicion de dependencias, middleware y transporte HTTP.

Las dependencias apuntan hacia las reglas de negocio. `Infrastructure` implementa
interfaces declaradas en `Application`.

### 3.3 Flujo general del sistema

1. El cliente llama al Gateway.
2. El Gateway obtiene o crea `X-Correlation-Id`.
3. Si hay JWT valido, extrae `tenant_id` y genera `X-Tenant-Id`.
4. YARP reenvia la request al servicio destino.
5. El servicio reconstruye los contextos de correlation y tenant.
6. El controller convierte HTTP en un comando o query.
7. Wolverine localiza y ejecuta el handler.
8. El handler usa dominio, repositorios y `IUnitOfWork`.
9. Si corresponde, publica un evento de integracion.
10. El controller convierte `Result` en una respuesta HTTP.

## 4. Estructura de carpetas y proyectos

```text
TaxVision/
|-- TaxVision.slnx
|-- global.json
|-- README.md
|-- .env                         # local, ignorado por Git
|-- Postman_Collection/
|   |-- TaxVision_Backend.postman_collection.json
|   `-- TaxVisionBackEnd.postman_environment.json
|-- deploy/
|   |-- docker-compose.yml       # solo RabbitMQ y Redis
|   `-- docker/
|       `-- docker-compose.yml   # stack principal
`-- src/
    |-- BuildingBlocks/
    |-- Gateway/
    |   `-- TaxVision.Gateway/
    `-- Services/
        |-- Auth/
        |   |-- Api/
        |   |-- Application/
        |   |-- Domain/
        |   `-- Infrastructure/
        `-- Tenant/
            |-- TaxVision.Tenant.Api/
            |-- TaxVision.Tenant.Application/
            |-- TaxVision.Tenant.Domain/
            `-- TaxVision.Tenant.Infrastructure/
```

### 4.1 `BuildingBlocks`

Contiene piezas compartidas:

- `Domain/`: `BaseEntity`, `IDomainEvent`, `ITenantOwned`, `TenantEntity`.
- `Results/`: `Result`, `Result<T>` y `Error`.
- `Messaging/`: contratos de eventos de integracion.
- `Caching/`: abstraccion e implementacion Redis.
- `Common/`: `CorrelationContext` y registro compartido.
- `Tenancy/`: `TenantContext`.
- `Middleware/`: correlation, errores y resolucion de tenant.
- `Observability/`: configuracion central de Serilog.
- `Persistence/`: `IRepository<T>` e `IUnitOfWork`.

Es una biblioteca tecnica compartida, no un microservicio. Debe mantenerse pequena
para evitar acoplar los dominios.

### 4.2 `TaxVision.Gateway`

Responsabilidades:

- cargar rutas y clusters YARP;
- validar JWT cuando se presenta;
- eliminar cualquier `X-Tenant-Id` suministrado por el cliente;
- obtener `tenant_id` desde el JWT;
- reenviar el tenant confiable hacia el servicio;
- crear y propagar `X-Correlation-Id`;
- registrar logs del borde del sistema.

### 4.3 Tenant Service

| Proyecto | Responsabilidad |
| --- | --- |
| `TaxVision.Tenant.Domain` | Entidad `Tenant`, estados y eventos de dominio |
| `TaxVision.Tenant.Application` | Crear y listar tenants |
| `TaxVision.Tenant.Infrastructure` | EF Core, repositorios, SQL Server y lecturas |
| `TaxVision.Tenant.Api` | Controllers, Swagger, middleware, Wolverine y RabbitMQ |

### 4.4 Auth Service

| Proyecto | Responsabilidad |
| --- | --- |
| `TaxVision.Auth.Domain` | `User`, `RefreshToken` y tenant proyectado |
| `TaxVision.Auth.Application` | Registro, login y consumidor de eventos |
| `TaxVision.Auth.Infrastructure` | EF Core, PBKDF2, JWT y refresh tokens |
| `TaxVision.Auth.Api` | Controllers, Swagger, middleware, Wolverine y RabbitMQ |

## 5. Tecnologias y licenciamiento

Las versiones provienen de `global.json`, archivos `.csproj` y Docker Compose.
La licencia del paquete se verifico en su metadata NuGet cuando estaba disponible.

### 5.1 Plataforma y componentes principales

| Tecnologia | Version declarada | Proposito | Licencia | Por que se usa |
| --- | --- | --- | --- | --- |
| .NET SDK | 10.0.300 | Compilar y ejecutar la solucion | MIT | Plataforma moderna, tipada y multiplataforma |
| ASP.NET Core | `net10.0` / paquetes 10.0.9 | APIs HTTP y middleware | MIT | Pipeline web, DI y configuracion integrados |
| Entity Framework Core SQL Server | 10.0.9 | ORM, mappings y migraciones | MIT | Persistencia tipada y migraciones versionadas |
| SQL Server | Version no declarada | Bases Auth y Tenant | Comercial Microsoft; Developer solo no-produccion | Motor relacional compatible con EF Core y Wolverine |
| WolverineFx | 6.14.0 | Mediator, handlers y mensajeria durable | MIT | Unifica CQRS local y transporte asincrono |
| RabbitMQ | imagen `rabbitmq:3-management` | Broker AMQP y panel de administracion | MPL-2.0 | Desacopla publicadores y consumidores |
| Redis | imagen `redis:7` | Cache distribuida | Depende del minor de Redis 7 | Cache compartida entre instancias |
| YARP | 2.3.0 | Reverse proxy/API Gateway | MIT | Enrutamiento nativo sobre ASP.NET Core |
| Serilog.AspNetCore | 10.0.0 | Logging estructurado | Apache-2.0 | Enriquecimiento y multiples destinos |
| Serilog.Sinks.File | 7.0.0 | Logs diarios en archivo | Apache-2.0 | Retencion local para diagnostico |
| Serilog.Sinks.MSSqlServer | 10.0.0 | Sink SQL disponible en Tenant | Apache-2.0 | Paquete instalado, pero no configurado actualmente |
| Swashbuckle.AspNetCore | 10.2.2 | Swagger UI y OpenAPI | MIT | Explorar APIs en desarrollo |
| System.IdentityModel.Tokens.Jwt | 8.19.1 | Crear y leer JWT | MIT | Estandar de tokens firmado |
| Mapster | 10.0.9 | Mapeo de objetos | MIT | Instalado como capacidad compartida; sin uso actual |
| StackExchangeRedis integration | 10.0.9 | `IDistributedCache` sobre Redis | MIT | Abstraccion oficial de cache distribuida |
| Docker / Docker Compose | Version local no fijada | Construccion y orquestacion | Docker Engine Apache-2.0; Docker Desktop sujeto a suscripcion | Entorno repetible y red privada |
| Postman | Version no declarada | Pruebas manuales de API | Propietaria | Coleccion y variables de entorno reutilizables |

### 5.2 Paquetes Microsoft adicionales

Los paquetes siguientes usan licencia MIT:

- `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.9.
- `Microsoft.AspNetCore.Cryptography.KeyDerivation` 10.0.9.
- `Microsoft.AspNetCore.OpenApi` 10.0.9.
- `Microsoft.EntityFrameworkCore.Design` 10.0.9.
- `Microsoft.Extensions.Logging.Abstractions` 10.0.9.
- `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.9.

### 5.3 Consideraciones de licencia

- El tag `redis:7` no fija el minor. Redis 7.2 y anteriores usan BSD-3-Clause;
  Redis 7.4 a 7.8 usan RSALv2 o SSPLv1. Debe fijarse una version exacta antes de
  una revision legal o despliegue productivo.
- `rabbitmq:3-management` tampoco fija patch; conviene usar un tag inmutable.
- SQL Server no esta incluido en el Compose principal. La edicion Developer es solo
  para desarrollo, pruebas y demostracion, no para produccion.
- Docker Desktop es gratuito solo bajo las condiciones de su acuerdo de suscripcion.
- El repositorio no contiene un archivo `LICENSE`; por tanto, la licencia del codigo
  propio de TaxVision no esta declarada.

Fuentes:

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [RabbitMQ licensing](https://www.rabbitmq.com/)
- [Redis licenses](https://redis.io/legal/licenses/)
- [SQL Server licensing guidance](https://www.microsoft.com/licensing/guidance/SQL)
- [Docker Desktop license](https://docs.docker.com/subscription/desktop-license/)

## 6. Patrones de diseno y arquitectura

### 6.1 Domain-Driven Design pragmatico

Las entidades controlan su estado mediante setters privados y fabricas:

```csharp
public static Result<Tenant> Create(string name, string subdomain)
{
    if (string.IsNullOrWhiteSpace(name))
        return Result.Failure<Tenant>(
            new Error("Tenant.Name", "Name is required."));

    // Normalizacion, validacion y creacion.
}
```

**Problema que resuelve:** impedir entidades invalidas y evitar que controllers
manipulen estado directamente.

**Implementacion:** `Tenant.Create`, `Tenant.Suspend`, `User.Register`,
`RefreshToken.Create` y `Tenant.Register` dentro de Auth.

**Alcance actual:** existen entidades y eventos de dominio, pero no hay value objects,
servicios de dominio ni un dispatcher de `DomainEvents`. Los eventos levantados por
`BaseEntity.Raise` permanecen en memoria y no se procesan actualmente.

### 6.2 CQRS

Commands y queries estan separados:

```csharp
public sealed record CreateTenantCommand(
    string Name,
    string Subdomain,
    string AdminEmail);

public sealed record GetTenantsQuery(int Page = 1, int Size = 20);
```

Wolverine encuentra los metodos `Handle` y los invoca mediante `IMessageBus`.

**Beneficio:** controllers pequenos, casos de uso aislados y posibilidad de aplicar
politicas por mensaje.

**Alcance actual:** es CQRS logico, no fisico. Lecturas y escrituras usan la misma
base SQL Server. `TenantReadService` si usa una proyeccion directa y `AsNoTracking`.

### 6.3 Repository

Application depende de abstracciones como `ITenantRepository` e `IUserRepository`.
Infrastructure implementa consultas EF Core.

**Beneficio:** los casos de uso no dependen directamente de `DbContext`.

**Buena practica:** agregar a la interfaz solo operaciones requeridas por casos de
uso reales, evitando un repositorio generico que exponga detalles del ORM.

### 6.4 Unit of Work

`AuthDbContext` y `TenantDbContext` implementan `IUnitOfWork`:

```csharp
public sealed class AuthDbContext : DbContext, IUnitOfWork
{
}
```

Los handlers guardan con `SaveChangesAsync`. Esto define explicitamente el limite de
persistencia del caso de uso.

### 6.5 Result pattern

Errores de negocio esperados se expresan como datos:

```csharp
return Result.Failure<UserResponse>(
    new Error("Tenant.NotFound", "Tenant does not exist or is inactive."));
```

**Beneficio:** no usar excepciones para validaciones normales y mantener estable el
flujo de control.

### 6.6 Event-Driven Architecture

Tenant publica `TenantCreatedIntegrationEvent`; Auth lo consume y actualiza su
registry local. Esto evita una llamada HTTP sincronica desde Auth hacia Tenant.

### 6.7 Outbox e inbox durable

Cada API configura:

```csharp
options.PersistMessagesWithSqlServer(sqlConn);
options.Policies.UseDurableOutboxOnAllSendingEndpoints();
```

Auth agrega:

```csharp
options.ListenToRabbitQueue("auth-tenant-events", queue =>
{
    queue.BindExchange("taxvision-events", string.Empty);
}).UseDurableInbox();
```

Wolverine usa tablas `wolverine_*`:

- `wolverine_outgoing_envelopes`: mensajes pendientes de salida;
- `wolverine_incoming_envelopes`: mensajes recibidos;
- `wolverine_dead_letters`: mensajes agotados o fallidos;
- tablas de nodos, asignaciones y control: coordinacion interna.

**Importante:** el repositorio no muestra integracion transaccional explicita del
`DbContext` con Wolverine. Debe verificarse que el commit de negocio y la insercion
del envelope saliente compartan transaccion antes de afirmar una garantia atomica
completa.

### 6.8 API Gateway

YARP aplica el patron Gateway:

```json
"Routes": {
  "auth": {
    "ClusterId": "auth",
    "Match": { "Path": "/auth/{**catch-all}" }
  },
  "tenant": {
    "ClusterId": "tenant",
    "Match": { "Path": "/tenants/{**catch-all}" }
  }
}
```

**Beneficio:** los clientes conocen una sola URL y los servicios permanecen sin
puertos HTTP publicados en Docker.

## 7. Flujo general de una request

### 7.1 Crear un tenant

```text
POST /tenants
  -> Gateway
  -> CorrelationIdMiddleware
  -> YARP
  -> Tenant API
  -> TenantController.Create
  -> CreateTenantHandler
  -> Tenant.Create
  -> TenantRepository + TenantDbContext
  -> TenantCreatedIntegrationEvent
  -> RabbitMQ
  -> Auth TenantCreatedConsumer
  -> Auth.Tenants
```

Body:

```json
{
  "name": "Empresa Demo",
  "subdomain": "empresa-demo",
  "adminEmail": "admin@empresa-demo.com"
}
```

El endpoint devuelve `201 Created`. `AdminEmail` viaja en el evento, pero el consumidor
actual de Auth no crea automaticamente un administrador ni usa ese campo.

### 7.2 Registrar un usuario

```text
POST /auth/register
  -> RegisterHandler
  -> confirma tenant activo en Auth.Tenants
  -> normaliza email
  -> verifica conflicto
  -> PBKDF2(password)
  -> User.Register
  -> AuthDbContext
  -> UserRegisteredIntegrationEvent
```

Body:

```json
{
  "tenantId": "00000000-0000-0000-0000-000000000000",
  "name": "Jorge",
  "lastName": "Turbi",
  "email": "jorge@example.com",
  "password": "Use-A-Real-Secret-123!"
}
```

### 7.3 Login

```text
POST /auth/login
  -> busca usuario
  -> PBKDF2 Verify
  -> genera JWT con tenant_id
  -> genera refresh token aleatorio
  -> almacena solo SHA-256(refresh token)
  -> devuelve accessToken + refreshToken
```

Body:

```json
{
  "tenantId": "00000000-0000-0000-0000-000000000000",
  "email": "jorge@example.com",
  "password": "Use-A-Real-Secret-123!"
}
```

## 8. Configuracion

ASP.NET Core combina `appsettings.json`, variables de entorno, User Secrets y
argumentos. Las variables de entorno usan doble guion bajo para representar `:`.

### 8.1 Claves requeridas

| Clave | Servicio | Proposito |
| --- | --- | --- |
| `ConnectionStrings:Default` | Auth, Tenant | Conexion SQL del servicio |
| `ConnectionStrings:Redis` | Auth, Tenant | Redis; fallback local `localhost:6379` |
| `RabbitMq:Uri` | Auth, Tenant | URI AMQP |
| `Jwt:Secret` | Auth, Gateway | Firma y validacion JWT |
| `Jwt:Issuer` | Auth, Gateway | Emisor esperado |
| `Jwt:Audience` | Auth, Gateway | Audiencia esperada |
| `Jwt:AccessMinutes` | Auth | Duracion del access token |
| `RefreshToken:ExpirationDays` | Auth | Duracion del refresh token |
| `ReverseProxy` | Gateway | Rutas, clusters y destinos YARP |

Auth exige que `Jwt:Secret` tenga al menos 32 bytes. Nunca debe almacenarse en Git.

### 8.2 `.env` para Docker

Crear `D:\TaxVision\.env`:

```env
JWT_SECRET=replace-with-a-random-secret-of-at-least-32-bytes
AUTH_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Auth;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
TENANT_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Tenants;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
```

`.env` esta ignorado en `.gitignore`. No debe pegarse en issues, commits ni logs.

### 8.3 User Secrets para ejecucion local

Auth:

```powershell
dotnet user-secrets set "ConnectionStrings:Default" "<AUTH_CONNECTION>" `
  --project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" `
  --project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
dotnet user-secrets set "RabbitMq:Uri" "amqp://guest:guest@localhost:5672" `
  --project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
dotnet user-secrets set "Jwt:Secret" "<SAME_32_BYTE_SECRET>" `
  --project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
```

Tenant:

```powershell
dotnet user-secrets set "ConnectionStrings:Default" "<TENANT_CONNECTION>" `
  --project src\Services\Tenant\TaxVision.Tenant.Api\TaxVision.Tenant.Api.csproj
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" `
  --project src\Services\Tenant\TaxVision.Tenant.Api\TaxVision.Tenant.Api.csproj
dotnet user-secrets set "RabbitMq:Uri" "amqp://guest:guest@localhost:5672" `
  --project src\Services\Tenant\TaxVision.Tenant.Api\TaxVision.Tenant.Api.csproj
```

Gateway:

```powershell
dotnet user-secrets set "Jwt:Secret" "<SAME_32_BYTE_SECRET>" `
  --project src\Gateway\TaxVision.Gateway\TaxVision.Gateway.csproj
```

Consultar nombres de secrets sin imprimirlos en documentacion:

```powershell
dotnet user-secrets list --project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
dotnet user-secrets list --project src\Services\Tenant\TaxVision.Tenant.Api\TaxVision.Tenant.Api.csproj
dotnet user-secrets list --project src\Gateway\TaxVision.Gateway\TaxVision.Gateway.csproj
```

## 9. Middleware

El orden del pipeline es significativo.

### 9.1 Gateway

```csharp
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantPropagationMiddleware>();
app.MapReverseProxy();
```

1. Correlation engloba todo el procesamiento y el proxy.
2. Authentication construye `HttpContext.User`.
3. Authorization evalua endpoints que exijan autorizacion.
4. Tenant propagation obtiene el claim de un usuario autenticado.
5. YARP reenvia la request.

### 9.2 Auth

```csharp
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

### 9.3 Tenant

```csharp
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();
app.MapControllers();
```

### 9.4 `TenantPropagationMiddleware`

El Gateway elimina primero el header enviado por el cliente:

```csharp
ctx.Request.Headers.Remove("X-Tenant-Id");

var tenantId = ctx.User.FindFirst("tenant_id")?.Value;
if (!string.IsNullOrWhiteSpace(tenantId))
    ctx.Request.Headers["X-Tenant-Id"] = tenantId;
```

Esto reduce suplantacion de tenant: el identificador reenviado proviene del JWT
validado, no de un valor arbitrario del cliente.

### 9.5 `TenantResolutionMiddleware`

Tenant API convierte `X-Tenant-Id` en un `TenantContext` scoped:

```csharp
var raw = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
if (Guid.TryParse(raw, out var tenantId))
    tenant.SetTenant(tenantId);
```

Actualmente los handlers de Tenant no consumen `ITenantContext`. Es infraestructura
preparada para futuras entidades pertenecientes a un tenant.

## 10. CorrelationId

### 10.1 Que es

Un `CorrelationId` es un identificador opaco asociado a una operacion. Permite unir
los logs generados por Gateway, Auth, Tenant y, cuando se complete la propagacion,
los consumidores de eventos.

No autentica al usuario y no reemplaza `tenantId`, `userId`, `EventId` ni un trace
distribuido de OpenTelemetry.

### 10.2 Header

TaxVision usa:

```http
X-Correlation-Id: 7c89fd90f55045b7b61735586536cc29
```

El cliente puede proporcionarlo, pero no esta obligado. Si falta, el primer
`CorrelationIdMiddleware` genera:

```csharp
id = Guid.NewGuid().ToString("N");
```

### 10.3 Donde se configura

- Registro scoped: `BuildingBlocks/Common/DependecyInjection.cs`.
- Contexto: `BuildingBlocks/Common/ICorrelationContext.cs`.
- Middleware: `BuildingBlocks/Middleware/CorrelationIdMiddleware.cs`.
- Logging: `BuildingBlocks/Observability/TaxVisionLogging.cs`.
- Pipeline Gateway: `Gateway/TaxVision.Gateway/Program.cs`.
- Pipeline Auth: `Services/Auth/Api/Program.cs`.
- Pipeline Tenant: `Services/Tenant/TaxVision.Tenant.Api/Program.cs`.
- Contrato de eventos: `BuildingBlocks/Messaging/IIntegrationEvent.cs`.

### 10.4 Como se genera y almacena durante HTTP

```csharp
var id = ctx.Request.Headers[Header].FirstOrDefault();

if (string.IsNullOrWhiteSpace(id))
    id = Guid.NewGuid().ToString("N");

ctx.Request.Headers[Header] = id;
corr.Set(id);
```

Durante la request vive en tres lugares:

1. `HttpContext.Request.Headers`.
2. `CorrelationContext`, registrado como scoped.
3. `Serilog.LogContext`.

No se persiste en una tabla propia. Puede quedar materializado en archivos o sistemas
de logs si el evento de log se escribe.

### 10.5 Como viaja entre servicios

1. Gateway lee o genera el valor.
2. Lo coloca en el request header.
3. YARP reenvia los headers de la request al destino.
4. Auth o Tenant leen el mismo header.
5. El servicio devuelve el valor en el response header:

```csharp
ctx.Response.OnStarting(() =>
{
    ctx.Response.Headers[Header] = id;
    return Task.CompletedTask;
});
```

Por ello, el cliente puede reportar el ID recibido aunque no lo haya creado.

### 10.6 Integracion con logs

```csharp
using (LogContext.PushProperty("CorrelationId", id))
{
    await next(ctx);
}
```

La plantilla central muestra la propiedad:

```text
[{Timestamp} {Level}] [{Service}] [{CorrelationId}] {Message}
```

Ejemplo esperado:

```text
[2026-06-27 10:15:30.120 +02:00 INF] [gateway] [prueba-001] ...
[2026-06-27 10:15:30.145 +02:00 INF] [tenant-service] [prueba-001] ...
```

### 10.7 Estado actual en eventos

`IntegrationEvent` declara:

```csharp
public string CorrelationId { get; init; } = string.Empty;
```

Sin embargo, `CreateTenantHandler` y `RegisterHandler` no asignan esa propiedad.
Por tanto:

- la trazabilidad HTTP esta implementada;
- el contrato permite correlation en eventos;
- la propagacion aplicativa HTTP -> RabbitMQ -> consumidor esta incompleta.

La implementacion esperada seria inyectar `ICorrelationContext`:

```csharp
public static async Task<Result<TenantResponse>> Handle(
    CreateTenantCommand cmd,
    ITenantRepository repo,
    IUnitOfWork unitOfWork,
    IMessageBus bus,
    ICorrelationContext correlation,
    CancellationToken ct)
{
    await bus.PublishAsync(new TenantCreatedIntegrationEvent
    {
        NewTenantId = tenant.Id,
        TenantId = tenant.Id,
        Name = tenant.Name,
        SubDomain = tenant.SubDomain,
        AdminEmail = cmd.AdminEmail,
        CorrelationId = correlation.CorrelationId
    });
}
```

El consumidor debe volver a introducirlo en su contexto de logs:

```csharp
using (LogContext.PushProperty("CorrelationId", evt.CorrelationId))
{
    await tenants.UpsertCreatedAsync(
        evt.NewTenantId, evt.Name, evt.SubDomain, ct);
    await unitOfWork.SaveChangesAsync(ct);
}
```

Este ejemplo es una recomendacion; no describe codigo ya aplicado.

### 10.8 Como probarlo

Sin proporcionar ID:

```powershell
curl.exe -i http://localhost:5047/tenants
```

Con ID controlado:

```powershell
curl.exe -i `
  -H "X-Correlation-Id: prueba-taxvision-001" `
  http://localhost:5047/tenants
```

Buscarlo en logs:

```powershell
docker compose --env-file .env -f deploy\docker\docker-compose.yml `
  logs gateway auth-api tenant-api | Select-String "prueba-taxvision-001"
```

### 10.9 Buenas practicas pendientes

- validar longitud y caracteres del valor recibido para evitar abuso de logs;
- generar el ID en Gateway y reutilizarlo en toda la operacion;
- no reutilizar un ID entre operaciones independientes;
- propagarlo en eventos y consumidores;
- agregar `traceparent` y OpenTelemetry para trazas distribuidas estandar;
- incluir correlation en errores y respuestas `ProblemDetails`;
- evitar datos personales o secretos dentro del ID.

## 11. Errores y excepciones

### 11.1 Errores esperados

`Result<T>` representa fallos de negocio:

```csharp
if (await repo.SubDomainExistsAsync(cmd.Subdomain, ct))
{
    return Result.Failure<TenantResponse>(
        new Error("Tenant.Subdomain", "Subdomain already exists."));
}
```

Los controllers convierten el resultado:

```csharp
return result.IsSuccess
    ? Created($"/tenants/{result.Value.Id}", result.Value)
    : BadRequest(result.Error);
```

Actualmente todos los errores de negocio se convierten en `400 Bad Request`.
Una API mas precisa deberia mapear conflictos a `409`, no encontrados a `404` y
credenciales invalidas a `401`.

### 11.2 Excepciones no controladas

`ExceptionHandlingMiddleware` registra la excepcion y devuelve:

```json
{
  "status": 500,
  "title": "Internal Server Error",
  "detail": "An unexpected error occurred while processing your request. Use the Correlation ID to report the issue."
}
```

No expone stack trace al cliente. El detalle tecnico queda en logs.

### 11.3 Retries de mensajeria

Auth y Tenant configuran reintentos:

```csharp
options.Policies.OnException<Exception>()
    .RetryWithCooldown(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15));
```

Despues de agotar la politica, Wolverine puede registrar el mensaje como dead letter.
Los handlers deben ser idempotentes; `TenantRegistry.UpsertCreatedAsync` cumple esa
intencion al actualizar o insertar.

## 12. Logging y observabilidad

### 12.1 Configuracion central

Los tres procesos llaman:

```csharp
builder.Host.UseTaxVisionSerilog("service-name");
```

`TaxVisionLogging` configura:

- lectura de opciones desde configuration;
- enriquecimiento desde `LogContext`;
- propiedad constante `Service`;
- salida a consola;
- archivo diario bajo `AppContext.BaseDirectory/Logs`;
- retencion de 30 archivos.

### 12.2 Destinos

En Docker, la consola se consulta con:

```powershell
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f gateway
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f auth-api
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f tenant-api
```

Los directorios `/app/Logs` no tienen volumen Docker. Los archivos desaparecen al
recrear el contenedor. Para produccion se necesita un sink central o volumen.

### 12.3 Limites actuales

- No hay OpenTelemetry, spans ni metricas.
- No existe backend central como Seq, Elasticsearch, Grafana Loki o Application
  Insights.
- El sink MSSQL esta instalado en Tenant API, pero `TaxVisionLogging` no lo usa.
- Los logs de startup no tienen un correlation asociado y pueden mostrar el campo
  vacio; esto es normal.
- Los eventos RabbitMQ aun no restauran el correlation aplicativo.

## 13. Validaciones

### 13.1 Tenant

`Tenant.Create` aplica:

- nombre obligatorio;
- subdominio en minusculas;
- longitud entre 3 y 40;
- caracteres `a-z`, `0-9` y `-`;
- estado inicial `Active`;
- fecha UTC.

La unicidad se protege dos veces:

1. preconsulta `SubDomainExistsAsync`;
2. indice unico SQL `IX_Tenants_SubDomain`.

El indice es la garantia final frente a requests concurrentes.

### 13.2 Usuario

Registro aplica:

- tenant no vacio en dominio;
- tenant existente y activo en Auth;
- nombre y apellido obligatorios;
- email normalizado a minusculas;
- validacion basica de `@`;
- password de al menos 12 caracteres;
- hash obligatorio;
- rol inicial `User`.

La base de datos posee un indice unico:

```csharp
builder.HasIndex(user => new { user.TenantId, user.Email })
    .IsUnique();
```

Esto permite el mismo email en tenants distintos y lo rechaza dentro del mismo
tenant.

### 13.3 Brecha en el repositorio de usuarios

Aunque los metodos reciben `tenantId`, actualmente lo ignoran:

```csharp
public Task<User?> GetByEmailAsync(
    Guid tenantId, string email, CancellationToken ct = default)
    => db.Users.FirstOrDefaultAsync(user => user.Email == email, ct);
```

Consecuencias:

- el precheck de registro puede rechazar globalmente un email de otro tenant;
- login puede encontrar al usuario de un tenant incorrecto;
- la firma del metodo aparenta aislamiento que la consulta no aplica.

La consulta correcta debe ser:

```csharp
=> db.Users.FirstOrDefaultAsync(
    user => user.TenantId == tenantId && user.Email == email,
    ct);
```

Y para existencia:

```csharp
=> db.Users.AnyAsync(
    user => user.TenantId == tenantId && user.Email == email,
    ct);
```

Esta correccion es prioritaria.

### 13.4 Validaciones pendientes

- validar `AdminEmail`;
- usar una validacion de email mas robusta;
- limitar `page` y `size`;
- capturar la excepcion de indice unico y convertirla en `409 Conflict`;
- validar estado del tenant tambien durante login;
- evitar passwords comunes o comprometidos;
- validar formato y longitud de `X-Correlation-Id`.

## 14. Seguridad y autenticacion

### 14.1 Password hashing

`Pbkdf2PasswordHasher` usa:

- salt aleatorio de 16 bytes;
- PBKDF2 con HMAC-SHA256;
- 100,000 iteraciones;
- clave derivada de 32 bytes;
- comparacion constante con `CryptographicOperations.FixedTimeEquals`.

```csharp
var hash = KeyDerivation.Pbkdf2(
    password,
    salt,
    KeyDerivationPrf.HMACSHA256,
    Iterations,
    KeySize);
```

Nunca se almacena la password original.

### 14.2 JWT

El access token incluye:

- `sub`: identificador del usuario;
- `email`;
- `tenant_id`;
- claims de rol;
- claims de permission.

Se firma con HMAC-SHA256 y expira por defecto en 15 minutos.

Auth y Gateway deben usar exactamente el mismo `Jwt:Secret`, `Issuer` y `Audience`.
El secreto debe tener alta entropia, no ser una frase predecible.

### 14.3 Refresh tokens

`RefreshTokenService` genera 64 bytes aleatorios. Devuelve el token original una sola
vez y guarda SHA-256:

```csharp
var tokenHash = Convert.ToHexString(
    SHA256.HashData(Encoding.UTF8.GetBytes(token)));
```

Esto reduce el impacto de una lectura no autorizada de la base de datos.

### 14.4 Aislamiento del tenant en el Gateway

El cliente no debe establecer `X-Tenant-Id`. El Gateway lo elimina y reconstruye
desde el claim firmado `tenant_id`.

### 14.5 Superficie publica actual

En Docker solo se publica el puerto HTTP del Gateway (`5047:8080`). Auth y Tenant
usan `expose`, por lo que son accesibles dentro de `taxvision-network`, no desde el
host por un puerto publicado.

Sin embargo, actualmente:

- los controllers no usan `[Authorize]`;
- las rutas YARP no llaman `RequireAuthorization`;
- `UseAuthorization` por si solo no protege endpoints;
- crear y listar tenants es publico;
- register y login son publicos, como suele esperarse;
- no hay rate limiting;
- no hay endpoint para renovar o revocar refresh tokens;
- no hay politicas por rol o permiso.

La presencia de `UseAuthentication` no equivale a exigir autenticacion.

## 15. Comunicacion entre servicios

### 15.1 HTTP mediante YARP

Rutas:

```text
/auth/{**catch-all}     -> Auth API
/tenants/{**catch-all}  -> Tenant API
```

Localmente:

```text
Auth:    http://localhost:5124
Tenant:  http://localhost:5217
Gateway: http://localhost:5047
```

En Docker, los destinos son:

```text
http://auth-api:8080/
http://tenant-api:8080/
```

Docker DNS resuelve `auth-api` y `tenant-api` porque todos pertenecen a
`taxvision-network`.

### 15.2 RabbitMQ

`taxvision-events` es un **exchange**, no una cola.

`auth-tenant-events` es la cola consumida por Auth:

```text
TenantCreatedIntegrationEvent
        |
        v
exchange: taxvision-events
        |
        v
queue: auth-tenant-events
        |
        v
TenantCreatedConsumer
```

Wolverine crea la topologia con `AutoProvision()`.

### 15.3 Registry local de tenants

El consumidor ejecuta:

```csharp
await tenants.UpsertCreatedAsync(
    evt.NewTenantId,
    evt.Name,
    evt.SubDomain,
    ct);

await unitOfWork.SaveChangesAsync(ct);
```

Auth valida registros contra su propia tabla, no contra Tenant Service.

### 15.4 Redis

`AddRedisCache` registra `ICacheService` con operaciones:

- `GetAsync`;
- `SetAsync`;
- `RemoveAsync`;
- `GetOrCreateAsync`.

El TTL predeterminado es 10 minutos y las claves usan prefijo `taxvision:`.

Estado actual: ningun handler consume `ICacheService`; Redis esta conectado y listo,
pero no participa en los flujos documentados.

## 16. Acceso a datos y persistencia

### 16.1 Contextos

`TenantDbContext`:

```text
Tenants
wolverine_*
```

`AuthDbContext`:

```text
Tenants
Users
RefreshTokens
wolverine_*
```

### 16.2 Configuraciones EF Core

Se usan clases `IEntityTypeConfiguration<T>` y:

```csharp
modelBuilder.ApplyConfigurationsFromAssembly(
    Assembly.GetExecutingAssembly());
```

Esto evita concentrar todos los mappings en `OnModelCreating`.

### 16.3 Indices y relaciones

- Tenant `SubDomain`: unico.
- Auth tenant `SubDomain`: unico.
- User `(TenantId, Email)`: unico compuesto.
- Refresh token `TokenHash`: unico.
- Refresh token `UserId`: indexado y FK con borrado cascade.

No existe actualmente una FK explicita `User.TenantId -> Auth.Tenants.Id`.

### 16.4 Lecturas

`TenantReadService` usa:

```csharp
db.Tenants
    .AsNoTracking()
    .OrderBy(t => t.Name)
    .Skip((page - 1) * size)
    .Take(size)
    .Select(t => new TenantResponse(t.Id, t.Name, t.SubDomain));
```

`AsNoTracking` evita tracking innecesario en una consulta de solo lectura.

### 16.5 Migraciones

Auth incluye:

- `InitialAuth`;
- `AddUserNameAndLastName`;
- `AddAuthTenantRegistry`;
- `MakeUserEmailUniquePerTenant`.

Tenant incluye `InitialCreate`.

Las APIs no ejecutan `Database.Migrate()` al arrancar. Las migraciones se aplican
explicitamente antes de iniciar o actualizar contenedores.

## 17. Inyeccion de dependencias

### 17.1 BuildingBlocks

```csharp
services.AddScoped<TenantContext>();
services.AddScoped<ITenantContext>(
    sp => sp.GetRequiredService<TenantContext>());

services.AddScoped<CorrelationContext>();
services.AddScoped<ICorrelationContext>(
    sp => sp.GetRequiredService<CorrelationContext>());
```

Se registra la misma instancia concreta para su interfaz dentro del scope HTTP.

### 17.2 Infrastructure

Auth registra:

- `AuthDbContext`;
- `IUnitOfWork`;
- `ITenantRegistry`;
- `IUserRepository`;
- `IPasswordHasher`;
- `IJwtTokenGenerator`;
- `IRefreshTokenService`.

Tenant registra:

- `TenantDbContext`;
- `IUnitOfWork`;
- `ITenantRepository`;
- `ITenantReadService`.

### 17.3 Lifetimes

Los contextos, repositorios y servicios de request son scoped. Es apropiado para que
cada request tenga una unidad de trabajo y contextos de tenant/correlation propios.

## 18. Buenas practicas aplicadas

| Practica | Problema que resuelve | Implementacion |
| --- | --- | --- |
| Separacion por capas | Controllers y SQL acoplados al dominio | Proyectos Domain/Application/Infrastructure/API |
| Database per service | Acoplamiento de datos entre contextos | `TaxVision_Auth` y `TaxVision_Tenants` |
| Setters privados y fabricas | Estado invalido | `Tenant.Create`, `User.Register` |
| Result pattern | Excepciones para reglas normales | `Result<T>` y `Error` |
| Normalizacion | Comparaciones inconsistentes | email/subdominio en minusculas |
| Indices unicos | Condiciones de carrera | subdominio y `(TenantId, Email)` |
| Password hashing | Exposicion de credenciales | PBKDF2 + salt + comparacion constante |
| Refresh token hasheado | Robo desde base de datos | SHA-256 del token |
| Secretos fuera de Git | Filtracion de credenciales | User Secrets, `.env`, `.gitignore` |
| Correlation ID | Logs imposibles de relacionar | header, contexto scoped y Serilog |
| Logs estructurados | Diagnostico inconsistente | configuracion Serilog central |
| Middleware de excepciones | Filtrar stack traces al cliente | `ProblemDetails` 500 |
| Mensajeria durable | Perdida/duplicacion de mensajes | Wolverine outbox/inbox |
| Consumidor idempotente | Redelivery | `UpsertCreatedAsync` |
| Retry con cooldown | Fallos transitorios | politica Wolverine 1/5/15 segundos |
| `AsNoTracking` | Tracking innecesario | listado de tenants |
| CancellationToken | Trabajo inutil tras cancelar request | controllers, handlers y EF |
| Docker multi-stage | Imagen final con SDK innecesario | SDK para build, ASP.NET runtime final |
| Red Docker privada | Exponer microservicios | `taxvision-network` y `expose` |
| Gateway limpia tenant header | Suplantacion de tenant | `TenantPropagationMiddleware` |
| Configuracion validada al inicio | Fallos tardios | excepciones si faltan JWT/Rabbit/SQL |

### Practicas presentes solo parcialmente

- DDD: los eventos de dominio no se despachan.
- EDA: correlation no llega a eventos.
- Outbox: falta confirmar atomicidad con el commit EF.
- Multitenancy: el indice es correcto, pero las consultas de usuario ignoran tenant.
- Autorizacion: JWT se valida, pero ningun endpoint exige autorizacion.
- Cache: esta registrada, pero no se utiliza.

## 19. Ejemplos de codigo explicados

### 19.1 Unicidad de email por tenant

```csharp
builder.HasIndex(user => new { user.TenantId, user.Email })
    .IsUnique();
```

La identidad logica del usuario es `(TenantId, Email)`. SQL Server rechaza:

```text
Tenant A + user@example.com
Tenant A + user@example.com  <- duplicado
```

Y permite:

```text
Tenant A + user@example.com
Tenant B + user@example.com
```

La consulta de repositorio debe usar ambas columnas; ver la brecha de la seccion 13.

### 19.2 Validacion eventual de tenant

```csharp
if (!await tenants.ExistsActiveAsync(command.TenantId, ct))
{
    return Result.Failure<UserResponse>(
        new Error(
            "Tenant.NotFound",
            "Tenant does not exist or is inactive."));
}
```

El registro no confia en cualquier GUID. Consulta la proyeccion local alimentada por
RabbitMQ.

### 19.3 Token multitenant

```csharp
new("tenant_id", user.TenantId.ToString())
```

El claim permite que Gateway determine el tenant confiable de requests autenticadas.

### 19.4 Consumer idempotente

```csharp
var existing = await db.Tenants
    .FirstOrDefaultAsync(tenant => tenant.Id == tenantId, ct);

if (existing is not null)
{
    existing.UpdateFromCreatedEvent(name, subDomain);
    return;
}
```

Si RabbitMQ entrega nuevamente el evento, Auth actualiza el registro en lugar de
insertar otro.

### 19.5 Cache-aside disponible

```csharp
var value = await cache.GetOrCreateAsync(
    key,
    token => repository.LoadAsync(token),
    TimeSpan.FromMinutes(10),
    ct);
```

Este patron se puede aplicar a lecturas estables, pero el ejemplo es adaptado:
actualmente ningun handler del proyecto lo usa.

## 20. Ejecucion local y Docker

### 20.1 Requisitos

- .NET SDK 10.0.300.
- Docker Desktop o Docker Engine con Compose.
- SQL Server accesible.
- `dotnet-ef` 10.0.9.
- Puertos libres: 5047, 5124, 5217, 5672, 6379 y 15672.

Instalar EF CLI:

```powershell
dotnet tool update --global dotnet-ef --version 10.0.9
```

### 20.2 Restaurar y compilar

Desde `D:\TaxVision`:

```powershell
dotnet restore
dotnet build
```

### 20.3 Aplicar migraciones

Tenant:

```powershell
dotnet ef database update `
  --project src\Services\Tenant\TaxVision.Tenant.Infrastructure\TaxVision.Tenant.Infrastructure.csproj `
  --startup-project src\Services\Tenant\TaxVision.Tenant.Api\TaxVision.Tenant.Api.csproj
```

Auth:

```powershell
dotnet ef database update `
  --project src\Services\Auth\Infrastructure\TaxVision.Auth.Infrastructure.csproj `
  --startup-project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
```

### 20.4 Infraestructura y APIs en Docker

Crear `.env` y ejecutar:

```powershell
docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  up -d --build
```

Estado:

```powershell
docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  ps
```

URLs:

```text
Gateway:             http://localhost:5047
RabbitMQ management: http://localhost:15672
Redis:               localhost:6379
```

Auth y Tenant no publican puertos al host en el Compose principal.

### 20.5 Actualizar contenedores

Solo Auth:

```powershell
docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  up -d --build --force-recreate auth-api
```

Todo el stack:

```powershell
docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  up -d --build --force-recreate
```

Build limpio de Auth:

```powershell
docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  build --no-cache auth-api

docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  up -d --force-recreate auth-api
```

Detener sin borrar volumenes:

```powershell
docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  down
```

No usar `down -v` salvo que se quiera eliminar deliberadamente datos de RabbitMQ y
Redis.

### 20.6 Ejecucion hibrida local

Levantar solo RabbitMQ y Redis:

```powershell
docker compose -f deploy\docker-compose.yml up -d
```

Despues, en terminales separadas:

```powershell
dotnet run --project src\Services\Auth\Api\TaxVision.Auth.Api.csproj
dotnet run --project src\Services\Tenant\TaxVision.Tenant.Api\TaxVision.Tenant.Api.csproj
dotnet run --project src\Gateway\TaxVision.Gateway\TaxVision.Gateway.csproj
```

Swagger local:

```text
Auth:   http://localhost:5124/swagger
Tenant: http://localhost:5217/swagger
```

El Gateway no agrega Swagger de los microservicios.

### 20.7 Prueba funcional minima

1. Crear tenant mediante `POST http://localhost:5047/tenants`.
2. Guardar `response.id` como `tenantId`.
3. Esperar a que Auth consuma el evento.
4. Registrar mediante `POST http://localhost:5047/auth/register`.
5. Hacer login mediante `POST http://localhost:5047/auth/login`.
6. Guardar `accessToken` y `refreshToken`.
7. Repetir el email en el mismo tenant: debe rechazarse.
8. Usar el mismo email en otro tenant: el indice SQL lo permite, pero primero debe
   corregirse el filtro faltante en `UserRepository`.

## 21. Depuracion

### 21.1 Confirmar compilacion

```powershell
dotnet build
```

### 21.2 Estado y logs Docker

```powershell
docker compose --env-file .env -f deploy\docker\docker-compose.yml ps
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f gateway
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f auth-api
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f tenant-api
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f rabbitmq
```

### 21.3 DNS interno

Si Gateway muestra `Name or service not known (tenant-api:8080)`:

```powershell
docker compose --env-file .env -f deploy\docker\docker-compose.yml ps
docker network inspect taxvision-network
```

Verificar que Gateway y `tenant-api` esten activos y en la misma red.

### 21.4 RabbitMQ

Abrir `http://localhost:15672` y revisar:

- exchange `taxvision-events`;
- queue `auth-tenant-events`;
- consumidores activos;
- mensajes ready/unacked;
- dead letters en persistencia Wolverine.

Las credenciales `guest/guest` son solo apropiadas para desarrollo local.

### 21.5 SQL Server y Wolverine

Revisar:

```text
dbo.__EFMigrationsHistory
dbo.Tenants
dbo.Users
dbo.RefreshTokens
dbo.wolverine_incoming_envelopes
dbo.wolverine_outgoing_envelopes
dbo.wolverine_dead_letters
```

### 21.6 Correlation

Enviar un ID conocido y buscarlo en todos los logs:

```powershell
curl.exe -i `
  -H "X-Correlation-Id: debug-tenant-001" `
  http://localhost:5047/tenants

docker compose --env-file .env -f deploy\docker\docker-compose.yml `
  logs gateway tenant-api | Select-String "debug-tenant-001"
```

### 21.7 Errores frecuentes

| Error | Causa probable | Verificacion |
| --- | --- | --- |
| Login SQL de `sa` falla | password/host/DB incorrecto | User Secrets o `.env` |
| No se resuelve `tenant-api` | servicio fuera de red o detenido | `docker network inspect` |
| Auth rechaza tenant nuevo | evento aun no consumido | queue y tabla Auth.Tenants |
| JWT invalido | secret/issuer/audience distintos | configuracion Auth/Gateway |
| Redis connection refused | contenedor detenido o host incorrecto | `docker compose ps` |
| Logs sin correlation | log fuera de request o evento asincrono | seccion 10 |

## 22. Recomendaciones para nuevos desarrolladores

1. Leer primero `Program.cs` de Gateway, Auth y Tenant.
2. Seguir una request desde controller hasta handler, dominio y repositorio.
3. No compartir entidades de dominio entre servicios; compartir solo contratos
   estables de integracion.
4. No consultar directamente la base de otro microservicio.
5. Agregar migracion por cada cambio de modelo y revisar el SQL generado.
6. Filtrar siempre entidades multitenant por `TenantId`.
7. Mantener `tenantId` fuera de headers controlados por el cliente; usar el claim.
8. Pasar `CancellationToken` hasta EF, cache y mensajeria.
9. No registrar passwords, JWT, refresh tokens ni connection strings.
10. Incluir `CorrelationId` al publicar y consumir eventos.
11. Diseñar consumidores idempotentes porque un broker puede redeliver.
12. Probar happy path, validaciones, duplicados, concurrencia y aislamiento.
13. Ejecutar `dotnet build` y migraciones antes de reconstruir contenedores.
14. Usar el Compose `deploy/docker/docker-compose.yml` para el stack completo.
15. Documentar si una capacidad esta implementada, preparada o pendiente.

## 23. Mejoras futuras y brechas conocidas

### Prioridad critica

- Corregir `UserRepository` para filtrar por `TenantId` y `Email`.
- Agregar pruebas que demuestren aislamiento de email entre tenants.
- Exigir autorizacion en endpoints administrativos de Tenant.
- Completar propagacion de `CorrelationId` en eventos y consumidores.
- Confirmar/configurar transaccion atomica EF Core + outbox Wolverine.

### Alta prioridad

- Consumir eventos de suspension/cierre de tenant en Auth.
- Impedir login cuando el tenant este suspendido.
- Crear endpoint de refresh y revocacion de tokens.
- Implementar rate limiting en register/login.
- Mapear errores a HTTP `401`, `403`, `404` y `409`.
- Capturar conflictos SQL concurrentes.
- Agregar health checks para SQL Server, Redis y RabbitMQ.
- Agregar tests unitarios, integracion, arquitectura y end-to-end.
- Proteger RabbitMQ con credenciales no predeterminadas.
- Centralizar logs y agregar OpenTelemetry.

### Prioridad media

- Validar `AdminEmail` o eliminarlo si no se usa.
- Crear el usuario administrador a partir de un flujo definido e idempotente.
- Aplicar limites de paginacion.
- Incorporar cache solo donde exista una estrategia de invalidacion.
- Despachar o eliminar eventos de dominio actualmente acumulados.
- Separar BuildingBlocks por responsabilidades si sigue creciendo.
- Eliminar paquetes no usados como Mapster o configurar su uso real.
- Eliminar/configurar `Serilog.Sinks.MSSqlServer`.
- Corregir `TaxVision.Tenant.Api.http`, que aun apunta a `/weatherforecast`.
- Fijar tags exactos de RabbitMQ, Redis e imagenes .NET.
- Montar volumen de logs o usar un sink remoto.
- Agregar un archivo `LICENSE` para el codigo propio.
- Crear el microservicio de Suscripcion.

### Automatizacion recomendada

- CI con restore, build, tests y validacion de migraciones.
- analisis de vulnerabilidades de paquetes e imagenes;
- formato y analizadores de C#;
- generacion de SBOM;
- escaneo de secretos;
- migraciones controladas durante despliegue;
- observabilidad y alertas por dead letters.
