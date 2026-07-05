
# Guia de implementación de los test (Pendiente)
<a href="https://firebasestorage.googleapis.com/v0/b/c5iffaa-10025.firebasestorage.app/o/Guia_Implementacion_Pruebas_Automatizadas_TaxVision.pdf?alt=media&token=2ba4fb54-9e81-4812-b75c-03fe2b5e61d5"> Guía Implementación Test</a>

<a href="https://firebasestorage.googleapis.com/v0/b/c5iffaa-10025.firebasestorage.app/o/Guia_Implementacion_Customer_Subscription_TaxVision.pdf?alt=media&token=ae21b127-f034-49c0-a04b-d28c03097212">Customer y Subscription </br>
Guia de implementacion</a>

# TaxVision Backend

<img width="2400" height="1560" alt="taxvision_arch" src="https://github.com/user-attachments/assets/65b9f169-c0f1-4098-8533-f3d50e90c015" />


Backend multitenant de TaxVision construido con microservicios en .NET 10.

**Autor de las implementaciones documentadas:** Jorge Turbi

**Actualizado:** 02-07-2026

**Licencia del codigo propio:** propietaria; consulte [LICENSE](LICENSE).

Esta documentacion describe el estado real del repositorio despues de incorporar
seguridad multitenant, mensajeria transaccional, administracion de tenants,
autenticacion exclusivamente por invitaciones, un tenant interno reservado para el
control plane, CorrelationId de extremo a extremo, cache con invalidacion y una
plataforma local de observabilidad con Grafana, Loki, Prometheus, Tempo y
OpenTelemetry.

# Idea Principal de Desarrollo 
<img width="2400" height="1560"  src="https://firebasestorage.googleapis.com/v0/b/c5iffaa-10025.firebasestorage.app/o/DiagramsBackEnd.png?alt=media&token=9daa5550-50b2-4ca2-bae3-dac0e39ed332" />



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
25. Customer Service: implementacion
26. Iteracion 02-07-2026: Auth avanzado, Subscription, Notification y correcciones Customer
27. CloudStorage / Media Security Gateway

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
|-- .env
|-- README.md
|-- Postman_Collection/
|-- deploy/
|   |-- docker-compose.yml
|   |-- docker/
|   |   `-- docker-compose.yml
|   |-- tests/
|   |   |-- TaxVision.Auth.Tests/
|   |   |-- TaxVision.Tenant.Tests/
|   |   |-- TaxVision.Customer.Tests/
|   |   |-- TaxVision.Subscription.Tests/
|   |   |-- TaxVision.Notification.Tests/
|   |   `-- TaxVision.CloudStorage.Tests/
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
            |-- TaxVision.Tenant.Api/
            |-- TaxVision.Tenant.Application/
            |-- TaxVision.Tenant.Domain/
            `-- TaxVision.Tenant.Infrastructure/
        `-- CloudStorage/
            |-- TaxVision.CloudStorage.Api/
            |-- TaxVision.CloudStorage.Application/
            |-- TaxVision.CloudStorage.Domain/
            `-- TaxVision.CloudStorage.Infrastructure/
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
| MinIO (servidor) | RELEASE.2025-09-07T16-13-09Z | object storage S3 local | AGPL-3.0 |
| Minio (.NET SDK) | 7.0.0 | cliente S3 en CloudStorage | Apache-2.0 |
| ClamAV | 1.4.3 | antivirus de archivos subidos | GPL-2.0 |
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

### Aviso de licencia MinIO (AGPL-3.0)

El servidor **MinIO** que se usa localmente (`minio/minio`) es open source bajo
**AGPL-3.0**, no el producto comercial MinIO AIStor. TaxVision no requiere una licencia
comercial para usarlo: el codigo propio se comunica con MinIO exclusivamente por el
protocolo S3 sobre la red (cliente `Minio` 7.0.0, Apache-2.0) y no enlaza ni modifica el
codigo del servidor MinIO, por lo que el copyleft de la AGPL no alcanza al codigo
propietario de TaxVision.

Como el servidor se ejecuta sin modificaciones, la obligacion de la clausula de red de la
AGPL (seccion 13) se satisface poniendo a disposicion el codigo fuente original de MinIO:

- MinIO server: <https://github.com/minio/minio> (AGPL-3.0)
- Minio .NET client SDK: <https://github.com/minio/minio-dotnet> (Apache-2.0)

Para produccion existen dos alternativas sin obligaciones AGPL, ambas sin cambios en el
codigo del servicio CloudStorage (ver seccion 27.5): apuntar a un object storage
gestionado S3-compatible (AWS S3, Cloudflare R2, Backblaze B2, GCS) o autohospedar un
servidor S3 con licencia permisiva (SeaweedFS o Zenko CloudServer, Apache-2.0). Comprar
una licencia MinIO AIStor solo es necesario si se desea soporte oficial o evitar la AGPL
por completo; no es obligatorio para usar MinIO.

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

La configuracion local de Docker vive exclusivamente en `.env`. El archivo esta
ignorado por Git y debe protegerse como secreto del entorno; no se mantiene una
copia de ejemplo en el repositorio.

Estructura:

```env
JWT_SECRET=replace-with-a-random-secret-of-at-least-32-bytes
AUTH_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Auth;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
TENANT_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Tenants;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
CUSTOMER_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Customer;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
SUBSCRIPTION_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Subscription;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
NOTIFICATION_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Notification;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
CLOUDSTORAGE_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_CloudStorage;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
RABBITMQ_USER=taxvision
RABBITMQ_PASSWORD=replace-with-a-strong-rabbitmq-password
RABBITMQ_CONNECTION=amqp://taxvision:replace-with-url-encoded-password@rabbitmq:5672
MINIO_ROOT_USER=taxvision-storage
MINIO_ROOT_PASSWORD=replace-with-a-strong-minio-password
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
dotnet user-secrets set "Encryption:MasterKey" "<el-base64-generado>" `
  --project src\Services\Customer\TaxVision.Customer.Api
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

Los proyectos xUnit se encuentran bajo `deploy/tests`, uno por cada
microservicio implementado:

- `TaxVision.Auth.Tests`;
- `TaxVision.Tenant.Tests`;
- `TaxVision.Customer.Tests`;
- `TaxVision.Subscription.Tests`;
- `TaxVision.Notification.Tests`;
- `TaxVision.CloudStorage.Tests`.

`Billing` no tiene tests porque aun no contiene un proyecto o dominio.

```powershell
# Host
dotnet test TaxVision.slnx

