
# Guia de implementación de los test (Pendiente)
<a href="https://firebasestorage.googleapis.com/v0/b/c5iffaa-10025.firebasestorage.app/o/Guia_Implementacion_Pruebas_Automatizadas_TaxVision.pdf?alt=media&token=2ba4fb54-9e81-4812-b75c-03fe2b5e61d5"> Guía Implementación Test</a>

<a href="https://firebasestorage.googleapis.com/v0/b/c5iffaa-10025.firebasestorage.app/o/Guia_Implementacion_Customer_Subscription_TaxVision.pdf?alt=media&token=ae21b127-f034-49c0-a04b-d28c03097212">Customer y Subscription </br>
Guia de implementacion</a>

# TaxVision Backend

<img width="2400" height="1560" alt="taxvision_arch" src="https://github.com/user-attachments/assets/65b9f169-c0f1-4098-8533-f3d50e90c015" />


Backend multitenant de TaxVision construido con microservicios en .NET 10.

**Autor de las implementaciones documentadas:** Jorge Turbi

**Actualizado:** 27-06-2026

**Licencia del codigo propio:** propietaria; consulte [LICENSE](LICENSE).

Esta documentacion describe el estado real del repositorio despues de incorporar
seguridad multitenant, mensajeria transaccional, administracion de tenants,
autenticacion exclusivamente por invitaciones, un tenant interno reservado para el
control plane, CorrelationId de extremo a extremo, cache con invalidacion y una
plataforma local de observabilidad con Grafana, Loki, Prometheus, Tempo y
OpenTelemetry.

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
     +-----+----------------------+
     |                            |
     v                            v
+-------------------+       +-------------------+
| Tenant API        |       | Auth API          |
| puerto interno    |       | puerto interno    |
+---------+---------+       +---------+---------+
          |                           |
          v                           v
 TaxVision_Tenants              TaxVision_Auth
          |
          | eventos transaccionales
          v
  RabbitMQ exchange: taxvision-events
          |
          v
  queue: auth-tenant-events
          |
          v
  Auth tenant registry

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
| Gateway | borde HTTP y propagacion de contexto | ninguna |

Auth nunca consulta directamente `TaxVision_Tenants`. Consume eventos y mantiene una
proyeccion local. Esto crea consistencia eventual y evita acoplamiento de bases.

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
        `-- Tenant/
            |-- TaxVision.Tenant.Api/
            |-- TaxVision.Tenant.Application/
            |-- TaxVision.Tenant.Domain/
            `-- TaxVision.Tenant.Infrastructure/
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
- enruta `/auth/*` y `/tenants/*`;
- valida JWT;
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

Application depende de `IUserRepository`, `ITenantRepository`, `ITenantRegistry` e
`IUnitOfWork`. Los `DbContext` implementan Unit of Work.

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

Cada API configura:

```csharp
options.PersistMessagesWithSqlServer(sqlConn);
options.Policies.UseDurableOutboxOnAllSendingEndpoints();
options.UseEntityFrameworkCoreTransactions()
    .WithDbContextAbstraction<IUnitOfWork, ServiceDbContext>();
options.Policies.AutoApplyTransactions();
```

La transaccion EF conserva conjuntamente cambios de negocio y envelopes salientes.

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
RABBITMQ_USER=taxvision
RABBITMQ_PASSWORD=replace-with-a-strong-rabbitmq-password
RABBITMQ_CONNECTION=amqp://taxvision:replace-with-url-encoded-password@rabbitmq:5672
GRAFANA_ADMIN_USER=admin
GRAFANA_ADMIN_PASSWORD=replace-with-a-strong-grafana-password
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

Tenant requiere los mismos tipos de claves y el mismo JWT secret. Gateway requiere
`Jwt:Secret`.

### Bootstrap del primer PlatformAdmin

El tenant interno se crea mediante migraciones con el identificador fijo:

```text
8f58a521-4c25-4d91-9f4e-7ad5df14c001
```

No representa una suscripcion comercial. Para crear la primera invitacion de
`PlatformAdmin`, configure temporalmente Auth mediante secretos:

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
4. Auth/Tenant reutilizan el mismo ID.
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
| `Tenant.Inactive` | 403 |
| `Invitation.Forbidden` | 403 |
| `User.EmailConflict` | 409 |
| `Invitation.PendingConflict` | 409 |
| `Tenant.SubdomainConflict` | 409 |

`AuthDbContext` y `TenantDbContext` convierten errores SQL 2601/2627 en
`ConflictException`, cubriendo condiciones de carrera.

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

Se firma con HMAC-SHA256. Auth, Tenant y Gateway comparten secret, issuer y audience.

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

### Autorizacion Tenant

| Endpoint | Acceso |
| --- | --- |
| `POST /tenants` | onboarding publico, rate limited |
| `GET /tenants` | rol `PlatformAdmin` |
| `PATCH /tenants/{id}/status` | rol `PlatformAdmin` |
| `POST /auth/invitations` | `TenantAdmin` o `PlatformAdmin`, sujeto a la matriz |
| `POST /auth/invitations/accept` | anonimo con token valido |
| `POST /auth/invitations/{id}/cancel` | `TenantAdmin` o `PlatformAdmin` |

La creacion del primer `PlatformAdmin` se resuelve mediante el bootstrap secreto. No
existe un endpoint publico que otorgue ese rol.

### Rate limiting

Gateway limita por IP y path:

- login;
- refresh;
- crear invitacion;
- aceptar invitacion;
- crear tenant.

El limite actual es 10 requests por minuto, sin cola.

## 15. Comunicacion entre servicios

### YARP

```text
/auth/{**catch-all}     -> auth-api:8080
/tenants/{**catch-all}  -> tenant-api:8080
```

Solo Gateway publica API al host.

### RabbitMQ

`taxvision-events` es un exchange fanout. `auth-tenant-events` es una cola durable.

El consumidor de creacion usa upsert. El consumidor de estado actualiza `IsActive`.
Ambos usan inbox durable y correlation.

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

No se cachean credenciales, tokens ni operaciones de escritura.

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

Indices:

- Tenant `SubDomain` unico;
- Auth tenant `SubDomain` unico;
- User `(TenantId, Email)` unico;
- Invitation `TokenHash` unico;
- RefreshToken `TokenHash` unico.

Aplicar:

```powershell
dotnet ef database update `
  --project src\Services\Auth\Infrastructure\TaxVision.Auth.Infrastructure.csproj `
  --startup-project src\Services\Auth\Api\TaxVision.Auth.Api.csproj

dotnet ef database update `
  --project src\Services\Tenant\TaxVision.Tenant.Infrastructure\TaxVision.Tenant.Infrastructure.csproj `
  --startup-project src\Services\Tenant\TaxVision.Tenant.Api\TaxVision.Tenant.Api.csproj
```

## 17. Inyeccion de dependencias

`AddBuildingBlocks` registra contexts scoped:

- `CorrelationContext`;
- `TenantContext`.

`AddRedisCache` registra:

- `IDistributedCache`;
- `ICacheService`.

`AddAuthInfrastructure` registra repositorios de usuarios, tenants e invitaciones,
PBKDF2, tokens de invitacion, JWT y refresh tokens.

`AddTenantInfrastructure` registra repositorios y lecturas.

`AddTaxVisionJwtAuthentication`, `AddTaxVisionOpenTelemetry` y
`AddTaxVisionGatewayRateLimiting` son extensiones reutilizables para nuevos hosts.

## 18. Buenas practicas