# Docker
docker compose -f deploy/tests/docker-compose.tests.yml build
docker compose -f deploy/tests/docker-compose.tests.yml run --rm tests
```

Los comandos completos de build, migraciones y despliegue estan documentados en
`deploy/tests/README.md`.

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

### Customer Service

- gestiona el registro maestro del cliente fiscal dentro del tenant;
- aggregate root `Customer` con value objects embebidos (PersonalName, BusinessIdentity, EmailAddress, PhoneNumber);
- child entities: addresses, contact points, relations, fiscal profile y fiscal profile de relaciones;
- catalogos seed: 171 occupations y 769 NAICS (PrincipalBusinessActivities);
- cifra SSN/ITIN/EIN con AES-256-GCM y mantiene blind index HMAC por tenant para deduplicar;
- expone CRUD, invitacion al portal, archive y fiscal profile;
- publica eventos al exchange `taxvision-events`;
- usa outbox transaccional EF Core + Wolverine sobre SQL Server.

## 25. Customer Service: implementacion

Describe el microservicio Customer entregado sobre la guia
`output/pdf/Guia_Implementacion_Customer_Subscription_TaxVision.pdf`.
Esta seccion documenta lo entregado, no lo planificado.

### 25.1 Limite del bounded context

Customer es el sistema de registro del cliente fiscal dentro de un tenant.
Administra identidad de persona o negocio, contacto maestro, direcciones,
relaciones (conyuge, dependientes, contactos) y perfil fiscal cifrado.
No guarda credenciales, login, marketing, facturacion ni millas.

### 25.2 Estructura de proyectos

Cuatro proyectos en `src/Services/Customer/`:

- `TaxVision.Customer.Domain`
- `TaxVision.Customer.Application`
- `TaxVision.Customer.Infrastructure`
- `TaxVision.Customer.Api`

### 25.3 Aggregate y child entities

- `Customer` aggregate root con value objects embebidos: `PersonalName`,
  `BusinessIdentity`, `EmailAddress`, `PhoneNumber`.
- Child entities: `CustomerAddress`, `CustomerContactPoint`, `CustomerRelation`,
  `CustomerFiscalProfile`, `CustomerRelationFiscalProfile`.
- Catalogos seed global: `Occupation` (171 filas) y `PrincipalBusinessActivity`
  (769 codigos NAICS oficiales).
- Acceso a colecciones via backing field privado expuesto como
  `IReadOnlyCollection`; las altas pasan por metodos del aggregate.

### 25.4 Persistencia

Base `TaxVision_Customer`. Migraciones aplicadas en orden:

- `InitialCustomer`: crea las 8 tablas de dominio mas el seed de catalogos y
  las tablas `wolverine_*` de outbox/inbox.
- `CustomerImports`: agrega las 3 tablas del flujo de bulk import.
- `CustomerImportActiveJobUniqueIndex`: indice filtrado unique para garantizar
  un solo job activo por tenant a nivel BD.

Tablas de dominio core:

- `Customers`
- `CustomerAddresses`
- `CustomerContactPoints`
- `CustomerRelations`
- `CustomerFiscalProfiles`
- `CustomerRelationFiscalProfiles`
- `Occupations`
- `PrincipalBusinessActivities`

Tablas del flujo de bulk import:

- `CustomerImportAttempts`
- `CustomerImportRows`
- `CustomerImportFiles`

Indices clave:

- `(TenantId, Status, DisplayName)` para listados;
- `(TenantId, OccupationId)`;
- `IX_Customers_PrimaryEmailNormalized` para busqueda por email;
- `(TenantId, TaxIdentifierBlindIndex)` en fiscal profiles para deduplicar SSN
  sin almacenar texto claro;
- indices filtrados unique en addresses y contact points primarios por tipo;
- `UX_CustomerImportAttempts_Tenant_Active` filtrado unique sobre `TenantId`
  donde `Status` esta en estados activos, para enforce de un solo job activo
  por tenant a nivel BD.

### 25.5 Cifrado de identificadores fiscales

Implementacion en `Customer.Infrastructure/Security/AesGcmSensitiveDataProtector.cs`,
abstraida via `ISensitiveDataProtector` en Application.

- Algoritmo: AES-256-GCM autenticado.
- Layout almacenado: `nonce(12) | ciphertext | tag(16)` como `varbinary(512)`.
- Clave maestra desde `Encryption:MasterKey` en User Secrets (32 bytes en base64).
- Blind index: HMAC-SHA256 con clave derivada por tenant via HKDF.
- Garantia multi-tenant: el mismo SSN en dos tenants produce blind indexes distintos.
- En claro solo se almacena `TaxIdentifierLast4` para mostrar `***-**-1234`.
- Aplica a SSN/ITIN/EIN del customer y de las relaciones fiscalmente relevantes
  (conyuge, dependientes, household members).
- Identificadores fiscales y datos bancarios no aparecen en logs, eventos
  generales ni read models.

### 25.6 Endpoints CRUD y child entities

Base path `/customers`. Autorizacion por rol claim del JWT firmado.

| Verbo y ruta | Actor minimo |
| --- | --- |
| POST `/customers` | TenantEmployee |
| GET `/customers` | TenantEmployee |
| GET `/customers/{id}` | TenantEmployee |
| PATCH `/customers/{id}` | TenantEmployee |
| POST `/customers/{id}/addresses` | TenantEmployee |
| POST `/customers/{id}/contact-points` | TenantEmployee |
| POST `/customers/{id}/relations` | TenantEmployee |
| POST `/customers/{id}/portal-invitations` | TenantAdmin |
| POST `/customers/{id}/archive` | TenantAdmin |
| PUT `/customers/{id}/fiscal-profile` | TenantAdmin |
| PUT `/customers/{id}/relations/{relationId}/fiscal-profile` | TenantAdmin |

`GET /customers/{id}` incluye `OccupationName` y `PrincipalBusinessActivityDescription`
resueltos por JOIN via `ICustomerReadService` con `AsNoTracking` y proyeccion a DTO.
Los demas endpoints de lectura usan el mismo read service.

### 25.7 Carpeta `Requests` en Api

A diferencia de Auth y Tenant que aceptan el `Command` directo en el body del
controller, Customer expone DTOs en `TaxVision.Customer.Api/Requests/`. Razones:

- el `Command` lleva `TenantId` y `ModifiedByUserId` que se extraen del JWT firmado,
  no del body del cliente;
- si Command y Request fueran el mismo tipo, un cliente podria enviar `TenantId`
  en el body e intentar operar en otro tenant;
- separar Request de Command permite validaciones HTTP en el borde sin contaminar
  la capa Application.

### 25.8 Eventos de integracion publicados

Customer publica al exchange `taxvision-events`. Contratos en
`BuildingBlocks/Messaging/CustomerIntegrationEvents/`.

- `CustomerCreatedIntegrationEvent`
- `CustomerUpdatedIntegrationEvent`
- `CustomerArchivedIntegrationEvent`
- `CustomerPortalInvitationRequestedIntegrationEvent`
- `CustomersBulkImportedIntegrationEvent`

Ninguno transporta identificadores fiscales, contrasenas ni datos bancarios.
Solo `CustomerId`, `TenantId`, `DisplayName`, contacto basico, idioma, canal
preferido y metadatos.

### 25.9 Consumer en Auth

Auth consume `CustomerPortalInvitationRequestedIntegrationEvent` en
`Auth.Application/Customers/IntegrationEvents/CustomerPortalInvitationRequestedConsumer.cs`.

El consumer:

- valida que el tenant exista y este activo;
- aplica idempotencia por `(TenantId, Email)`;
- crea una `Invitation` con `ActorType=CustomerPortal` y `CustomerId`;
- imprime el token plano en log con prefijo `[DEV]` mientras no exista Email
  Service que consuma un segundo evento con el token.

### 25.10 Bulk import masivo

Flujo async de carga masiva desde CSV o XLSX. La entidad y el evento siguen
los nombres dictados por la guia PDF: `CustomerImportAttempt` (pag 8, sec 4.4)
y `CustomersBulkImportedV1` (pag 11, paso 9).

#### 25.10.1 Patron async job

1. POST `/customers/imports` con archivo multipart y header `Idempotency-Key`
   retorna 202 Accepted con `importJobId`.
2. Wolverine encola `RunCustomerImportMessage` que procesa en background.
3. Cliente hace polling a GET `/customers/imports/{id}` cada dos segundos.
4. Al completar se publica `CustomersBulkImportedIntegrationEvent` y se ofrece
   reporte descargable.

No hay request HTTP de larga duracion. El frontend nunca bloquea.

#### 25.10.2 Endpoints

Base path `/customers/imports`. Todos requieren rol `TenantAdmin`.

| Verbo y ruta | Notas |
| --- | --- |
| POST `/customers/imports` | multipart/form-data + header `Idempotency-Key`; retorna 202. |
| GET `/customers/imports/{id}` | Status del job para polling. |
| GET `/customers/imports` | Listado paginado de jobs del tenant. |
| GET `/customers/imports/{id}/report?format=csv` | Reporte por fila streamed. |
| POST `/customers/imports/{id}/cancel` | Cancelacion cooperativa. |
| GET `/customers/imports/template` | Plantilla CSV con headers y dos filas ejemplo. |

#### 25.10.3 Reglas de procesamiento

- chunking de 500 filas, transaccion por chunk: un chunk malo no aborta el job;
- hard limit 10 000 filas por job (config `CustomerImport:MaxRows`);
- hard limit 10 MB por archivo (config `CustomerImport:MaxFileBytes`);
- un solo job activo por tenant garantizado a nivel BD por
  `UX_CustomerImportAttempts_Tenant_Active`;
- idempotency key obligatoria estilo Stripe; replay devuelve 200 con el job
  ganador en lugar de duplicar;
- catalogos cerrados: si `OccupationName` o `PrincipalBusinessActivityCode`
  no existe, la fila se marca `Failed` con `Catalog.UnknownOccupation` o
  `Catalog.UnknownNaics`; no se contamina el catalogo curado;
- cifrado obligatorio: todo SSN/ITIN/EIN pasa por `ISensitiveDataProtector`
  antes de tocar la BD;
- spouse del archivo se crea como `CustomerRelation` con
  `RelationshipKind=Spouse` y `Purposes=TaxHouseholdMember`;
- cancelacion cooperativa: el worker chequea estado antes de cada chunk;
- las mutaciones de rows pasan siempre por el aggregate via `RecordSuccess`,
  `RecordFailed`, `RecordSkipped` o `RecordUpdated`; el handler nunca toca
  `CustomerImportRow` directamente.

#### 25.10.4 Estrategias de duplicado

- `Skip` (default): mantiene el existente, marca la fila como Skipped.
- `Merge`: solo completa campos vacios del existente; no pisa lo que ya hay.
- `Overwrite`: reemplaza preferencias, telefono, email y fiscal profile.

#### 25.10.5 Deteccion de duplicados

El detector de BD ejecuta una sola query SQL por chunk que matchea por
prioridad descendente:

| Prioridad | Senal | Tipo de match |
| --- | --- | --- |
| 1 | SSN/EIN blind index (HMAC por tenant) | Hard |
| 2 | Email normalizado | Hard |
| 3 | Phone E.164 | High |
| 4 | Nombre normalizado + DOB (solo Individual) | High |

El blind index permite deduplicar sin descifrar SSN/EIN. Cumple la regla del
PDF: identificadores fiscales prohibidos en queries y logs.

Dedup intra-chunk paralelo en memoria del worker (cuatro HashSets) cubre las
mismas cuatro senales para filas que se duplican entre si dentro del mismo
chunk. Codigo de error unificado `Import.DuplicateInChunk` con mensaje segun
la senal que disparo.

#### 25.10.6 Normalizacion de telefono en el boundary

`IdentifierNormalizer.NormalizePhoneToE164()` acepta formatos humanos del
operador (`(305) 555-1234`, `305-555-1234`, `3055551234`, `+13055551234`) y
los normaliza a E.164 antes de pasar al VO `PhoneNumber`. El VO sigue siendo
estricto para POST directos via `/customers`; el import es el unico boundary
amigable con formatos sucios. La plantilla descargable muestra siempre el
formato canonico E.164.

#### 25.10.7 Concurrencia

- POST concurrente con misma idempotency key: el segundo recibe el job del
  primero como replay 200, no error;
- POST concurrente sin colision de key pero con job activo del tenant: el
  segundo recibe 409 `Import.AlreadyRunning` limpio;
- ambos casos los maneja `StartCustomerImportHandler` con
  `try/catch (ConflictException)` y refetch del attempt ganador.
  `CustomerDbContext.SaveChangesAsync` ya convierte SQL 2601/2627 a
  `ConflictException`; Application no referencia `Microsoft.Data.SqlClient`.

#### 25.10.8 Evento publicado

`CustomersBulkImportedIntegrationEvent` (V1) publicado al completar el job.
Un solo evento batched, alineado con "lotes acotados" del PDF. Payload:

- `ImportJobId`, `CreatedByUserId`, `CompletedAtUtc`;
- `TotalRows`, `SuccessCount`, `UpdatedCount`, `SkippedCount`, `FailedCount`;
- `CreatedCustomerIds: Guid[]` y `UpdatedCustomerIds: Guid[]`.

Sin nombres, sin SSN, sin emails. Consumidores que necesitan datos hacen GET
`/customers/{id}` o se suscriben a `CustomerCreatedV1` individual.

#### 25.10.9 Reporte descargable

GET `/customers/imports/{id}/report?format=csv` streamea filas de
`CustomerImportRows` sin cargar todo en memoria. Cada fila trae numero,
status, ResultingCustomerId, DisplayName, MatchedBy, ErrorCode y Message.
Trail de auditoria reutilizable para compliance IRS Pub 4557.

#### 25.10.10 Plantilla CSV

GET `/customers/imports/template` devuelve un CSV con headers en el orden que
espera el parser y dos filas de ejemplo (Individual y Business) que el
operador puede usar como molde. Telefonos en formato E.164.

#### 25.10.11 Cleanup automatico

`CustomerImportCleanupHostedService` corre como `BackgroundService` diario.
Purga attempts terminales con `CreatedAtUtc < UtcNow - 90 dias` y sus filas
y archivos asociados. Configurable via `CustomerImport:ReportRetentionDays`.

El binario del archivo en `CustomerImportFiles` se borra inmediatamente al
terminar el job; el cleanup de 90 dias solo aplica a attempts huerfanos
o a la auditoria del reporte.

### 25.11 Wolverine y observabilidad

Configuracion identica a Auth y Tenant:

- `PersistMessagesWithSqlServer` y `UseDurableOutboxOnAllSendingEndpoints`
  sobre `TaxVision_Customer`;
- `UseEntityFrameworkCoreTransactions` con `CustomerDbContext` e `IUnitOfWork`;
- politica de retry con cooldown 1s, 5s, 15s;
- health checks `sql-server` y `rabbitmq` etiquetados como `ready`;
- OpenTelemetry y Serilog desde los BuildingBlocks compartidos con servicio
  `customer-service`.

### 25.12 User Secrets requeridos

Para `TaxVision.Customer.Api`:

```powershell
dotnet user-secrets set "ConnectionStrings:Default" "<CUSTOMER_CONNECTION>" `
  --project src\Services\Customer\TaxVision.Customer.Api
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" `
  --project src\Services\Customer\TaxVision.Customer.Api