| Practica | Implementacion |
| --- | --- |
| Database per service | bases Auth y Tenant |
| Identidad multitenant | filtro e indice `(TenantId, Email)` |
| Registro cerrado | solo invitaciones con actor y alcance validados |
| Secrets fuera de Git | `.env`, User Secrets |
| Password hashing | PBKDF2 |
| Token storage seguro | hash SHA-256 |
| Tenant header confiable | derivado del JWT |
| Error mapping | status HTTP por codigo |
| Race protection | indices + `ConflictException` |
| Atomic outbox | middleware EF Core Wolverine |
| Durable inbox | queue Auth |
| Idempotencia | upsert e invitacion reentrante |
| Control plane aislado | tenant interno no comercial |
| Correlation completo | HTTP, eventos, logs y traces |
| Cache responsable | solo lectura + invalidacion + fallback |
| Health checks | SQL, Redis, Rabbit y downstream |
| Imagenes reproducibles | tags exactos |
| Observabilidad central | OTLP, Loki, Prometheus y Tempo |
| Capas compartidas | base, infrastructure y web |

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

Respuesta:

```json
{
  "id": "tenant-guid",
  "name": "Empresa Demo",
  "subdomain": "empresa-demo",
  "defaultTimeZoneId": "America/Santo_Domingo",
  "adminActivationToken": "one-time-token",
  "adminInvitationExpiresAtUtc": "2026-07-04T12:00:00Z"
}
```

### Aceptar la invitacion del administrador

```http
POST /auth/invitations/accept
```

```json
{
  "invitationToken": "one-time-token",
  "name": "Jorge",
  "lastName": "Turbi",
  "password": "Use-A-Strong-Password-123!"
}
```

El mismo endpoint acepta invitaciones para cualquiera de los cuatro actores.

### Invitar empleado

```http
POST /auth/invitations
Authorization: Bearer <tenant-admin-jwt>
```

```json
{
  "tenantId": "tenant-guid",
  "email": "ana@example.com",
  "actorType": "TenantEmployee",
  "customerId": null
}
```

La respuesta incluye `invitationToken` una sola vez.

### Invitar cliente al portal

```http
POST /auth/invitations
Authorization: Bearer <tenant-admin-jwt>
```

```json
{
  "tenantId": "tenant-guid",
  "email": "cliente@example.com",
  "actorType": "CustomerPortal",
  "customerId": "customer-guid"
}
```

### Invitar otro PlatformAdmin

```http
POST /auth/invitations
Authorization: Bearer <platform-admin-jwt>
```

```json
{
  "tenantId": "8f58a521-4c25-4d91-9f4e-7ad5df14c001",
  "email": "admin2@taxvision.com",
  "actorType": "PlatformAdmin",
  "customerId": null
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

### Refresh

```http
POST /auth/refresh
```

```json
{
  "refreshToken": "token"
}
```

### Revoke

```http
POST /auth/revoke
```

### Cambiar estado

```http
PATCH /tenants/{tenantId}/status
Authorization: Bearer <platform-admin-jwt>
```

```json
{
  "status": "Suspended"
}
```

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
`deploy/docker/docker-compose.yml`; `deploy/docker-compose.yml` conserva solo la
infraestructura minima. RabbitMQ crea el usuario de `RABBITMQ_USER` unicamente al
inicializar un volumen nuevo. Si `rabbitmq-data` ya contiene una instalacion anterior,
cree el usuario dentro de RabbitMQ o migre el volumen de forma controlada; cambiar
solo `.env` no modifica credenciales almacenadas.

Estado:

```powershell
docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  ps
```

Actualizar Auth:

```powershell
docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  up -d --build --force-recreate auth-api
```

Actualizar todo:

```powershell
docker compose --env-file .env `
  -f deploy\docker\docker-compose.yml `
  up -d --build --force-recreate
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
docker compose --env-file .env -f deploy\docker\docker-compose.yml logs -f otel-collector
```

### RabbitMQ

Abra `http://localhost:15672` con `RABBITMQ_USER` y `RABBITMQ_PASSWORD`.

Revise:

- `taxvision-events`;
- `auth-tenant-events`;
- consumidores;
- ready/unacked.

### Wolverine SQL

```sql
SELECT * FROM dbo.wolverine_outgoing_envelopes;
SELECT * FROM dbo.wolverine_incoming_envelopes;
SELECT * FROM dbo.wolverine_dead_letters;
```

### DNS Docker

```powershell
docker network inspect taxvision-network
```

## 22. Pruebas automatizadas

Por solicitud, no se crearon proyectos ni pruebas automatizadas.

Se genero una guia completa en:

```text
D:\TaxVision Docs\Guia_Implementacion_Pruebas_Automatizadas_TaxVision.pdf
```

Tambien se conserva la fuente Markdown:

```text
D:\TaxVision Docs\Guia_Implementacion_Pruebas_Automatizadas_TaxVision.md
```

La guia explica:

- estructura de proyectos;
- comandos de creacion;
- referencias y paquetes;
- fixtures Testcontainers;
- pruebas unitarias, integracion, arquitectura y E2E;
- multitenancy;
- outbox/inbox;
- admin idempotente;
- refresh/revoke;
- authorization y rate limiting;
- cache;
- correlation;
- observabilidad;
- criterios para los 25 microservicios.

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
13. Ruta YARP.
14. Pruebas siguiendo la guia PDF.

### Pendientes reales

- reemplazar credenciales locales por un secret manager en produccion;
- mover el bootstrap de `PlatformAdmin` a un secret manager/Job de provisioning en produccion;
- habilitar TLS externo e interno segun el entorno;
- agregar CI/CD, SBOM y escaneo de secretos;
- implementar las pruebas de la guia;
- definir retencion y almacenamiento object storage para observabilidad productiva;
- crear el microservicio de Suscripcion;
- crear `UserProfile` y aplicar la sobrescritura personal de `TimeZoneId`;
- versionar contratos de eventos antes de incorporar mas consumidores.

## 24. Guia de implementacion Customer y Subscription

La guia detallada y verificada visualmente se encuentra en:

```text
output/pdf/Guia_Implementacion_Customer_Subscription_TaxVision.pdf
```

Incluye:

- modelo final del aggregate `Customer`;
- `CustomerRelation` para conyuges, dependientes, contactos y socios;
- separacion entre `RelationshipKind` y `RelationPurpose`;
- `CustomerFiscalProfile` y `CustomerRelationFiscalProfile`;
- exclusion de `CustomerNotes` y `PortalUserId` del bounded context;
- implementacion por capas, tablas, indices, endpoints, eventos y pruebas;
- modelo de `SubscriptionEnrollment` previo al tenant;
- provisioning Subscription -> Payment -> Tenant -> Auth;
- planes versionados, proration, renovacion y entitlements;
- migracion desde las entidades legacy y orden de cutover.

## 25. Payment Service — Stripe SaaS y adaptadores tenant

**Actualizado:** 02-07-2026

Payment es el cuarto microservicio de TaxVision. Encapsula dos contextos de cobro
completamente independientes que no comparten base de datos ni eventos entre si.

### Contexto 1: SaaS / Control Plane

TaxVision cobra a los tenants usando Stripe como procesador global de la plataforma.

Flujo:

```text
Subscription publica evento de pago solicitado
    → taxvision-events → payment-service-events (queue durable)
    → Payment Service consume (inbox Wolverine)
    → GetOrCreateStripeCustomer (por TenantId, cacheado en StripeCustomers)
    → CreatePaymentIntent (Stripe API)
    → ConfirmPaymentIntent (Stripe API)
    → SaaSPayment.MarkProcessing(intentId)
    → Stripe webhook POST /webhooks/stripe  [verifica Stripe-Signature]
    → ProcessSaaSPaymentCommand
    → SaaSPayment.MarkCompleted() o MarkFailed()
    → publica *PaymentCompleted o *PaymentFailed → Subscription
```

Eventos consumidos (`payment-service-events`):