dotnet user-secrets set "RabbitMq:Uri" "amqp://taxvision:<password-url-encoded>@localhost:5672" `
  --project src\Services\Customer\TaxVision.Customer.Api
dotnet user-secrets set "Jwt:Secret" "<SAME_SECRET>" `
  --project src\Services\Customer\TaxVision.Customer.Api
dotnet user-secrets set "Encryption:MasterKey" "<BASE64_32_BYTES>" `
  --project src\Services\Customer\TaxVision.Customer.Api
```

`Encryption:MasterKey` debe ser exactamente 32 bytes en base64. Generar con:

```powershell
$bytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes)
```

Variables opcionales del flujo de bulk import con defaults sanos:

```powershell
dotnet user-secrets set "CustomerImport:MaxFileBytes" "10485760" `
  --project src\Services\Customer\TaxVision.Customer.Api
dotnet user-secrets set "CustomerImport:MaxRows" "10000" `
  --project src\Services\Customer\TaxVision.Customer.Api
dotnet user-secrets set "CustomerImport:ReportRetentionDays" "90" `
  --project src\Services\Customer\TaxVision.Customer.Api
```

En produccion el master key va a Key Vault o equivalente; nunca al repo ni al
`.env`.

### 25.13 Gateway

Ruta YARP `/customers/{**catch-all}` enrutada al cluster `customer` en
`http://localhost:5263/`. Health check `customer-api` agregado al endpoint
`/health/ready` del Gateway.

### 25.14 Aplicar migraciones

```powershell
dotnet ef database update `
  --project src\Services\Customer\TaxVision.Customer.Infrastructure\TaxVision.Customer.Infrastructure.csproj `
  --startup-project src\Services\Customer\TaxVision.Customer.Api\TaxVision.Customer.Api.csproj
```

### 25.15 Pendientes reales

- Proyeccion local de Customer en Auth para validar `CustomerId` al crear
  invitaciones `CustomerPortal`. Diseno definido en la guia PDF; implementacion
  no iniciada.
- Eventos `CustomerEmailChangedIntegrationEvent` y
  `CustomerPhoneChangedIntegrationEvent` con detector de cambios en el handler
  de update.
- Asignacion Customer-preparer (`AssignedToUserId` o entity hija).
- Segundo evento `CustomerPortalInvitationCreated` con token raw para que
  Email Service envie el correo cuando exista.
- Notificacion push al frontend cuando completa el job de bulk import. Hoy
  solo polling; cuando exista RealTime Service, agregar SignalR.
- Migracion de `CustomerImportFiles` a CloudStorage Service. Hoy el archivo
  vive en BD; cuando exista el microservicio, reemplazar `IImportFileStore`
  por un fileId externo y borrar la tabla local.
- Notificacion por email al operador cuando completa: requiere Email Service.
- Pruebas automatizadas siguiendo la guia general del proyecto.

### 25.16 Operaciones de status, CRUD granular y bulk

Endpoints anadidos despues de la implementacion inicial del Paso 7 del PDF. Cubren
operaciones que el aggregate ya soportaba pero no estaban expuestas, mas operaciones
estandar para una oficina de impuestos real (correcciones, pausa estacional, fin de
campana).

#### 25.16.1 Status del customer

El enum `CustomerStatus` define tres estados explicitos:

- `Active`: customer operativo, aparece en listados por default.
- `Inactive`: pausado (no engaged este ciclo fiscal). Sigue editable; no aparece en
  listados con filtro default.
- `Archived`: removido de flujo operativo, requiere admin para reactivar. El aggregate
  bloquea modificaciones (`EnsureActive()`).

Transiciones expuestas como endpoints individuales. El aggregate valida la transicion;
si es invalida, devuelve error de dominio en lugar de throw.

| Verbo y ruta | Actor minimo | Transicion | Error si... |
| --- | --- | --- | --- |
| POST `/customers/{id}/archive` | TenantAdmin | Any -> Archived | ya esta Archived |
| POST `/customers/{id}/reactivate` | TenantAdmin | Archived -> Active | no esta Archived |
| POST `/customers/{id}/deactivate` | TenantAdmin | Active -> Inactive | esta Archived o ya Inactive |
| POST `/customers/{id}/activate` | TenantAdmin | Inactive -> Active | esta Archived o ya Active |

Cada transicion publica su propio evento de integracion:

- `CustomerArchivedIntegrationEvent`
- `CustomerReactivatedIntegrationEvent`
- `CustomerActivatedIntegrationEvent`
- `CustomerDeactivatedIntegrationEvent`

Sin PII. Consumidores tipicos: proyeccion de Auth, Campaign, read models de operaciones.

#### 25.16.2 CRUD granular de child entities

El aggregate expone tres familias de child entities (Addresses, ContactPoints,
Relations) con CRUD completo. Las mutaciones siempre pasan por metodos del aggregate
root; los child entities solo tienen `Create` y `Update` internos.

| Verbo y ruta | Actor minimo |
| --- | --- |
| POST `/customers/{id}/addresses` | TenantEmployee |
| PATCH `/customers/{id}/addresses/{addressId}` | TenantEmployee |
| DELETE `/customers/{id}/addresses/{addressId}` | TenantEmployee |
| POST `/customers/{id}/contact-points` | TenantEmployee |
| PATCH `/customers/{id}/contact-points/{contactPointId}` | TenantEmployee |
| DELETE `/customers/{id}/contact-points/{contactPointId}` | TenantEmployee |
| POST `/customers/{id}/relations` | TenantEmployee |
| PATCH `/customers/{id}/relations/{relationId}` | TenantEmployee |
| DELETE `/customers/{id}/relations/{relationId}` | TenantEmployee |

Reglas validadas por el aggregate:

- una sola direccion primary por `AddressKind`;
- un solo contact point primary por `Type`;
- no se permite duplicar `(Type, NormalizedValue)` en contact points;
- al eliminar una relacion con fiscal profile, EF borra el
  `CustomerRelationFiscalProfile` por cascade configurada en el modelo.

Las operaciones CRUD de children no publican eventos de integracion. Si en el futuro
algun consumidor lo necesita, se agrega via patron `CustomerUpdatedIntegrationEvent`
con campo `ChangedFields`, no eventos por child entity.

#### 25.16.3 Listado con filtro por status y paginacion

GET `/customers` ahora devuelve `PagedResult<CustomerSummaryResponse>` con metadatos
de paginacion. Acepta filtro de status via query param.

| Query param | Valores | Default |
| --- | --- | --- |
| `term` | texto libre (matchea DisplayName o email normalizado) | null |
| `status` | `Active`, `Inactive`, `Archived`, `NotArchived`, `All` | `Active` |
| `page` | entero >= 1 | 1 |
| `size` | entero >= 1 | 20 |