| Evento | Tipo |
| --- | --- |
| `EnrollmentPaymentRequestedIntegrationEvent` | Enrollment onboarding |
| `SeatPurchaseRequestedIntegrationEvent` | Compra de asientos adicionales |
| `SeatRenewalPaymentRequestedIntegrationEvent` | Renovacion de asientos |
| `SubscriptionRenewalPaymentRequestedIntegrationEvent` | Renovacion de suscripcion |

Eventos publicados (`taxvision-events`):

- `EnrollmentPaymentCompletedIntegrationEvent` / `EnrollmentPaymentFailedIntegrationEvent`
- `SeatPaymentCompletedIntegrationEvent` / `SeatPaymentFailedIntegrationEvent`
- `SeatRenewalPaymentCompletedIntegrationEvent` / `SeatRenewalPaymentFailedIntegrationEvent`
- `SubscriptionRenewalPaymentCompletedIntegrationEvent` / `SubscriptionRenewalPaymentFailedIntegrationEvent`

Tablas: `SaaSPayments`, `StripeCustomers`, `wolverine_*`.

### Contexto 2: Tenant / Aplicacion del cliente

Los tenants cobran a sus propios clientes con cualquier proveedor que configuren.

```text
POST /payments/tenant/config  (TenantAdmin)
    → TenantPaymentConfig.Create(provider, publicKey, secretKeyEncrypted, ...)
    → claves almacenadas cifradas; nunca expuestas en respuestas HTTP

POST /payments/tenant/charge  (TenantAdmin)
    → ProcessTenantPaymentHandler
    → IPaymentAdapterFactory.GetAdapter(provider)
    → IPaymentAdapter.ChargeAsync(amount, currency, description)
    → TenantTransaction.MarkCompleted(externalId) o MarkFailed(reason)
```

Proveedores:

| Proveedor | Estado |
| --- | --- |
| Stripe | Implementado (`StripePaymentAdapter`) |
| PayPal | Stub (`PayPalPaymentAdapter`) — SDK pendiente |
| Square, MercadoPago, Manual | Enum declarado; adaptador pendiente |

Tablas: `TenantPaymentConfigs`, `TenantTransactions`. Sin eventos ni Redis.

### Variables de entorno requeridas

```env
PAYMENT_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Payment;...
STRIPE_SECRET_KEY=sk_live_...          # clave secreta Stripe (nunca en appsettings.json)
STRIPE_WEBHOOK_SECRET=whsec_...        # verifica firma del webhook de Stripe
```

### Endpoints

| Metodo | Ruta | Rol | Descripcion |
| --- | --- | --- | --- |
| `GET` | `/payments/saas/{id}` | `PlatformAdmin` | Consultar cobro SaaS por ID |
| `GET` | `/payments/saas` | `PlatformAdmin` | Listar cobros SaaS del tenant en JWT |
| `GET` | `/payments/tenant/config` | `TenantAdmin` | Ver config del proveedor del tenant |
| `POST` | `/payments/tenant/config` | `TenantAdmin` | Crear o actualizar proveedor |
| `POST` | `/payments/tenant/charge` | `TenantAdmin` | Cobrar a un cliente del tenant |
| `GET` | `/payments/tenant/transactions` | `TenantAdmin` | Listar transacciones del tenant |
| `POST` | `/webhooks/stripe` | Anonimo | Receptor webhooks Stripe (verifica firma) |

### Estructura del servicio

```text
src/Services/Payment/
├── TaxVision.Payment.Domain/
│   ├── SaaSPayments/          (SaaSPayment, SaaSPaymentType, PaymentStatus)
│   ├── StripeCustomers/       (StripeCustomer)
│   └── TenantPayments/        (TenantPaymentConfig, TenantTransaction,
│                               TenantPaymentProvider, IPaymentAdapter,
│                               IPaymentAdapterFactory)
├── TaxVision.Payment.Application/
│   ├── Abstractions/          (ISaaSPaymentRepository, IStripeCustomerRepository,
│   │                           ITenantPaymentConfigRepository,
│   │                           ITenantTransactionRepository, IStripeGateway)
│   ├── SaaSPayments/
│   │   ├── Commands/          (ProcessSaaSPayment)
│   │   ├── Queries/           (GetSaaSPayment, GetSaaSPaymentsByTenant)
│   │   └── IntegrationEvents/ (4 handlers de eventos de Subscription)
│   └── TenantPayments/
│       ├── Commands/          (ConfigureTenantProvider, ProcessTenantPayment)
│       └── Queries/           (GetTenantPaymentConfig, GetTenantTransactions)
├── TaxVision.Payment.Infrastructure/
│   ├── Payments/              (StripeGateway, StripeOptions,
│   │                           StripePaymentAdapter, PayPalPaymentAdapter,
│   │                           PaymentAdapterFactory)
│   ├── Persistence/
│   │   ├── PaymentDbContext
│   │   ├── Configurations/    (EF Core mappings)
│   │   └── Repositories/      (4 repositorios)
│   └── DependencyInjection.cs
└── TaxVision.Payment.Api/
    ├── Controllers/           (SaaSPaymentsController, TenantPaymentsController,
    │                           WebhooksController)
    └── Program.cs             (Wolverine outbox + inbox + retry 1s/5s/15s)
```

### Pendientes de produccion

- Cifrado AES real para `SecretKeyEncrypted` / `WebhookSecretEncrypted` en `TenantPaymentConfig`.
- Reemplazar `pm_card_visa` en `StripeGateway.ConfirmPaymentIntentAsync` con el
  `paymentMethodId` real del frontend (Stripe Elements / Stripe.js).
- Implementar PayPal REST Orders API en `PayPalPaymentAdapter`.
- Mover confirmacion del PaymentIntent a flujo asincrono (webhook) en produccion.
- Separar en dos microservicios: `TaxVision.PaymentApp` (SaaS) y `TaxVision.PaymentClient`
  (tenant-side) — ver plan de arquitectura pendiente.
migracion desde las entidades legacy y orden de cutover.

## 25. Payment Service — Guia de implementacion

**Actualizado:** 01-07-2026

### Descripcion

Payment es el cuarto microservicio de TaxVision. Resuelve dos contextos de pago
independientes que no deben mezclarse:

**SaaS / Control Plane** — TaxVision cobra a los tenants usando Stripe como
procesador global. Cuando Subscription publica un evento de pago solicitado
(`EnrollmentPaymentRequestedIntegrationEvent`, `SeatPurchaseRequestedIntegrationEvent`,
etc.), Payment lo consume, crea o reutiliza un `StripeCustomer`, genera un
`PaymentIntent`, lo confirma y publica el evento de resultado
(`*PaymentCompleted` o `*PaymentFailed`) de vuelta a `taxvision-events`.

**Tenant / Aplicacion del cliente** — el tenant puede cobrar a sus propios clientes
con cualquier proveedor que elija (Stripe, PayPal, Square, MercadoPago, etc.). El
patron `IPaymentAdapter` / `IPaymentAdapterFactory` permite agregar nuevos adaptadores
sin tocar el dominio ni la aplicacion. Las credenciales del proveedor se almacenan
cifradas y nunca se exponen en respuestas HTTP.

### Variables de entorno requeridas

```env
PAYMENT_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Payment;...
STRIPE_SECRET_KEY=sk_live_...
STRIPE_WEBHOOK_SECRET=whsec_...
```

Nunca coloque `STRIPE_SECRET_KEY` ni `STRIPE_WEBHOOK_SECRET` en `appsettings.json`.

### Estructura