Respuesta:

```json
{
  "items": [ { "id": "...", "displayName": "...", "status": "Active", ... } ],
  "page": 1,
  "size": 20,
  "totalCount": 254,
  "totalPages": 13,
  "hasMore": true,
  "hasPrevious": false
}
```

`PagedResult<T>` esta definido en `BuildingBlocks` para reuso en otros servicios.

#### 25.16.4 Check-exists preflight

GET `/customers/check-exists` permite al frontend validar duplicados antes de
submit. Tenant-scoped: dos tenants pueden tener el mismo email o SSN sin colisionar.

| Query param | Notas |
| --- | --- |
| `email` | normalizado a lowercase trim antes del lookup |
| `taxIdentifier` | normalizado a solo digitos; busca via blind index HMAC por tenant; nunca descifra |

Al menos uno de los dos es requerido (400 si ninguno).

Respuesta:

```json
{
  "emailExists": true,
  "taxIdentifierExists": false,
  "existingCustomerId": "6d005cbb-..."
}
```

`existingCustomerId` es el id del primer match encontrado entre las dos senales; util
para que el UI sugiera "abrir el customer existente" en lugar de crear duplicado.

#### 25.16.5 Bulk status operations

POST `/customers/bulk/{action}` con `action` en `archive`, `reactivate`, `activate`,
`deactivate`. Util para fin de campana fiscal (archivar 100 customers que ya filtraron)
o reapertura de temporada (reactivar masivamente).

| Limite | Valor |
| --- | --- |
| Maximo customers por call | 100 |
| Modo | sincrono (>100 devuelve 400 `Bulk.TooMany`) |
| Autorizacion | TenantAdmin |

Body:

```json
{
  "customerIds": [ "guid1", "guid2", "..." ],
  "reason": "End of season cleanup"
}
```

Respuesta:

```json
{
  "totalRequested": 100,
  "succeeded": 97,
  "failed": 3,
  "failures": [
    { "customerId": "...", "errorCode": "Customer.AlreadyArchived", "message": "..." }
  ]
}
```

Comportamiento:

- valida ownership por tenant en cada customer antes de mutar;
- itera secuencialmente aplicando la transicion del aggregate;
- ids invalidos o de otro tenant se reportan en `failures` pero no abortan el batch;
- publica un evento individual por customer exitoso (consistente con las APIs
  single-customer). Si el volumen crece, se migrara al patron batched
  estilo `CustomersBulkImportedV1`.

Cuando se necesite procesar mas de 100 customers a la vez, debe usarse el flujo
async de bulk import (seccion 25.10) que ya esta diseñado para eso.

#### 25.16.6 Resumen de cambios

- 4 endpoints status: archive, reactivate, deactivate, activate;
- 6 endpoints CRUD granular de children (PATCH y DELETE para Address, ContactPoint, Relation);
- 1 endpoint check-exists preflight;
- 4 acciones bulk: archive, reactivate, deactivate, activate;
- 3 eventos nuevos: Reactivated, Activated, Deactivated
---

# 26. Iteracion 02-07-2026: Auth avanzado, Subscription, Notification y correcciones Customer

Esta seccion documenta de forma detallada el trabajo incorporado en la iteracion
del 2 de julio de 2026. Complementa (no reemplaza) las secciones anteriores. El
objetivo fue completar la seguridad del microservicio **Auth** para un SaaS real
de oficinas de taxes en EE. UU., levantar los microservicios **Subscription** y
**Notification** que estaban vacios, y corregir fugas multi-tenant detectadas en
**Customer**. Todo respeta la arquitectura existente: Clean Architecture por
capas, CQRS con handlers estaticos de Wolverine, patron Result, multi-tenancy con
`TenantEntity`/`X-Tenant-Id`, outbox/inbox durable sobre SQL Server, eventos por
el exchange `taxvision-events`, `CorrelationId` de extremo a extremo y
observabilidad OTEL.

### Diagrama del flujo de una peticion (actualizado)

El siguiente diagrama muestra el recorrido de una peticion a traves del Gateway y
el pipeline de BuildingBlocks hasta el handler CQRS, la persistencia transaccional
con outbox y la propagacion de eventos por RabbitMQ hacia los microservicios
(incluidos los nuevos Subscription y Notification):

![Flujo de una peticion en TaxVision](Flujo_Peticion_BuildingBlocks_TaxVision.svg)

## 26.1 Resumen ejecutivo de la iteracion

| Area | Antes | Despues |
|---|---|---|
| Auth | Login, refresh basico, invitaciones, bootstrap | + Sesiones con reuse detection, MFA (TOTP/OTP/recovery/device trust), RBAC granular, recuperacion/cambio de contrasena, verificacion email/telefono, lockout, auditoria de seguridad, limites por plan, denylist Redis, RS256/JWKS dual, gestion de usuarios y queries de lectura |
| Subscription | Carpeta vacia | Microservicio completo: planes sembrados, suscripcion trial por tenant, upgrade/downgrade, compra de asientos, suspension/cancelacion, eventos de limites hacia Auth |
| Notification | Carpeta vacia | Microservicio completo: consumers de eventos de Auth, plantillas de correo en espanol, envio SMTP, historial por tenant |
| Customer | Fugas cross-tenant en queries y comandos | Filtro de `TenantId` en GetById y Search, validacion de tenant en comandos, `TenantResolutionMiddleware` registrado |
| Gateway | Rutas auth/tenant/customer | + Rutas plans/subscriptions/notifications, CORS explicito, cabeceras de seguridad |

## 26.2 Microservicio Auth: lo implementado

Todo el codigo nuevo respeta el patron del servicio: entidades de dominio con
constructor privado y factory estatica que devuelve `Result<T>`, handlers
estaticos de Wolverine, repositorios detras de interfaces en `Application/
Abstractions`, e implementaciones EF en `Infrastructure`.

### 26.2.1 Sesiones y refresh tokens con deteccion de reuso

- Nueva entidad `UserSession` (`Domain/Sessions/UserSession.cs`): representa una
  sesion = dispositivo + familia de refresh tokens, con IP, user-agent, ultima
  actividad y revocacion.
- `RefreshToken` migrado de `BaseEntity` a `TenantEntity` (corrige la deuda #1 de
  la auditoria): ahora guarda `TenantId`, `SessionId`, `ReplacedByTokenId` y
  `RevokedReason`, habilitando la cadena de rotacion.
- **Reuse detection**: si se presenta un refresh token ya rotado/revocado, se
  interpreta como robo, se revoca la **sesion completa**, se registra en auditoria
  y se publica `SecurityAlertIntegrationEvent`. Esto convierte la rotacion
  (antes cosmetica) en una defensa real.
- Revocaciones masivas: por sesion, por usuario (cambio de contrasena,
  desactivacion) y por tenant (suspension). El consumer
  `TenantStatusChangedConsumer` ahora corta todas las sesiones al suspender un
  tenant.

### 26.2.2 MFA (autenticacion multifactor)

- Entidades: `MfaMethod`, `MfaChallenge`, `RecoveryCode`, `TrustedDevice`,
  `TenantMfaPolicy` (carpeta `Domain/Mfa`).
- **TOTP** (RFC 6238) implementado a mano en `Infrastructure/Security/
  TotpService.cs`, sin dependencias externas, compatible con Google/Microsoft
  Authenticator, Authy y 1Password.
- Secretos TOTP cifrados en reposo con **AES-256-GCM** (`AesGcmSecretProtector`),
  clave `Mfa:EncryptionKey` (deriva del `Jwt:Secret` como fallback de desarrollo).
- **OTP por email/SMS**: se generan codigos numericos y se publican via
  `MfaChallengeRequestedIntegrationEvent` hacia Notification.
- **Recovery codes** (10 por usuario, un solo uso, hasheados) y **device trust**
  ("recordar este dispositivo" durante N dias segun politica del tenant).
- **Login en dos pasos**: el paso 1 (`/auth/login`) devuelve un `loginTicket`
  cuando se requiere MFA; el paso 2 (`/auth/mfa/verify`) valida el codigo y emite
  tokens. Si la politica exige MFA y el usuario aun no lo configuro, el login
  responde `mfaSetupRequired: true` para que el frontend fuerce el enrolamiento.
- Politica por tenant: MFA obligatorio para administradores por diseno (no
  desactivable), opcional para empleados y portal.

### 26.2.3 RBAC granular (roles y permisos)

- Entidades: `Role`, `Permission`, `RolePermission`, `UserRole` (carpeta
  `Domain/Roles`), con un catalogo de 22 permisos (`PermissionCatalog`) sembrado
  por migracion con GUID fijos.
- Permisos operativos internos (usuarios, roles, clientes, firmas, documentos,
  correo, campanas, reportes) **y de cara al portal del cliente final**
  (llamadas, modulo de millas, folders visibles, firma de documentos).
- Al crear un tenant se siembran los roles de sistema (`Tenant Admin`,
  `Employee`, `Customer Portal`) con sus permisos por defecto.
- El JWT ahora incluye los claims `perm` (permisos efectivos) y `perm_v`
  (version de permisos para invalidacion). La autorizacion por permiso se aplica
  con el atributo `[HasPermission("...")]` y un `PermissionPolicyProvider`.

### 26.2.4 Credenciales, verificacion y anti-fuerza bruta

- Recuperacion de contrasena (`/auth/password/forgot` y `/reset`), cambio
  autenticado (`/auth/password/change`), con revocacion de sesiones al cambiar.
- Verificacion de cambio de email (enlace a la direccion nueva + aviso a la
  anterior) y verificacion de telefono por OTP.
- Politica de contrasenas (`PasswordPolicy`) alineada con NIST 800-63B.
- **Lockout por cuenta** (10 intentos, en `User`) mas throttle por IP en Redis
  (`LoginThrottler`), y respuesta unificada anti-enumeracion en el login.

### 26.2.5 Auditoria, JWT endurecido y limites de plan

- `AuthAuditLog` (append-only) registra todos los eventos de seguridad (login,
  fallos, MFA, revocaciones, cambios de rol, invitaciones); consultable en
  `/auth/audit` con permiso `audit.view`.
- JWT: se anaden `jti`, `sid`, `amr`, `iat`; migracion a **RS256/JWKS en modo
  dual** (`SigningKeyProvider` + endpoint `/auth/.well-known/jwks.json`), con
  fallback a HS256 para no romper la configuracion actual.
- **Denylist en Redis** por `sid` (`AccessTokenDenylist` +
  `SessionDenylistMiddleware`) para revocar access tokens vigentes de inmediato.
- Proyeccion `TenantPlanLimits` alimentada por eventos de Subscription; las
  invitaciones y la reactivacion de usuarios validan asientos disponibles
  (`PlanGuard`).

### 26.2.6 Gestion de usuarios y queries

- Comandos: desactivar/reactivar usuario, actualizar perfil (incluida zona
  horaria propia con herencia del tenant), asignar roles.
- Queries de lectura que antes no existian: `GetMe`, listado y detalle de
  usuarios, sesiones activas, estado MFA, auditoria y limites del plan.

## 26.3 Microservicio Subscription (nuevo)

Ruta: `src/Services/Subscription`. Cuatro proyectos (Domain, Application,
Infrastructure, Api) siguiendo el patron de Tenant/Auth.

- **Dominio**: `Plan` (catalogo sembrado: Starter 49 USD/3 usuarios, Pro
  129 USD/10, Enterprise 299 USD/25, con modulos habilitados por plan) y
  `TenantSubscription` (estados Trial/Active/Suspended/Cancelled, asientos extra,
  periodos).
- **Flujo de alta**: al crear un tenant, el consumer `TenantCreatedConsumer` crea
  la suscripcion en periodo de prueba (14 dias) con el plan por defecto y publica
  `SubscriptionActivatedIntegrationEvent` con los limites, que Auth proyecta.
- **Operaciones**: upgrade/downgrade de plan, compra de asientos, cancelacion por
  el tenant, suspension/reactivacion administrativa (impago). Cada operacion
  publica el evento correspondiente para que Auth actualice los limites.
- **API**: `GET /plans` (publico, para la landing), `GET /subscriptions/me`,
  `change-plan`, `seats`, `cancel`, y `suspend`/`reactivate` (PlatformAdmin).
- La factoria de eventos (`SubscriptionEventFactory`) es el unico punto donde se
  calculan los limites efectivos (plan + asientos extra), evitando duplicacion.

## 26.4 Microservicio Notification (nuevo)

Ruta: `src/Services/Notification`. Cierra el circuito: los eventos que Auth ya
publicaba ahora se entregan al usuario final.

- **Consumers**: invitaciones (empleados, admins y portal cliente), recuperacion
  de contrasena, OTP (login MFA y verificacion de telefono), cambio de email y
  alertas de seguridad. Todos hacen `correlation.Push()` del `CorrelationId` del
  evento, de modo que una invitacion se traza de punta a punta
  (Customer/Tenant -> Auth -> Notification) con el mismo id en Loki/Tempo.
- **Plantillas** de correo en espanol (`EmailTemplates`), HTML con enlaces
  construidos desde `Portal:BaseUrl` y codificacion HTML de los valores de
  usuario para evitar inyeccion.
- **Envio SMTP** con `System.Net.Mail` (sustituible por MailKit/SendGrid detras
  de `IEmailSender`). Sin `Smtp:Host` configurado opera en modo desarrollo:
  registra el envio en el log sin exponer tokens.
- **SMS** provisional (`LoggingSmsSender`, enmascara el numero) hasta integrar un
  proveedor tipo Twilio/SNS.
- **Historial** `NotificationLogs` por tenant (nunca persiste el cuerpo, que
  contiene tokens), consultable en `GET /notifications`.

## 26.5 Correcciones en Customer

Se audito el microservicio Customer y se corrigieron fugas de aislamiento
multi-tenant (riesgo de acceso a PII de otros tenants):

- **Fuga en `GET /customers/{id}`**: `GetCustomerByIdQuery` no llevaba `TenantId`
  y el read service no filtraba; ahora recibe y filtra por el tenant del
  solicitante.
- **Fuga en `GET /customers` (search)**: `SearchCustomersQuery` y
  `CustomerReadService.SearchAsync` no filtraban por tenant; devolvian clientes de
  toda la plataforma. Corregido con filtro obligatorio `Where(c => c.TenantId ==
  tenantId)`.
- **Validacion de tenant** anadida en los comandos `Update`, `Archive`,
  `AddAddress`, `AddContactPoint` y `AddRelation` (antes cargaban el customer sin
  comparar `TenantId`).
- Registrado `TenantResolutionMiddleware` en el pipeline de Customer, consistente
  con el resto de la plataforma.
- Corregido el consumer `CustomerPortalInvitationRequestedConsumer`: ya no
  registra el token de invitacion en claro en los logs; ahora publica
  `InvitationCreatedIntegrationEvent` para que Notification envie el correo.

## 26.6 Cambios en Gateway y BuildingBlocks

- Gateway: rutas YARP nuevas para `/plans`, `/subscriptions` y `/notifications`;
  politica CORS explicita (`Cors:Origins`) y cabeceras de seguridad
  (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, HSTS).
- BuildingBlocks/Messaging: nuevos contratos de eventos en
  `AuthIntegrationEvents/` (7 eventos) y `SubscriptionIntegrationEvents/`
  (4 eventos).
- `ErrorHttpMapping` ampliado con los nuevos codigos de error (429 para lockout y
  throttle, 409 para limites de plan, etc.).
- `JwtAuthenticationRegistration` con validacion dual HS256/RS256.

## 26.7 Cambios de contrato (breaking changes)

Al integrar el frontend o actualizar la coleccion Postman, tener en cuenta:

1. **`POST /auth/login`** ya no devuelve `{accessToken, refreshToken}` planos.
   Ahora devuelve un objeto con `mfaRequired`, `mfaSetupRequired`, `tokens`
   (`tokens.accessToken`, `tokens.refreshToken`, `tokens.expiresInSeconds`),
   `loginTicket` y `mfaMethods`.
2. **`POST /auth/refresh`** devuelve `{accessToken, refreshToken,
   expiresInSeconds, deviceToken}` sin envoltura.
3. **`POST /auth/invitations`** ya no devuelve el token en claro salvo que
   `Invitations:ReturnRawToken=true` (solo desarrollo). En produccion el token
   viaja por el evento hacia Notification.
4. Los refresh tokens emitidos antes de la migracion de Auth quedan invalidos (no
   tienen `SessionId`): los usuarios existentes deben re-loguear una vez.

## 26.8 Bases de datos, migraciones y despliegue

- Cada microservicio mantiene su propia base: `TaxVision_Auth`,
  `TaxVision_Tenants`, `TaxVision_Customers`, `TaxVision_Subscriptions`,
  `TaxVision_Notifications`.
- Se anadio un `IDesignTimeDbContextFactory` por servicio para que `dotnet ef`
  pueda crear/aplicar migraciones sin levantar el host (JWT/RabbitMQ), tomando la
  cadena de `--connection` o de `ConnectionStrings__Default`.
- El paquete `Microsoft.EntityFrameworkCore.Design` incluye ahora el asset
  `compile` en los proyectos Infrastructure (necesario para que el tipo
  `IDesignTimeDbContextFactory` sea visible en compilacion).
- Migraciones de esta iteracion:
  - Auth: `AddSecurityRbacMfaSessionsAndPlanLimits` (sesiones, MFA, RBAC con seed
    de permisos, credenciales, auditoria, limites, campos nuevos en `Users`).
  - Subscription: `InitialSubscription` (incluye seed de los 3 planes).
  - Notification: `InitialNotification`.
- Docker Compose: se anadieron los servicios `customer-api`, `subscription-api` y
  `notification-api`, con sus variables (`*_DB_CONNECTION`, `Portal__BaseUrl`,
  `Smtp__*`) en el `.env`. Se incluye el wrapper
  `deploy/docker/compose.ps1` que fuerza siempre el `.env` de la raiz.

### 26.8.1 Comandos de puesta en marcha

```powershell
# 1. Compilar
dotnet build TaxVision.slnx

# 2. Migraciones (una por servicio; ejemplo Auth)
dotnet ef database update `
  --project src/Services/Auth/Infrastructure/TaxVision.Auth.Infrastructure.csproj `
  --startup-project src/Services/Auth/Api/TaxVision.Auth.Api.csproj `
  --connection "Server=localhost,1433;Database=TaxVision_Auth;User Id=sa;Password=<clave>;TrustServerCertificate=True"
# (repetir para TaxVision_Tenants, TaxVision_Customers, TaxVision_Subscriptions, TaxVision_Notifications)