```text
src/Services/Payment/
├── TaxVision.Payment.Domain/
│   ├── SaaSPayments/    (SaaSPayment, SaaSPaymentType, PaymentStatus)
│   ├── StripeCustomers/ (StripeCustomer)
│   └── TenantPayments/  (TenantPaymentConfig, TenantTransaction, TenantPaymentProvider)
├── TaxVision.Payment.Application/
│   ├── Abstractions/    (7 interfaces)
│   ├── SaaSPayments/    (Commands, Queries, IntegrationEvents — 4 handlers)
│   └── TenantPayments/  (Commands, Queries)
├── TaxVision.Payment.Infrastructure/
│   ├── Persistence/     (PaymentDbContext, 4 configuraciones EF, 4 repos)
│   └── Payments/        (StripeGateway, StripePaymentAdapter, PayPalPaymentAdapter, Factory)
└── TaxVision.Payment.Api/
    └── Controllers/     (SaaSPaymentsController, WebhooksController, TenantPaymentsController)
```

### Eventos de integracion consumidos

Payment escucha la cola `payment-service-events` ligada al exchange `taxvision-events`:

- `EnrollmentPaymentRequestedIntegrationEvent`
- `SeatPurchaseRequestedIntegrationEvent`
- `SeatRenewalPaymentRequestedIntegrationEvent`
- `SubscriptionRenewalPaymentRequestedIntegrationEvent`

### Eventos de integracion publicados

Payment publica hacia `taxvision-events` (cola `subscription-service-events`):

- `EnrollmentPaymentCompletedIntegrationEvent` / `EnrollmentPaymentFailedIntegrationEvent`
- `SeatPaymentCompletedIntegrationEvent` / `SeatPaymentFailedIntegrationEvent`
- `SeatRenewalPaymentCompletedIntegrationEvent` / `SeatRenewalPaymentFailedIntegrationEvent`
- `SubscriptionRenewalPaymentCompletedIntegrationEvent` / `SubscriptionRenewalPaymentFailedIntegrationEvent`

Todos estos contratos se definieron en `BuildingBlocks.Messaging` para ser compartidos
con el Subscription service.

### Tablas (TaxVision_Payment)

- `SaaSPayments` — registro de cada cobro de la plataforma;
- `StripeCustomers` — mapping `TenantId` → `StripeCustomerId`;
- `TenantPaymentConfigs` — configuracion de proveedor del tenant;
- `TenantTransactions` — transacciones del lado del tenant.

### Migracion inicial

```powershell
dotnet ef database update `
  --project src\Services\Payment\TaxVision.Payment.Infrastructure\TaxVision.Payment.Infrastructure.csproj `
  --startup-project src\Services\Payment\TaxVision.Payment.Api\TaxVision.Payment.Api.csproj `
  --connection "Server=localhost,1433;Database=TaxVision_Payment;User Id=sa;Password=<SA_PASSWORD>;TrustServerCertificate=true"
```

### Rutas YARP (Gateway)

```
/payments/{**catch-all}  → payment-api:8080  (autenticado)
/webhooks/{**catch-all}  → payment-api:8080  (anonimo, verificacion Stripe-Signature)
```

### Endpoints

| Metodo | Ruta | Acceso | Descripcion |
| --- | --- | --- | --- |
| `GET` | `/payments/saas/{id}` | `PlatformAdmin` | Detalle de un cobro SaaS |
| `GET` | `/payments/saas` | `PlatformAdmin` | Lista de cobros del tenant |
| `POST` | `/webhooks/stripe` | anonimo | Recibe eventos de Stripe |
| `GET` | `/payments/tenant/config` | `TenantAdmin` | Configuracion de proveedor |
| `POST` | `/payments/tenant/config` | `TenantAdmin` | Configura proveedor del tenant |
| `POST` | `/payments/tenant/charge` | `TenantAdmin` | Procesa un cobro del tenant |
| `GET` | `/payments/tenant/transactions` | `TenantAdmin` | Lista de transacciones |

### Proximos pasos

- implementar cifrado real (AES-256) para `SecretKeyEncrypted` y `WebhookSecretEncrypted`;
- completar `PayPalPaymentAdapter` con el SDK real de PayPal;
- mover la confirmacion del `PaymentIntent` a webhooks asincronos (flujo actual es sincrono, adecuado solo para pruebas);
- agregar health check del endpoint de Stripe (`https://status.stripe.com/api/v2/status.json`).