# 3. Levantar el stack
.\deploy\docker\compose.ps1 up -d --build
.\deploy\docker\compose.ps1 ps
```

## 26.9 Prueba de humo end-to-end

```powershell
# 1. Crear tenant (dispara suscripcion trial + invitacion + limites)
$tenant = Invoke-RestMethod -Method Post -Uri http://localhost:5047/tenants -ContentType application/json -Body (@{
  name = "Oficina Demo"; subdomain = "demo1"; adminEmail = "admin@demo.com"; defaultTimeZoneId = "America/New_York"
} | ConvertTo-Json)

# 2. Aceptar invitacion y login
Invoke-RestMethod -Method Post -Uri http://localhost:5047/auth/invitations/accept -ContentType application/json -Body (@{
  invitationToken = $tenant.adminActivationToken; name = "Admin"; lastName = "Demo"; password = "MiClaveSegura2026!"
} | ConvertTo-Json)
$login = Invoke-RestMethod -Method Post -Uri http://localhost:5047/auth/login -ContentType application/json -Body (@{
  tenantId = $tenant.id; email = "admin@demo.com"; password = "MiClaveSegura2026!"
} | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($login.tokens.accessToken)" }

# 3. Identidad, plan y limites (los 3 servicios juntos)
Invoke-RestMethod http://localhost:5047/auth/me -Headers $headers
Invoke-RestMethod http://localhost:5047/subscriptions/me -Headers $headers
Invoke-RestMethod http://localhost:5047/auth/tenants/limits -Headers $headers
```

## 26.10 Documentacion del codigo

Todo el codigo nuevo de esta iteracion incluye comentarios XML (`/// <summary>`)
en espanol a nivel de clase, interfaz, record y en los metodos publicos cuyo
proposito no es evidente por el nombre. Las entidades de dominio documentan sus
invariantes y factories; los handlers, su responsabilidad y efectos (eventos
publicados, revocaciones); los servicios de seguridad, sus garantias
criptograficas.

## 26.11 Trabajo pendiente (siguiente iteracion)

- Ampliar las pruebas existentes con integracion de endpoints mediante
  Testcontainers y escenarios E2E.
- Microservicio Billing (facturas, pagos, metodos de pago), que ya puede
  engancharse a los contratos de Subscription.
- Actualizar la coleccion Postman al nuevo formato de login.
- Refactor SOLID del `RunCustomerImportHandler` (responsabilidad unica).
- Activar RS256/JWKS en produccion (generar par de claves y distribuir la publica).

# 27. CloudStorage / Media Security Gateway

`CloudStorage` es el unico microservicio autorizado para poseer bytes de archivos.
Los demas bounded contexts conservan solamente referencias `FileId`; no guardan
archivos en filesystem local ni acceden directamente a MinIO.

La implementacion sigue la arquitectura del resto de TaxVision:

- Domain, Application, Infrastructure y Api separados;
- CQRS y handlers descubiertos por Wolverine;
- SQL Server para metadata, cuota, estado y auditoria;
- outbox/inbox durable y eventos sobre `taxvision-events`;
- JWT, roles y permisos emitidos por Auth;
- aislamiento por `TenantId` en consultas y claves de objetos;
- MinIO para almacenamiento y ClamAV para antivirus.

## 27.1 Flujo seguro de subida

1. `POST /storage/files/uploads` valida permiso, tenant, owner, folder fiscal,
   extension, MIME declarado, limite por archivo y cuota disponible.
2. La cuota se reserva usando `UsedBytes + ReservedBytes` y concurrencia
   optimista mediante `RowVersion`.
3. CloudStorage devuelve una politica POST de MinIO con cinco minutos de vida,
   `ObjectKey`, MIME y tamano exacto. No entrega credenciales de MinIO.
4. El cliente envia el archivo directamente al bucket `taxvision-temp` usando la
   URL y los campos de formulario recibidos.
5. `POST /storage/files/{fileId}/complete` comprueba que el objeto exista y que
   su tamano real coincida con el declarado.
6. Un mensaje durable `ScanFileCommand` calcula SHA-256, detecta MIME por magic
   bytes, compara extension y contenido, limita expansion/ratio de ZIP y envia el
   stream a ClamAV.
7. Si esta limpio, se copia al bucket versionado `taxvision-storage`, cambia a
   `Available`, la reserva pasa a uso real y se publica
   `FileAvailableIntegrationEvent`.
8. Si esta infectado, se mueve a `taxvision-quarantine`, se libera la reserva,
   se registra el incidente y se publica
   `FileInfectedDetectedIntegrationEvent`.
9. Una reserva que no se completa expira a las 24 horas: se elimina el objeto
   temporal, se libera cuota y se registra auditoria.
10. Solo un archivo `Available` puede recibir una URL temporal de descarga.

## 27.2 Comunicacion con los demas microservicios

| Origen | Destino | Canal | Accion implementada |
| --- | --- | --- | --- |
| Cliente web/movil | Gateway -> CloudStorage | HTTPS/REST `/storage/*` | Iniciar/completar upload, consultar, listar, descargar, eliminar, uso y auditoria |
| Cliente web/movil | MinIO | HTTPS con POST policy temporal | Subir exactamente el objeto autorizado, sin conocer credenciales |
| Auth | Gateway y CloudStorage | JWT firmado | Propaga `tenant_id`, `sub`, `actor_type`, `customer_id`, roles y claims `perm` |
| Subscription | CloudStorage | RabbitMQ/Wolverine | `SubscriptionActivated`, `SubscriptionPlanChanged` y `SubscriptionSuspended` actualizan `TenantStorageLimits` |
| CloudStorage | Modulos consumidores | RabbitMQ/Wolverine | Publica archivo disponible, archivo infectado, eliminacion, cuota excedida y accesos auditados |
| CloudStorage | Notification | RabbitMQ/Wolverine | Notification registra alertas in-app para malware y cuota excedida |
| CloudStorage | Audit futuro | RabbitMQ/Wolverine | Publica `FileAccessAuditedIntegrationEvent`; la auditoria local ya se persiste aunque el Audit Service global aun no existe |
| Gateway | CloudStorage | Health HTTP | Incluye `cloudstorage-api` en readiness |

Los modulos Media Manager, Signature, Communication, Email, Billing e IRS deben
guardar unicamente el `FileId` y proyectar la metadata que necesiten al consumir
los eventos. Para archivos seleccionados por un usuario utilizan el flujo REST +
POST policy anterior. La integracion de archivos generados exclusivamente en
backend mediante un comando de integracion todavia no esta implementada; no se
debe introducir acceso directo de esos servicios a MinIO como atajo.

## 27.3 Acciones, roles y permisos

Auth es la unica fuente de verdad de RBAC. CloudStorage no posee tablas propias de
usuarios, sesiones, roles o permisos. Auth incluye el catalogo `cloudstorage.*`,
lo incorpora al JWT como claims `perm` y permite asignarlo a roles administrados
por el tenant.

| Accion | Endpoint | Permiso requerido | Roles de sistema por defecto |
| --- | --- | --- | --- |
| Ver metadata/listar | `GET /storage/files*` | `cloudstorage.file.view` | Tenant Admin, Employee, Customer Portal |
| Iniciar/completar subida | `POST /storage/files/uploads`, `POST .../complete` | `cloudstorage.file.upload` | Tenant Admin, Employee, Customer Portal |
| Emitir URL de descarga | `POST /storage/files/{id}/download-url` | `cloudstorage.file.download` | Tenant Admin, Employee, Customer Portal |
| Soft-delete | `DELETE /storage/files/{id}` | `cloudstorage.file.delete` | Tenant Admin |
| Ver uso/cuota | `GET /storage/usage` | `cloudstorage.settings.manage` | Tenant Admin |
| Consultar auditoria | `GET /storage/audit` | `cloudstorage.audit.view` | Tenant Admin |
| Gestion futura de politicas | Sin endpoint todavia | `cloudstorage.settings.manage` | Tenant Admin |

La autorizacion tiene varias barreras acumulativas:

1. **JWT valido**: identidad emitida por Auth.
2. **Permiso efectivo**: policy ASP.NET exige el claim `perm` exacto.
3. **Tenant**: toda lectura y escritura usa el `tenant_id` autenticado; nunca un
   tenant enviado libremente por el cliente.
4. **Owner para Customer Portal**: aunque posea `view/upload/download`, solamente
   puede crear, listar, consultar, completar y descargar archivos cuyo
   `OwnerType=Customer` y `OwnerId` coincidan con su claim `customer_id`.
5. **Reglas del plan**: cuota total, suspension, tamano maximo, extensiones y MIME
   permitidos.
6. **Estado de seguridad**: no existe descarga mientras el archivo no sea
   `Available`.
7. **Rate limit**: Gateway limita iniciaciones/completados por tenant.

Por tanto, las acciones si son parametrizables mediante roles: los codigos del
catalogo son fijos y auditables, pero Auth permite crear roles y cambiar sus
asignaciones de permisos. Las reglas de almacenamiento por plan se configuran en
`CloudStorage:PlanPolicies` y la cuota total se proyecta desde Subscription.

## 27.4 Endpoints

- `POST /storage/files/uploads`
- `POST /storage/files/{fileId}/complete`
- `GET /storage/files/{fileId}`
- `GET /storage/files`
- `POST /storage/files/{fileId}/download-url`
- `DELETE /storage/files/{fileId}`
- `GET /storage/usage`
- `GET /storage/audit`
- `GET /health/live`
- `GET /health/ready`

## 27.5 Infraestructura y configuracion

Docker Compose incorpora MinIO, ClamAV y `cloudstorage-api`. Los secretos se
declaran en `.env` o en el secret manager del entorno; nunca se incluyen valores
reales en el repositorio.

```powershell
docker compose --env-file .env -f deploy/docker/docker-compose.yml up -d

dotnet ef database update `
  --project src/Services/CloudStorage/TaxVision.CloudStorage.Infrastructure `
  --startup-project src/Services/CloudStorage/TaxVision.CloudStorage.Api
```

Produccion debe utilizar TLS, credenciales MinIO de alcance minimo, cifrado
server-side/KMS, red privada entre servicios y backups de SQL Server y object
storage.

### Apuntar a un object storage gestionado (S3 / R2) en produccion

El servicio CloudStorage habla el protocolo S3, por lo que puede apuntar a cualquier
backend S3-compatible **sin cambios de codigo**: solo se ajustan las variables
`Minio__*`. Esto evita autohospedar MinIO (y su licencia AGPL) en produccion.

La configuracion vive en la seccion `Minio` (`MinioOptions`): `Endpoint`, `AccessKey`,
`SecretKey` y `UseTls`. Como variables de entorno se expresan con doble guion bajo.

Cloudflare R2 (funciona con el cliente tal cual, region `auto`):

```env
Minio__Endpoint=<accountid>.r2.cloudflarestorage.com
Minio__AccessKey=<r2-access-key-id>
Minio__SecretKey=<r2-secret-access-key>
Minio__UseTls=true
```

AWS S3:

```env
Minio__Endpoint=s3.amazonaws.com
Minio__AccessKey=<aws-access-key-id>
Minio__SecretKey=<aws-secret-access-key>
Minio__UseTls=true
```

Notas:

- Para AWS S3 fuera de `us-east-1`, las URLs presignadas (SigV4) requieren fijar la region;
  hoy el cliente en `DependencyInjection.AddCloudStorageInfrastructure` no expone region, asi
  que habria que agregar `.WithRegion(...)` al `MinioClient` (y un campo `Region` en
  `MinioOptions`). R2 usa `auto` y no lo necesita.
- Los buckets (`taxvision-storage`, `taxvision-temp`, `taxvision-quarantine`) deben existir o
  poder crearse; `MinioBucketBootstrapper` los provisiona al arrancar si las credenciales lo
  permiten.
- En el proveedor gestionado, conceda a la credencial solo permisos sobre esos buckets.

## 27.6 Pruebas

El proyecto se encuentra en `deploy/tests/TaxVision.CloudStorage.Tests` y cubre:

- traversal y canonicalizacion de `ObjectKey`;
- aislamiento del nombre original y prefijo por tenant;
- deteccion por magic bytes;
- rechazo de ZIP bombs;
- reserva concurrente de cuota;
- transiciones de seguridad;
- folders fiscales que requieren ano;
- expiracion de uploads abandonados;
- aislamiento por owner para Customer Portal.

# 28. Modulo de Email avanzado (Notification)

El servicio Notification se amplio con un subsistema de email completo dentro del mismo
microservicio (sin crear un `Email Service` separado): configuracion de proveedores,
plantillas, layouts, envio, campanas y sincronizacion de cuentas externas. Se respetan
las convenciones del repo: Clean Architecture, CQRS con Wolverine (sin MediatR),
Result pattern, `IUnitOfWork`, EDA con outbox/inbox, aislamiento multitenant por
`tenant_id` del JWT y secretos cifrados.

## 28.1 Modulos

| Modulo | Responsabilidad | Entidades / tablas |
| --- | --- | --- |
| Configuracion SMTP/API | Proveedor de envio global (System) o por tenant; resolucion tenant→global | `EmailProviderConfigurations` |
| Plantillas | Metadata + versionado; HTML/design/preview en CloudStorage | `EmailTemplates`, `EmailTemplateVersions` |
| Layouts | Envoltura del cuerpo (marcador `{{ body }}`), default por scope | `EmailLayouts` |
| Rendering | `ITemplateRenderer` con **Fluid** (Liquid sandboxed); auto-escape en HTML | — |
| Envio | Correos salientes + tracking + entrega asincrona por evento | `OutboundEmailMessages`, `EmailRecipients`, `EmailDeliveryLogs` |
| Campanas | Draft → programar → fan-out por cola; contadores por eventos de entrega | `EmailCampaigns`, `EmailCampaignRecipients` |
| Cuentas + Sync | Conexion Gmail/Graph/IMAP; carpetas, mensajes, hilos, adjuntos | `EmailAccountConnections`, `EmailFolders`, `EmailSyncedMessages`, `EmailMessageAttachments`, `EmailSyncLogs` |

### Decisiones de diseno relevantes

- **Motor de plantillas: Fluid** (no Scriban). Scriban 5.12.1 arrastra CVEs *high*
  conocidos; Fluid es Liquid sandboxed, seguro para plantillas escritas por usuarios.
- **Almacenamiento de contenido en CloudStorage**: la BD guarda solo metadata y storage
  keys; el HTML/design/preview de plantillas y layouts viven en CloudStorage (se agrego
  `text/html` al allowlist). Notification reenvia el bearer token del usuario al llamar a
  CloudStorage (operaciones iniciadas por request); nunca accede a MinIO ni a su BD.
- **Render en el request, envio asincrono**: las plantillas/layout se renderizan cuando
  hay token de usuario (request) y se guarda el cuerpo final; el consumer async solo
  envia por SMTP. Esto evita depender de CloudStorage en background.
- **Cifrado de secretos**: `ISecretProtector` compartido en BuildingBlocks (AES-256-GCM,
  clave `Encryption:MasterKey`). Passwords SMTP, API keys, client secrets y tokens OAuth
  se guardan cifrados y nunca se exponen en responses.
- **Sync IMAP real con MailKit**; Gmail API y Microsoft Graph quedan como adaptadores
  *stub* con el contrato listo (`IEmailProviderAdapter`) hasta configurar sus apps OAuth.

## 28.2 Endpoints

Todos bajo el Gateway (`http://localhost:5047`), prefijo `/notifications/email`.

```text
# Configuracion SMTP/API (permiso notification.settings.manage)
POST   /notifications/email/configurations
GET    /notifications/email/configurations
GET    /notifications/email/configurations/{id}
PUT    /notifications/email/configurations/{id}
POST   /notifications/email/configurations/{id}/set-default
POST   /notifications/email/configurations/{id}/test

# Plantillas (notification.template.view | notification.template.manage)
POST   /notifications/email/templates
GET    /notifications/email/templates
GET    /notifications/email/templates/{id}
POST   /notifications/email/templates/{id}/versions
POST   /notifications/email/templates/{id}/publish
POST   /notifications/email/templates/{id}/archive

# Layouts (notification.layout.manage | notification.template.view)
POST   /notifications/email/layouts
GET    /notifications/email/layouts
POST   /notifications/email/layouts/{id}/set-default

# Envio (notification.email.send | notification.email.view)
POST   /notifications/email/send
POST   /notifications/email/send-template
GET    /notifications/email/messages
GET    /notifications/email/messages/{id}

# Campanas (notification.campaign.view | notification.campaign.manage)
POST   /notifications/email/campaigns
GET    /notifications/email/campaigns
GET    /notifications/email/campaigns/{id}
POST   /notifications/email/campaigns/{id}/schedule
POST   /notifications/email/campaigns/{id}/send-test
POST   /notifications/email/campaigns/{id}/cancel

# Cuentas + sincronizacion (notification.account.view | notification.account.manage)
POST   /notifications/email/accounts/connect
GET    /notifications/email/accounts
GET    /notifications/email/accounts/{id}
POST   /notifications/email/accounts/{id}/disconnect
POST   /notifications/email/accounts/{id}/sync
POST   /notifications/email/accounts/{id}/full-sync
GET    /notifications/email/accounts/{id}/folders
GET    /notifications/email/accounts/{id}/messages
GET    /notifications/email/accounts/{id}/messages/{messageId}
GET    /notifications/email/accounts/{id}/threads/{threadId}
GET    /notifications/email/accounts/{id}/sync-logs
```

Los permisos `notification.*` estan en `BuildingBlocks.Authorization.NotificationPermissions`
y se aplican con `[HasPermission(...)]`; TenantAdmin/PlatformAdmin pasan siempre.

## 28.3 Eventos (Wolverine/RabbitMQ)

Nuevos eventos en `BuildingBlocks/Messaging/EmailIntegrationEvents`, publicados al
exchange fanout `taxvision-events` (registrados con `PublishMessage<T>()` en
`Program.cs`) y consumidos por el propio Notification (cola durable `notification-events`):

- Envio: `EmailSendRequested`, `EmailDeliverySucceeded`, `EmailDeliveryFailed`.
- Campanas: `EmailCampaignScheduled`, `EmailCampaignStarted`, `EmailCampaignCompleted`.
- Cuentas: `EmailAccountConnected`, `EmailAccountDisconnected`, `EmailFullSyncRequested`,
  `EmailIncrementalSyncRequested`, `EmailSyncCompleted`, `EmailSyncFailed`.

Dos `IHostedService` en `Api/Jobs`: `CampaignSchedulerService` (inicia campanas
programadas) y `EmailSyncSchedulerService` (sincronizacion incremental periodica).

## 28.4 Configuracion nueva

Notification requiere dos claves adicionales (ver `appsettings.json` y el
`docker-compose`):

```env
# Cifrado de secretos del modulo email (base64 de 32 bytes). En Docker: ENCRYPTION_MASTER_KEY.
Encryption__MasterKey=<BASE64_32_BYTES>
# Microservicio CloudStorage para plantillas/layouts. En Docker: http://cloudstorage-api:8080.
CloudStorageClient__BaseUrl=http://localhost:5330
```

Generar la clave (PowerShell):

```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 }))
```

En local via User Secrets:

```powershell
dotnet user-secrets set "Encryption:MasterKey" "<BASE64_32_BYTES>" `
  --project src\Services\Notification\TaxVision.Notification.Api\TaxVision.Notification.Api.csproj
```

## 28.5 Migraciones

Se agregaron cuatro migraciones aditivas a `NotificationDbContext` (no tocan
`NotificationLogs`):

- `AddEmailProviderConfigurations`
- `AddEmailTemplatesAndLayouts`
- `AddOutboundEmailMessages`
- `AddEmailCampaigns`
- `AddEmailAccountsAndSync`

Aplicar (host):

```powershell
dotnet ef database update `
  --project src\Services\Notification\TaxVision.Notification.Infrastructure\TaxVision.Notification.Infrastructure.csproj `
  --startup-project src\Services\Notification\TaxVision.Notification.Api\TaxVision.Notification.Api.csproj
```

O via el contenedor `migrations` del stack (aplica todos los servicios):

```powershell
docker compose --env-file .env -f deploy/docker/docker-compose.yml --profile tools run --build --rm migrations
```

## 28.6 Guia de pruebas paso a paso

Requisitos previos: stack levantado (`docker compose ... up -d`), migraciones aplicadas,
`Encryption:MasterKey` configurada, y un `accessToken` de un `TenantAdmin` (ver seccion
14/26 para el flujo de login). Todas las peticiones van al Gateway con
`Authorization: Bearer <accessToken>`.

### 1) Configuracion SMTP y prueba de envio

```http
POST /notifications/email/configurations
{
  "scope": "Tenant",
  "providerType": "Smtp",
  "displayName": "SMTP del tenant",
  "fromEmail": "no-reply@empresa-demo.com",
  "fromName": "Empresa Demo",
  "host": "smtp.mailtrap.io",
  "port": 587,
  "username": "<user>",
  "password": "<pass>",
  "useSsl": true,
  "isDefault": true
}
```

```http
POST /notifications/email/configurations/{id}/test
{ "toEmail": "prueba@empresa-demo.com" }
```

El secreto se guarda cifrado; el GET nunca lo devuelve (solo `hasPassword: true`).

### 2) Plantilla y layout

```http
POST /notifications/email/layouts
{ "scope": "Tenant", "layoutName": "Base", "html": "<html><body>{{ body }}</body></html>", "isDefault": true }
```

```http
POST /notifications/email/templates
{ "scope": "Tenant", "templateKey": "welcome", "subject": "Hola {{ customer_name }}", "variables": ["customer_name"] }
```

```http
POST /notifications/email/templates/{id}/versions
{ "subjectTemplate": "Hola {{ customer_name }}", "html": "<h1>Bienvenido {{ customer_name }}</h1>" }
```

```http
POST /notifications/email/templates/{id}/publish
{ "versionId": "<versionId>" }
```

El HTML se sube a CloudStorage (usa tu bearer token). La version queda `PendingScan`
hasta que ClamAV la marque `Available`; publicar/enviar funciona una vez disponible.

### 3) Envio individual y por plantilla

```http
POST /notifications/email/send
{ "subject": "Aviso", "htmlBody": "<p>Hola</p>", "recipients": [{ "address": "cliente@example.com" }] }
```

```http
POST /notifications/email/send-template
{ "templateId": "<id>", "recipients": [{ "address": "cliente@example.com" }], "variables": { "customer_name": "Maria" } }
```

Ambos devuelven `202 Accepted` con el `id` del mensaje. Consulta el estado:

```http
GET /notifications/email/messages/{id}
```

### 4) Campana

```http
POST /notifications/email/campaigns
{ "name": "Newsletter Julio", "type": "Newsletter", "templateId": "<id>",
  "recipients": [ { "address": "a@example.com", "variables": { "customer_name": "Ana" } },
                  { "address": "b@example.com", "variables": { "customer_name": "Beto" } } ] }
```

```http
POST /notifications/email/campaigns/{id}/schedule
{ "scheduledAtUtc": null }   // null = ahora; el scheduler la inicia en <=30s
```

El fan-out crea un correo por destinatario; los contadores (`sentCount`, `failedCount`)
se actualizan via eventos de entrega. `GET /campaigns/{id}` muestra el progreso.
`POST /campaigns/{id}/send-test` envia una prueba sin afectar contadores.

### 5) Conexion y sincronizacion de cuenta (IMAP)

```http
POST /notifications/email/accounts/connect
{ "provider": "Imap", "emailAddress": "buzon@empresa-demo.com",
  "imapHost": "imap.empresa-demo.com", "imapPort": 993, "imapUsername": "buzon@empresa-demo.com",
  "imapPassword": "<pass>", "imapUseSsl": true }
```

Se dispara una sincronizacion completa automatica. Tambien manual:

```http
POST /notifications/email/accounts/{id}/full-sync
POST /notifications/email/accounts/{id}/sync            // incremental (usa cursor)
GET  /notifications/email/accounts/{id}/folders
GET  /notifications/email/accounts/{id}/messages?folderId=<id>&page=1&size=20
GET  /notifications/email/accounts/{id}/messages/{messageId}
GET  /notifications/email/accounts/{id}/threads/{threadId}
GET  /notifications/email/accounts/{id}/sync-logs
```

Los tokens/credenciales se cifran y `disconnect` los borra. Gmail/Graph devuelven
`EmailAccount.ProviderNotConfigured` hasta habilitar sus adaptadores OAuth.

### 6) Pruebas multitenant

Repite cualquier flujo con el `accessToken` de otro tenant y verifica que **no** ve las
configuraciones, plantillas, campanas ni cuentas del primero (aislamiento por `tenant_id`).
Las plantillas/config con `scope=System` solo las gestiona un `PlatformAdmin`.

### 7) Eventos y tracking

En Grafana/Loki, filtra por `service_name="notification-service"` y sigue el
`CorrelationId` para ver la cadena `EmailSendRequested → EmailDeliverySucceeded/Failed`.
En RabbitMQ (`http://localhost:15672`) revisa la cola `notification-events`.

## 28.7 Grant M2M (service-to-service) y adjuntos sincronizados

Para que el worker de sincronizacion (sin contexto de usuario) suba los binarios de los
adjuntos a CloudStorage, Auth expone un grant **client-credentials (M2M)**:

```http
POST /auth/service-token
{ "clientId": "notification-worker", "clientSecret": "<secret>", "tenantId": "<tenantGuid>" }
```

Devuelve un token de servicio corto (`actor_type=Service`) con los permisos configurados
para ese cliente. Los clientes se declaran en Auth via `ServiceAuth:Clients` (secret
store / `.env`), por ejemplo:

```env
NOTIFICATION_SERVICE_CLIENT_ID=notification-worker
NOTIFICATION_SERVICE_CLIENT_SECRET=<secret-fuerte>
```

Auth los mapea a los permisos `cloudstorage.file.upload|download|view`. Notification
(`ServiceAuthClient`) obtiene y cachea estos tokens por tenant. El token provider de
CloudStorage usa el token del usuario en contexto request y el token de servicio en
background; asi, el worker sube el binario del adjunto y enlaza su `FileId` en
`EmailMessageAttachments` (si el tipo no esta permitido o falla, conserva solo la metadata).

## 28.8 Proveedores reales, webhooks y fan-out por lotes

- **Adaptadores Gmail API y Microsoft Graph reales** (via HTTP con el access token OAuth de
  la cuenta): listan carpetas y sincronizan mensajes (headers, cuerpo HTML/texto, adjuntos)
  y refrescan el access token con el refresh token (`OAuthTokenService`). El token inicial se
  obtiene con el flujo OAuth del frontend y se pasa en `connect`; las credenciales de la app
  para refrescar se configuran en `EmailOAuth:{Gmail,Microsoft}` (client id/secret). Sin esas
  credenciales el refresh no ocurre y el adapter devuelve `EmailAccount.ProviderNotConfigured`.
- **Webhooks de tracking**: `POST /notifications/email/webhooks/tracking` (anonimo,
  autenticado por header `X-Webhook-Secret` = `EmailWebhook:Secret`) recibe eventos
  normalizados `{ events: [{ messageId, type }] }` (Delivered/Opened/Clicked/Bounced) y
  actualiza el tracking del correo y los contadores de apertura/clic de su campana. Un
  adaptador por proveedor (SendGrid/Mailgun) traduciria su formato a este payload.
- **Fan-out por lotes**: el consumer de inicio de campana divide los destinatarios en lotes
  de 100 y publica un evento por lote (`EmailCampaignBatchIntegrationEvent`); cada lote se
  procesa en su propia transaccion, evitando una transaccion gigante.

## 28.9 Pendientes documentados

- **Verificacion end-to-end con stack real**: el flujo HTTP a CloudStorage (upload presignado
  + M2M), el envio SMTP y las llamadas Gmail/Graph estan validados en compilacion; requieren
  los servicios levantados y credenciales OAuth reales para probarse.
- **Sincronizacion incremental por delta nativo** (historyId de Gmail / deltaToken de Graph):
  hoy el incremental usa cursores por fecha/UID; el delta nativo es mas eficiente.
- **Subida de adjuntos por cola** en vez de inline dentro de la sincronizacion, para buzones
  con muchos adjuntos grandes.
