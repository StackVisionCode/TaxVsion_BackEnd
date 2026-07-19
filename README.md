
# Guia de implementación de los test (Pendiente)
<a href="https://firebasestorage.googleapis.com/v0/b/c5iffaa-10025.firebasestorage.app/o/Guia_Implementacion_Pruebas_Automatizadas_TaxVision.pdf?alt=media&token=2ba4fb54-9e81-4812-b75c-03fe2b5e61d5"> Guía Implementación Test</a>

<a href="https://firebasestorage.googleapis.com/v0/b/c5iffaa-10025.firebasestorage.app/o/Guia_Implementacion_Customer_Subscription_TaxVision.pdf?alt=media&token=ae21b127-f034-49c0-a04b-d28c03097212">Customer y Subscription </br>
Guia de implementacion</a>

# TaxVision Backend

<img width="2400" height="1560" alt="taxvision_arch" src="https://github.com/user-attachments/assets/65b9f169-c0f1-4098-8533-f3d50e90c015" />


Backend multitenant de TaxVision construido con microservicios en .NET 10.

**Autor de las implementaciones documentadas:** Jorge Turbi

**Actualizado:** 14-07-2026

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
28. Modulo de Email avanzado (Notification)
29. Signature Service (firma electronica multi-tenant)
30. Communication Service (chat, calls, meetings, notifs realtime)
31. Claves JWT RS256 — Setup de desarrollo
32. Subscription Service: implementacion completa (Fases 1-4, 6-7)
33. Multi-tenancy: subdominios y dominios propios (Auth)
34. Terminos de servicio (ToS) — aceptacion y gating (Auth)
35. Postmaster Service (dispatch de email desacoplado de Notification)
36. Scribe Service (templating centralizado de email)
37. Connectors Service (integraciones Gmail/Graph/IMAP)
38. Correspondence Service (inbox del cliente final + compose/send)

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
|-- .github/
|   `-- workflows/
|       `-- deploy.yml           # unico workflow: build+migra+deploy a produccion (sec 20)
|-- deploy/
|   |-- docker-compose.yml       # infraestructura minima
|   |-- docker/
|   |   |-- docker-compose.yml   # stack completo (canonico, sec 20)
|   |   |-- compose.ps1          # wrapper que fuerza el .env de la raiz
|   |   |-- caddy/Caddyfile      # reverse proxy publico + TLS automatico
|   |   |-- minio/               # provisioning de cuentas de servicio MinIO (policies/*.json)
|   |   `-- migrations/Dockerfile
|   |-- tests/
|   |   |-- TaxVision.Auth.Tests/
|   |   |-- TaxVision.Tenant.Tests/
|   |   |-- TaxVision.Customer.Tests/
|   |   |-- TaxVision.Subscription.Tests/
|   |   |-- TaxVision.Notification.Tests/
|   |   |-- TaxVision.Signature.Tests/
|   |   `-- TaxVision.CloudStorage.Tests/
|   `-- observability/
|       |-- loki.yml
|       |-- tempo.yml
|       |-- prometheus.yml
|       |-- otel-collector.yml
|       `-- grafana/provisioning/
|           `-- dashboards/      # overview.json, service-detail.json, infrastructure.json
`-- src/
    |-- BuildingBlocks/
    |   |-- BuildingBlocks.csproj
    |   |-- BuildingBlocks.Infrastructure/
    |   `-- BuildingBlocks.Web/
    |-- Gateway/TaxVision.Gateway/
    `-- Services/
        |-- Auth/                 # Api / Application / Domain / Infrastructure
        |-- Tenant/                TaxVision.Tenant.{Api,Application,Domain,Infrastructure}
        |-- Customer/              TaxVision.Customer.{Api,Application,Domain,Infrastructure}
        |-- Subscription/          TaxVision.Subscription.{Api,Application,Domain,Infrastructure}
        |-- Notification/          TaxVision.Notification.{Api,Application,Domain,Infrastructure}
        |-- CloudStorage/          TaxVision.CloudStorage.{Api,Application,Domain,Infrastructure}
        |-- Signature/             TaxVision.Signature.{Api,Application,Domain,Infrastructure}
        |-- Communication/         Node.js/TypeScript, Fastify + Socket.IO + Prisma (sec 30)
        `-- CommunicationTranscriptWorker/  # worker Node standalone, whisper.cpp (sec 27.2/29)
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
| AWSSDK.S3 (.NET) | ver `.csproj` | multipart upload directo a S3/MinIO (sec 27.8) | Apache-2.0 |
| ClamAV | 1.4.3 | antivirus de archivos subidos | GPL-2.0 |
| YARP | 2.3.0 | reverse proxy interno (Gateway) | MIT |
| Caddy | 2-alpine | reverse proxy publico + TLS automatico en produccion (sec 20) | Apache-2.0 |
| cAdvisor | v0.49.1 | metricas de contenedores para Prometheus | Apache-2.0 |
| node-exporter | v1.8.2 | metricas de host para Prometheus | Apache-2.0 |
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
POST /tenants (ticket firmado de Auth o rol PlatformAdmin, ver §33.3)
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
| `POST /tenants` | ticket firmado de Auth (`POST /auth/subdomains/reserve`) o rol `PlatformAdmin`; policy `TenantRegistration`, rate limit `tenant-registration` (5/min/IP) — ver §33.3 |
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

### Servicios adicionales del stack completo

`deploy/docker/docker-compose.yml` ya no es solo "APIs + infraestructura minima": incluye
todo lo necesario para un despliegue de produccion real detras de un solo host:

- `sqlserver` (`mcr.microsoft.com/mssql/server:2022-latest`, edicion Developer):
  **SQL Server esta containerizado** en este compose, no es `host.docker.internal`. Puerto
  1433 solo en loopback (acceso por VPN/SSH, nunca publico).
- `cadvisor` (`gcr.io/cadvisor/cadvisor:v0.49.1`, privileged) y `node-exporter`
  (`prom/node-exporter:v1.8.2`, `pid: host`): metricas de contenedores y de host para
  Prometheus/Grafana.
- `caddy` (`caddy:2-alpine`): unico servicio que publica 80/443 al host; ver mas abajo.
- `minio-provision` (`minio/mc:latest`, perfil `tools`, un solo uso): aprovisiona cuentas de
  servicio MinIO con alcance minimo (`signature-worker`, `notification-worker`,
  `communication-transcript-worker`) y sus policies IAM (`deploy/docker/minio/policies/*.json`)
  para que cada servicio de la Fase D (sec 27.2) suba directo a su propio prefijo temporal, sin
  usar `MINIO_ROOT_USER`/`PASSWORD`. Idempotente: se puede correr en cada deploy.
- `signature-api`, `communication-api`, `communication-transcript-worker`: los tres
  microservicios mas recientes tambien estan dockerizados en este compose (Dockerfiles propios
  bajo cada `src/Services/<Nombre>/`).
- `migrations` (perfil `tools`): contenedor que aplica las migraciones EF Core de los 7
  servicios .NET en una sola corrida.

### Despliegue en produccion (Caddy + TLS wildcard automatico + RS256)

En produccion el `gateway` YARP **no publica puerto al host**: queda solo en la red interna
`taxvision-network`, y `caddy` es el unico punto de entrada:

```text
Internet :80/:443 -> caddy (TLS WILDCARD automatico via Let's Encrypt, DNS-01/Cloudflare)
                       |-- /taxvision-storage/*, /taxvision-temp/*, /taxvision-quarantine/*
                       |     -> reverse_proxy minio:9000   (URLs presignadas de CloudStorage)
                       `-- todo lo demas -> reverse_proxy gateway:8080
                             (incluye la resolucion de tenant por subdominio que hace Auth)
```

**Fase X1 (completitud del plan de subdominios de tenant, ver `Auth_y_CloudStorage_Plan_
Completitud_v2.md`)** — el `Caddyfile` (`deploy/docker/caddy/Caddyfile`) usa un site block
**wildcard** sobre `{$TAXVISION_BASE_DOMAIN:taxprocore.com}, *.{$TAXVISION_BASE_DOMAIN}`, con
`caddy` construido desde un `Dockerfile` propio (`deploy/docker/caddy/Dockerfile`, no la imagen
`caddy:2-alpine` estandar) que agrega el plugin `caddy-dns/cloudflare`. Esto es necesario porque
Let's Encrypt solo emite certificados wildcard via desafio **DNS-01** (crear un registro TXT de
validacion) — el HTTP-01 automatico que usaba el `Caddyfile` anterior (single-domain) no puede
emitir para un Host que Caddy no conoce de antemano, asi que nunca iba a poder cubrir subdominios
de tenant dinamicos (`oficina1.taxprocore.com`, `oficina2.taxprocore.com`, ...). Requiere:

- DNS wildcard (`CNAME *` o `A *`) apuntando a este server, ya configurado antes del primer arranque.
- `CLOUDFLARE_DNS_API_TOKEN` en `.env` — token de Cloudflare con permiso **Zone:DNS:Edit** sobre
  la zona, **distinto** del que ya usa Auth para provisionar custom hostnames (`Cloudflare__ApiToken`
  en `auth-api`, con permisos Zone:DNS:Edit + Zone:SSL:Edit) — no reusar el mismo token entre los
  dos usos aunque compartan la misma cuenta de Cloudflare.
- `TAXVISION_DOMAIN` (el host que usan hoy `MINIO_PUBLIC_ENDPOINT`/el healthcheck del workflow de
  deploy, ej. `api.taxprocore.com`) sigue funcionando sin cambios — como es un subdominio de
  `TAXVISION_BASE_DOMAIN`, el mismo certificado wildcard ya lo cubre, no hace falta nada mas.

La regla de MinIO existe porque las URLs presignadas que CloudStorage entrega al cliente apuntan
al mismo dominio publico, con el nombre del bucket como primer segmento — no colisiona con rutas
del Gateway (`/auth`, `/customers`, `/storage`, etc.).

JWT en produccion usa **RS256 con las claves montadas como archivo**, no HS256 con secreto
compartido: todos los servicios .NET montan
`${TAXVISION_SECRETS_DIR:-/opt/taxvision/secrets}/jwt-public.pem` (`Jwt__PublicKeyPath`); solo
`auth-api` monta ademas `jwt-private.pem` (`Jwt__PrivateKeyPath`). Esto es necesario para que
`/auth/.well-known/jwks.json` publique un JWKS real (con HS256 ese endpoint devolveria un
keyset vacio, ver sec 31) — de eso depende `communication-api` (Node), que valida tokens solo
via JWKS remoto. `signature-api` monta ademas su certificado PAdES
(`Signature__Sealing__Cms__CertificatePath`) desde el mismo directorio de secretos.

Grafana trae tres dashboards provisionados automaticamente (`deploy/observability/grafana/
provisioning/dashboards/`): `overview.json` (request rate y error rate 5xx agregados por
servicio), `service-detail.json` (mismo detalle con un selector `$service` — un solo dashboard
parametrizado cubre los ocho servicios, no uno por archivo) e `infrastructure.json`
(disco/memoria del host, alimentado por cadvisor/node-exporter).

### CI/CD (GitHub Actions)

`.github/workflows/deploy.yml` — unico workflow del repo. Trigger: push a `main` o
`workflow_dispatch` manual; `concurrency: production-deploy` evita despliegues solapados;
corre en runner `self-hosted` con timeout de 45 minutos. Pasos, en orden:

1. Checkout.
2. Escribe un `.env` de produccion (nunca commiteado) desde GitHub Secrets: las 8 connection
   strings, credenciales de RabbitMQ/MinIO/Grafana, secretos de cuentas MinIO por servicio,
   claves de cifrado, credenciales SMTP/OAuth (Gmail/Graph), clientes M2M `ServiceAuth`,
   password del PFX de Signature, variables TURN/CORS de Communication, token de bootstrap del
   primer `PlatformAdmin`, `TAXVISION_DOMAIN` y (Fase X1) `TAXVISION_BASE_DOMAIN` +
   `CLOUDFLARE_DNS_API_TOKEN` para el cert wildcard de Caddy; `chmod 600`.
3. Decodifica `JWT_PRIVATE_KEY_PEM_B64`, `JWT_PUBLIC_KEY_PEM_B64` y
   `SIGNATURE_SEALING_PFX_B64` a `$TAXVISION_SECRETS_DIR` (`/opt/taxvision/secrets`,
   `chmod 700`/`600`).
4. `docker compose ... --profile tools build` — build de todas las imagenes, incluyendo las
   que solo existen bajo el profile `tools` (`migrations`). **El `--profile tools` acá es
   obligatorio**: sin él, Compose ignora por completo el build de `migrations` (cualquier
   servicio gateado por un profile no activo queda excluido de `build`, no solo de `up`) y
   el paso 6 reutiliza el tag `taxvision/migrations:dev` que ya exista en el runner —
   `docker compose run` solo compila si el tag no existe todavía. Root cause real de un
   incidente en producción (migraciones de Scribe agregadas y nunca aplicadas, sin ningún
   error visible): el runner self-hosted conservaba una imagen vieja de `migrations` entre
   deploys, así que las migraciones nuevas nunca se compilaban dentro del contenedor que
   corre `dotnet ef database update --no-build` — el comando terminaba en éxito sin aplicar
   nada nuevo. Corregido en `.github/workflows/deploy.yml` (paso "Build images").
5. `docker compose ... --profile tools run --rm minio-provision` — antes de levantar nada que
   dependa de las cuentas de servicio.
6. `docker compose ... --profile tools run --rm migrations` — migraciones EF Core de los 13
   servicios .NET (`apply-migrations.sh`).
7. `docker compose ... run --rm communication-api npx prisma migrate deploy` — migraciones
   Prisma del servicio Node aparte, porque no pasa por el contenedor `migrations`.
8. `docker compose ... up -d --remove-orphans` — nunca toca volumenes nombrados
   (`sqlserver-data`, `minio-data`, etc.).
9. Limpieza: `docker image prune -f` y `docker builder prune -f --filter "until=24h"` (nunca
   volumenes).
10. Healthcheck: hasta 20 intentos con 5s de espera contra
    `https://${TAXVISION_DOMAIN}/health/ready`; falla el job si el Gateway nunca queda `ready`.

No hay SBOM ni escaneo de secretos automatizado todavia (ver "Pendientes reales", sec 23).

### Probar subdominios de tenant en local y staging (Fase X2 completitud)

El middleware de resolucion de tenant (`TenantHostResolutionMiddleware` en Auth) es agnostico de
Cloudflare — lee `HttpContext.Request.Host`, nada mas — asi que la logica multi-tenant se prueba
sin tocar Cloudflare ni Caddy en ningun entorno:

**Local (recomendado — sin instalar nada):** Chrome/Edge resuelven **cualquier** subdominio de
`*.localhost` a `127.0.0.1` de forma nativa, sin tocar el archivo hosts. Corriendo Auth con
`dotnet run` (`http://localhost:5124` por defecto, ver `launchSettings.json`), un tenant sembrado
con `TenantDomain.Host = "oficina1.localhost:5124"` ya resuelve pegandole a
`http://oficina1.localhost:5124/...` — no hace falta HTTPS ni certificados para probar la
resolucion en si. Para otros navegadores (o si se necesita HTTPS local): agregar entradas al
archivo hosts (`127.0.0.1 oficina1.taxprocore.test`, una por tenant de prueba) y generar un
certificado wildcard local con **mkcert**:
```
mkcert -install
mkcert "*.taxprocore.test" taxprocore.test localhost 127.0.0.1
```
y servirlo con Kestrel (`Kestrel:Certificates:Default:Path` en `appsettings.Development.json`) o
con una segunda instancia local de Caddy apuntando al `.pfx` generado. Evitar el TLD `.dev`
(Chrome lo fuerza a HSTS/HTTPS via preload list) y `.local` (reservado para mDNS).

**Staging:** misma estrategia que produccion pero en una zona separada — `TAXVISION_BASE_DOMAIN=
staging.taxprocore.com` (o el subdominio que se use como raiz de staging) con su propio wildcard
DNS y su propio `CLOUDFLARE_DNS_API_TOKEN`/despliegue de `caddy`, para que un certificado o un
tenant de prueba en staging nunca comparta nada con produccion. El codigo de resolucion de tenant
no cambia entre entornos — solo la config (`TenantDomainOptions`/`TAXVISION_BASE_DOMAIN`).

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

- reemplazar GitHub Secrets por un secret manager/KMS dedicado en produccion (hoy
  `deploy.yml` escribe `.env` y las PEM desde secrets de GitHub Actions en cada deploy, ver
  sec 20);
- el bootstrap de `PlatformAdmin` sigue siendo una variable de entorno
  (`PLATFORM_BOOTSTRAP_INVITATION_TOKEN`) inyectada por CI, no un Job de provisioning separado;
- TLS externo ya esta resuelto (Caddy + Let's Encrypt automatico, sec 20); falta TLS interno
  entre servicios dentro de `taxvision-network` (hoy HTTP/AMQP en texto plano dentro de la red
  Docker privada);
- CI/CD de despliegue ya existe (`.github/workflows/deploy.yml`, sec 20); faltan SBOM y
  escaneo de secretos automatizado en el pipeline;
- implementar las pruebas de la guia;
- definir retencion y almacenamiento object storage para observabilidad productiva;
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
- permite revelar el SSN/ITIN/EIN en claro via permiso granular auditado y rate-limited (ver sec 25.5);
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
- `CustomerImports`: agrega las 3 tablas del flujo de bulk import (`CustomerImportFiles`
  entre ellas, retirada despues — ver `DropCustomerImportFiles`).
- `CustomerImportActiveJobUniqueIndex`: indice filtrado unique para garantizar
  un solo job activo por tenant a nivel BD.
- `UniqueFiscalProfileBlindIndex`: convierte los indices
  `(TenantId, TaxIdentifierBlindIndex)` de fiscal profiles (customer y relation)
  en unique. Garantiza a nivel BD que un mismo SSN/EIN no puede aparecer en dos
  customers o dos relaciones del mismo tenant.
- `AddCustomerAuditLog`: crea `CustomerAuditLogs` — audit trail de acciones
  sensibles (hoy solo `RevealTaxIdentifier`, ver sec 25.5).
- `DropCustomerImportFiles`: elimina la tabla `CustomerImportFiles` — el
  archivo de import ya no vive en SQL, ver sec 25.10.1a (CloudStorage).

Tablas de dominio core:

- `Customers`
- `CustomerAddresses`
- `CustomerContactPoints`
- `CustomerRelations`
- `CustomerFiscalProfiles`
- `CustomerRelationFiscalProfiles`
- `Occupations`
- `PrincipalBusinessActivities`
- `CustomerAuditLogs`

Tablas del flujo de bulk import:

- `CustomerImportAttempts`
- `CustomerImportRows`

(El archivo subido en si ya no es una tabla — vive en CloudStorage, ver sec 25.10.1a.)

Indices clave:

- `(TenantId, Status, DisplayName)` para listados;
- `(TenantId, OccupationId)`;
- `IX_Customers_PrimaryEmailNormalized` para busqueda por email;
- `UX_CustomerFiscalProfiles_Tenant_BlindIndex` unique sobre
  `(TenantId, TaxIdentifierBlindIndex)` en `CustomerFiscalProfiles`; imposibilita
  dos customers del mismo tenant con el mismo SSN/EIN a nivel BD (ver sec 25.17);
- `UX_CustomerRelationFiscalProfiles_Tenant_BlindIndex` mismo enforce para
  spouses y dependientes;
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

**Revelar el numero completo** — `GET /customers/{id}/fiscal-profile/tax-identifier`
(`RevealTaxIdentifierHandler.cs`) es el unico consumidor de `Unprotect()` en todo
el servicio. Formatea segun `SubjectKind` (`123-45-6789` para Individual,
`12-3456789` para Business — asi trabaja el software profesional de impuestos:
Drake, ProSeries, UltraTax muestran la mascara por defecto y solo revelan bajo
accion explicita del preparador). Controles, todos nuevos para este endpoint:

- Permiso granular `customers.fiscalprofile.reveal` (no solo el rol
  TenantAdmin — un TenantEmployee puede recibirlo puntualmente sin volverse
  admin). Primera vez que este servicio usa autorizacion por permiso en vez de
  solo rol; plumbing copiado de Signature (`HasPermissionAttribute` +
  `PermissionPolicyProvider`).
- Audit trail propio: tabla `CustomerAuditLogs` (actor, outcome, IP, user
  agent, correlationId) — se escribe tanto si la revelacion es exitosa como si
  se deniega (customer inexistente o sin fiscal profile), asi que un intento
  fallido de scraping tambien queda registrado.
- Rate limit dedicado: 5 req/min por usuario+ruta (`AddRateLimiter`, policy
  `fiscal-reveal`), independiente del rate limit generico del Gateway.

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
| GET `/customers/{id}/fiscal-profile/tax-identifier` | Permiso `customers.fiscalprofile.reveal` (TenantAdmin lo tiene siempre) |

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
- `SaveFileRequestedIntegrationEvent` — Fase D, ver sec 25.10.1a. Le pide a
  CloudStorage que registre/escanee el archivo de import ya subido a MinIO.

Ninguno transporta identificadores fiscales, contrasenas ni datos bancarios.
Solo `CustomerId`, `TenantId`, `DisplayName`, contacto basico, idioma, canal
preferido y metadatos.

Customer tambien **consume** del mismo exchange (`customer-events`, bindeada
a `taxvision-events`): `FileAvailableIntegrationEvent`,
`FileInfectedDetectedIntegrationEvent`, `FileBlockedByPolicyIntegrationEvent`
de CloudStorage — ver `ImportFileScanResultConsumer`, sec 25.10.1a.

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
   retorna 202 Accepted con `importJobId` (status `Queued`).
2. `StartCustomerImportHandler` sube el archivo directo a CloudStorage (ver
   25.10.1a) — no encola el worker todavia.
3. `ImportFileScanResultConsumer` recibe `FileAvailableIntegrationEvent` cuando
   CloudStorage confirma que el archivo paso el antivirus/politica de
   contenido, y recien ahi encola `RunCustomerImportMessage` que procesa en
   background. Si el escaneo falla (`FileInfectedDetectedIntegrationEvent` o
   `FileBlockedByPolicyIntegrationEvent`), el job pasa directo a `Failed`.
4. Cliente hace polling a GET `/customers/imports/{id}` cada dos segundos —
   el status se queda en `Queued` un poco mas de tiempo que antes (el
   escaneo agrega latencia, tipicamente sub-segundo en local con ClamAV).
5. Al completar se publica `CustomersBulkImportedIntegrationEvent` y se ofrece
   reporte descargable.

No hay request HTTP de larga duracion. El frontend nunca bloquea.

#### 25.10.1a Archivo de import en CloudStorage

El binario ya no vive en SQL (`CustomerImportFiles`, retirada por
`DropCustomerImportFiles`) — sigue el mismo patron Fase D que usan
Signature/Notification:

- **Subida**: `CustomerImportCloudStorageClient.UploadAsync` sube el objeto
  directo a MinIO con credenciales propias de Customer (IAM scoped a
  `taxvision-temp/customer/*`, ver `deploy/docker/minio/policies/
  customer-source.json`) y publica `SaveFileRequestedIntegrationEvent`. El
  `FileId` que viaja en el evento **es el mismo `CustomerImportAttempt.Id`** —
  no hace falta persistir una correlacion aparte, el consumer solo busca el
  attempt por ese Id.
- **Descarga/borrado**: siguen via HTTP+M2M (`ServiceAuthClient` +
  `CloudStorageClient`, grant client-credentials contra `/auth/service-token`)
  — mismo mecanismo que Signature/Notification, cliente M2M propio
  `customer-worker` con permisos `cloudstorage.file.download`,
  `cloudstorage.file.view`, `cloudstorage.file.delete` (ver `ServiceAuth:Clients`
  en Auth, config directa por `.env`/user-secrets — sin codigo nuevo en Auth).
- `FolderType.Imports` / `OwnerType.Tenant` — CloudStorage ya tenia una policy
  dedicada para `.csv`/`.xlsx` en esa carpeta (100 MB, ver sec 27.1/27.6).

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
asociadas. Configurable via `CustomerImport:ReportRetentionDays`.

El archivo en CloudStorage se borra inmediatamente al terminar el job
(`FinishAttemptAsync` llama `ICustomerImportCloudStorageClient.DeleteAsync`);
el cleanup de 90 dias reintenta ese borrado remoto por cada attempt purgado
como defense-in-depth (si `FinishAttemptAsync` no llego a hacerlo por un
fallo de red o un crash mid-flight) — un archivo ya borrado responde
NotFound y no genera error.

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

Credenciales para que el archivo de import hable con CloudStorage (sec 25.10.1a
— mismo patron que `Signature:ServiceAuth`, ver sec 27):

```powershell
dotnet user-secrets set "ServiceAuthClient:AuthBaseUrl" "http://localhost:5124" `
  --project src\Services\Customer\TaxVision.Customer.Api
dotnet user-secrets set "ServiceAuthClient:ClientId" "customer-worker" `
  --project src\Services\Customer\TaxVision.Customer.Api
dotnet user-secrets set "ServiceAuthClient:ClientSecret" "<mismo valor que ServiceAuth:Clients:3:Secret en Auth>" `
  --project src\Services\Customer\TaxVision.Customer.Api
dotnet user-secrets set "CloudStorageClient:BaseUrl" "http://localhost:5330" `
  --project src\Services\Customer\TaxVision.Customer.Api
dotnet user-secrets set "Customer:Minio:Endpoint" "localhost:9000" `
  --project src\Services\Customer\TaxVision.Customer.Api
dotnet user-secrets set "Customer:Minio:AccessKey" "customer-worker" `
  --project src\Services\Customer\TaxVision.Customer.Api
dotnet user-secrets set "Customer:Minio:SecretKey" "<generado por provision-service-accounts.sh>" `
  --project src\Services\Customer\TaxVision.Customer.Api
dotnet user-secrets set "Customer:Minio:UseTls" "false" `
  --project src\Services\Customer\TaxVision.Customer.Api
```

El cliente M2M `customer-worker` se da de alta en Auth con permisos
`cloudstorage.file.download`, `cloudstorage.file.view`,
`cloudstorage.file.delete` (`ServiceAuth:Clients:3:*` en user-secrets de Auth,
mismo patron que `signature-worker`/`notification-worker`).

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
- 3 eventos nuevos: Reactivated, Activated, Deactivated;
- 1 wrapper `PagedResult<T>` en BuildingBlocks;
- 0 migraciones de BD (todo es codigo).

### 25.17 Unicidad del identificador fiscal (SSN/EIN)

Regla de negocio: en el mismo tenant, un SSN/ITIN/EIN no puede pertenecer a dos
customers ni a dos relaciones. Deriva del PDF sec 5 ("el mismo SSN cifrado debe
detectarse mediante un blind index") y del compliance IRS Pub 4557.

Enforce con tres lineas de defensa complementarias:

#### 25.17.1 Nivel base de datos

Migracion `UniqueFiscalProfileBlindIndex` crea dos indices unique:

- `UX_CustomerFiscalProfiles_Tenant_BlindIndex` en
  `(TenantId, TaxIdentifierBlindIndex)` de `CustomerFiscalProfiles`;
- `UX_CustomerRelationFiscalProfiles_Tenant_BlindIndex` en
  `(TenantId, TaxIdentifierBlindIndex)` de `CustomerRelationFiscalProfiles`.

Es la barrera fisica: incluso con race conditions o codigo bugueado, la BD
rechaza el INSERT/UPDATE con `SqlException 2601/2627` que el DbContext
convierte a `ConflictException`.

#### 25.17.2 Nivel aplicacion (pre-check)

`SetCustomerFiscalProfileHandler` y `SetRelationFiscalProfileHandler` invocan
al repositorio antes del `SaveChanges`:

- `ICustomerRepository.FindCustomerIdByFiscalBlindIndexAsync(tenantId,
  blindIndex, excludeCustomerId, ct)`
- `ICustomerRepository.FindRelationIdByFiscalBlindIndexAsync(tenantId,
  blindIndex, excludeRelationId, ct)`

Si existe otro registro con el mismo blind index en el tenant, devuelve
`FiscalProfile.TaxIdentifierAlreadyExists` (o
`RelationFiscalProfile.TaxIdentifierAlreadyExists`) como error de dominio 409.
El usuario recibe un mensaje limpio en lugar de la excepcion SQL.

El parametro `excludeCustomerId`/`excludeRelationId` evita falsos positivos
cuando se actualiza el mismo profile (update permite pasar por el mismo blind
index del propio registro).

#### 25.17.3 Nivel bulk import

`SqlServerCustomerDuplicateDetector` sigue usando el blind index para dedup
batch en el flujo de import; los HashSets del worker cubren dedup intra-chunk.
Nada cambia en ese flujo: ya operaba correctamente antes del enforce estructural.

#### 25.17.4 Como se computa el blind index

Sin cambios respecto a implementaciones previas:

- Normalizar el identificador (solo digitos, 9 caracteres para SSN/EIN).
- `ISensitiveDataProtector.ComputeBlindIndex(normalized, tenantId)` produce
  HMAC-SHA256 con clave derivada por tenant via HKDF sobre `Encryption:MasterKey`.
- El resultado es determinista por tenant: mismo SSN en dos tenants distintos
  produce blind indexes distintos.
- El SSN en texto claro nunca se guarda ni se compara directamente.

### 25.18 Identidad editable del customer

El endpoint PATCH `/customers/{id}` acepta ahora todos los campos de identidad
para casos operativos comunes (correccion de tipeo, cambio de apellido post
matrimonio, cambio de estructura empresarial). Antes solo permitia editar
preferencias y contacto; ahora tambien nombre, DOB e identidad de negocio.

#### 25.18.1 Metodos nuevos del aggregate

- `Customer.ChangePersonalName(PersonalName newName, Guid byUserId)` solo
  aplica a `Kind == Individual`; recalcula `DisplayName`.
- `Customer.ChangeBusinessIdentity(BusinessIdentity newIdentity, Guid byUserId)`
  solo aplica a `Kind == Business`; recalcula `DisplayName`.
- `Customer.ChangeDateOfBirth(DateOnly? dob, Guid byUserId)` aplica a
  cualquier `Kind`.

El aggregate rechaza combinaciones invalidas: `ChangePersonalName` sobre un
Business devuelve `Customer.NotIndividual`; `ChangeBusinessIdentity` sobre un
Individual devuelve `Customer.NotBusiness`.

#### 25.18.2 Body del PATCH

Todos los campos son opcionales (patron PATCH puro). El handler solo aplica lo
que viene con valor.

| Grupo | Campos |
| --- | --- |
| Preferencias y contacto | `language`, `preferredChannel`, `occupationId`, `profilePictureFileId`, `primaryEmail`, `primaryPhone` |
| Identidad Individual | `firstName`, `middleName`, `lastName`, `prefix`, `suffix`, `dateOfBirth` |
| Identidad Business | `legalName`, `dba`, `businessStructure`, `formationDate`, `principalBusinessActivityId` |

Merge inteligente: si solo mandas `lastName`, el resto del nombre conserva su
valor actual del aggregate. Aplica igual para business identity.

#### 25.18.3 Efecto en downstream

- `DisplayName` recalcula automaticamente cuando cambia el nombre o el
  `legalName`; el evento `CustomerUpdatedIntegrationEvent` publica el nuevo
  `DisplayName` para que proyecciones (Auth, Campaign, futuros) se actualicen.
- Los eventos `CustomerEmailChangedV1` y `CustomerPhoneChangedV1` mencionados
  en el PDF siguen sin implementarse: cuando existan consumers, se detectan
  cambios en el handler antes de publicar.

#### 25.18.4 Ejemplos

Individual corrige apellido despues de casarse:

```json
PATCH /customers/{id}
{
  "language": "En",
  "preferredChannel": "Email",
  "primaryEmail": "maria.new@example.com",
  "lastName": "Garcia-Lopez"
}
```

Business cambia de LLC a SCorp:

```json
PATCH /customers/{id}
{
  "language": "En",
  "preferredChannel": "Email",
  "primaryEmail": "contact@acme.com",
  "businessStructure": "SCorp"
}
```

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
- **Consumers de Communication** (Node.js, Fase F11 QA — antes de esto Communication
  publicaba `communication.meeting.recording_ready.v1`/`.recording_failed.v1` y sus
  equivalentes de `call` al bus sin que nadie los escuchara): 4 stubs log-only en
  `Application/Consumers/Communication/` (`MeetingRecordingReadyConsumer`,
  `MeetingRecordingFailedConsumer`, `CallRecordingReadyConsumer`,
  `CallRecordingFailedConsumer`), mismo patron que
  `MeetingInvitationCreatedConsumer` (§30.5). Registran la notificacion via
  `NotificationDispatcher.RecordInAppAsync` contra un recipient **simbolico**
  (`meeting:{meetingId}` / `call:{callId}`, no un usuario real) porque estos 4
  eventos no traen `userId` ni `tenantId` de un destinatario — Communication
  nunca resuelve quien deberia verlo antes de publicar. No envian email ni push
  real; eso requeriria que Communication resuelva y publique el
  host/organizador del meeting/call primero. Los 4 tipos CLR viven en
  `BuildingBlocks/Messaging/CommunicationIntegrationEvents/` (nuevos:
  `MeetingRecordingReadyIntegrationEvent`, `MeetingRecordingFailedIntegrationEvent`,
  `CallRecordingReadyIntegrationEvent`, `CallRecordingFailedIntegrationEvent`),
  con `[MessageIdentity]` mapeando el string de evento real que Communication
  escribe — Wolverine los descubre por convencion (mismo assembly scan que ya
  usaba `MeetingInvitationCreatedConsumer`), sin registro explicito en
  `Program.cs`.
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
- Refactor SOLID del `RunCustomerImportHandler` (**hecho en ms-customer** — commit `775ec64` con
  metodos privados que separan el codigo por responsabilidades).
- Activar RS256/JWKS en produccion (generar par de claves y distribuir la publica).
- Restaurar filtro por `TenantId` en `CustomerReadService.SearchAsync` y `GetByIdAsync`
  (**hecho en ms-customer** — commit `2a84ff3` con el tenant filter re-aplicado por seguridad
  multi-tenant).

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
los eventos. Para archivos seleccionados por un usuario, o descargados por un
proceso en background en nombre de un request, usan el flujo REST + POST policy
anterior con JWT de usuario o token M2M.

Para archivos generados exclusivamente en backend (sin usuario en el request) existe,
desde la fase de decoupling por eventos ("Fase D"), un segundo camino: el productor
sube el objeto directamente a MinIO/S3 con credenciales IAM propias acotadas a un
bucket temporal (`taxvision-temp/<servicio>/*`, aprovisionadas por el servicio
`minio-provision`, ver sec 20) y publica `SaveFileRequestedIntegrationEvent`
(`BuildingBlocks/Messaging/CloudStorageIntegrationEvents`) en vez de llamar al flujo
HTTP+M2M de 3 pasos. `SaveFileFromSourceHandler` en CloudStorage consume el evento de
forma idempotente, revalida politica de folder/plan y cuota, copia el objeto al bucket
temporal canonico y continua por el mismo pipeline de escaneo que un upload normal
(ScanFileCommand -> ClamAV -> Available/Quarantine). Los productores .NET (Wolverine)
publican al exchange fanout compartido `taxvision-events`; los productores Node (que no
usan Wolverine) publican a una cola dedicada `cloudstorage-external-uploads` consumida
aparte por CloudStorage.

Migrado a este flujo (solo el lado de **subida**; las **descargas** siguen usando el
flujo REST `download-url` + M2M anterior en los tres casos, deliberadamente fuera de
alcance de esta fase):

- **Signature** (`SignatureCloudStorageClient.UploadAsync`): PDF sellado y certificado
  de firma (ver sec 29.8).
- **CommunicationTranscriptWorker** (`cloudstorage-client.ts`, microservicio Node
  standalone en `src/Services/CommunicationTranscriptWorker/`): transcript de
  whisper.cpp de calls/meetings.
- **Notification**, unicamente el path de adjuntos entrantes por sincronizacion IMAP
  (`InboundAttachmentStorageWriter`).

Pendiente real, no oculto: `TemplateStorageService` y `LayoutStorageService` de
Notification (`Infrastructure/Storage/EmailStorageServices.cs`, assets HTML/design
JSON/preview PNG de plantillas y layouts de email, ver sec 28.1) **siguen usando** el
flujo HTTP+M2M de 3 pasos anterior (`Infrastructure/Storage/CloudStorageClient.cs`:
initiate -> POST a la URL presignada -> complete) porque se suben en contexto de
request con el JWT del usuario que edita la plantilla, no desde un worker en
background. No hay urgencia arquitectonica para migrarlos — el criterio de Fase D es
"sin usuario en el request" — pero es, a la fecha, el unico productor de archivos entre
microservicios que no paso por el flujo de eventos.

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

Archivos:

- `POST /storage/files/uploads`
- `POST /storage/files/{fileId}/complete`
- `POST /storage/files/uploads/initiate-multipart` (Fase U, ver sec 27.8)
- `POST /storage/files/{fileId}/complete-multipart` (Fase U, ver sec 27.8)
- `GET /storage/files/{fileId}`
- `GET /storage/files`
- `POST /storage/files/{fileId}/download-url`
- `POST /storage/files/zip` (Fase B2, ver sec 27.7)
- `DELETE /storage/files/{fileId}`
- `PUT /storage/files/{fileId}/legal-hold`, `DELETE /storage/files/{fileId}/legal-hold` (ver sec 27.12)

Folders (ver sec 27.9):

- `GET /storage/folders`
- `POST /storage/folders`
- `PUT /storage/folders/{folderId}/rename`
- `PUT /storage/folders/{folderId}/move`

Sharing (ver sec 27.10):

- `POST /storage/files/{fileId}/shares`, `GET /storage/files/{fileId}/shares`
- `POST /storage/folders/{folderId}/shares`, `GET /storage/folders/{folderId}/shares`
- `GET /storage/shares/shared-with-me`
- `DELETE /storage/shares/{shareLinkId}`
- `PUT /storage/shares/{shareLinkId}/expiration`
- `GET /storage/public/{token}` (anonimo)
- `GET /storage/private/{token}` (autenticado)

Recycle bin (ver sec 27.11):

- `GET /storage/recycle-bin`
- `POST /storage/recycle-bin/restore/{fileId}`
- `DELETE /storage/recycle-bin/empty`

Legal / DMCA (ver sec 27.12):

- `POST /storage/legal/dmca-notices`
- `POST /storage/legal/dmca-notices/{dmcaNoticeId}/counter-notice`
- `POST /storage/legal/dmca-notices/{dmcaNoticeId}/reinstate`

Cuota, auditoria y salud:

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

## 27.7 Descarga ZIP multi-archivo y multi-carpeta (Fase B2/B2.1)

`POST /storage/files/zip` (permiso `cloudstorage.file.download`, `FilesController.DownloadZip`)
recibe `{ fileIds, folderIds }` (ambos opcionales, al menos uno debe traer algo) y devuelve un
`.zip` de streaming real: el controller abre un `System.IO.Compression.ZipArchive` directamente
sobre `Response.Body` (`ZipArchiveMode.Create, leaveOpen: true`) y por cada entrada copia el
objeto desde MinIO al stream de la entrada (`entry.Open()`) sin bufferear el ZIP completo en
memoria. La validacion, resolucion de carpetas y armado del plan de descarga (que archivos, en
que orden, tamano total) pasan por `PrepareZipDownloadQuery`/`PrepareZipDownloadHandler` via
Wolverine; el volcado de bytes es la unica parte que corre fuera del bus porque un handler no
tiene acceso al `HttpResponse`.

**`fileIds`** — archivos sueltos, van en la raiz del ZIP. Resolucion **estricta**: un id
invalido, de otro tenant/owner, o que no este `Available` aborta TODO el pedido con
`File.NotFound`/`File.NotAvailable` (el usuario los eligio uno por uno, un descarte silencioso
seria confuso).

**`folderIds`** — carpetas completas, **recursivo** (incluye subcarpetas). Cada carpeta pedida
se resuelve via el `RelativePath` materializado de `Folder` (`IFolderRepository.
ListByPathPrefixAsync`, 1 query trae todo el subarbol) y sus archivos se traen en un unico
batch (`IFileObjectRepository.ListInFoldersAsync`) — evita el N+1 de una query por carpeta.
Dentro del ZIP, la estructura de carpetas se preserva como directorios (ej.
`Recibos/2025/factura.pdf`); si dos carpetas pedidas comparten nombre en el request, la segunda
se desambigua como `Recibos_1/...`. Resolucion **tolerante**: una carpeta vacia o con archivos
aun en escaneo no aborta el resto del ZIP, simplemente no aporta esas entries — solo falla si
el `folderId` en si no existe/no es accesible (`Folder.NotFound`, `Folder.Forbidden`) o si tras
resolver todo no queda ningun archivo para incluir (`File.NoFilesResolved`).

Si un `fileId` explicito tambien cae dentro de un `folderId` pedido en el mismo request, no se
duplica — gana la resolucion estricta (queda en la raiz del ZIP, no bajo el prefijo de carpeta).

Limites, en `CloudStorageOptions`:

- `MaxZipFiles` = 500 archivos por request (default) — se chequea sobre `fileIds` de entrada
  (fail fast) y de nuevo sobre el total ya resuelto (archivos sueltos + de carpetas).
- `MaxZipFolders` = 50 carpetas por request (default) — chequeado ANTES de resolver contenido,
  para no pagar el costo de I/O de una carpeta pedida de mas.
- `MaxZipAggregateBytes` = 500 MB agregados (default).
- Excederlos devuelve `FileErrors.TooManyItems` / `FileErrors.TooManyFolders` /
  `FileErrors.ZipTooLarge` (413).

Rate limit dedicado `zip-download`: 5 requests/minuto, particionado por `sub` del JWT (con
fallback a IP). Si MinIO falla a mitad de la descarga, los headers HTTP ya se enviaron (ZIP
truncado) y la conexion simplemente se corta — limitacion inherente al streaming aceptada por
diseno, no un bug pendiente.

## 27.8 Multipart upload directo a S3/MinIO (Fase U)

Para archivos grandes, `IMultipartUploadStorage` (implementado por `S3MultipartUploadStorage`
sobre `AWSSDK.S3`) permite subir por partes directamente al object storage sin pasar los bytes
por el backend:

1. `POST /storage/files/uploads/initiate-multipart` — valida igual que un upload normal
   (permiso, tenant, owner, folder, cuota) y llama `InitiateMultipartUploadAsync`; genera una URL
   PUT presignada por parte (`GetPreSignedURLAsync`) con tamano de parte `MultipartPartSizeBytes`
   (default 5 MB, el minimo de S3).
2. El cliente sube cada parte directamente a MinIO/S3 con las URLs recibidas, sin credenciales de
   MinIO ni pasar por CloudStorage.
3. `POST /storage/files/{fileId}/complete-multipart` — recibe la lista `Parts` (`PartNumber`,
   `ETag`) del cliente; `CompleteMultipartUploadHandler` llama
   `multipartStorage.CompleteAsync(...)` y despues cae en el mismo `CompleteUploadHandler` que usa
   el flujo de upload simple (verifica tamano, marca `PendingScan`).

**Abort en fallo o abandono**: `FileObject.MultipartUploadId` persiste el `UploadId` que S3
devuelve en `InitiateAsync` (migracion `AddMultipartUploadIdAndShareLinkRowVersion`). Si
`CompleteAsync` falla, `CompleteMultipartUploadHandler` captura la excepcion, llama
`multipartStorage.AbortAsync(...)` para liberar las partes ya subidas, y devuelve
`File.MultipartCompleteFailed` (409) — el archivo queda en `PendingUpload`. Si el cliente
abandona el upload sin completar, `ExpiredUploadCleanupService` (barre cada 30 minutos las
reservas vencidas) distingue el caso: si `MultipartUploadId` esta presente llama
`multipartStorage.AbortAsync(...)` en vez del `storage.DeleteAsync` simple (que no hace nada
sobre una sesion multiparte nunca ensamblada).

## 27.9 Folders (arbol jerarquico)

`FoldersController.cs` (`storage/folders`) expone un arbol de carpetas puramente logico en base
de datos — distinto del `FolderType` (enum fijo usado para componer la clave fisica en MinIO, ver
sec 27.1/27.6) y sin tocar el storage fisico:

- `GET /storage/folders?parentFolderId=` — contenido de una carpeta.
- `POST /storage/folders` — crear, con `OwnerType`/`OwnerId`.
- `PUT /storage/folders/{folderId}/rename` — renombrar.
- `PUT /storage/folders/{folderId}/move` — mover a otro padre.

El agregado `Folder` (`Domain/Folders/Folder.cs`, `TenantEntity`) mantiene un `RelativePath`
materializado (p. ej. `/Clientes/Oficina A/Recibos`) para evitar recorridos recursivos en cada
listado o movimiento. Al renombrar o mover una carpeta, `FolderPathCascader.CascadeAsync`
reescribe el `RelativePath` de todos los descendientes sustituyendo el prefijo antiguo por el
nuevo (`RebasePath`), compartido por `RenameFolderHandler` y `MoveFolderHandler`.

## 27.10 Sharing / ShareLinks

Tres controllers cubren el ciclo de vida completo de enlaces compartidos:

- `ShareLinksController.cs` (`[Authorize]`): crear/listar para archivo o carpeta, listar "shared
  with me", revocar, actualizar expiracion, y (Fase C4 completitud)
  `PUT storage/shares/{shareLinkId}/permission` — cambiar el `Permission` de un link ya creado,
  gateado por `cloudstorage.share.manage`, reusando la misma validacion de politica que la
  creacion.
- `PublicShareController.cs` (`[AllowAnonymous]`, `GET storage/public/{token}`, rate limit
  `share-public` 20 req/min por `{ip}:{path}`).
- `PrivateShareController.cs` (`[Authorize]`, `GET storage/private/{token}`).

`ShareVisibility` (`Domain/Sharing/ShareEnums.cs`): `Public`, `TenantOnly`, `SpecificUsers`,
`TenantCustomers`, `ExternalRecipients`. `SharePermission`: `View`, `Preview`, `Download`,
`Upload`, `EditMetadata` (estos dos ultimos solo asignables por un actor con
`cloudstorage.share.manage`, **y nunca junto con `Visibility.Public`** —
`ShareErrors.ElevatedPermissionNotAllowedOnPublicLink` si se intenta, en creacion o en el cambio
de permiso). `View`/`Preview` fuerzan `Content-Disposition: inline` en la URL presignada
(se renderiza en el browser); `Download` fuerza `attachment` (descarga forzada) —
`IObjectStorage.PresignGetAsync` tiene un overload dedicado para esto. **Nota honesta**: `Upload`/
`EditMetadata` estan validados y se pueden asignar a un link, pero hoy **ningun endpoint los
consume realmente** — no existe un flujo de "subir un archivo nuevo via el token de un link
compartido" ni de "editar metadata via el token"; construir esas dos capacidades es trabajo
pendiente real, no solo documentacion.

Compartir carpetas (`ShareLink.IsRecursive` + `ShareLink.AppliesToFutureItems`, ambos forzados a
`false` para links de archivo — `ShareErrors.RecursiveOnlyForFolders` si no) permite que el link
cubra el contenido actual y, opcionalmente, los items que se agreguen despues.

Password protection: `Pbkdf2ShareLinkPasswordHasher` (PBKDF2-HMACSHA256, 100k iteraciones — mismo
esquema que Auth; el plan original pedia Argon2id, ver "Pendientes reales" mas abajo).
Expiracion: `ShareLink.ExpiresAtUtc` (default `CreatedAtUtc + 7 dias` si no se especifica).
Limite de accesos: `MaxAccessCount` opcional; al alcanzarlo el link pasa a `Exhausted`.

El token (`ShareToken`) es opaco: 32 bytes CSPRNG en base64url, mostrado una sola vez; solo se
persiste su hash SHA-256 (mas `TokenLast4` para mostrar en UI). La resolucion siempre es por hash,
nunca decodificando el token. `ResolvePublicShareHandler` solo sirve `Public` (y
`ExternalRecipients` si el email coincide); `ResolvePrivateShareHandler` es **fail-closed**: exige
JWT, rechaza `Public` de plano, y deniega si el tenant del JWT no coincide con el del link aunque
el token sea estructuralmente valido. Ambos caminos colapsan cualquier fallo (no encontrado,
revocado, expirado, agotado, no autorizado) en la misma respuesta `Denied`, sin distinguir el
motivo, para no facilitar enumeracion. El acceso concedido es una URL GET presignada de MinIO de 2
minutos; CloudStorage nunca hace proxy del binario.

**Eventos de integracion** (`BuildingBlocks/Messaging/CloudStorageIntegrationEvents/`):
`ShareLinkCreated`, `ShareLinkRevoked`, `ShareLinkFolderItemAdded`, y (Fase C4 completitud)
`ShareLinkAccessed`/`ShareLinkAccessDenied` (publicados en cada resolucion exitosa/denegada, con
un campo `Reason` de texto libre para el caso denegado — pensados para que un consumidor externo
tipo SIEM/alertas reaccione, no cambian la respuesta HTTP que sigue siendo el mismo `Denied`
generico), `ShareLinkExpired` (subconjunto de `AccessDenied` cuando la razon puntual es
expiracion — se sigue evaluando siempre en vivo contra `ExpiresAtUtc`, no hay un job de
expiracion proactivo) y `ShareLinkPermissionChanged`. Deliberadamente **no** existe un evento
`ShareLinkResolutionSucceeded` de alta frecuencia — el middleware/handler corre en cada request,
publicar un evento por cada uno inundaria el bus sin aportar nada que `StorageAccessLog` (local)
no cubra ya.

## 27.11 Recycle bin

`RecycleBinController.cs` (`storage/recycle-bin`, permiso `cloudstorage.recyclebin.manage`, no
asignable a roles de Customer Portal):

- `GET /storage/recycle-bin?skip=&take=` — listar.
- `POST /storage/recycle-bin/restore/{fileId}` — restaurar.
- `DELETE /storage/recycle-bin/empty` — vaciar todo el tenant.

`FileObject.SoftDelete` rechaza el borrado si `IsLegalHeld` (sec 27.12). `RecycleBinPurgeService`
(`BackgroundService`, `PeriodicTimer` de **24 horas**) purga fisicamente lo vencido segun
`CloudStorageOptions.RecycleBinRetentionDays` (default 30 dias). Tanto la purga manual
(`EmptyRecycleBinHandler`) como la del job diario comparten `RecycleBinPurger.PurgeAsync`: salta
(sin borrar) los archivos con legal hold, y para el resto borra el objeto en MinIO, libera la
cuota (`TenantStorageLimit.ReleaseUsed(sizeBytes)`) y elimina la fila.

## 27.12 Legal hold y DMCA

Legal hold vive en `FilesController.cs`: `PUT /storage/files/{fileId}/legal-hold` y
`DELETE .../legal-hold` (permiso `cloudstorage.legal.manage`). `FileObject.PlaceLegalHold()` /
`LiftLegalHold()` son idempotency-guarded (fallan si ya esta en ese estado). Un archivo con legal
hold no puede pasar por soft-delete ni por la purga fisica del recycle bin; levantar el hold no
revierte por si solo un estado como `BlockedByPolicy`.

`LegalController.cs` (`storage/legal/dmca-notices`) implementa el ciclo DMCA sobre el agregado
`DmcaNotice` (`Domain/Legal/DmcaNotice.cs`), estados `Received -> CounterNoticeSubmitted ->
Reinstated`:

- `POST /storage/legal/dmca-notices` — registrar (permiso `cloudstorage.legal.manage`), exige
  `SwornStatementAccepted` mas datos del reclamante y del contenido.
- `POST /storage/legal/dmca-notices/{id}/counter-notice` — contra-notificacion (permiso
  `cloudstorage.file.dmca_counternotice`), solo valido desde `Received`.
- `POST /storage/legal/dmca-notices/{id}/reinstate` — reinstalar (permiso
  `cloudstorage.legal.manage`), valido desde `Received` o `CounterNoticeSubmitted`.

`CloudStorageOptions.FolderTypePolicies` define, por `FolderType`, un whitelist/blacklist de
extensiones y MIME mas un tope de tamano (p. ej. `Avatars` 5 MB solo jpg/png/webp; `Recordings`
500 MB solo webm/mp4; `Transcripts` 5 MB solo txt/text-plain). Una blacklist global
`DangerousExtensions` (ejecutables, imagenes de disco, formatos asociados a pirateria, `.torrent`)
se resta siempre. La politica efectiva de una subida es la interseccion de la politica del
`FolderType` con la del plan del tenant, menos la blacklist global; un rechazo por tamano devuelve
`File.TooLarge` (413) — separado de `File.UnsupportedType` (400, extension/content-type no
permitidos) desde que un caso real (recording de meeting sobre el limite de tamano de un plan
"starter") mostraba el mensaje generico "tipo de archivo no permitido" en vez de indicar que el
problema era el tamano.

`StoragePlanPolicy.FolderOverridesBytes` (dict `FolderType→bytes`, keyed by nombre) reemplaza el
`MaxFileSizeBytes` generico del plan para un `FolderType` puntual cuando hay una entrada — sigue
acotado por el `MaxSizeBytes` propio del `FolderType` (nunca lo supera). Existe porque el limite
generico por-archivo de un plan (pensado para documentos: 10-25 MB) siempre era mas chico que
cualquier grabacion real de mas de unos minutos, aunque `Recordings` en si permitiera hasta 500 MB —
`appsettings.json` define un override de `Recordings` tieredo por plan (`starter` 150 MB, `pro`
300 MB, `enterprise` 500 MB, este ultimo igual al tope duro del `FolderType`).

`FolderType.Transcripts` (dedicado a los `.txt` que sube `CommunicationTranscriptWorker` via
whisper.cpp) es un folder aparte de `Recordings` a proposito: `RecordingsPolicy` solo permite
webm/mp4, asi que un transcript etiquetado como `Recordings` era rechazado siempre por whitelist
(`SaveFileRequested ... rejected by upload policy`), sin importar el tamano — no era una falla
transitoria, el pipeline de transcripts nunca pudo registrar un archivo en CloudStorage hasta este
fix.

## 27.13 Pendientes reales de CloudStorage

- Region explicita (`.WithRegion(...)`) para AWS S3 fuera de `us-east-1` (sec 27.5).
- Migrar `TemplateStorageService`/`LayoutStorageService` de Notification al flujo de eventos de
  Fase D — hoy siguen en HTTP+M2M por diseno (request-scoped con JWT de usuario), ver sec 27.2.
- Descargas de archivos generados en backend (Signature, CommunicationTranscriptWorker,
  Notification) siguen usando `download-url` + M2M; no migraron a un flujo de eventos porque no
  hay problema de acoplamiento HTTP que resolver en ese sentido (CloudStorage sigue siendo la
  unica fuente de la URL presignada).

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

**Fase 17 (2026-07-18, cierre de migracion de Notification)**: el modulo "Cuentas + Sync"
(conexion Gmail/Graph/IMAP propia de Notification, duplicada de lo que `Connectors` +
`Correspondence` ya reemplazan) se elimino por completo — domain, adaptadores de
proveedor, controller, scheduler, DbSets, eventos de integracion huerfanos y el
pipeline de subida de adjuntos IMAP a MinIO (`IInboundAttachmentStorageWriter`,
exclusivo de este modulo). Verificado sin caller externo antes de borrar (grep
exhaustivo del monorepo: los 6 eventos que publicaba solo los consumia el propio
Notification; ningun otro servicio ni el Postman propio de otro servicio referenciaba
`EmailAccountsController`). Ver `Responsabilidades/Hardening_Audit_Fixes_And_Notification_Migration_Plan.md`
§5 Fase 17.

**Fase 18 (2026-07-18)**: el plan original preveia retirar el sistema de plantillas/layouts
propio de Notification entero (`EmailTemplate`/`EmailTemplateVersion`/`EmailLayout`,
`FluidTemplateRenderer`, `EmailTemplatesController`, `EmailLayoutsController`) por duplicar
lo que Scribe existe para reemplazar, confirmado sin caller de frontend. La verificacion
previa (obligatoria por el propio plan, para no romper `EmailCampaigns`) encontro que
**si es una dependencia real**, no solo tablas compartidas: `ScheduleEmailCampaignHandler`
exige un `EmailTemplate` Activo con version publicada y lee el `EmailLayout` default via
`IEmailTemplateRepository`/`IEmailLayoutRepository`; `EmailCampaignBatchConsumer` llama a
`ITemplateRenderer` directo; `SendCampaignTestHandler` invoca en proceso
`SendTemplateEmailCommand`/`SendTemplateEmailHandler`. Ademas, `EmailTemplatesController`/
`EmailLayoutsController` son el UNICO punto de entrada dentro de Notification para crear
esas filas — no hay seeder. Retirarlos habria dejado a `EmailCampaigns` (fuera de alcance
de este plan, se resuelve en su propio esfuerzo) sin forma de crear una plantilla/layout
nueva jamas. Alcance real ejecutado: solo se elimino la ruta HTTP `POST
/notifications/email/send-template` (self-service ad-hoc, confirmada sin caller — ni
siquiera `EmailCampaigns` la usa por HTTP, la reusa en proceso). Todo lo demas
(`EmailTemplate`/`EmailTemplateVersion`/`EmailLayout`, `FluidTemplateRenderer`/
`ITemplateRenderer`, ambos controllers CRUD, `SendTemplateEmailCommand`/Handler, los 3
DbSets) sigue vivo — comentarios XML en cada punto explican por que. Sin migracion de DB
(no se dropeo ninguna tabla). Ver `Responsabilidades/Hardening_Audit_Fixes_And_Notification_Migration_Plan.md`
§5 Fase 18 para el detalle completo del hallazgo.

**Fase 19 (2026-07-18)**: `EmailDeliveryService` (el transporte real detras de `POST
/notifications/email/send` y de `EmailCampaigns`) enviaba SMTP directo via `ISmtpSendClient`
sin feature flag. Se agrego `PostmasterEmailDeliveryService`, una segunda implementacion de
`IEmailDeliveryService` que publica `notifications.email_send_requested.v1` hacia Postmaster
en vez de enviar directo, registrada bajo el MISMO flag `Notification:UsePostmasterDispatch`
que ya gateaba el otro path (Auth/Signature/Communication) desde una fase anterior. Se
investigo el impacto en `EmailCampaigns` antes de proceder (confirmado: campañas SI pasan por
`EmailDeliveryService` via `EmailSendRequestedIntegrationEvent`) y se determino que el cambio
es seguro — ambos flujos de campaña ya eran 100% asincronos antes de esta fase (nunca hubo
confirmacion sincronica que romper) y el fan-out ya era per-mensaje (encaja con el modelo de
Postmaster). Como `notifications.email_send_requested.v1` solo tiene un `NotificationLog`
como dueño de correlacion en el otro path, `PostmasterEmailDeliveryService` reusa el mismo
campo (`NotificationLogId`) como id opaco de `OutboundEmailMessage` en vez de crear un
`NotificationLog`; un nuevo `PostmasterOutboundEmailCallbackConsumers.cs` (paralelo a
`PostmasterCallbackConsumers.cs`) resuelve los 5 callbacks contra `IOutboundEmailRepository`.
El flag sigue en `false` por default (no se flipeo en esta fase — es tarea de la Fase 21), asi
que `EmailDeliveryService`/`EmailProviderConfigurationRepository`/`SystemNetSmtpSendClient`/
`EmailConfigurationsController` NO se retiraron (siguen siendo el comportamiento activo hoy).
Se corrigio ademas una imprecision del texto original del plan: `SmtpEmailSender` (que el plan
listaba junto a `SystemNetSmtpSendClient` para retirar) en realidad implementa `IEmailSender`,
la interfaz que usa el OTRO path (`InProcessEmailDispatchGateway`, Fase 21) — no tiene relacion
con `EmailDeliveryService`. Si se retiro `EmailWebhooksController` (webhooks de tracking
delivered/opened/clicked/bounced): nunca tuvo un `EmailWebhook:Secret` configurado en ningun
appsettings/.env del repo (401 siempre, cero llamadas reales posibles) y su propio comentario
XML ya admitia ser scaffolding especulativo para adaptadores SendGrid/Mailgun que nunca se
construyeron — Postmaster es ahora la unica fuente de tracking real para lo que routea (el
callback de bounce alimenta `OutboundEmailMessage.MarkBounced`, antes solo alcanzable por ese
webhook muerto). Build+test limpio, 1395→1406 (+11, los tests nuevos de las dos clases
agregadas). Ver `Responsabilidades/Hardening_Audit_Fixes_And_Notification_Migration_Plan.md`
§5 Fase 19 para el detalle completo.

**Fase 21 (2026-07-18) — cierre del plan de hardening**: `Notification:UsePostmasterDispatch`
se flippeo a `true` por default en las 2 capas donde el valor puede fijarse
(`appsettings.json` de `TaxVision.Notification.Api`, y el fallback de
`docker-compose.yml`: `${NOTIFICATION_USE_POSTMASTER_DISPATCH:-true}`) — antes de esta
fase ninguna de las dos definia el valor explicitamente y `GetValue<bool>` resolvia a
`false` por default de C# cuando la clave estaba ausente. Con esto, tanto
`EventBasedEmailDispatchGateway` (path Auth/Signature/Communication) como
`PostmasterEmailDeliveryService` (path `/send`+`EmailCampaigns`, agregado en la Fase 19)
quedan activos por default — comparten el mismo flag desde la Fase 19, asi que no hubo
flags separados que reconciliar. `InProcessEmailDispatchGateway`/`EmailDeliveryService`
(los paths SMTP-directo/in-process originales) **no se eliminaron**: siguen registrados
y funcionales, seleccionables con `Notification:UsePostmasterDispatch=false` como
rollback operacional explicito — retirarlos de verdad es trabajo futuro fuera de este
plan, condicionado a confianza operacional real ganada en un despliegue en produccion.
Se agrego cobertura de test nueva a nivel de `AddNotificationInfrastructure` (no existia
ningun test que ejercitara el flag SIN overridearlo explicitamente — todos los tests
existentes de ambas clases las construian directo) confirmando que, sin ninguna clave de
config, se registra el par Postmaster-based, y que `false` explicito sigue cayendo al par
in-process/SMTP-directo. **Importante**: lo que quedo verificado en esta fase es que la
suite de tests pasa bajo el nuevo default — la verificacion real en produccion (una
ventana de monitoreo con el flag en `true` contra trafico real) es un paso operacional
fuera del alcance de este repo, mismo criterio honesto ya usado para el aprovisionamiento
de cuentas MinIO en Correspondence Fase 8. Ver
`Responsabilidades/Hardening_Audit_Fixes_And_Notification_Migration_Plan.md` §5 Fase 21
para el detalle completo — cierra las 21 fases del plan de hardening
Correspondence/Connectors/Scribe/Postmaster/Notification.

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
GET    /notifications/email/messages
GET    /notifications/email/messages/{id}

# Campanas (notification.campaign.view | notification.campaign.manage)
POST   /notifications/email/campaigns
GET    /notifications/email/campaigns
GET    /notifications/email/campaigns/{id}
POST   /notifications/email/campaigns/{id}/schedule
POST   /notifications/email/campaigns/{id}/send-test
POST   /notifications/email/campaigns/{id}/cancel
```

Los permisos `notification.*` estan en `BuildingBlocks.Authorization.NotificationPermissions`
y se aplican con `[HasPermission(...)]`; TenantAdmin/PlatformAdmin pasan siempre.

## 28.3 Eventos (Wolverine/RabbitMQ)

Nuevos eventos en `BuildingBlocks/Messaging/EmailIntegrationEvents`, publicados al
exchange fanout `taxvision-events` (registrados con `PublishMessage<T>()` en
`Program.cs`) y consumidos por el propio Notification (cola durable `notification-events`):

- Envio: `EmailSendRequested`, `EmailDeliverySucceeded`, `EmailDeliveryFailed`.
- Campanas: `EmailCampaignScheduled`, `EmailCampaignStarted`, `EmailCampaignCompleted`.

Un `IHostedService` en `Api/Jobs`: `CampaignSchedulerService` (inicia campanas
programadas).

**Fase 19 (2026-07-18)**: con `Notification:UsePostmasterDispatch=true`, `EmailDeliveryService`
(el consumer de `EmailSendRequested`) publica `notifications.email_send_requested.v1` hacia
Postmaster en vez de enviar SMTP directo — mismo evento que ya usaba el path de
Auth/Signature/Communication desde una fase anterior, dos productores compartiendo un flag.
`PostmasterOutboundEmailCallbackConsumers.cs` consume los 5 callbacks de Postmaster
(`postmaster.email_delivery.{succeeded,failed,bounced,suppressed,provider_not_configured}.v1`)
y, si el `NotificationLogId` del callback corresponde a un `OutboundEmailMessage` (no a un
`NotificationLog`), transiciona el mensaje y republica `EmailDeliverySucceeded`/`Failed` para
que los contadores de campaña sigan funcionando igual que antes. Flag en `true` por default
desde la Fase 21 (2026-07-18) — `false` sigue siendo un override valido para rollback.

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

Migraciones a `NotificationDbContext` (no tocan `NotificationLogs`):

- `AddEmailProviderConfigurations`
- `AddEmailTemplatesAndLayouts`
- `AddOutboundEmailMessages`
- `AddEmailCampaigns`
- `AddEmailAccountsAndSync` (historica — creo las 5 tablas del modulo "Cuentas + Sync")
- `DropEmailAccountsAndSync` (Fase 17, 2026-07-18 — elimina esas 5 tablas al retirar el
  modulo duplicado; `Down()` las recrea completas con sus indices para rollback)

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

### 3) Envio individual

```http
POST /notifications/email/send
{ "subject": "Aviso", "htmlBody": "<p>Hola</p>", "recipients": [{ "address": "cliente@example.com" }] }
```

`POST /notifications/email/send-template` (envio ad-hoc por plantilla, self-service) se
retiro en la Fase 18 del plan de hardening (2026-07-18) — confirmado por el usuario sin
caller real, el frontend nunca lo conecto. El command/handler que renderizaba
(`SendTemplateEmailCommand`/`SendTemplateEmailHandler`) sigue vivo porque `Campana` (mas
abajo) lo reusa en proceso para su envio de prueba (`send-test`); solo se elimino la ruta
HTTP publica redundante. Devuelve `202 Accepted` con el `id` del mensaje. Consulta el estado:

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

### 5) Pruebas multitenant

Repite cualquier flujo con el `accessToken` de otro tenant y verifica que **no** ve las
configuraciones, plantillas ni campanas del primero (aislamiento por `tenant_id`).
Las plantillas/config con `scope=System` solo las gestiona un `PlatformAdmin`.

### 6) Eventos y tracking

En Grafana/Loki, filtra por `service_name="notification-service"` y sigue el
`CorrelationId` para ver la cadena `EmailSendRequested → EmailDeliverySucceeded/Failed`.
En RabbitMQ (`http://localhost:15672`) revisa la cola `notification-events`.

## 28.7 Webhooks y fan-out por lotes

- **Webhooks de tracking**: `POST /notifications/email/webhooks/tracking` se retiro en la
  Fase 19 del plan de hardening (2026-07-18) — nunca tuvo un `EmailWebhook:Secret` real
  configurado (401 siempre, ninguna llamada posible) y su propio comentario ya admitia ser
  scaffolding especulativo para un adaptador SendGrid/Mailgun que nunca se construyo.
  Postmaster es ahora la unica fuente de tracking de entrega/bounce/suppression para los
  correos que routea (bajo el flag `Notification:UsePostmasterDispatch`, `true` por
  default desde la Fase 21 del plan de hardening, 2026-07-18).
- **Fan-out por lotes**: el consumer de inicio de campana divide los destinatarios en lotes
  de 100 y publica un evento por lote (`EmailCampaignBatchIntegrationEvent`); cada lote se
  procesa en su propia transaccion, evitando una transaccion gigante.

## 28.8 Pendientes documentados

- **Verificacion end-to-end con stack real**: el flujo HTTP a CloudStorage (upload presignado
  + M2M) y el envio SMTP estan validados en compilacion; requieren los servicios levantados
  para probarse.
- **Verificacion en produccion del default `Notification:UsePostmasterDispatch=true`
  (Fase 21, 2026-07-18)**: esta sesion dejo el build+test monorepo en verde con el nuevo
  default (incluye tests nuevos que ejercitan el registro de DI sin overridear el flag,
  ver §28.1), pero eso es distinto de una verificacion en produccion — no hay stack real
  desplegado desde este repo contra el cual observar trafico. Antes de considerar el
  cutover completamente probado hace falta un despliegue real con Postmaster corriendo y
  una ventana de monitoreo real (logs/métricas de `postmaster.email_delivery.*`, sin
  regresion de entregabilidad) — el mismo criterio honesto que ya se aplico al
  aprovisionamiento de cuentas MinIO en Correspondence Fase 8. Hasta entonces,
  `Notification:UsePostmasterDispatch=false` sigue disponible como rollback inmediato sin
  tocar codigo.

## 28.9 Cierre del plan de hardening (Fases 17-21)

Esta seccion documenta, para quien llegue despues, que las Fases 17 a 21 de
`Responsabilidades/Hardening_Audit_Fixes_And_Notification_Migration_Plan.md` (§5) cierran
una migracion de fondo que atraveso varias sesiones: Notification arranco con un modulo
de conexion/sync de mailbox propio, un sistema de plantillas propio y un transporte SMTP
directo — exactamente lo que Connectors, Scribe y Postmaster se disenaron para reemplazar.
Fase 17 elimino el modulo de mailbox/sync duplicado (dominio, adaptadores, controller,
scheduler, 5 tablas). Fase 18 investigo el sistema de plantillas propio y encontro que
`EmailCampaigns` (fuera de alcance, migra en su propio esfuerzo) depende en vivo de casi
todo — solo se retiro la ruta HTTP self-service genuinamente huerfana. Fase 19 le dio a
`EmailDeliveryService` (el transporte real detras de `/send` y de `EmailCampaigns`) un
segundo camino event-based hacia Postmaster, bajo el mismo flag que ya gateaba el otro
path desde una fase anterior. Fase 20 elimino scaffolding sin consumidor
(`UserNotificationPreference`). Fase 21 (esta) flippeo ese flag compartido a `true` por
default, dejando el transporte real de Notification apuntando a Postmaster salvo rollback
explicito — sin eliminar el codigo SMTP-directo/in-process, que queda como fallback hasta
que una fase futura (fuera de este plan) tenga confianza operacional para retirarlo.
`EmailCampaigns` (el feature de campañas masivas) queda explicitamente fuera de todo este
esfuerzo por instruccion del usuario — se resuelve en su propio esfuerzo cuando le toque
migrar.

# 29. Signature Service (firma electronica multi-tenant)

`Signature` es el microservicio autoritativo del ciclo de vida de firmas electronicas
para las oficinas de impuestos: preparacion del documento, invitacion a firmantes
externos, captura de firma, verificacion multi-canal (PIN, SMS/Email OTP, KBA
extensible), sellado criptografico PAdES y cadena de audit HMAC.

Cumple exactamente el patron del resto del repo (Clean Architecture, CQRS con
Wolverine, outbox/inbox durable, aislamiento multi-tenant por JWT, Result pattern,
correlation end-to-end, OTLP) y **nunca** posee bytes de archivos: los delega a
`CloudStorage` mediante el mismo flujo POST-policy que Notification.

## 29.1 Limite del bounded context

- El microservicio ES autoritativo sobre: solicitud de firma, firmantes, campos de
  firma sobre el PDF, tokens firmados del firmante externo, evidencia de captura,
  retos de verificacion, PIN del practitioner, preparer PTIN/EFIN, snapshot analytics
  diario, cadena de audit HMAC y sellado PAdES.
- El microservicio NO es autoritativo sobre: bytes de PDF originales o sellados
  (delegados a `CloudStorage` con `OwnerType=Signature`), envio de email/SMS
  (delegado a `Notification` via eventos), identidad de staff (delegada a `Auth`).
- No hay endpoint publico que suba PDF: la solicitud se crea con `originalFileId`
  ya existente en CloudStorage, previamente subido con permiso
  `cloudstorage.file.upload` y `OwnerType=Signature`.

## 29.2 Estructura de proyectos

Cuatro proyectos en `src/Services/Signature/`:

- `TaxVision.Signature.Domain`
- `TaxVision.Signature.Application`
- `TaxVision.Signature.Infrastructure`
- `TaxVision.Signature.Api`

## 29.3 Aggregates y value objects

- `SignatureRequest` aggregate root: gobierna signers, fields, transiciones
  Draft → ReadyToSend → Sent → Completed → Sealed | Canceled | Expired, y las
  ventanas legal-hold + expiration.
- `Signer` entity: guarda estado (Pending/Notified/Consent/Verified/Signed/Rejected),
  metodo de captura (`Typed`/`Drawn`/`Uploaded`), evidencia (`typedName`,
  `signatureImageFileId`), verificacion (`PractitionerPin`, `OtpChallenge`).
- `SignatureField` entity: `Kind` (Signature/Initials/Date/Text/Checkbox), pagina y
  `FieldPosition` con **coordenadas normalizadas en `[0.0, 1.0]`** (fracciones
  del ancho/alto de la pagina). El VO valida `x, y, w, h ∈ [0, 1]` y ademas
  `x + w <= 1`, `y + h <= 1`. La API devuelve `Signature.FieldPosition.Origin`,
  `Signature.FieldPosition.Size` o `Signature.FieldPosition.Overflow` si el
  request usa puntos PDF o pixeles. El motor de sellado multiplica por
  `page.Width.Point` / `page.Height.Point` en tiempo real, asi el mismo campo
  funciona con cualquier tamano de pagina (Letter, A4, Legal).
- `SignatureTemplate` aggregate root: reutilizable por tenant con `Slots` (roles) y
  `Fields` fijas; instanciar produce una `SignatureRequest` lista para asignar
  signers concretos al slot.
- `SignatureAnalyticsSnapshot` aggregate root: proyeccion diaria event-sourced
  (una fila por dia+tenant); alimentada por consumers propios.
- `SignatureAuditEvent`: append-only HMAC-chain (ver 29.9).
- `ConsentEvent`: append-only del consentimiento aceptado por firmante.
- Value objects: `PractitionerPin`, `Preparer` (`PtinOrEfin`, `DisplayName`,
  `TitleLabel`), `SignerFullName`, `SignerVerificationMethod`,
  `SignatureCaptureMethod`.

## 29.4 Persistencia

Base `TaxVision_Signature`. Migraciones aplicadas en orden:

- `InitialSignature`: `TenantSignatureSettings` + `wolverine_*`.
- `AddSignatureRequests`: `SignatureRequests`, `Signers`, `SignatureFields`.
- `AddSignatureProjections`: proyecciones locales `CustomerEmailProjection` y
  `FileMetadataRef` (evita HTTP entre servicios).
- `AddSignerConsentAndFirstView`: consent + first-view tracking en `Signers`.
- `AddSignatureTemplates`: `SignatureTemplates`, `TemplateSlots`, `TemplateFields`.
- `AddPractitionerPinVerification`: hash del PIN y estado Verified en `Signers`.
- `AddPreparerAndVerificationFramework`: `Preparer_*` en request; `OtpChallenge_*`
  en signer (framework extensible por `SignerVerificationMethod`).
- `AddSignatureAnalytics`: `SignatureAnalyticsSnapshots` (`(TenantId, Day)` unique).
- `AddValidationConsentAudit`: `DocumentValidations`, `ConsentEvents` y
  `SignatureAuditEvents` (append-only HMAC).
- `AddSchedulingLegalHoldAndUserPermissions`: `LegalHold*` en request,
  `ExpirationReminderSentUtc`, `UserPermissionsProjection` (proyeccion desde Auth).
- `AddCaptureMethodEvidence`: `CaptureMethod`, `TypedName`,
  `SignatureImageFileId` en `Signers`.

Filtro global de tenant: `SignatureDbContext` aplica `HasQueryFilter` con
reflexion sobre entidades `ITenantOwned` usando el predicado
`!hasTenant || e.TenantId == currentTenant` (seguro para background jobs sin
contexto de request como el `PurgeScheduler`).

## 29.5 Comunicacion con otros microservicios

| Origen | Destino | Canal | Uso |
| --- | --- | --- | --- |
| Cliente staff | Gateway → Signature | HTTPS `/signature/*` | CRUD solicitudes, plantillas, analytics |
| Firmante externo | Gateway → Signature | HTTPS `/signature/public/{token}/*` | Ver, consentir, verificar, firmar, rechazar |
| Auth | Signature | RabbitMQ | `TenantCreated` → siembra settings; `UserPermissionsChanged` → proyeccion RBAC |
| Customer | Signature | RabbitMQ | `Customer*` → proyeccion `CustomerEmailProjection` (evita HTTP) |
| CloudStorage | Signature | RabbitMQ | `FileAvailable` / `FileDeleted` → proyeccion `FileMetadataRef` |
| Subscription | Signature | RabbitMQ | Reservado para limites por plan (asientos/solicitudes) |
| Signature | Notification | RabbitMQ | `SignerInvited`, `SignerVerificationChallengeIssued`, `SignatureRequestReminderDue`, `SignatureRequestExpired`, `SignatureRequestCompleted`, `SignerRejected` → templates `sig.*.v1` |
| Signature | CloudStorage | HTTPS + JWT del usuario y token M2M | Descargar PDF original, subir PDF sellado + certificado |
| Signature | Analytics propios | Wolverine local | Consumers propios de sus eventos → snapshot diario |

## 29.6 Endpoints staff

Base `/signature` bajo el Gateway. Autorizacion por `[HasPermission(...)]` con
codigos del catalogo `signature.*` (28 permisos totales, sembrados por la migracion
`AddSignaturePermissions` en Auth). TenantId siempre del JWT, nunca del body.

### 29.6.1 Documents preflight

| Verbo y ruta | Permiso |
| --- | --- |
| POST `/signature/documents/validate` | `signature.request.create` |

Multipart preflight (max 25 MB). Valida MIME, tamano, integridad, numero de paginas
y firmas previas antes de crear una `SignatureRequest`. Regla P-04 del diseño: nunca
crear una request sobre un PDF que no paso por aqui.

### 29.6.2 Signature requests

| Verbo y ruta | Permiso |
| --- | --- |
| POST `/signature/requests` | `signature.request.create` |
| GET `/signature/requests` | `signature.request.read` |
| GET `/signature/requests/{id}` | `signature.request.read` |
| POST `/signature/requests/{id}/signers` | `signature.request.create` |
| DELETE `/signature/requests/{id}/signers/{signerId}` | `signature.request.create` |
| PUT `/signature/requests/{id}/signers/order` | `signature.request.create` |
| POST `/signature/requests/{id}/fields` | `signature.document.prepare` |
| DELETE `/signature/requests/{id}/signers/{signerId}/fields/{fieldId}` | `signature.document.prepare` |
| POST `/signature/requests/{id}/send` | `signature.request.create` |
| POST `/signature/requests/{id}/cancel` | `signature.request.cancel` |
| POST `/signature/requests/{id}/extend-expiration` | `signature.request.resend` |
| POST `/signature/requests/{id}/signers/{signerId}/resend` | `signature.request.resend` |
| PUT `/signature/requests/{id}/practitioner-pin` | `signature.request.create` |
| DELETE `/signature/requests/{id}/practitioner-pin` | `signature.request.create` |
| POST `/signature/requests/{id}/legal-hold` | `signature.document.audit.read` |
| DELETE `/signature/requests/{id}/legal-hold` | `signature.document.audit.read` |
| PUT `/signature/requests/{id}/preparer` | `signature.request.create` |
| DELETE `/signature/requests/{id}/preparer` | `signature.request.create` |
| POST `/signature/requests/{id}/preparer/sign` | `signature.document.sign` |

`GET /signature/requests` acepta query params `status`, `category`, `page`, `size`;
la lectura pasa por `CachedSignatureRequestReadService` con TTL 30s en Redis
(clave versionada `v1`).

**`POST /signature/requests/{id}/fields`** — el body espera coordenadas
normalizadas en `[0.0, 1.0]` (fracciones de las dimensiones de la pagina), NO
puntos PDF ni pixeles. El VO `FieldPosition` valida `x, y, w, h ∈ [0, 1]` y
ademas `x + w <= 1`, `y + h <= 1`. Ejemplo de body:

```json
{
  "signerId": "...",
  "kind": "Signature",
  "page": 1,
  "x": 0.20,
  "y": 0.83,
  "width": 0.30,
  "height": 0.05,
  "label": "Firma del contribuyente",
  "isRequired": true
}
```

Errores comunes: `Signature.FieldPosition.Origin` si `x` o `y` estan fuera de
`[0, 1]`; `Signature.FieldPosition.Size` si `width` o `height` no cumplen;
`Signature.FieldPosition.Overflow` si `x + w` o `y + h` exceden `1`.

### 29.6.3 Signature templates

| Verbo y ruta | Permiso |
| --- | --- |
| POST `/signature/templates` | `signature.template.create` |
| GET `/signature/templates` | `signature.template.create` |
| GET `/signature/templates/{id}` | `signature.template.create` |
| PUT `/signature/templates/{id}/metadata` | `signature.template.update` |
| PUT `/signature/templates/{id}/defaults` | `signature.template.update` |
| POST `/signature/templates/{id}/slots` | `signature.template.update` |
| DELETE `/signature/templates/{id}/slots/{slotOrder}` | `signature.template.update` |
| POST `/signature/templates/{id}/fields` | `signature.template.update` |
| DELETE `/signature/templates/{id}/fields/{fieldId}` | `signature.template.update` |
| POST `/signature/templates/{id}/publish` | `signature.template.update` |
| POST `/signature/templates/{id}/archive` | `signature.template.delete` |
| POST `/signature/templates/{id}/instantiate` | `signature.request.create` |

Instanciar liga cada slot de la plantilla a un signer concreto (email + nombre) via
`slotBindings`, copia los `TemplateFields` como `SignatureFields` de la request y
devuelve `201 /signature/requests/{id}`.

### 29.6.4 Analytics

| Verbo y ruta | Permiso |
| --- | --- |
| GET `/signature/analytics/summary?from=&to=` | `signature.request.read` |
| GET `/signature/analytics/timeline?from=&to=` | `signature.request.read` |
| GET `/signature/analytics/by-category?from=&to=` | `signature.request.read` |

Rango default: ultimos 30 dias. Datos del snapshot diario alimentado por consumers
propios de `SignatureRequestCreated/Completed/Canceled/Expired` y
`DocumentSigned` (no consulta la tabla operacional).

### 29.6.5 Settings

| Verbo y ruta | Permiso |
| --- | --- |
| GET `/signature/settings` | `signature.settings.manage` |
| PUT `/signature/settings` | `signature.settings.manage` |

**GET** devuelve la configuracion vigente del tenant.

**PUT** reemplaza toda la configuracion del tenant (semantica PUT: se deben enviar
todos los campos). El TenantId se toma del JWT; los admins solo pueden modificar
su propio tenant.

```json
{
  "allowedVerificationChannels": ["Email", "Sms"],
  "defaultVerificationChannel": "Email",
  "defaultTokenExpirationHours": 168,
  "remindersEnabledByDefault": true,
  "generateCertificateByDefault": true,
  "documentLimits": {
    "maxPdfBytes": 26214400,
    "maxImageBytes": 10485760,
    "maxPagesPerDocument": 100
  },
  "retentionPolicy": {
    "retentionYears": 7,
    "allowPurge": false
  }
}
```

Canales validos: `Email`, `Sms`, `PractitionerPin` (y `WhatsApp` / `AuthenticatorApp` /
`KnowledgeBased` reservados para fases futuras). El `defaultVerificationChannel` debe
estar incluido en `allowedVerificationChannels`.

Limites: `maxPdfBytes` 1 KB – 200 MB; `maxImageBytes` 1 KB – 200 MB;
`maxPagesPerDocument` 1 – 1000; `retentionYears` 1 – 20.

**Restricciones de plan**: cada campo del PUT es validado contra `PlanConstraints` del
tenant antes de aplicar los cambios. Si el valor excede el techo del plan se retorna
`400`. El GET incluye un objeto `planConstraints` con los techos actuales del plan para
que el frontend pueda deshabilitar las opciones que los excedan.

Respuestas: `204 No Content` en exito, `400` si los valores violan invariantes del
dominio o exceden los limites del plan, `404` si el tenant aun no tiene settings (se
crean automaticamente al recibir `TenantCreatedIntegrationEvent`).

### 29.6.6 Signature Plan Constraints (Platform Admin)

| Verbo y ruta | Permiso |
| --- | --- |
| PUT `/admin/tenants/{tenantId}/signature-constraints` | `signature.constraints.manage` |

Endpoint exclusivo de la **plataforma** (PlatformAdmin). Establece los techos de plan
para un tenant. La configuracion existente del tenant se auto-corrige si excede los
nuevos limites (canales no permitidos se deshabilitan, limites se clampean, retention
se eleva al minimo si estaba por debajo).

```json
{
  "maxAllowedPdfBytes": 52428800,
  "maxAllowedImageBytes": 10485760,
  "maxAllowedPages": 200,
  "minRetentionYears": 7,
  "purgeAllowed": false,
  "allowedChannels": ["Email", "Sms", "PractitionerPin"],
  "maxTokenExpirationHours": 720
}
```

Defaults plan basico: 25 MB PDF, 10 MB imagen, 100 paginas, 7 anos minimo,
purge deshabilitada, Email + SMS, 720 h token (30 dias).

Respuestas: `204 No Content` en exito, `400` si los valores superan los techos absolutos
del dominio Signature, `401` si el JWT no tiene `signature.constraints.manage`, `404` si
el tenant no existe.

**Eventos publicados**:
- `SignaturePlanConstraintsUpdatedIntegrationEvent` → RabbitMQ `taxvision-events`
  (Notification para informar al tenant admin; Billing/Subscription para reconciliar plan).

**Eventos del PUT /signature/settings**:
- `SignatureSettingsUpdatedIntegrationEvent` → RabbitMQ `taxvision-events`
  (Audit service para log de cambios; Notification para confirmacion al admin).

### 29.6.7 JWKS

| Verbo y ruta | Acceso |
| --- | --- |
| GET `/signature/.well-known/jwks.json` | anonimo |

Publica la clave publica RSA del `SigningTokenService` para que verificadores
terceros validen los tokens del firmante sin acceso a la BD.

## 29.7 Endpoints publicos del firmante

Base `/signature/public/{token}`. El token codifica firmado
`TenantId + RequestId + SignerId + RevocationEpoch + exp` (RS256). Todos los
endpoints comparten policy de rate limit `public-signature` (15 req/min por
IP+ruta). El TenantContext se rehidrata desde el propio token — no requiere JWT
del portal.

| Verbo y ruta | Uso |
| --- | --- |
| GET `/signature/public/{token}` | Vista inicial: metadata, campos, PDF (URL temporal), estado |
| POST `/signature/public/{token}/consent` | Aceptar consentimiento IRS / ESIGN Act |
| POST `/signature/public/{token}/verify-pin` | Verificar el PIN opcional del practitioner |
| POST `/signature/public/{token}/challenge` | Emitir reto (SMS OTP / Email OTP / KBA) — cubre resend (cooldown 30s) y switch-channel |
| POST `/signature/public/{token}/verify-challenge` | Verificar la respuesta al reto |
| POST `/signature/public/{token}/sign` | Enviar la firma con `Method` + evidencia (typedName o signatureImageFileId) |
| POST `/signature/public/{token}/reject` | Rechazar con motivo (marca request `Rejected`) |
| GET `/signature/public/{token}/verify-audit` | Verifica publicamente la cadena de audit HMAC |

Body de `sign`:

```json
{
  "method": "Typed",
  "typedName": "Jorge Turbi"
}
```

o

```json
{
  "method": "Drawn",
  "signatureImageFileId": "8f58a521-4c25-4d91-9f4e-7ad5df14c001"
}
```

## 29.8 Sealing PAdES (cripto)

El sellado se dispara al completarse la ultima firma del ultimo signer. Un
consumer envuelve la pipeline con un lock distribuido Redis
(`signature:sealing:{requestId}` TTL 10 min) para evitar doble sellado en despliegues
multi-nodo. Fallback a `NoOpDistributedLock` si no hay Redis configurado.

- **Font resolver global (obligatorio)**: PdfSharp 6.x es puro managed .NET sin
  backend GDI y requiere un `IFontResolver`. `SealingFontResolver` se registra
  una sola vez en `AddSignatureInfrastructure`, busca TTFs en `C:\Windows\Fonts`
  (Windows), `/usr/share/fonts/**` (Linux) y `/System/Library/Fonts` (macOS), y
  aliasa `"Helvetica"` → Arial / Liberation Sans / DejaVu Sans. Sin este
  resolver, `new XFont(...)` explota con
  `No appropriate font found for family name`.
- **PAdES-B (Baseline)**: firma CMS/PKCS#7 sobre el hash pre-sellado del PDF via
  BouncyCastle (`PadesCmsSigner` + `PadesBSealer`). El `Signature Dictionary` con
  `/ByteRange` y `/Contents` placeholder se materializa en el post-process
  byte-level (ver 29.14). El PFX se carga con
  `EphemeralKeySet | Exportable` — sin `Exportable` Windows CNG bloquea el
  `RSA.ExportParameters(true)` que BouncyCastle necesita.
- **PAdES-B-T (Timestamp RFC 3161)**: si `Signature:Sealing:Tsa:Endpoint` esta
  configurado, `FreeTsaClient` obtiene un token DER que se embebe como
  `unsignedAttribute` (OID `1.2.840.113549.1.9.16.2.14`). Default: FreeTSA
  (`https://freetsa.org/tsr`). `PadesCmsSigner.AddTimestampAttributeAsync`
  **hashea SHA-256 la `signer.GetSignature()` antes de pasarla al TSA** — el TSA
  recibe siempre un digest de 32 bytes segun RFC 3161 §2.4.1, nunca la firma
  raw de 256 bytes.
- **PAdES-B-LT (Long-Term Validation)**: tras B-T, se emite un revision
  incremental que incluye el `DSS Dictionary` con la cadena de certificados
  (`/Certs`), CRLs (`/CRLs`) y respuestas OCSP (`/OCSPs`). Ver 29.15.
- **Rendering profesional**: `PdfSharpCertificateRenderer` produce el
  Certificate of Completion con branding **TaxProCore**, hashes SHA-256
  chunked cada 8 chars (convencion DocuSign / Adobe Sign) y cards por signer
  con status pills; `PdfSharpSealingEngine.DrawFieldBox` dibuja la firma sin
  fondo azul: nombre del signer en **Times BoldItalic** imitando manuscrita,
  caption `DIGITALLY SIGNED BY` arriba, timestamp UTC abajo y una franja
  acento azul marino de 2pt a la izquierda.
- Salida: el PDF sellado se sube a CloudStorage con `OwnerType=Signature`,
  `FolderType=Signatures`, `TaxYear=CompletedAtUtc.Year` usando el token M2M
  `signature-worker`. Objeto resultante en el bucket `taxvision-storage`:
  `tenants/.../signatures/{year}/signed-{requestId}.pdf` (mas
  `certificate-{requestId}.pdf` si `generateCertificate=true`).

## 29.9 Cadena de audit HMAC

Cada evento relevante (`RequestCreated`, `RequestSent`, `SignerNotified`,
`SignerViewed`, `SignerConsented`, `SignerVerified`, `SignerSigned`,
`RequestSealed`, `RequestCanceled`, `RequestExpired`) se materializa como
`SignatureAuditEvent` con hash HMAC-SHA256 encadenado al anterior. La clave HMAC
por tenant se deriva del `Encryption:MasterKey` compartido via HKDF.

`HmacAuditChainAppender` (write path) y `HmacAuditChainVerifier` (read path)
comparten la misma formula sobre `(TenantId, RequestId, Kind, PayloadHash,
OccurredAt, PreviousHash)`. El endpoint publico `/verify-audit` devuelve el veredicto
y todos los eventos con material verificable, sin exponer el HMAC ni permitir mutar
nada.

## 29.10 Integration events publicados

`taxvision-events` fanout. Contratos en `BuildingBlocks/Messaging/SignatureIntegrationEvents/`:

- Ciclo de vida request: `SignatureRequestCreated`, `SignatureRequestReadyForSending`,
  `SignatureRequestSent`, `SignatureRequestCanceled`, `SignatureRequestExpirationExtended`,
  `SignatureRequestReminderDue`, `SignatureRequestExpired`, `SignatureRequestCompleted`,
  `SignatureRequestSealed`, `SignatureRequestSealingFailed`.
- Ciclo signer: `SignerInvited`, `SignerConsentAccepted`, `DocumentSigned`,
  `SignerRejected`, `SignerPinVerified`, `SignerPinFailed`,
  `SignerVerificationChallengeIssued`, `SignerVerificationSucceeded`,
  `SignerVerificationFailed`.
- Preparer: `PreparerSigned`.

Ninguno transporta identificadores fiscales, PIN, OTP, ni SSN. Solo IDs, timestamps,
metodo, canal y contadores. Consumers tipicos: Notification (envio), Analytics
(snapshot), Communication (a futuro, notificaciones in-app).

## 29.11 Background schedulers

- `ExpirationScheduler` (BackgroundService): marca `SignatureRequest` como
  `Expired` cuando pasa `ExpiresAtUtc` y publica `SignatureRequestExpired`.
- `ReminderScheduler` (BackgroundService): identifica requests con firmantes
  pendientes cerca de expirar y publica `SignatureRequestReminderDue`
  (Notification consume la plantilla `sig.reminder.v1`).
- `PurgeScheduler` (BackgroundService, feature flag OFF por default): purga
  soft-deleted y requests fuera de retencion, respetando legal-hold. Config
  `Signature:Purge:*`.

## 29.12 Configuracion y User Secrets

Requeridos para `TaxVision.Signature.Api`:

```powershell
dotnet user-secrets set "ConnectionStrings:Default" "<SIGNATURE_CONNECTION>" `
  --project src\Services\Signature\TaxVision.Signature.Api

dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" `
  --project src\Services\Signature\TaxVision.Signature.Api

dotnet user-secrets set "RabbitMq:Uri" "amqp://taxvision:<password-url-encoded>@localhost:5672" `
  --project src\Services\Signature\TaxVision.Signature.Api

dotnet user-secrets set "Jwt:Secret" "<SAME_HS256_SECRET>" `
  --project src\Services\Signature\TaxVision.Signature.Api

dotnet user-secrets set "Encryption:MasterKey" "<BASE64_32_BYTES>" `
  --project src\Services\Signature\TaxVision.Signature.Api
```

Opcionales avanzados:

```powershell
# --- CMS signer (PAdES-B) ---
# Sin CertificatePath el DI no registra ICmsPdfSigner y el sealing degrada
# a "visual stamp only" (util para dev sin PFX; sin firma criptografica).
# El PFX debe generarse via .NET (RSA.Create + CertificateRequest + cert.Export)
# porque PowerShell New-SelfSignedCertificate produce keys que Windows CNG
# marca como no exportables al recargarse con EphemeralKeySet.
dotnet user-secrets set "Signature:Sealing:Cms:CertificatePath" "<C:\keys\signature.pfx>" `
  --project src\Services\Signature\TaxVision.Signature.Api
dotnet user-secrets set "Signature:Sealing:Cms:CertificatePassword" "<pfx-password>" `
  --project src\Services\Signature\TaxVision.Signature.Api

# --- TSA RFC 3161 para PAdES-B-T ---
# Default: FreeTSA. El PadesCmsSigner hashea SHA-256 la firma antes de pedir
# el timestamp (RFC 3161 §2.4.1 exige digest de 32 bytes, no la firma raw).
dotnet user-secrets set "Signature:Sealing:Tsa:Endpoint" "https://freetsa.org/tsr" `
  --project src\Services\Signature\TaxVision.Signature.Api

# --- M2M para llamar a CloudStorage sin usuario (background sealing worker) ---
# El prefijo correcto es Signature:ServiceAuth (no ServiceAuthClient, que era
# un bug historico). Auth service debe tener el cliente signature-worker
# registrado en ServiceAuth:Clients con los permisos correspondientes.
dotnet user-secrets set "Signature:ServiceAuth:AuthBaseUrl" "http://localhost:5124" `
  --project src\Services\Signature\TaxVision.Signature.Api
dotnet user-secrets set "Signature:ServiceAuth:ClientId" "signature-worker" `
  --project src\Services\Signature\TaxVision.Signature.Api
dotnet user-secrets set "Signature:ServiceAuth:ClientSecret" "<secret-fuerte>" `
  --project src\Services\Signature\TaxVision.Signature.Api

dotnet user-secrets set "Signature:CloudStorage:BaseUrl" "http://localhost:5330" `
  --project src\Services\Signature\TaxVision.Signature.Api

# --- Clave RSA persistente para tokens del firmante externo ---
# Sin esto, RsaSigningKeyProvider genera una RSA-2048 efimera al arrancar y
# todos los links de firma activos se invalidan al reiniciar el servicio.
dotnet user-secrets set "Signature:SignerJwt:PrivateKeyPem" "<C:\...\dev-keys\jwt-private.pem>" `
  --project src\Services\Signature\TaxVision.Signature.Api

# --- PurgeScheduler (default OFF; solo produccion con backup verificado) ---
dotnet user-secrets set "Signature:Purge:Enabled" "false" `
  --project src\Services\Signature\TaxVision.Signature.Api
```

Auth service (`c9f518b8-3374-4abd-a7b6-f0b20cb0877f`) debe declarar el cliente
M2M `signature-worker` con los permisos `cloudstorage.file.download`,
`cloudstorage.file.view` y `cloudstorage.file.upload`. En user secrets del
Auth:

```json
{
  "ServiceAuth:Clients:0:ClientId": "signature-worker",
  "ServiceAuth:Clients:0:Secret": "<mismo-secret>",
  "ServiceAuth:Clients:0:Permissions:0": "cloudstorage.file.download",
  "ServiceAuth:Clients:0:Permissions:1": "cloudstorage.file.view",
  "ServiceAuth:Clients:0:Permissions:2": "cloudstorage.file.upload"
}
```

Variables Docker equivalentes (`.env`):

```env
SIGNATURE_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Signature;User Id=sa;Password=<clave>;TrustServerCertificate=true
SIGNATURE_SERVICE_CLIENT_ID=signature-worker
SIGNATURE_SERVICE_CLIENT_SECRET=<secret-fuerte>
Signature__Sealing__Cms__CertificatePath=/keys/signature.pfx
Signature__Sealing__Cms__CertificatePassword=<pfx-password>
Signature__Sealing__Tsa__Endpoint=https://freetsa.org/tsr
Signature__SignerJwt__PrivateKeyPem=/keys/jwt-private.pem
```

Para el font resolver en Docker Linux instala `fonts-liberation` (una fuente
sans-serif compatible con Helvetica) en el image base:

```dockerfile
RUN apt-get update && apt-get install -y --no-install-recommends fonts-liberation \
  && rm -rf /var/lib/apt/lists/*
```

## 29.13 Gateway

Ruta YARP `/signature/{**catch-all}` enrutada al cluster `signature` en
`http://localhost:5340/`. Health check `signature-api` agregado a `/health/ready`
del Gateway. Rate limiting adicional en el Gateway sobre los publicos:

- `POST /signature/public/{token}/challenge` — reto (respalda cooldown de 30s del aggregate)
- `POST /signature/public/{token}/verify-challenge` — 5 intentos por token antes de lock
- `POST /signature/public/{token}/sign` — 3 intentos por token antes de invalidar

## 29.14 PAdES-B ByteRange (post-process byte-level)

El sellado PAdES exige un **Signature Dictionary** dentro del PDF con dos claves
que solo pueden materializarse tras conocer el offset final del blob CMS: `/ByteRange`
y `/Contents`. El proceso es:

1. `PdfSharpSealingEngine` estampa la representacion visual de cada firma (imagen o
   texto) y agrega una entrada `/Sig` en el `AcroForm` con placeholders vacios.
2. `ByteRangePlaceholderInjector` (`Infrastructure/Sealing/ByteRange/`) hace surgery
   byte-level sobre el PDF resultante:
   - localiza el token `/ByteRange [0 0 0 0]` y el `/Contents <00...00>` de tamano
     fijo (default 16 KB en hex, ajustable via `Signature:Sealing:ContentsReservedBytes`);
   - reemplaza el placeholder `/ByteRange` por
     `[0 sigOffset (sigOffset+contentsLen) (docLen-(sigOffset+contentsLen))]`;
   - reescribe solo esos bytes (no re-serializa el PDF).
3. `PadesHasher` calcula el `messageDigest` SHA-256 sobre los rangos declarados en
   `/ByteRange` (excluyendo el hueco de `/Contents`).
4. `BouncyCastleCmsPdfSigner` firma ese digest y produce el CMS DER; opcionalmente
   agrega el `unsignedAttribute` de timestamp (PAdES-B-T).
5. `ContentsWriter` escribe el DER hexeado dentro del hueco de `/Contents`,
   preservando la longitud reservada (padding con ceros).
6. Resultado: PDF cuya firma verifica en Adobe Acrobat con "Signature is valid" y
   "Timestamp signature is valid" si TSA esta habilitada.

Cada clase tiene una unica responsabilidad — inyector, hasher, signer y writer no
comparten estado ni conocimiento. Fallar rapido y con codigo de error propio:
`Signature.PadesB.PlaceholderNotFound`, `Signature.PadesB.ContentsOverflow`,
`Signature.PadesB.HashMismatch`.

## 29.15 PAdES-B-LT (Long-Term Validation)

Tras completar B-T, `LongTermValidationEnricher` produce una revision incremental
al final del PDF con:

- **`/DSS` Dictionary** (Document Security Store) con arrays `/Certs`, `/CRLs`,
  `/OCSPs`.
- Certificados de la cadena del signer y del TSA (embebidos como streams).
- CRLs frescos obtenidos de los `CRL Distribution Points` de cada certificado
  (`CrlFetcher` HTTP con timeout 15s, cache Redis por dia).
- OCSP responses de cada cert intermedio contra los `Authority Info Access` de la
  cadena (`OcspFetcher` con nonce, cache Redis 6h).
- `/VRI` (Validation Related Information) dictionary indexado por hash de la firma
  para que el validador encuentre rapidamente la evidencia de esa firma concreta.

Requerimientos operativos: `OcspFetcher`/`CrlFetcher` toleran fallos parciales;
si un cert no publica CRL/OCSP se registra `Signature.PadesLt.EvidenceMissing`
con detalle y se persiste igual (validador reportara "no CRL"). Cache Redis
opcional; sin Redis va a HTTP en vivo.

## 29.16 CA comercial en produccion (documentacion)

El pipeline PAdES no impone la CA. Para produccion se recomienda una CA comercial
adherida a EUTL/AATL (Adobe Approved Trust List) o Adobe AATL, sin cambios de
codigo — solo configuracion:

```env
Signature__Sealing__Cms__CertificatePath=/keys/prod-signature.pfx
Signature__Sealing__Cms__CertificatePassword=${SIGN_CERT_PWD}
```

Opciones tipicas:

- **DigiCert Document Signing Trust Assured** (AATL + EUTL) — recomendada para
  documentos IRS y firmas de contratos internacionales.
- **GlobalSign Document Signing Certificate** — alternativa con precios agresivos
  para pymes.
- **Sectigo Document Signing** — otra AATL comun.
- **eIDAS QES (Qualified Electronic Signature)** — obligatoria para firmas
  legalmente equivalentes a manuscrita en la UE (D-Trust, Buypass, Certipost).

El PFX se monta como secreto (Key Vault, AWS Secrets Manager, Docker secret) —
nunca se compromete al repositorio. La TSA en produccion debe apuntar a un TSA
comercial (DigiCert Timestamp, Sectigo Timestamp) en lugar de FreeTSA:

```env
Signature__Sealing__Tsa__Endpoint=http://timestamp.digicert.com
Signature__Sealing__Tsa__RequestCertificate=true
```

## 29.17 Aplicar migraciones

```powershell
dotnet ef database update `
  --project src\Services\Signature\TaxVision.Signature.Infrastructure\TaxVision.Signature.Infrastructure.csproj `
  --startup-project src\Services\Signature\TaxVision.Signature.Api\TaxVision.Signature.Api.csproj
```

## 29.18 Pruebas

Proyecto en `deploy/tests/TaxVision.Signature.Tests`. Cubre a nivel de dominio
(sin BD) los invariantes criticos:

- transiciones de la aggregate root y errores esperados;
- consent + first-view no pisan valores previos;
- practitioner PIN se hashea antes de comparar; N intentos → lock;
- `SignatureCaptureMethod` valida evidencia por metodo (Typed / Drawn / Uploaded);
- cooldown 30s en resend + bypass en switch-channel;
- token de firmante rechaza expirado y epoch obsoleto;
- audit HMAC-chain: recomputo por evento y deteccion de manipulacion;
- template instantiate copia fields y liga slots correctamente.

Testcontainers (SQL + Rabbit + Redis end-to-end) no forma parte de esta iteracion —
se deja al pipeline general del proyecto cuando se defina la estrategia comun de
integracion.

## 29.19 Pendientes reales

- **PAdES-B-LT DSS/VRI**: implementacion base descrita en 29.15; falta ejercitar
  contra CAs comerciales reales (hoy testeado contra self-signed + FreeTSA).
- **KBA (Knowledge-Based Authentication)**: framework de verificacion soporta
  agregar `SignerVerificationMethod.Kba`; falta el adapter contra un proveedor
  comercial (LexisNexis, IDology).
- **App-based challenge**: canal `SignerVerificationMethod.App` reservado — falta
  el consumer que empuje push notification via Communication.
- **Retention policies**: `PurgeScheduler` respeta legal-hold pero la politica por
  categoria (`Form 1040 = 7 anos`, `1099 = 4 anos`) hoy es un default global.
- **Signature UI staff assets**: hoy el frontend consume los DTOs; falta un
  visor PDF-in-browser con drag-and-drop de fields sobre paginas (fuera de
  scope del backend).

# 30. Communication Service (chat, calls, meetings, notifs realtime)

`Communication` es el **unico microservicio en Node.js/TypeScript** del stack.
Cubre chat 1:1, llamadas WebRTC, meetings multi-party, notifications in-app
realtime, support cross-tenant y analytics diario. Reemplaza al legacy
`RealTimeService` (Node.js + Socket.IO) del CRMTAXPROBACKEND cerrando 18 CRIT
de seguridad y multi-tenant.

## 30.1 Stack

| Capa | Tecnologia |
|---|---|
| Runtime | Node.js >= 20.11 + TypeScript strict |
| HTTP | Fastify 5 (+ helmet, cors, rate-limit, sensible) |
| Realtime | Socket.IO 4 + `@socket.io/redis-adapter` (backplane multi-pod) |
| Persistencia | Prisma 5 sobre SQL Server (`TaxVision_Communication`) |
| Bus eventos | RabbitMQ (`amqplib`) al exchange `taxvision-events` |
| Cache/lock/presence | Redis (`ioredis`) |
| Auth | Verificacion JWT RS256 via JWKS remoto de Auth (`jose`) |
| Validacion | Zod en boundaries + branded types en dominio |
| Logs | Pino JSON estructurado |
| Observabilidad | OpenTelemetry SDK Node -> OTLP |
| Testing | Vitest |

## 30.2 Layout DDD

```text
src/
+-- domain/            # aggregates, VOs, ports (interfaces)
|   +-- conversations/ # Conversation + Message + Participant
|   +-- calls/         # Call + CallParticipant + MediaStatus
|   +-- meetings/      # Meeting + Participant + Invitation
|   +-- notifications/ # Notification
|   +-- support/       # SupportTicket (cross-tenant)
|   +-- settings/      # TenantCommunicationSettings/Limits
|   `-- shared/        # Result, ids branded, permissions mirror
+-- application/       # use cases, event handlers
+-- infrastructure/    # Prisma, Socket.IO, Fastify, Rabbit, Redis, JWKS
+-- api/
|   +-- http/          # rutas + plugins Fastify
|   `-- socket/        # handlers Socket.IO
`-- contracts/         # integration events + tipos socket
```

## 30.3 Endpoints HTTP (bajo `/communication` del Gateway)

| Verbo y ruta | Permiso |
|---|---|
| `GET /health/live` `/health/ready` | anonimo |
| `GET /communication/webrtc/ice` | JWT valido |
| `GET /communication/conversations` | JWT valido |
| `GET /communication/conversations/{id}/messages` | JWT valido |
| `POST /communication/conversations/{id}/read` | JWT valido |
| `GET /communication/calls` | JWT valido |
| `POST /communication/meetings` | `communication.meeting.create` |
| `GET /communication/meetings` | JWT valido |
| `POST /communication/meetings/{id}/start` `end` | host implicito |
| `GET /communication/notifications` | JWT valido |
| `GET /communication/notifications/unread-count` | JWT valido |
| `POST /communication/notifications/{id}/read` | JWT valido |
| `POST /communication/support` | `communication.support.open` |
| `GET /communication/support?view=agent&mine=true` | `support.agent` requerido para view=agent |
| `POST /communication/support/{id}/claim` `resolve` `close` | segun ownership + permisos |
| `GET /communication/settings` | `communication.settings.manage` |
| `PUT /communication/settings` | `communication.settings.manage` |
| `GET /communication/analytics/summary` `timeline` | `communication.analytics.read` |

## 30.4 Contratos Socket.IO

Todos bajo `wss://gateway/communication/socket.io/` con JWT en
`handshake.auth.token`. Nombres jerarquicos `dominio.entidad.accion`. Server
emite envelopes `{ eventId, correlationId, emittedAtUtc, payload }`.

**Chat**: `chat.conversation.start_direct/start_group/add_participant/remove_participant`,
`chat.message.send/edit/delete/mark_read`, `chat.typing.start/stop`. Server ->
`chat.message.new/edited/deleted/read`, `chat.typing.started/stopped`,
`chat.conversation.created/participant_added/participant_removed`,
`chat.presence.changed`, `chat.message.attachment_flagged` (status `Infected` |
`Deleted` | `BlockedByPolicy` — Fase 7). Grupos (`start_group`/`add_participant`/
`remove_participant`) apagados por default via
`TenantCommunicationSettings.internalGroupsEnabled`.

**Calls 1:1**: `call.initiate/accept/reject/cancel/end/signal/media_status/connection_quality`.
Server -> `call.incoming/state_changed/peer_joined/signal_from/media_status_changed`,
`call.transcript_ready` (Fase 6 — `CommunicationTranscriptWorker`, whisper.cpp).

**Meetings**: `meeting.join/leave/host.admit|remove|lock|mute_all|transfer/signal/media_status/raise_hand/dominant_speaker/recording.attach`,
`meeting.chat.send/edit/delete/mark_read` (Fase 8 — chat visible para TODOS los
presentes, no 1:1; reusa `sendMessage`/`editMessage`/`deleteMessage`/
`markMessagesRead` sobre una `Conversation` kind `Meeting` cuyos participantes
se sincronizan solos con quien esta `Joined`, autorizado por "estar en el
meeting", no por `communication.chat.reply`).
Server -> `meeting.snapshot/participant.changed/state.changed/signal.from/dominant_speaker.changed/you.muted/transcript_ready`,
`meeting.chat.message.new/edited/deleted/read` (mismos DTOs que `chat.message.*`,
namespace propio). `meeting.snapshot`/ack de `meeting.join` incluyen
`conversationId` (null en waiting room).

**Notifications**: `notification.mark_read/dismiss`. Server -> `notification.received/unread_count.changed/read.confirmed`,
`session.revoked` (canal SEPARADO — cierre CRIT-legacy).

## 30.5 Integration events publicados

- **Chat**: `communication.chat.conversation_started.v1` (kind `Direct`|`Group`|`Support`|`Meeting`),
  `.message_sent.v1` (sin contenido), `.message_edited.v1`, `.message_deleted.v1`,
  `.conversation_participant_added.v1`, `.conversation_participant_removed.v1`
  (solo grupos — el alta/baja de chat de meeting NO publica estos, ya la cubre
  `meeting.participant_joined/left.v1`, evita señal duplicada).
- **Calls**: `.call.started.v1`, `.ended.v1`, `.missed.v1`, `.recording_ready.v1`,
  `.recording_failed.v1` (Fase 10, `CallRecordingFailedEvent` en
  `contracts/events/call-events.ts`), `.transcript_ready.v1` (Fase 6).
- **Meetings**: `.meeting.scheduled.v1`, `.started.v1`, `.ended.v1`,
  `.invitation_requested.v1`, `.recording_ready.v1`, `.recording_failed.v1`
  (Fase 10, `MeetingRecordingFailedEvent`), `.transcript_ready.v1` (Fase 6).
  Hasta la iteracion F11 QA (§30.11) estos 2 ultimos no tenian **ningun**
  consumer en el resto de la plataforma — se publicaban al bus y se perdian.
- **Support**: `.support.opened.v1`, `.claimed.v1`, `.resolved.v1`, `.closed.v1`.
- **Consumido** (no publicado): `cloudstorage.file.blocked_by_policy.v1` — CloudStorage
  lo emite cuando `IContentScanner` (Fase 7, hoy `NoOpContentScanner`) marca un
  archivo. Communication lo refleja como `chat.message.attachment_flagged`
  (status `BlockedByPolicy`).

## 30.6 Consumers de otros microservicios

- **Signature**: `signer.invited`, `document.signed`, `request.completed|canceled|sealed|reminder_due`,
  `signer.verification.challenge_issued` (con `method='App'` -> push in-app **Urgent**).
  Cierra pendiente README §29.19 (canal App).
- **Customer**: `customer.bulk_imported.v1` -> push al usuario que lanzo el import
  (cierra TODO `Customer/DependencyInjection.cs:46`).
- **Auth**: `user.registered/roles_changed/deactivated` -> alimentan la proyeccion
  local `UserPermissionsProjection` para autorizacion fuera de banda.
- **Subscription**: `activated/plan_changed/seats_purchased/suspended` ->
  alimentan `TenantCommunicationLimits` (PlanGuard).

Cada consumer es idempotente via inbox durable (`ProcessedEvent`).

## 30.7 Persistencia

Base propia `TaxVision_Communication` con Prisma. Tablas core:

- `Conversation`, `ConversationParticipant`, `Message`, `MessageReceipt`
- `Call`, `CallParticipant`
- `Meeting`, `MeetingParticipant`, `MeetingInvitation`
- `NotificationEntry`
- `SupportTicket`
- `TenantCommunicationSettings`, `TenantCommunicationLimits`
- `UserPermissionsProjection`
- `ProcessedEvent` (inbox), `OutboxMessage` (outbox transaccional), `IdempotencyRecord`
- `CommunicationAnalyticsSnapshot` (event-sourced diario)

Aplicar migraciones (dev):

```powershell
cd src\Services\Communication
npm install
npx prisma generate
copy .env.example .env
npx prisma migrate dev --name init
npm run dev
```

## 30.8 Configuracion (`.env`)

```env
COMMUNICATION_HTTP_HOST=0.0.0.0
COMMUNICATION_HTTP_PORT=5350
COMMUNICATION_DB_CONNECTION="sqlserver://host.docker.internal:1433;database=TaxVision_Communication;user=sa;password=...;encrypt=false;trustServerCertificate=true"
COMMUNICATION_REDIS_URI=redis://redis:6379/0
COMMUNICATION_SESSION_DENYLIST_PREFIX=auth:session-denylist
COMMUNICATION_RABBITMQ_URI=amqp://taxvision:...@rabbitmq:5672
COMMUNICATION_RABBITMQ_EXCHANGE=taxvision-events
COMMUNICATION_RABBITMQ_QUEUE=communication-events
COMMUNICATION_JWT_ISSUER=TaxVision.Auth
COMMUNICATION_JWT_AUDIENCE=TaxVision.Services
COMMUNICATION_JWKS_URI=http://auth-api:8080/auth/.well-known/jwks.json
COMMUNICATION_TURN_URL=turn:turn:3478
COMMUNICATION_TURN_STATIC_AUTH_SECRET=...
COMMUNICATION_PLATFORM_TENANT_ID=8f58a521-4c25-4d91-9f4e-7ad5df14c001
COMMUNICATION_CORS_ORIGINS=http://localhost:5173
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4318

# Rate limiting — todos con default = el literal hardcodeado original, cero
# cambio de comportamiento si no se tocan. Los primeros 5 pares son de Fase
# Backend 11 (rate limit por socket event); los ultimos 3 pares (HTTP global +
# los 2 endpoints publicos de join-by-token/by-code) se agregaron en la
# iteracion F11 QA — antes eran literales `{max: N, timeWindow: '1 minute'}`
# inline en build-server.ts y meeting-invitations.route.ts pese a que el
# docblock de esa ruta ya afirmaba (incorrectamente) que salian de config.
COMMUNICATION_RATE_LIMIT_CALL_INITIATE_MAX=10
COMMUNICATION_RATE_LIMIT_CALL_INITIATE_WINDOW_SECONDS=30
COMMUNICATION_RATE_LIMIT_CALL_SIGNAL_MAX=60
COMMUNICATION_RATE_LIMIT_CALL_SIGNAL_WINDOW_SECONDS=10
COMMUNICATION_RATE_LIMIT_MEETING_CHAT_SEND_MAX=30
COMMUNICATION_RATE_LIMIT_MEETING_CHAT_SEND_WINDOW_SECONDS=10
COMMUNICATION_RATE_LIMIT_CHAT_SEND_MAX=30
COMMUNICATION_RATE_LIMIT_CHAT_SEND_WINDOW_SECONDS=10
COMMUNICATION_RATE_LIMIT_CHAT_EDIT_MAX=20
COMMUNICATION_RATE_LIMIT_CHAT_EDIT_WINDOW_SECONDS=10
COMMUNICATION_RATE_LIMIT_CHAT_TYPING_MAX=20
COMMUNICATION_RATE_LIMIT_CHAT_TYPING_WINDOW_SECONDS=10
COMMUNICATION_RATE_LIMIT_HTTP_GLOBAL_MAX=300
COMMUNICATION_RATE_LIMIT_HTTP_GLOBAL_WINDOW_SECONDS=60
COMMUNICATION_RATE_LIMIT_MEETING_JOIN_TOKEN_MAX=5
COMMUNICATION_RATE_LIMIT_MEETING_JOIN_TOKEN_WINDOW_SECONDS=60
COMMUNICATION_RATE_LIMIT_MEETING_JOIN_CODE_MAX=20
COMMUNICATION_RATE_LIMIT_MEETING_JOIN_CODE_WINDOW_SECONDS=60
```

## 30.9 Reglas de oro (no repetir el legacy)

1. Redis adapter Socket.IO obligatorio — nunca in-memory Map.
2. Rol/tenant SIEMPRE del JWT verificado con JWKS; jamas del handshake.query
   (cierra CRIT-18 legacy).
3. `console.log` prohibido; solo Pino estructurado con redact automatic.
4. Idempotencia obligatoria en cada mutacion socket (`Idempotency-Key`).
5. Passcodes con Argon2id 64MB; tokens de invitacion / recording opacos y con
   solo SHA-256 persistido.
6. Presence: Redis con lease TTL + heartbeat + Pub/Sub (no `sleep(2000)`).
7. Session events (`session.revoked`, `force.logout`) en canal socket SEPARADO
   del canal de business notifications.
8. Fallback TURN dummy `'fallback'/'fallback'` PROHIBIDO — sin secreto, solo STUN.

## 30.10 Pendientes documentados

Cerrados en esta iteracion (ver 30.11):
- ~~DLQ formal para consumer runtime~~ — ahora hay binding real (deadLetterExchange + deadLetterRoutingKey a la DLQ; nack con requeue=false).
- ~~Room mismatch calls/meetings~~ — `emitter.emitToCall/emitToMeeting` alineados con los joins.
- ~~Presence broadcast~~ — watcher `presence-changed-watcher` subscribe al canal Redis y emite `chat.presence.changed`.
- ~~TURN sin STUN propio~~ — el `HmacTurnCredentialFactory` deriva `stun:host:port` del propio TURN URL; username hasheado (no expone UUID).

Sigue pendiente:

- **`UserDirectoryProjection` para hidratar `displayName` real en payloads de chat/call/meeting**. Hoy los handlers ponen el `userId` (UUID) como placeholder porque el FE no recibe nombres. Requiere:
  1. Extender `UserRegisteredIntegrationEvent` en Auth para incluir `Name` + `LastName` (compat: son nullable en el consumer).
  2. Nueva tabla Prisma `UserDirectoryEntry(userId, tenantId, displayName, email, isActive, updatedAtUtc)`.
  3. Consumer que hidrata desde `auth.user.registered.v1` + un nuevo `auth.user.profile_updated.v1` para tracking de cambios.
  4. Refactorizar los handlers de `send-message.ts`, `initiate-call.ts`, `join-meeting.ts` para leer del directorio antes de emitir.
- **CloudStorage attachment validation consumer** (`cloudstorage.file.available.v1` / `.infected.v1` / `.deleted.v1`). Hoy los chat attachments confian en el `fileId` que pasa el cliente sin validacion server-side. Requiere:
  1. Nueva tabla Prisma `AttachmentTracking(fileId, messageId, tenantId, status)`.
  2. Refactorizar `send-message.ts` para registrar el tracking al insertar.
  3. Consumer que actualiza el status y broadcastea `chat.message.attachment_flagged` al room de la conversation si el file resulta infected.
- **LiveKit SFU switching** en meetings >4 — hoy `strategy: Sfu` se declara en el snapshot pero el signaling sigue siendo mesh peer-to-peer, asi que meetings >6-8 fallan por N^2 conexiones del cliente. Feature flag reservado, adapter (LiveKit / mediasoup / janus) fuera del scope actual.
- **Server-side recording** (LiveKit Egress o similar) para calls y meetings. `Call.attachRecording` y `Meeting.attachRecording` existen en el dominio pero **nunca se llaman** — no hay orquestador que suba el resultado a CloudStorage con `OwnerType=Communication`, `FolderType=Recordings`. Recording client-side sigue siendo la unica via.
- **Backfill / catchup al reconectar**: no hay `chat.sync.since`, `call.resync` ni `meeting.reconnect`. Un cliente que reconecta despues de la desconexion pierde eventos emitidos durante la caida y debe reconstruir via HTTP.
- **Auto-timeout de typing indicators** — si el cliente pierde la conexion sin emitir `chat.typing.stop`, el otro peer queda con "Escribiendo..." pegado. Fix: TTL server-side en Redis + broadcast automatico al expirar.
- ~~**Rate limit no aplicado**~~ — RESUELTO. Fase Backend 11: rate limit real
  por socket event via `config.rateLimit.*` + `@fastify/rate-limit`. Iteracion
  F11 QA (§30.11): cerrados los ultimos 2 puntos que seguian hardcodeados (el
  limite HTTP global de `build-server.ts` y los 2 endpoints publicos de
  invitaciones), ver `COMMUNICATION_RATE_LIMIT_HTTP_GLOBAL_*` /
  `_MEETING_JOIN_TOKEN_*` / `_MEETING_JOIN_CODE_*` en §30.8.
- **Cleanup en disconnect abrupto de calls/meetings**: hoy solo el `missed-call-scheduler` procesa calls Ringing con timeout. Calls Active y meetings con participantes sin heartbeat quedan colgados.
- **Purge/retention scheduler**: `TenantCommunicationSettings.recordingRetentionDays` y `messageRetentionDays` son configurables pero no hay purgador que ejerza la politica.
- **Denylist Redis fail-open**: si Redis se cae, `JwtVerifier` sigue aceptando tokens revocados. Feature flag para modo fail-closed pendiente.
- ~~**Grupos de chat**~~ — RESUELTO (Fase 7): `startGroupConversation`/`addGroupParticipant`/`removeGroupParticipant` + handlers socket (`chat.conversation.start_group`/`add_participant`/`remove_participant`), gateado por `TenantCommunicationSettings.internalGroupsEnabled` y permisos `communication.group.create`/`manage_members`.
- ~~**Content moderation** (`IContentScanner`)~~ — RESUELTO (Fase 7) del lado de CloudStorage: `IContentScanner`/`NoOpContentScanner` (siempre Clean) wireado en `ScanFileHandler` tras ClamAV, con los 3 verdicts reales (Clean/PolicyViolation/Uncertain) manejados aunque el NoOp de MVP solo dispare Clean. Communication consume `cloudstorage.file.blocked_by_policy.v1`. Falta: un scanner real (NSFW/CSAM) que reemplace el NoOp.
- ~~**Transcripts** de meetings~~ — RESUELTO (Fase 6): worker separado `CommunicationTranscriptWorker` (whisper.cpp + ffmpeg), con inbox Redis propio para idempotencia ante redeliveries de RabbitMQ.
- ~~**Advisory lock Redis** en outbox drainer~~ — ya implementado (`RedisDistributedLock.withLock` en `outbox-drainer.ts`); esta entrada estaba desactualizada.
- **Push nativo** FCM/APNs — PARCIAL (Fase 7): infraestructura real y reusable en Notification (`PushDeviceToken`, `IPushSender`, `NotificationDispatcher.SendPushAsync`, endpoints `POST/DELETE notifications/push/devices`), y el canal `"AppPush"` ya está wireado en `SignerVerificationChallengeIssuedConsumer`. Falta: (1) un `IPushSender` real (Firebase Admin SDK / APNs HTTP2) — hoy `LoggingPushSender` solo loguea; (2) un flujo de registro de dispositivo para signers externos — el link de firma es web sin sesión autenticada, así que `AppPush` siempre resuelve "sin dispositivos registrados" hasta que ese flujo se construya.
- ~~**Chat y media dentro de meetings**~~ — RESUELTO (Fase 8): nuevo `ConversationKind.Meeting`, 1:1 con cada `Meeting`, con participantes sincronizados automáticamente con quién está `Joined` (`ensureMeetingConversation`/`removeFromMeetingConversation`, wireados en join/leave/admit/host-remove). Reusa integramente `sendMessage`/`editMessage`/`deleteMessage`/`markMessagesRead` — adjuntos/media incluidos gratis vía `AttachmentTracking`, sin código nuevo. Eventos socket dedicados (`meeting.chat.send/edit/delete/mark_read`) en vez de los genéricos `chat.*` porque la autorización correcta es "estás Joined en este meeting", no `communication.chat.reply`. El historial se lee con los mismos endpoints HTTP `GET/POST /communication/conversations/:id/messages` — el cliente recibe el `conversationId` en el snapshot del meeting al entrar.

## 30.11 Fixes aplicados en la iteracion 07-11 (post-auditoria)

- **Room mismatch en calls y meetings**: los handlers hacian `socket.join('t:{tenantId}:call:{callId}')` / `:m:{meetingId}` pero los emits pasaban por `emitToConversation({conversationId: 'call:...'})` que construia `t:{tenantId}:c:call:{callId}` (con `:c:` en medio). Los rooms no coincidian y `state_changed`, `peer_joined`, `media_status_changed`, `participant.changed`, `dominant_speaker.changed`, `you.muted` no llegaban a nadie. Fix: `RealtimeEmitter` gano `emitToCall`, `emitToMeeting`, `emitToTenant` con room names correctos; `call-handlers.ts` y `meeting-handlers.ts` refactorizados.
- **Presence sin broadcast**: `RedisPresenceService` publicaba en `comm:presence:changed:{tenantId}` pero nadie subscribia. Nuevo `presence-changed-watcher.ts` hace `PSUBSCRIBE` al patron y emite `chat.presence.changed` al room del tenant. Ademas se corrigio una race condition en `register` (dos sockets simultaneos del mismo user podian ambos publicar 'online' — ahora SET va primero, count despues).
- **TURN sin STUN propio**: `HmacTurnCredentialFactory` solo devolvia `stun:stun.l.google.com:19302` (dependencia externa). Ahora deriva `stun:host:port` del propio TURN URL y lo devuelve como primer ICE server (menor latencia). El username del TURN ahora usa SHA-256 truncado de `tenantId:userId` en vez del UUID crudo (no expone identidad en logs de coturn / traza WebRTC).
- **DLQ no bindeada**: la cola principal se declaraba con `deadLetterExchange: ''` pero sin `deadLetterRoutingKey`, y el consumer runtime hacia `ack` ciego en error. Los failures se descartaban silenciosamente. Fix: `assertQueue` ahora usa `deadLetterExchange: ''` + `deadLetterRoutingKey: <dlq>` (routing al default exchange con la DLQ como target); el consumer runtime hace `nack(requeue=false)` en error y `unmark` la inbox para permitir reproceso manual.

## 30.12 Fixes aplicados en la iteracion F11 QA (checklist final del frontend)

Al correr el checklist final de aceptacion del frontend QA console (`taxvision-communication-frontend`) se encontraron y cerraron 3 gaps reales:

- **Transferir host sin boton en la UI**: `meeting.host.transfer` ya existia en
  el backend (guard estricto `Meeting.HostOnly` — a diferencia de la mayoria de
  acciones de host, un cohost NO puede transferir) pero `MeetingRoomPage.tsx`
  no tenia ningun control para dispararlo. Agregado, gateado a `isHost`
  (nunca `isHostOrCohost`) y usando `emit()` plano — este evento, junto con
  `Lock`/`MuteAll`, no manda `ack()`, asi que `emitWithAck()`/el helper
  `hostAction()` colgarian 10s con un falso timeout.
- **Notification click no deep-linkea a su recurso**: investigado a fondo, no
  aplica. Los unicos `kind` que Communication crea hoy
  (`signature.signer.invited`, `signature.document.signed`,
  `signature.request.*`, `signature.push_challenge`,
  `customer.bulk_import_completed`) pertenecen a dominios de OTROS
  microservicios que tienen su propia UI — este frontend no tiene ruta para
  ninguno. Forzar un deep-link hubiera significado inventar rutas a recursos
  que esta app no posee. Documentado como limite de scope, no como bug.
- **Notification (.NET) sin consumer de recording ready/failed**: cerrado — ver
  §26.4.
- **Rate limiters HTTP con literales inline** pese a que un docblock afirmaba
  lo contrario: cerrado — ver §30.5/§30.8/§30.10.

Adicionalmente, durante la sesion de pruebas manuales del login en local
(`localhost` sin subdominio real) se encontraron y cerraron 2 bugs mas, fuera
del checklist original pero bloqueantes para poder loguearse:

- **`EffectiveLoginTenantResolver` (Auth) ignoraba una resolucion de Host
  real en Development** — ver §33.2 (seccion de Auth) para el detalle
  completo; el fix vive en Auth, no en Communication, pero el sintoma
  (login pide `TenantId` a mano) solo aparecia probando este frontend.
- **`LoginPage.tsx` nunca mandaba `tenantId`**, asumiendo que
  `EnforceHostResolution=true` corria siempre — roto en local, donde
  `appsettings.Development.json` de Auth pone esa flag en `false`. Fix
  temporal: campo manual opcional en el form. Fix definitivo (una vez
  arreglado el resolver de Auth): el campo se volvio innecesario y se
  **elimino del formulario** — hoy el login local con un subdominio real
  registrado (`demo.localhost`, ver §33.11) funciona identico a produccion,
  sin ningun input extra.

## 30.13 Fixes aplicados durante el QA end-to-end del pipeline de grabacion+transcript

Al probar una grabacion real de meeting de punta a punta (upload → scan →
transcript worker → attach) aparecieron 5 bugs reales encadenados — cada uno
tapaba al siguiente, asi que el pipeline nunca habia corrido completo hasta
esta sesion:

- **`File.UnsupportedType` generico tapaba el limite real de tamano**:
  `UploadRegistration.ReserveAndRegisterAsync` colapsaba 3 causas de rechazo
  distintas (nombre invalido, tamano excedido, tipo no permitido) en el mismo
  error — una grabacion rechazada solo por tamano (plan `starter`,
  `MaxFileSizeBytes` de 10 MB) mostraba "tipo de archivo no permitido" en vez
  de senalar el tamano. Fix: nuevo `File.TooLarge` (413) separado de
  `File.UnsupportedType`, chequeado antes que extension/content-type. Ver
  §27.12 para el detalle completo del sistema de cuotas por `FolderType`/plan
  que se toco junto con esto (`FolderOverridesBytes`, tiers de `Recordings`
  por plan).
- **Race condition download-vs-scan en el transcript worker**:
  `attachMeetingRecording`/`attachCallRecording` publican
  `recording_processing_started.v1` apenas termina el upload, sin esperar el
  escaneo ClamAV asincronico de CloudStorage. El transcript worker consumia
  el evento casi al instante y pedia `download-url` mientras el archivo
  seguia `Scanning` — CloudStorage devolvia `File.NotAvailable` (403), y el
  worker lo trataba como error fatal (el retry solo cubria `status >= 500`,
  a proposito, para no reintentar errores reales de permisos). Fix:
  `DownloadStatusError` ahora lleva el `errorCode` del body de CloudStorage;
  el worker reintenta especificamente en `File.NotAvailable` (transitorio)
  pero sigue sin reintentar otros 403 (`File.Forbidden`, mismatch real de
  scope). Con el backoff de 1s/5s/30s ya configurado
  (`TRANSCRIPT_WORKER_RETRY_MAX_ATTEMPTS`/`_BACKOFF_MS` en
  `CommunicationTranscriptWorker/.env`), el primer reintento cubre sobra la
  carrera tipica de unos cientos de ms.
- **`z.coerce.boolean()` interpretaba `"false"` como `true`**: Zod no parsea
  el texto "true"/"false" con `coerce.boolean()` — hace `Boolean(valor)`, y
  cualquier string no vacio (incluido literalmente `"false"`) da `true`. Con
  `TRANSCRIPT_WORKER_MINIO_USE_SSL=false` en `.env`, el config parseado
  quedaba en `useSSL: true`, y el cliente MinIO intentaba negociar TLS contra
  el puerto 9000 en HTTP plano (`EPROTO ... packet length too long` al subir
  el transcript). Mismo patron roto encontrado (no activo, la var no esta
  seteada en ningun `.env` hoy) en `COMMUNICATION_SESSION_DENYLIST_FAIL_CLOSED`
  de Communication. Fix en ambos: `z.string().default(...).transform(v => v
  === 'true')` en vez de `z.coerce.boolean()`.
- **`Meeting.attachTranscript`/`Call.attachTranscript` solo aceptaban `Ended`**:
  la premisa original ("el transcript siempre llega con el meeting/call ya
  terminado") no se sostiene — el pipeline de transcripcion
  (download+ffmpeg+whisper.cpp) es asincronico y puede tardar mas que lo que
  el host tarda en cerrar la reunion, o el host simplemente segui reunido
  despues de cortar la grabacion. `TranscriptReady` llegaba con el meeting
  todavia `Live`/`Active`, `attachMeetingTranscript`/`attachCallTranscript`
  fallaba con `Meeting.Transcript.InvalidState`/`Call.Transcript.InvalidState`,
  y el consumer solo logueaba un `warn` y descartaba el evento (sin retry,
  sin dead-letter) — el transcript quedaba subido en CloudStorage pero
  **nunca vinculado**, invisible para siempre en el frontend. Fix: mismo
  criterio que `attachRecording` (`Ended` **o** `Live`/`Active`) en ambos
  aggregates.
- **Transcripts rechazados siempre por `FolderType` incorrecto**: el
  transcript worker subia el `.txt` con `FolderType: 'Recordings'` — el mismo
  folder que la grabacion de video real — pero `RecordingsPolicy` solo
  permite `.webm`/`.mp4`. Un `.txt`/`text/plain` etiquetado como `Recordings`
  era rechazado **siempre** por whitelist (`SaveFileRequested ... rejected by
  upload policy`), determinístico, no una falla transitoria — el feature de
  transcripts nunca pudo registrar un archivo en CloudStorage desde que se
  construyo. Fix: nuevo `FolderType.Transcripts` dedicado (ver §27.12), y el
  publisher del worker ahora lo usa en vez de `Recordings`.

Ademas, se corrigio un ruido cosmetico (no bloqueante) en
`cloudstorage.file.available.v1` de Communication: el handler llamaba
`attachmentTracking.markStatus(...)` para **cualquier** archivo de
CloudStorage que pasara a `Available` (grabaciones, transcripts, imports,
firmas...), no solo adjuntos de chat trackeados — el `.update()` de Prisma
fallaba para archivos no-adjunto y lo logueaba como error (nivel `error`,
aunque el repo ya lo atrapaba con `.catch(() => null)` y no rompia nada
funcionalmente). Fix: chequea `findByFileId` primero y no llama `markStatus`
si el archivo no esta trackeado.

Verificacion: 202 tests en Communication (+7 nuevos de `attachTranscript` /
+2 de `cloudstorage-consumers`), 179 en CloudStorage (+1 nuevo de
`FolderType.Transcripts`), 30 en CommunicationTranscriptWorker — todos
verdes, typecheck limpio en los 3 proyectos.

---

# 31. Claves JWT RS256 — Setup de desarrollo

## 31.1 Por que RS256 y no HS256

Con HS256 todos los servicios comparten el mismo secreto para firmar y verificar tokens.
Basta que uno se comprometa para que cualquiera pueda emitir tokens validos.

Con RS256:

- Solo **Auth** posee la clave privada y firma los tokens.
- El resto de servicios (.NET y Node.js) solo necesitan la **clave publica** para verificar.
- La clave publica se puede publicar en GitHub sin riesgo.
- Auth expone `/auth/.well-known/jwks.json` con la clave publica en formato JWKS para que
  servicios externos (p.ej. Communication con `jose`) la descarguen automaticamente.

## 31.2 Archivos generados

```
dev-keys/
  jwt-private.pem   ← clave privada RSA 2048-bit (NUNCA subir al repo)
  jwt-public.pem    ← clave publica  RSA 2048-bit (segura para GitHub)
  .gitignore        ← protege jwt-private.pem automaticamente
```

> **jwt-public.pem se puede subir a GitHub** — es la clave de verificacion; no contiene
> secreto alguno. Solo `jwt-private.pem` debe mantenerse fuera del repositorio.

## 31.3 Generar las claves (una sola vez por desarrollador)

### Windows (PowerShell)

```powershell
.\scripts\generate-dev-keys.ps1
```

Requiere `openssl`. Git for Windows lo incluye en `C:\Program Files\Git\usr\bin\openssl.exe`.
Tambien se puede instalar con: `winget install ShiningLight.OpenSSL`

### macOS / Linux (bash)

```bash
bash scripts/generate-dev-keys.sh
```

### Manualmente con openssl

```bash
# Desde la raiz del repositorio
mkdir -p dev-keys

# 1. Generar clave privada PKCS#8 RSA 2048-bit
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out dev-keys/jwt-private.pem

# 2. Extraer clave publica SubjectPublicKeyInfo
openssl pkey -in dev-keys/jwt-private.pem -pubout -out dev-keys/jwt-public.pem
```

## 31.4 Configuracion por servicio

Cada servicio lee las rutas desde `appsettings.Development.json`. Las rutas son **relativas**
al directorio del proyecto (.csproj), por lo que funcionan en cualquier maquina sin hardcodear
rutas absolutas.

| Servicio      | Archivo configurado              | Clave usada     |
|---------------|----------------------------------|-----------------|
| Auth          | `appsettings.Development.json`   | privada + publica |
| Gateway       | `appsettings.Development.json`   | publica         |
| Tenant        | `appsettings.Development.json`   | publica         |
| Customer      | `appsettings.Development.json`   | publica         |
| CloudStorage  | `appsettings.Development.json`   | publica         |
| Notification  | `appsettings.Development.json`   | publica         |
| Signature     | `appsettings.Development.json`   | publica         |
| Subscription  | `appsettings.Development.json`   | publica         |
| Communication | `.env` → JWKS descargado de Auth | publica via JWKS |

Auth (ejemplo, ruta relativa desde `src/Services/Auth/Api/`):

```json
{
  "Jwt": {
    "PrivateKeyPath": "../../../../dev-keys/jwt-private.pem",
    "PublicKeyPath":  "../../../../dev-keys/jwt-public.pem"
  }
}
```

Servicios consumidores (ruta relativa desde `src/Services/<Nombre>/TaxVision.<Nombre>.Api/`):

```json
{
  "Jwt": {
    "PublicKeyPath": "../../../../dev-keys/jwt-public.pem"
  }
}
```

Gateway (ruta relativa desde `src/Gateway/TaxVision.Gateway/`):

```json
{
  "Jwt": {
    "PublicKeyPath": "../../../dev-keys/jwt-public.pem"
  }
}
```

Communication Node.js NO necesita configuracion adicional: lee la clave publica directamente
desde el JWKS endpoint de Auth (`COMMUNICATION_JWKS_URI=http://localhost:5124/auth/.well-known/jwks.json`).

## 31.5 Como funciona en runtime

1. Auth arranca, lee `jwt-private.pem` → activa modo RS256 en `SigningKeyProvider`.
2. `GET /auth/.well-known/jwks.json` devuelve el modulo RSA publico en formato JWK.
3. Cualquier servicio que llame `AddTaxVisionJwtAuthentication(config, contentRootPath)`
   lee `jwt-public.pem` → valida tokens con RS256 sin necesitar la clave privada.
4. Communication Node.js usa `createRemoteJWKSet` de `jose` para descargar y cachear el JWKS;
   refresca automaticamente cada 5 minutos.

## 31.6 Modo fallback HS256

Si un servicio NO tiene configurado `Jwt:PublicKeyPath` ni `Jwt:PublicKeyPem`, el codigo
cae automaticamente a HS256 con `Jwt:Secret`. Auth hace lo mismo para la firma: si no hay
`Jwt:PrivateKeyPath`, firma en HS256. Este fallback existe para compatibilidad; en desarrollo
activo siempre usar RS256.

## 31.7 Puertos de los servicios

| Servicio      | Puerto |
|---------------|--------|
| Auth          | 5124   |
| Tenant        | 5217   |
| Customer      | 5263   |
| Notification  | 5320   |
| CloudStorage  | 5330   |
| Signature     | 5340   |
| Communication | 5350   |
| Subscription  | 5360   |
| Gateway       | (ver appsettings) |

## 31.8 Notas de seguridad

- `dev-keys/jwt-private.pem` esta en `dev-keys/.gitignore` — nunca se commitea.
- En produccion/staging la clave privada se inyecta via variable de entorno `Jwt__PrivateKeyPem`
  (valor PEM completo) o secreto del gestor de secretos del proveedor cloud.
- La clave publica puede ser un archivo estatico en el repo o tambien variable de entorno
  `Jwt__PublicKeyPem`.
- Si se compromete la clave privada: regenerar el par, reiniciar Auth, invalidar todos los
  tokens activos (limpiar denylist o cambiar `Jwt:Issuer` para forzar re-login).

## 32.1 Alcance y arquitectura

Ruta: `src/Services/Subscription`. Cuatro proyectos siguiendo el mismo patron Clean
Architecture del resto del backend:

```text
TaxVision.Subscription.Domain          (agregados, value objects, sin EF/HTTP)
TaxVision.Subscription.Application     (comandos/queries vertical-slice, Wolverine)
TaxVision.Subscription.Infrastructure  (EF Core, repos, jobs, Redis)
TaxVision.Subscription.Api             (controllers, DI, Program.cs)
```

El disenio completo (49 secciones) vive en el documento
`Subscription_Service_Analysis_And_Design.md/.pdf` entregado aparte. Se implemento por
fases sobre la rama `task/ms-subscription-redesign`:

| Fase | Contenido | Estado |
| --- | --- | --- |
| 1 | Plan, TenantSubscription, SubscriptionSeat, SubscriptionTenantSettings | Hecho |
| 2 | SubscriptionSeatAssignment (asignar/liberar/reasignar asientos a usuarios) | Hecho |
| 3 | TenantEntitlementSnapshot, AddOns, cache Redis de entitlements | Hecho |
| 4 | Renovaciones (base/seats/add-ons), expiraciones, grace period, jobs | Hecho |
| 5 | Billing/cobro real | No implementado — depende de un proveedor de pagos |
| 6 | Cambios de plan (`PlanChangeRequest`, Immediate/EndOfPeriod) | Hecho, **sin prorrateo** |
| 7 | Audit log (`SubscriptionAuditLog`) + endpoints admin cross-tenant | Hecho |

La Fase 5 (Billing real) y cualquier calculo de prorrateo quedan fuera de este
microservicio a proposito — ver §32.3.

## 32.2 Dominio: agregados y ciclo de vida

- **`SubscriptionPlan`** + **`SubscriptionPlanVersion`**: catalogo versionado
  (Starter/Pro/Enterprise), con `PlanFeature`/`PlanEntitlementDefinition`/`PlanPriceTier`
  por version. Solo una version `Published` a la vez por plan.
- **`TenantSubscription`**: raiz del agregado de suscripcion base. Estados
  `Trialing -> Active -> (Suspended | PastDue | Cancelled | Expired)`, con
  `GracePeriod` antes de suspender por impago. Hijas: `TenantSubscriptionRenewal`
  (historial de renovaciones) y `PlanChangeRequest` (cambios de plan en curso).
- **`SubscriptionSeat`** + **`SubscriptionSeatAssignment`**: asientos comprados por
  el tenant y su asignacion a un usuario concreto (con cooldown configurable de
  reasignacion).
- **`TenantAddOn`** + **`AddOnDefinition`**: add-ons opcionales (almacenamiento
  extra, modulos), con su propio ciclo de renovacion independiente del plan base.
- **`TenantEntitlementSnapshot`**: proyeccion combinada (plan + seats + add-ons)
  que responde "que puede hacer este tenant ahora mismo", cacheada en Redis e
  invalidada por `RecalculateEntitlementsCommand` cada vez que algo cambia.
- **`SubscriptionTenantSettings`**: configuracion por tenant (`PlanChangeEffective`,
  `AllowSeatReassignment`, `SeatReassignmentCooldownDays`, `AllowAddons`,
  `AllowTrial`, `TrialDays`, `AutoRenewCascadeMode`, etc.), con default sensato si
  el tenant no tiene fila propia todavia.
- **`SubscriptionAuditLog`**: bitacora append-only de toda mutacion relevante
  (before/after JSON), consultable via `GET /audit`.

Todas las entidades siguen el mismo guardrail: `sealed class`, constructor privado,
factory estatica `Create(...)` que devuelve `Result<T>`, colecciones hijas expuestas
como `IReadOnlyCollection<T>` respaldadas por un campo privado, y cero LINQ dentro de
`TaxVision.Subscription.Domain` (las transiciones de estado se escriben con
`foreach`/loops explicitos, no con `Where`/`Select`/`Any`).

## 32.3 Cambios de plan: Immediate vs EndOfPeriod (sin prorrateo)

`PlanChangeRequest` registra la intencion de cambiar de plan sin calcular nunca
cuanto cobrar — ese calculo es responsabilidad de un futuro Billing (Fase 5), no de
Subscription:

- **`PlanChangeEffectiveMode.Immediate`** (default de `SubscriptionTenantSettings`,
  igual que Hostinger y la mayoria de plataformas de suscripcion): el plan cambia
  en el acto (`TenantSubscription.RequestPlanChange` llama a `ChangePlan`
  internamente) y la solicitud queda `Applied` de una vez. El nuevo precio se cobra
  de inmediato porque el plan ya es el nuevo — sin matematica de dias restantes.
- **`PlanChangeEffectiveMode.EndOfPeriod`**: la solicitud queda `Pending` con
  `EffectiveAtUtc = CurrentPeriodEndUtc`. El plan actual sigue vigente hasta esa
  fecha; `PendingPlanChangeApplicationJob` (background job, corre cada hora) aplica
  el cambio cuando `EffectiveAtUtc` ya paso, y el precio del plan nuevo se cobra sin
  mas en la proxima renovacion normal — no hay un paso especial de facturacion
  intermedia.
- Una solicitud pendiente nueva cancela automaticamente la anterior
  (`SupersedePendingPlanChangeRequest`); nunca hay dos `Pending` al mismo tiempo.
- `GET /subscriptions/plan-change` consulta el cambio pendiente actual (si existe) y
  `POST /subscriptions/plan-change/cancel` lo cancela.

## 32.4 Endpoints HTTP

Todos bajo el Gateway (`http://localhost:5047`), salvo el interno. `TenantAdmin`
para mutaciones del propio tenant, `PlatformAdmin` para operaciones cross-tenant.

| Recurso | Endpoints |
| --- | --- |
| Plans | `GET /plans` (publico) |
| Subscriptions | `GET /subscriptions/me`, `POST /change-plan`, `GET /plan-change`, `POST /plan-change/cancel`, `POST /cancel`, `PATCH /{tenantId}/suspend`, `PATCH /{tenantId}/reactivate`, `POST /{tenantId}/renew` |
| Seats | `GET /seats`, `GET /seats/{id}`, `POST /seats/purchase`, `POST /seats/{id}/assign`, `POST /seats/{id}/release`, `POST /seats/{id}/reassign`, `POST /seats/{id}/renew` |
| AddOns | `GET /addons` (publico), `GET /addons/tenant`, `POST /addons`, `POST /addons/{id}/cancel`, `POST /addons/{id}/renew` |
| Entitlements | `GET /entitlements/summary`, `GET /entitlements/{key}` |
| Audit | `GET /audit` (TenantAdmin o PlatformAdmin) |
| Admin | `GET /admin/subscription/upcoming-renewals`, `/expired-seats`, `/past-due-subscriptions`, `POST /admin/subscription/tenants/{tenantId}/recalculate-entitlements` (PlatformAdmin) |
| Interno | `GET /internal/users/{userId}/access` (policy `ServiceOnly`, solo Auth lo llama) |

## 32.5 Jobs en segundo plano

Todos heredan de `PeriodicSubscriptionJob` (timer + lock distribuido en Redis +
scope de DI por iteracion), para que dos replicas nunca procesen el mismo lote:

| Job | Que hace |
| --- | --- |
| `TenantSubscriptionRenewalJob` | Publica el intent de cobro cuando `NextRenewalAtUtc` llega |
| `SeatRenewalJob` / `AddOnRenewalJob` | Igual que el anterior, para seats y add-ons |
| `TrialExpirationJob` | Expira trials no convertidos a `Active` |
| `GracePeriodExpirationJob` | Suspende tras agotar el `GracePeriod` por impago |
| `SubscriptionExpirationJob` / `SeatExpirationJob` / `AddOnExpirationJob` | Cierran definitivamente lo cancelado/expirado |
| `RenewalNotificationJob` | Dispara aviso de renovacion proxima |
| `PendingPlanChangeApplicationJob` | Aplica cambios de plan `EndOfPeriod` cuya `EffectiveAtUtc` ya paso (§32.3) |

## 32.6 Eventos de integracion

Publicados en `taxvision-events` (RabbitMQ):

`TenantEntitlementsChangedIntegrationEvent`, `SubscriptionRenewalDueIntegrationEvent`,
`SubscriptionRenewalUpcomingIntegrationEvent`,
`SeatAssignedToUserIntegrationEvent`, `SeatReleasedFromUserIntegrationEvent`,
`SeatRenewalDueIntegrationEvent`, `SeatRenewalUpcomingIntegrationEvent`,
`AddOnActivatedIntegrationEvent`, `AddOnCancelledIntegrationEvent`,
`AddOnRenewalDueIntegrationEvent`.

Subscription tambien consume `TenantCreatedIntegrationEvent` (Tenant) via
`TenantCreatedConsumer` para abrir la suscripcion en trial al crear un tenant.

**`TenantEntitlementsChangedIntegrationEvent` es el evento canonico de "algo cambio
en la suscripcion"**: se publica una sola vez, al final de cada recalculo de
entitlements (alta, cambio de plan, compra/renovacion de seats o add-ons, suspension,
reactivacion, expiracion). Ademas de `RevisionNumber`/`ChangedKeys`/`PlanCode`/
`SubscriptionStatus` trae el snapshot resuelto completo en `EntitlementValues`
(`EntitlementKey -> valor stringificado`, ej. `"storage.max_bytes" -> "107374182400"`)
y `SeatCount`/`AvailableSeatCount` — el mismo contenido que expondria
`GET /entitlements/summary` en el instante del recalculo, para que los consumidores
no necesiten una llamada HTTP adicional a Subscription. Lo consumen:

- **Auth** (`TenantEntitlementsChangedConsumer`) — proyecta `TenantPlanLimits`.
  `MaxUsers` sale de `SeatCount` (no de un entitlement "seats.max": los seats son
  entidades independientes compradas por el tenant, ver §32.2), y los modulos
  habilitados de las claves `module.*` con valor `"True"`.
- **CloudStorage** (`TenantEntitlementsChangedQuotaConsumer`) — proyecta
  `TenantStorageLimit` desde `storage.max_bytes`.
- **Communication** (TS, `bindSubscriptionConsumers`) — proyecta
  `TenantCommunicationLimits` desde claves `communication.*`. Ningun plan define
  todavia esas claves en el catalogo (§32.2 solo siembra `module.*` y limites core),
  asi que hoy cae siempre a los defaults conservadores (4 participantes, 60 min,
  etc.) hasta que ese catalogo se extienda.

**Confiabilidad del recalculo (2026-07, actualizado 2026-07-19)**: `RecalculateEntitlementsExtensions.cs`
tiene dos variantes, con semantica de fallo distinta a proposito:

- `RecalculateEntitlementsSafelyAsync` — usada por las ~19 rutas donde el efecto principal
  del comando (compra de seat, cambio de plan, add-on, jobs de expiracion) ya se completo y
  no debe deshacerse ni reintentarse solo porque el recalculo de entitlements fallo. Loguea
  un `ERROR` con el `TenantId`/codigo/mensaje y sigue — el tenant queda con estado valido
  pero snapshot stale hasta el proximo recalculo o una reconciliacion manual.
- `RecalculateEntitlementsOrThrowAsync` — usada exclusivamente por `TenantCreatedConsumer`
  (los dos call-sites: alta nueva y "suscripcion ya existe"). **Bug real de produccion
  encontrado 2026-07-19**: hasta esa fecha, `TenantCreatedConsumer` tambien usaba la variante
  "Safely", asi que un `Result.Failure` de `RecalculateEntitlementsCommand` (p.ej.
  `Subscription.NotFound`/`Plan.NoPublishedVersion`) quedaba en un simple log — Wolverine
  consideraba `TenantCreatedIntegrationEvent` procesado con exito (la suscripcion SI se creo)
  y nunca reintentaba ni mandaba nada a la dead-letter queue. Un tenant podia quedar para
  siempre sin `TenantEntitlementSnapshot` — y por lo tanto sin fila de `TenantStorageLimits`
  en CloudStorage — de forma invisible. Ahora este call-site throw-ea, asi que el mismo
  `RetryWithCooldown(1s/5s/15s)` + dead-letter de `Program.cs` se aplica aca tambien (igual
  que `SaveFileFromSourceHandler` en CloudStorage, ver §41). Reprocesar
  `TenantCreatedIntegrationEvent` es seguro: la creacion de suscripcion es idempotente y el
  recalculo es un upsert.

Para reconciliar un tenant que quedo atascado por este bug **antes** del fix (el mensaje
original ya fue ACKed y no se puede reintentar solo), `POST /admin/subscription/tenants/{tenantId}/recalculate-entitlements`
(PlatformAdmin, §32.4) fuerza el recalculo y republica el evento a demanda — y ahora si falla
devuelve el error real en la respuesta HTTP (ver `AdminController.RecalculateEntitlements`),
en vez de solo poder verse en logs.

Retirados en la fase de cleanup del rediseno (2026-07): `SubscriptionActivatedIntegrationEvent`,
`SubscriptionPlanChangedIntegrationEvent`, `SubscriptionSuspendedIntegrationEvent`,
`SeatsPurchasedIntegrationEvent`. Cada uno era una traduccion redundante del mismo
snapshot que `TenantEntitlementsChangedIntegrationEvent` ya publicaba en el mismo
handler — se retiraron junto con `SubscriptionEventFactory` y los tres consumers
equivalentes en Auth/CloudStorage/Communication, sin periodo de coexistencia porque
ningun tenant productivo dependia todavia de ellos.

## 32.7 Persistencia y migraciones

Base `TaxVision_Subscription`, EF Core + SQL Server. Migraciones en orden:

```text
InitialSubscriptionRedesign     -- Plan/TenantSubscription/Seat/Settings
AddSeatAssignments              -- SubscriptionSeatAssignment
AddEntitlementsAndAddOns        -- TenantEntitlementSnapshot/AddOns
AddRenewals                     -- *Renewal (base/seats/add-ons)
AddAuditLog                     -- SubscriptionAuditLog
AddPlanChangeRequests           -- PlanChangeRequest (Fase 6)
```

Aplicar:

```powershell
dotnet ef database update `
  --project src\Services\Subscription\TaxVision.Subscription.Infrastructure `
  --startup-project src\Services\Subscription\TaxVision.Subscription.Api
```

Guardrail de persistencia: toda entidad hija cuyo `Id` se genera en la factory de
dominio (`Guid.NewGuid()` via `BaseEntity`) y cuelga de un `HasMany` del agregado
padre requiere `builder.Property(x => x.Id).ValueGeneratedNever()` en su
configuracion EF — de lo contrario EF Core confunde el insert con un update.

## 32.8 Configuracion y pruebas locales (Docker Desktop)

`subscription-api` (puerto interno 8080, expuesto localmente en `5360` fuera de
Docker) necesita, ademas de lo comun a todo microservicio:

```env
SUBSCRIPTION_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Subscription;User Id=sa;Password=...;TrustServerCertificate=true
```

En `deploy/docker/docker-compose.yml` el servicio tambien depende de `rabbitmq` y
`redis` (`ConnectionStrings__Redis: redis:6379`) — el cache de entitlements y el
lock distribuido de los jobs de §32.5 lo necesitan. `Subscriptions__DefaultPlanCode`
y `Subscriptions__TrialDays` fijan el plan/duracion de trial que recibe un tenant
nuevo.

El Gateway rutea `/plans`, `/subscriptions`, `/seats`, `/addons`, `/entitlements`,
`/audit` y `/admin/subscription` al cluster `subscription`
(`src/Gateway/TaxVision.Gateway/appsettings.json`).

Para probar en Docker Desktop:

```powershell
docker compose -f deploy/docker/docker-compose.yml up -d --build subscription-api
```

La coleccion Postman `Postman_Collection/TaxVision_Subscription.postman_collection.json`
cubre los 28 endpoints agrupados por recurso (Plans, Subscriptions, Seats, AddOns,
Entitlements, Audit, Admin). Usa el mismo environment `TaxVisionBackEnd` que el
resto de colecciones (`UrlBase` = Gateway, `accessToken` obtenido de `POST
/auth/login`, `tenantId` del claim del JWT). Flujo minimo sugerido:

1. `POST /tenants` + aceptar invitacion de `TenantAdmin` (coleccion Tenant/Auth) —
   esto crea la suscripcion en trial automaticamente.
2. `GetMySubscription` para confirmar el plan/estado inicial.
3. `ChangePlan` y luego `GetPendingPlanChange` — si `PlanChangeEffective` es
   `Immediate` (default) el plan ya cambio; si el tenant lo configuro en
   `EndOfPeriod`, aparece la solicitud pendiente.
4. `PurchaseSeats` / `PurchaseAddOn` y sus variantes de asignar/cancelar/renovar.
5. `GetEntitlementSummary` para ver el efecto combinado.
6. `SearchAuditLog` para confirmar que cada mutacion anterior quedo registrada.
7. `Admin - RecalculateEntitlements` (PlatformAdmin) si un tenant quedo con
   suscripcion pero sin snapshot de entitlements (ver §32.6) — reintenta el
   calculo y republica `TenantEntitlementsChangedIntegrationEvent` a demanda, sin
   esperar a que algo lo dispare de nuevo automaticamente.

## 32.9 Pendientes documentados

- **Billing real (Fase 5)**: no hay integracion con un proveedor de pagos. Los
  endpoints `renew` manuales y los jobs de renovacion son un sustituto temporal
  mientras no exista ese microservicio.
- **Prorrateo**: excluido a proposito de todo el microservicio (§32.3) — no es
  logica de este sistema, sera responsabilidad exclusiva de Billing si alguna vez
  se necesita cobrar la diferencia de un cambio de plan a mitad de periodo.
- `/internal/users/{userId}/access` no se expone via Gateway — solo Auth lo llama
  en la red interna con un JWT de servicio (`actor_type=Service`).
- **Entitlements `communication.*` (Fase 9)**: el catalogo de planes no define
  todavia claves para Communication (max participantes, minutos, grabacion, etc.).
  `TenantEntitlementsChangedQuotaConsumer`/`bindSubscriptionConsumers` ya leen esas
  claves si existen; falta sembrarlas en `SubscriptionPlanCatalogSeeder` cuando se
  defina el catalogo real por plan/tier.

# 33. Multi-tenancy: subdominios y dominios propios (Auth)

Esta seccion documenta el trabajo de las fases A3-A7 (julio 2026): Auth se
convierte en el dueno del ciclo de vida completo de los dominios de un tenant —
tanto el subdominio de plataforma (`oficina1.taxprocore.com`) como un dominio
propio opcional (`archivos.suoficina.com`). El diseno completo vive en el
documento externo `Auth_y_CloudStorage_Plan_Completitud_v2.md` §9-11. No se creo
un microservicio nuevo: se quedo en Auth porque ya es el dueno de la identidad y
de la resolucion de tenant, y separarlo hubiera significado una llamada HTTP
sincronica extra en el path critico de cada request (resolver el Host).

## 33.1 Dominio: `TenantDomain` y `TenantSubdomainReservation`

- **`TenantDomain`** (`Domain/TenantDomains/TenantDomain.cs`), agregado raiz que
  extiende `AggregateRoot` (ver §33.2 mas abajo). Campos clave: `DomainType`
  (`Subdomain` | `CustomHostname`), `Host` (unico globalmente), `SubdomainSlug`
  (solo subdominios, unico filtrado), `Status`
  (`Pending -> Provisioning -> Active -> Disabled | Failed`), `IsPrimary`.
  - `CreateSubdomain(...)`: arranca directo en `Active` — el certificado wildcard
    de Cloudflare ya cubre `*.taxprocore.com`, no hay paso de provisioning.
  - `CreateCustomHostname(...)`: arranca en `Pending` hasta que se dispara el
    provisioning en Cloudflare (§33.5).
  - `MarkProvisioning`, `MarkActive`, `MarkFailed`, `Disable`, `ChangeSubdomain`
    (Fase A7, solo subdominios, ver §33.4).
- **`SubdomainSlug`** (value object, `Domain/TenantDomains/SubdomainSlug.cs`):
  valida forma de etiqueta DNS (3-63 caracteres, minusculas/digitos/guiones, sin
  guion inicial/final) y rechaza una blocklist de mas de 50 subdominios
  reservados de sistema/branding (`www`, `api`, `admin`, `auth`, `billing`,
  `mail`, `status`, `portal`, etc.). Este VO **no** valida unicidad contra otros
  tenants — eso lo garantiza el indice unico de BD + el endpoint de
  disponibilidad (§33.3).
- **`TenantSubdomainReservation`** (`Domain/TenantDomains/TenantSubdomainReservation.cs`,
  `BaseEntity`, sin `TenantId` porque el tenant todavia no existe): reserva
  temporal de un slug para un email, con TTL corto (`SubdomainReservationTtlMinutes`,
  default 15 minutos) mientras se completa el registro. `Consume()` la marca
  usada; falla si ya esta consumida o expirada.
- **Domain events** (Fase A7-prep, `Domain/TenantDomains/Events/`):
  `TenantDomainCreated`, `TenantDomainActivated`, `TenantDomainDisabled`,
  `TenantDomainProvisioningFailed`, `TenantSubdomainChanged`. `TenantDomain`
  extiende el nuevo `AggregateRoot` (`BuildingBlocks/Domain/AggregateRoot.cs`),
  que anade `AddDomainEvent`/`DomainEvents`/`ClearDomainEvents` sobre
  `TenantEntity`. `AuthDbContext.SaveChangesAsync` drena y despacha estos
  eventos **antes** del commit (mismo criterio que el outbox de integration
  events), reutilizando el mismo `IMessageBus` de Wolverine: como estos tipos no
  estan registrados a RabbitMQ, `PublishAsync` los enruta solo a handlers locales
  en el mismo proceso (`Application/TenantDomains/DomainEvents/*Handler.cs`), que
  escriben la auditoria y publican el integration event correspondiente. Esto
  elimina la duplicacion que existia entre la activacion manual
  (`ActivateTenantDomainHandler`) y el poller automatico (§33.6) — antes cada uno
  reimplementaba la misma auditoria/publicacion por separado.

## 33.2 Resolucion de tenant por Host

- **`TenantHostResolutionMiddleware`** (`Api/Middleware/`): lee **solo**
  `HttpContext.Request.Host` (nunca `X-Forwarded-Host` directo — eso ya lo
  resolvio `ForwardedHeadersMiddleware` antes, solo si el origen esta en la red
  de confianza configurada) y publica el tenant candidato en
  `IResolvedTenantContext`. Si `TenantDomains:EnforceHostResolution` es `true` y
  el Host no resuelve a un tenant activo, responde 404 — nunca cae a un tenant
  por defecto. Rutas exentas: health checks, `/auth/service-token`,
  `/auth/.well-known`, `/openapi`, `/swagger`, y los 3 endpoints llamables desde
  el apex que nunca resuelven tenant: `check-availability`, `reserve` (Fase A7) y
  `tenant-resolution/by-email`.
- **`ITenantResolver`/`TenantResolutionCache`** (`Infrastructure/Tenancy/`):
  cachean en Redis la resolucion Host->TenantId contra la allowlist de hosts
  `Active` (`ITenantDomainRepository.GetActiveHostsAsync`). Clave real:
  `taxvision:tenant-resolution:host:{host}` (el prefijo `taxvision:` lo agrega
  `AddStackExchangeRedisCache` via `InstanceName`, no es parte de la clave que
  arma `TenantResolutionCache.Key()`), TTL fijo de 5 minutos
  (`TenantResolutionCache.Ttl`). Si cambias el `TenantId` de un `TenantDomain`
  a mano (ej. seed local, ver §33.11) y probas antes de que expire, seguis
  viendo el mapeo viejo — hay que invalidar la clave a mano (`DEL` en Redis) o
  esperar el TTL.
- **`EffectiveLoginTenantResolver`** (`Api/Common/`, Fase A6, revisado en la
  iteracion F11 QA): el gap real que hace efectivo el aislamiento de login.
  Antes de Fase A6, `LoginCommand`/`ForgotPasswordCommand` tomaban el
  `TenantId` directo del body — un cliente en `tenantB.taxprocore.com` podia
  mandar el `TenantId` de otro tenant y el subdominio no importaba nada. Con
  `EnforceHostResolution=true` (staging/produccion) el `TenantId` del body se
  **descarta siempre**: solo cuenta el que el middleware resolvio del Host
  real. Con `EnforceHostResolution=false` (Development) el body se acepta como
  excepcion de conveniencia — pero **solo cuando el Host no resolvio nada**.
  Antes de la revision F11 QA, esto se evaluaba al reves: con
  `EnforceHostResolution=false` el body ganaba **siempre**, aunque el Host
  hubiera resuelto un tenant real — un dev-escape-hatch que nunca contemplo
  que localmente pudiera existir una resolucion Host real (ver §33.11). El fix
  invierte la prioridad: el Host resuelto gana siempre que exista, en
  cualquier modo; el body solo es el ultimo recurso cuando el Host no resolvio
  y `EnforceHostResolution=false`. Cubierto por
  `EffectiveLoginTenantResolverTests` (`deploy/tests/TaxVision.Auth.Tests/Api/`,
  el nuevo caso es
  `Development_mode_prefers_the_resolved_host_over_the_body_when_both_are_present`).
  Lo usan igual `AuthController.Login` y `CredentialsController.ForgotPassword`.
  Sumado a que `User.Email` es unico solo por `(TenantId, Email)` (nunca
  global), esto cierra el escenario de un mismo email registrado en dos tenants:
  el tenant siempre lo fija el Host antes de tocar la tabla de usuarios, nunca el
  cliente.

## 33.3 Flujo de alta de un subdominio (Fase A4/A7)

1. **Disponibilidad**: `GET /auth/subdomains/check-availability?slug=...`
   (publico, sin auth) — valida formato/blocklist (`SubdomainSlug`), unicidad
   contra `TenantDomains` y contra reservas activas. Nunca falla: formato
   invalido/reservado/tomado son resultados validos `{ available, reason }`, no
   errores HTTP.
2. **Reserva** (Fase A7): `POST /auth/subdomains/reserve` (publico) crea un
   `TenantSubdomainReservation` para bloquear el slug por 15 minutos mientras el
   usuario termina el resto del formulario de alta. La respuesta incluye
   `RegistrationTicket`: un JWT de un solo proposito (`purpose=tenant-registration`,
   `reg_slug`, `reg_email`, firmado con la misma clave RS256 de siempre,
   `GenerateTenantRegistrationTicket` en `IJwtTokenGenerator`) que expira junto con
   la reserva.
3. El apex envia ese ticket como Bearer token al hacer `POST /tenants`. Tenant
   valida el JWT localmente (misma clave publica RS256 via
   `AddTaxVisionJwtAuthentication`, cero HTTP sincrono de vuelta a Auth) con la
   policy `TenantRegistration`: acepta el claim `purpose=tenant-registration` o el
   rol `PlatformAdmin` (alta directa sin pasar por reserva). Cuando el ticket esta
   presente, `EffectiveTenantRegistrationResolver` (`Api/Common/`) toma
   `Subdomain`/`AdminEmail` unicamente de sus claims — el body nunca los
   sobreescribe, mismo patron que `EffectiveLoginTenantResolver` en Auth. El
   servicio **Tenant** crea el `Tenant` y publica
   `TenantCreatedIntegrationEvent` con el subdominio elegido.
4. Auth consume ese evento en `TenantCreatedConsumer`
   (`Application/Tenants/IntegrationEvents/`): registra el tenant en su propio
   `ITenantRegistry`, siembra roles de sistema + politica MFA, crea el
   `TenantDomain` primario (`EnsurePrimaryDomainAsync`, idempotente) y **consume
   la reserva activa** si seguia vigente — sin bloquear el alta del resto del
   tenant si el slug ya no tenia reserva (tenants creados sin pasar por
   `reserve`, ej. el backfill de tenants viejos).
5. **Cambio posterior** (Fase A7): `PUT /auth/tenant-domains/{domainId}/subdomain`
   (autenticado, `tenant.domains.manage`) permite renombrar el subdominio
   primario ya activo (`oficina1` -> `oficina2`). Vuelve a validar disponibilidad
   igual que el alta; no aplica a custom hostnames.
6. **Backfill** (`TenantDomainBackfillService`, hosted service de un solo paso al
   arrancar): crea el `TenantDomain` primario para tenants dados de alta antes de
   que existiera este modelo.

## 33.4 Dominios propios y Cloudflare for SaaS (Fase A5)

- **`ICloudflareProvisioningClient`**/`CloudflareProvisioningClient`
  (`Infrastructure/Cloudflare/`): unico `HttpClient` externo del servicio (cero
  llamadas HTTP directas a otros microservicios TaxVision). Usa Cloudflare for
  SaaS (Custom Hostnames) con DCV automatico.
- Alta: `POST /auth/tenant-domains` (`tenant.domains.manage`) crea el hostname en
  Cloudflare y el `TenantDomain` en `Pending`/`Provisioning`. Si Cloudflare
  rechaza el alta, el `TenantDomain` **nunca se persiste** (es el unico camino
  que sigue auditando/publicando a mano en vez de via domain event, justamente
  porque el agregado nunca queda rastreado por EF).
- Verificacion: `PUT /{domainId}/verify` consulta (sin mutar) el estado
  DNS/TLS reportado por Cloudflare.
- Activacion: `PUT /{domainId}/activate` confirma `status=active` +
  `ssl.status=active` y pasa el dominio a `Active`. La misma transicion la
  dispara automaticamente **`TenantDomainProvisioningPoller`** (hosted service,
  intervalo de 5 minutos) reconsultando Cloudflare para los hostnames en
  `Provisioning` — ambos caminos comparten `TenantDomain.MarkActive`, que encola
  el mismo domain event.
- Baja: `PUT /{domainId}/disable` primero borra el hostname en Cloudflare
  (anti subdomain-takeover) y solo si eso funciona deshabilita en Auth. El
  dominio primario nunca se puede deshabilitar por esta via (usar
  `ChangeSubdomain`, §33.3).

## 33.5 Endpoints HTTP

| Recurso | Endpoints | Auth |
| --- | --- | --- |
| Subdominios | `GET /auth/subdomains/check-availability`, `POST /auth/subdomains/reserve` | Publico (rate limit `tenant-lookup`, 30/min/IP) |
| Resolucion | `GET /auth/tenant-resolution/by-host`, `POST /auth/tenant-resolution/by-email` | Publico (`tenant-lookup` / `tenant-recovery`, 5/min/IP) |
| Dominios del tenant | `GET /auth/tenant-domains`, `POST /auth/tenant-domains`, `PUT /{id}/verify`, `PUT /{id}/activate`, `PUT /{id}/disable`, `PUT /{id}/subdomain` | `tenant.domains.manage` (permiso reservado a la plataforma: `IsAssignableByTenant=false`, no se puede meter en un rol custom) |

## 33.6 Eventos de integracion publicados

`TenantDomainCreatedIntegrationEvent`, `TenantDomainVerifiedIntegrationEvent`,
`TenantDomainActivatedIntegrationEvent`, `TenantDomainDisabledIntegrationEvent`,
`TenantDomainProvisioningFailedIntegrationEvent`,
`TenantSubdomainChangedIntegrationEvent` (Fase A7) — en
`BuildingBlocks/Messaging/AuthIntegrationEvents/`. Hoy no tienen consumer en
otro microservicio (documentado como pendiente, §33.8); existen porque
`AuthAuditLog` y la trazabilidad de auditoria ya los necesitaban, y dejarlos
listos evita otro ciclo de migracion de contrato cuando algun servicio los
necesite (ej. Notification avisando "tu URL de acceso cambio").

No se publica un evento `TenantDomainReserved`: nada lo consume hoy, y agregarlo
hubiera sido infraestructura sin uso — se prefirio no construirlo hasta que
haya un consumidor real (mismo criterio que se aplico para no generalizar
domain events a todo Auth, ver `EnsurePrimaryDomainAsync` en el codigo).

## 33.7 Persistencia y migraciones

Migraciones relevantes en `TaxVision_Auth`:

```text
AddTenantDomains                    -- TenantDomains + TenantSubdomainReservations
AddTenantDomainsManagePermission    -- permiso tenant.domains.manage
```

`Host` es unico globalmente (no por tenant) — es la clave real de la resolucion
Host->TenantId. `SubdomainSlug` es unico con filtro (`WHERE SubdomainSlug IS NOT
NULL`), porque los custom hostnames no tienen slug. El agregado de reserva no
tiene tenant y su unicidad de "activa" se resuelve en el repositorio
(`ConsumedAtUtc IS NULL AND ExpiresAtUtc > now`), no con un indice `UNIQUE`
puro, porque un slug liberado (expirado o consumido) puede reservarse de nuevo.

## 33.8 Configuracion

```json
"TenantDomains": {
  "BaseDomain": "taxprocore.com",
  "EnforceHostResolution": true,
  "SubdomainReservationTtlMinutes": 15
},
"Cloudflare": {
  "BaseUrl": "https://api.cloudflare.com/client/v4/",
  "ApiToken": "",
  "ZoneId": ""
}
```

`EnforceHostResolution` se desactiva en `appsettings.Development.json` (no hay
subdominios reales apuntando a `localhost`). `Cloudflare:ApiToken` debe ser un
token scoped (Zone->DNS->Edit + Zone->SSL and Certificates->Edit), nunca la
Global API Key.

## 33.9 Pruebas

Suite en `deploy/tests/TaxVision.Auth.Tests/`: dominio
(`TenantDomainTests`, `TenantDomainDomainEventsTests`,
`TenantSubdomainReservationTests`), aplicacion (un test file por
comando/handler/domain-event-handler en `Application/`), infraestructura
(`AuthDbContextDomainEventDispatchTests`, con el proveedor InMemory de EF Core,
probando que `SaveChangesAsync` efectivamente drena y despacha los domain
events) y aislamiento (`EffectiveLoginTenantResolverTests`,
`TenantHostResolutionMiddlewareTests`).

## 33.10 Pendientes documentados

- La coleccion Postman de Auth no tenia **ninguna** entrada de esta seccion
  hasta ahora (ni siquiera `check-availability` de Fase A4) — corregido en esta
  misma iteracion (folders `Subdomains`, `TenantResolution`, `TenantDomains`).
- Sin limite de roles custom por tenant (ej. tope de 20) — un tenant admin
  puede crear roles ilimitados hoy; era una recomendacion de auditoria, no algo
  implementado.
- `TenantSubdomainChangedIntegrationEvent` y el resto de eventos de esta
  seccion no tienen consumer todavia en ningun otro microservicio.
- `ChangeSubdomain` no reprovisiona nada en Cloudflare porque los subdominios de
  plataforma no pasan por Cloudflare (certificado wildcard) — solo aplica a
  `DomainType.Subdomain`, nunca a custom hostnames.

## 33.11 Simular produccion en local: subdominio real en dev (`demo.localhost`)

Guia practica para un dev que quiere probar el login/resolucion de tenant
igual que en produccion (Host real, sin campo manual de `TenantId`) sin tener
un dominio real apuntando a su maquina. Surgio de un problema real: el
frontend QA console (`taxvision-communication-frontend`) asumia que Auth
siempre resuelve por subdominio, y rompia en local porque `localhost` no
resuelve a ningun `TenantDomain`.

1. **DNS gratis**: `*.localhost` resuelve a `127.0.0.1`/`::1` sin tocar el
   archivo hosts — tanto Windows como los navegadores basados en Chromium lo
   implementan nativo (RFC 6761 §6.3, dominio `.localhost` reservado para
   loopback). Confirmado navegando a `http://demo.localhost:5047/health/live`
   sin ninguna configuracion previa. Si tu entorno no lo soporta, agrega
   `127.0.0.1 demo.localhost` al hosts file a mano.
2. **Registrar el subdominio en `TenantDomains`** apuntando al tenant que
   queres usar para probar (no hay comando/endpoint que acepte un
   `BaseDomain` custom como `localhost` — `TenantDomain.CreateSubdomain` solo
   se invoca con el `BaseDomain` de config, `taxprocore.com` — asi que el seed
   es un INSERT directo, sin domain events. Aceptable porque es tooling de
   dev, no un flujo de producto):
   ```sql
   INSERT INTO TenantDomains (Id, DomainType, Host, SubdomainSlug, Status, IsPrimary, CreatedByUserId, CreatedAtUtc, TenantId)
   VALUES (NEWID(), 'Subdomain', 'demo.localhost', 'demo', 'Active', 0, '<un-user-id-existente>', SYSUTCDATETIME(), '<tenant-id>');
   ```
   `IsPrimary=0` para no pisar el dominio primario real del tenant
   (`oficina.taxprocore.com`, backfileado por `TenantDomainBackfillService`) —
   podes tener los dos apuntando al mismo tenant sin problema, `Host` es lo
   unico que tiene que ser unico.
3. **Por que no hace falta tocar CORS**: el Gateway rutea `/auth/{**catch-all}`
   con el transform `RequestHeaderOriginalHost: true`
   (`Gateway/TaxVision.Gateway/appsettings.json`) — esto hace que YARP
   preserve el Host *original* de la request (`demo.localhost:5047`) en vez
   de reescribirlo al destino (`localhost:5124`), asi que Auth ve el Host real
   directo en `HttpContext.Request.Host`, sin pasar por
   `ForwardedHeadersMiddleware`/`ReverseProxyTrust` (§33.2's nota de
   `ITenantResolver`) — CORS es sobre el `Origin` del navegador
   (`http://localhost:5173`, la pagina del frontend, que **no cambia**), no
   sobre el Host al que apunta la request. Alcanza con cambiar
   `VITE_GATEWAY_URL=http://demo.localhost:5047` en el `.env` del frontend
   (§30.8-equivalente del lado frontend) — la pagina se sigue sirviendo desde
   `localhost:5173` sin tocar nada mas.
4. **Cache**: invalidar `taxvision:tenant-resolution:host:demo.localhost` en
   Redis si cambiaste el `TenantId` del seed despues de haber probado una vez
   (TTL 5 min, ver §33.2).
5. **Reiniciar Auth** — el fix de `EffectiveLoginTenantResolver` (§33.2) y
   cualquier cambio de codigo no se recargan solos con `dotnet run`.

Con esto, el login (y cualquier otro endpoint gateado por
`TenantHostResolutionMiddleware`) funciona exactamente igual que en
produccion: cero campos manuales, el tenant sale del Host real.

# 34. Terminos de servicio (ToS) — aceptacion y gating (Auth)

Auth es tambien el dueno de la aceptacion de Terminos de Servicio / Acceptable Use
Policy por tenant. El diseno es deliberadamente simple: una version de texto vigente
por instalacion (no por tenant) y un historial append-only de aceptaciones.

## 34.1 Dominio y persistencia

- **`TenantTermsAcceptance`** (`Domain/Terms/TenantTermsAcceptance.cs`, `TenantEntity`,
  sellada, constructor privado): registro **inmutable y append-only** — cada aceptacion
  inserta una fila nueva, nunca actualiza una existente. Campos: `AcceptedByUserId`,
  `TermsVersion` (hasta 32 caracteres), `IpAddress` (hasta 45), `UserAgent` (hasta 512),
  `AcceptedAtUtc`. Se crea solo via la factory estatica
  `TenantTermsAcceptance.Accept(tenantId, acceptedByUserId, termsVersion, ipAddress,
  userAgent, nowUtc)`.
- Tabla `TenantTermsAcceptances` con indice compuesto `(TenantId, AcceptedAtUtc desc)`
  usado por `GetLatestAsync` para resolver la ultima aceptacion del tenant.
- Migracion `AddTenantTermsAcceptances`.
- `ITenantTermsAcceptanceRepository` expone `GetLatestAsync(tenantId, ct)` y `AddAsync`.

## 34.2 Endpoints

Base `/auth/tenant/terms`, `[Authorize]` a nivel de controller sin permiso especifico
(cualquier usuario autenticado del tenant puede consultar y aceptar; exigir un permiso
administrativo aqui crearia un deadlock de arranque si el `TenantAdmin` todavia no
acepto).

| Verbo y ruta | Uso |
| --- | --- |
| GET `/auth/tenant/terms/status` | `{ accepted, currentVersion, acceptedVersion, acceptedAtUtc }` para el tenant del JWT |
| POST `/auth/tenant/terms/accept` | Inserta una nueva `TenantTermsAcceptance`; siempre exitoso, no rechaza "ya aceptado" |

`POST /accept` tambien escribe un `AuthAuditLog` (`AuthAuditAction.TermsAccepted`) y
publica `TenantTermsAcceptedIntegrationEvent` (`TenantId`, `AcceptedByUserId`,
`TermsVersion`, `CorrelationId`).

## 34.3 Gating: `TermsAcceptanceMiddleware`

Registrado despues de autenticacion/autorizacion/rate limiting y del
`SessionDenylistMiddleware`, antes del mapeo de endpoints. Para cada request
autenticado con claim `tenant_id`:

- Si la ruta empieza por `/health`, `/auth/service-token`, `/auth/.well-known`,
  `/openapi`, `/swagger` o `/auth/tenant/terms` (el propio endpoint de aceptacion, para
  no bloquearse a si mismo), pasa de largo.
- Si el request no esta autenticado o el JWT no trae `tenant_id` (tokens M2M de
  servicio), tambien pasa de largo — el trafico entre microservicios nunca se bloquea
  por ToS.
- En cualquier otro caso, resuelve la ultima `TenantTermsAcceptance` del tenant. Si su
  `TermsVersion` no coincide con `TermsOptions.CurrentVersion` (incluyendo el caso de no
  tener ninguna aceptacion), corta la cadena con **HTTP 409**:

```json
{
  "type": "Terms.NotAccepted",
  "title": "The current Terms of Service/Acceptable Use Policy has not been accepted yet.",
  "currentVersion": "2026-07-14"
}
```

## 34.4 Configuracion

```json
"Terms": { "CurrentVersion": "2026-07-14" }
```

`TermsOptions.CurrentVersion` (seccion `Terms`) es la unica clave. Subir este valor
fuerza a **todos los tenants** a re-aceptar en su siguiente request autenticado — no
hay mecanismo de excepcion ni de periodo de gracia por tenant.

# 35. Postmaster Service (dispatch de email desacoplado de Notification)

Microservicio nuevo (`src/Services/Postmaster/`, puerto 5370) responsable del envio
material de emails transaccionales — reemplaza el envio in-process que Notification
hacia directamente por SMTP. Notification sigue siendo dueno de plantillas/layouts/
render y de campanas masivas; Postmaster solo transporta el MIME ya renderizado.
Diseno completo en `Responsabilidades/Postmaster_Service_Design_And_Implementation_Plan.md`.
Implementadas las Fases 1 a 7 y 9 (mas la 3.5, provider resolution + CID inline assets):
delivery logs con timeline de auditoria (35.5), suppression list manual/API (35.6) y
rate limiting + circuit breaker por provider (35.7). La **Fase 8** (tracking webhooks
de proveedores tipo SendGrid/Mailgun/Postmark) se implemento y **se retiro
deliberadamente** — el envio real es SMTP arbitrario configurado manualmente (Gmail,
SMTP2GO, etc.) mas, a futuro, Gmail API/Graph via Connector; ninguno de los dos habla
el protocolo de webhooks que esa fase asumia (tomado literal de un ejemplo ilustrativo
del plan — "patron SendGrid/Postmark" — no de un proveedor efectivamente elegido). Si
en el futuro se conecta un proveedor real con webhooks de tracking, se construye
entonces con el formato real de ESE proveedor. La Fase 10 de este plan (runbook de
cutover) sigue pendiente, pero la activacion en codigo del flag
`Notification:UsePostmasterDispatch` ya no lo esta: se completo en la Fase 21 del plan de
hardening independiente (`Responsabilidades/Hardening_Audit_Fixes_And_Notification_Migration_Plan.md`
§5, 2026-07-18, ver §28.1/§28.9), que lo flippeo a `true` por default. Lo que queda de
ambos planes converge en lo mismo: verificacion real en produccion (ventana de monitoreo
con trafico real) — no es un gap tecnico, requiere acceso al entorno de produccion.

## 35.1 Aggregates y tablas

| Aggregate | Responsabilidad | Tabla(s) |
| --- | --- | --- |
| `SentMessage` | Intento de envio (idempotente por tenant+clave), estado y eventos de entrega | `SentMessages`, `SentMessageRecipients`, `SentMessageEvents` |
| `SystemEmailProvider` | Config SMTP global (cross-tenant) usada como default | `SystemEmailProviders` |
| `TenantEmailProvider` | Config SMTP propia del tenant — su ausencia NUNCA cae a System (anti-spoofing) | `TenantEmailProviders` |
| `ProviderHealthStatus` | Circuit breaker a nivel de *resolucion* de provider (abre tras 3 fallos consecutivos, lo chequea `ProviderResolver` antes de resolver) | `ProviderHealthStatuses` |
| `EmailIdempotency` | Reserva tecnica por `(TenantId, IdempotencyKey)`, PK compuesta | `EmailIdempotency` |
| `SuppressionListEntry` | Lista negra por `(TenantId, EmailAddress)`, PK compuesta — Fase 7 | `SuppressionListEntries` |

Nota: hay un **segundo** circuit breaker, a nivel de *transporte* (Polly, dentro de
`SmtpEmailSender`, uno por `ProviderCode`, ver 35.7) — no es redundante con
`ProviderHealthStatus`: el de resolucion decide "que provider usar", el de transporte
decide "sigo intentando conectar a este SMTP ahora mismo".

## 35.2 Flujo end-to-end

Notification renderiza (Fluid, ver 28) y publica `notifications.email_send_requested.v1`
con el `HtmlBody`/`TextBody` ya resueltos — Postmaster no renderiza nada. El consumer
(`NotificationsEmailSendRequestedConsumer`):

1. `IIdempotencyGuard.TryReserveAsync` — si la clave ya se completo antes, reenvia el
   callback `Succeeded` con el `SentMessageId` existente (replay, no reenvia el email).
2. Resuelve el provider (`IProviderResolver`) segun `RequiredProviderScope` (`System` |
   `Tenant`) — Tenant sin `TenantEmailProvider` propio nunca cae a System, publica
   `postmaster.email_delivery.provider_not_configured.v1` y no reintenta solo.
3. Si resuelve, crea el `SentMessage`, arma el MIME con MailKit (`MimeMessageBuilder`,
   soporta `LinkedResources`/CID para logos), envia via `IEmailSender` (SMTP con retry
   Polly para 4xx transitorios; rechazos 5xx de destinatario se aislan sin abortar el
   resto), marca `Sent`/`Failed` y publica el callback correspondiente
   (`postmaster.email_delivery.succeeded|failed.v1`).

## 35.3 Notification Fase 5 — verificacion: el render NO vive en los consumers de Notification

Chequeo pedido explicitamente al cerrar esta fase: confirmar que ningun `Consumer`
de Notification invoca `ITemplateRenderer`/`FluidTemplateRenderer`/`IEmailTemplateRepository`
para el flujo transaccional (que ahora depende de Postmaster). Resultado real del grep
sobre `TaxVision.Notification.Application/Consumers/`:

- **Camino transaccional (limpio):** `SendTemplateEmailHandler` — un *command handler*,
  no un consumer — renderiza una sola vez con `ITemplateRenderer` dentro del request
  (mientras hay token de usuario para leer plantilla/layout de CloudStorage), persiste
  el `OutboundEmailMessage` con el cuerpo final y publica el evento. Ningun `Consumer`
  bajo esa carpeta usa el renderer para este camino — `InProcessEmailDispatchGateway`
  y `EventBasedEmailDispatchGateway` (Fases 3-4) solo reenvian contenido ya renderizado.
- **Hallazgo honesto, fuera de alcance de esta migracion:** `EmailCampaignConsumers.cs`
  (`EmailCampaignBatchIntegrationEvent`) SI sigue llamando a `ITemplateRenderer` directo
  dentro de un consumer — las campanas masivas son una feature propia y anterior de
  Notification (`EmailCampaigns`/`IEmailCampaignRepository`), nunca migrada a Postmaster
  (que solo cubre el stream `Transactional`; `EmailStream.Bulk` existe en el dominio de
  Postmaster pero nada lo usa todavia). No se toco en esta sesion — migrar campanas al
  stream Bulk de Postmaster queda como trabajo futuro, junto con la Fase 10 (cutover).

Conclusion: el objetivo real de la Fase 5 ("Notification ya no renderiza en el camino
que depende de un servicio externo") esta cumplido para el envio transaccional. La
frase "rendering vive en Scribe" de la definicion original de la fase no es exacta —
hoy el render vive en el propio `FluidTemplateRenderer` de Notification (seccion 28),
no en un microservicio Scribe separado (que no existe todavia); Postmaster solo
consume el resultado ya renderizado, que es la propiedad que realmente importaba
verificar (Postmaster nunca necesita saber renderizar nada).

## 35.4 Endpoint admin: `SystemEmailProvider`

El provider "default" de plataforma (el que usa `RequiredProviderScope=System` cuando
un tenant no configuro el suyo propio) se seedea al arrancar con credenciales
placeholder (`SystemEmailProviderSeeder`, `localhost:1025`) — el seeder solo corre si
`ProviderCode="smtp-default"` no existe todavia, asi que nunca pisa una config real.
Para cargar credenciales SMTP reales:

- `PUT /postmaster/system/provider/{providerCode}` (permiso `postmaster.providers.write`
  **y** rol `PlatformAdmin` — a diferencia del endpoint de tenant, ningun tenant admin
  puede tocar el provider de plataforma). Body: `{ displayName, providerType, host, port,
  useTls, username, password, fromAddressDefault, fromDisplayNameDefault,
  rateLimitPerMinute }`. Upsert por `ProviderCode` (crea si no existe, reconfigura si
  ya existe) — `UpsertSystemEmailProviderCommand`.

Nota (aclarada en su momento a partir de una confusion real): esto es un provider
*complementario* al de tenant, no compite con el — `SystemEmailProvider` cubre emails
de plataforma (bienvenida, reset de password) que tienen que salir aunque el tenant
todavia no configuro nada; `TenantEmailProvider` (ya en 35.1) es el que el tenant usa
para mandar con su propio dominio/SMTP.

## 35.5 Fase 6 — Timeline de auditoria por mensaje

`GET /postmaster/messages/{id}/events` (permiso `postmaster.messages.read`, tenant-scoped
por JWT) devuelve el timeline ordenado cronologicamente de `SentMessageEvent` de un
`SentMessage` (`Queued` → `Sending` no genera evento propio → `Sent`/`Failed`/`Suppressed`
→ eventos de tracking del webhook si aplica). El registro de eventos en si ya existia
desde la Fase 2 (`SentMessage.MarkAsSent/Failed/Suppressed`) — esta fase agrego la
consulta (`GetSentMessageWithEventsHandler`) y el endpoint, no el modelo de datos.

## 35.6 Fase 7 — Suppression list

`SuppressionListEntry` (PK `(TenantId, EmailAddress)`, `Reason` enum
`HardBounce | Complaint | Manual | AbuseReport`) bloquea el envio a una direccion
especifica. Endpoints bajo `/postmaster/suppression` (permisos `postmaster.suppression.read`/
`.write`, ya estaban reservados en el catalogo desde antes):

- `GET /postmaster/suppression?address=&reason=&page=`
- `POST /postmaster/suppression` — upsert manual (`AddSuppressionEntryCommand`; si la
  direccion ya estaba suprimida, reactiva con el motivo/fecha nuevos en vez de duplicar).
- `DELETE /postmaster/suppression/{address}`

Integracion con el consumer de Fase 5 (`NotificationsEmailSendRequestedConsumer`,
gap que su propia documentacion XML dejaba pendiente explicitamente): antes de enviar,
`ApplySuppressionAsync` chequea `ISuppressionListRepository.GetSuppressedAsync` para
todos los destinatarios del mensaje.

- Si **todos** los destinatarios estan suprimidos → el `SentMessage` pasa directo a
  `Suppressed` (nunca intenta SMTP), se completa la idempotencia y se publica
  `postmaster.email_delivery.suppressed.v1`.
- Si **algunos** estan suprimidos → esos recipients quedan marcados `Suppressed`
  (`SentMessage.RecordDeliveryEvent`) y el envio continua para el resto —
  `MimeMessageBuilder` excluye del MIME real a cualquier recipient en estado
  `Suppressed`, asi que nunca reciben el mensaje aunque el `SentMessage` global
  termine `Sent`.

## 35.7 Fase 9 — Rate limiting + circuit breaker de transporte

- **Rate limiting** (`IEmailProviderRateLimiter`): ventana fija de 1 minuto vía Redis
  INCR+EXPIRE, clave `postmaster:ratelimit:{provider}:{tenant}:{yyyyMMddHHmm}`, tope =
  `ResolvedEmailProvider.RateLimitPerMinute` (ya configurado por provider desde Fase 3).
  Sin `ConnectionStrings:Redis` configurado se degrada a un limiter no-op (mismo
  criterio que el distributed lock de Signature Fase 4) — nunca rompe el envio en dev
  local. Si se agota el cupo, el consumer marca el `SentMessage` `Failed` con razon
  `RateLimited: retry after Ns.` (terminal — el reintento real depende de que
  Notification vuelva a publicar el evento).
- **Circuit breaker de transporte** (`ProviderCircuitBreaker`, Polly): uno por
  `ProviderCode`, envuelve todo el intento SMTP (connect+auth+send) dentro de
  `SmtpEmailSender`. `FailureRatio=1.0` + `MinimumThroughput=5` sobre una ventana de
  2 minutos ≈ "5 fallos consecutivos" → abre 60s; durante ese tiempo lanza
  `BrokenCircuitException` sin intentar conectar (`SendResult` con razon
  "circuit breaker is open").
- **Metricas** (`PostmasterMetrics`, `System.Diagnostics.Metrics.Meter("postmaster-service")`,
  registrado en OTel via `AddMeter(serviceName)` en `OpenTelemetryRegistration`):
  `postmaster_rate_limit_hits_total{provider,tenant}` y
  `postmaster_circuit_breaker_opened_total{provider}` — la base para una alerta de
  Prometheus ("el circuit breaker de X abrio"), no la alerta en si (eso es config de
  Prometheus/Grafana, fuera del codigo del servicio).

## 35.8 D3 — Canal de envio #3: TenantOAuth (via Connectors)

Tercer canal de envio junto a System/Tenant SMTP (Fase 3/3.5) — un tenant que conecto
una cuenta Gmail/Graph via OAuth (seccion 37, "conectar cuenta") puede enviar
notificaciones transaccionales desde esa cuenta en vez de SMTP. Diseno completo en
`Responsabilidades/Postmaster_OAuthEmailSend_Design_And_Implementation_Plan.md`.

- **`ProviderScope.TenantOAuth`** (nuevo valor del enum junto a `System`/`Tenant`) —
  lo setea Notification en `RequiredProviderScope` del evento
  `notifications.email_send_requested.v1` cuando quiere forzar este canal.
- **Proyeccion local `TenantOAuthAccount`** (`TenantOAuthAccounts`,
  `TenantId+AccountId` unico) — mismo patron que `CustomerEmailProjection` (Signature):
  se alimenta de `connectors.tenant_email_account.connected.v1`/`.disconnected.v1`
  (ya publicados por Connectors desde el flujo de conectar cuenta, seccion 37.3) via
  `TenantEmailAccountConnectedConsumer`/`TenantEmailAccountDisconnectedConsumer`. Postmaster
  nunca llama a Connectors por red solo para resolver el provider — la proyeccion local
  alcanza (`IOAuthProviderResolver`, analogo a `IProviderResolver` con
  `ITenantEmailProviderRepository`). Regla interina: si un tenant conecto mas de una
  cuenta, la ultima conexion activa gana (no existe hoy un concepto de "cuenta primaria"
  seleccionable por el usuario).
- **`NotificationsEmailSendRequestedConsumer`** se ramifica al inicio segun
  `RequiredProviderScope`: `TenantOAuth` va a `HandleTenantOAuthPathAsync`
  (resolver local → suppression check compartido → `IOAuthEmailSender.SendAsync`),
  todo lo demas sigue por `HandleSmtpPathAsync` (logica SMTP sin cambios de Fase 5-9).
  Sin control de cupo propio en el camino OAuth — el rate limit ya lo aplica Connectors
  por (tenant, cuenta) en su M2M de envio (20/min default), duplicarlo aca seria el
  mismo cupo enforced dos veces con configuraciones potencialmente divergentes.
- **`ConnectorsSendClient`** (implementa `IOAuthEmailSender`) llama
  `POST {Postmaster:Connectors:BaseUrl}/connectors/accounts/{accountId}/send` con el
  mismo `IPostmasterServiceTokenAcquirer` M2M que ya usa `CloudStorageInlineAssetFetcher` —
  nunca ve el token OAuth del tenant, Connectors es quien de verdad habla con
  Gmail/Graph. Sin threading en v1 (los 3 parametros de threading de
  `IOAuthEmailSender.SendAsync` van null): el evento de Notification no trae
  identificadores nativos del proveedor para responder un hilo existente — mismo
  criterio de incrementalidad que los inline assets en Fase 3.5.
- **Config nueva**: `Postmaster:Connectors:BaseUrl` (default local
  `http://localhost:5390`, docker-compose `http://connectors-api:8080`).

**Cross-referencia (Fase 16 de Correspondence, verificado):** además del consumer de
Notification (35.2), Postmaster expone `POST /postmaster/correspondence-messages`
(policy `ServiceOnly`, `SendCorrespondenceMessageHandler`) — el confirmado consumidor
M2M del envío síncrono de `Correspondence` (`POST /correspondence/drafts/{id}/send`,
README 38.2/38.3), idempotente por `CorrespondenceDraftId`. Mismo pipeline de
suppression/idempotencia que el resto de este servicio (35.6), sin retry de su lado (la
idempotencia ya cubre el reintento manual del usuario desde la UI).

## 35.9 Riesgo aceptado: Postmaster es un único punto de egreso para dos dominios

Los dos flujos de arriba (35.2 notificaciones automáticas, 35.8/cross-referencia
correspondencia humana) comparten hoy el mismo proceso, la misma `SuppressionList` y el
mismo mecanismo de idempotencia (Fase 11 del hardening) — antes de D3 eran dos dominios
de falla independientes. Es un trade-off deliberado (centralizar suppression/idempotencia
en un solo lugar en vez de duplicarlos), no un descuido, pero significa que una caída o
degradación severa de Postmaster afecta **simultáneamente** las notificaciones
transaccionales del tenant y el envío de correspondencia humana en vivo. Decisión, alternativas
consideradas (incluyendo un circuit breaker/bulkhead separando ambos paths como mitigación
futura posible, no construida hoy) y consecuencias documentadas en
`Responsabilidades/ADR-0004-postmaster-single-egress-point.md`. Recomendación operacional
explícita: la prioridad de on-call/alerting de Postmaster debe ser más alta que la que
cualquiera de sus dos responsabilidades predecesoras habría justificado por sí sola —
fuera del alcance de este repositorio, nota para quien administre infraestructura real.

# 36. Scribe Service (templating centralizado de email)

Microservicio nuevo (`src/Services/Scribe/`, puerto 5340) dueno de plantillas,
layouts y render de email para todo el ecosistema — reemplaza el `FluidTemplateRenderer`
in-process que tenia Notification (seccion 28) y los catalogos hardcodeados en C#
(`EmailTemplates.cs`, `SignatureTemplateCatalog.cs`) que usaban Notification y Signature.
Diseno completo en `Responsabilidades/Scribe_Service_Design_And_Implementation_Plan.md`
y `Responsabilidades/Scribe_Email_Style_Guide.md` (convenciones HTML email-safe: tablas,
CID inline para imagenes, sin flex/grid/scripts). Implementadas las Fases 0 a 8 y 10
(Fase 9, Campaigns, queda **deliberadamente pendiente** — ver 36.7).

## 36.1 Aggregates y tablas

| Aggregate | Responsabilidad | Tabla(s) |
| --- | --- | --- |
| `EmailTemplate` | Plantilla (System o Tenant) — dueno de sus `EmailTemplateVersion`, invariante "solo una Published a la vez" | `EmailTemplates`, `EmailTemplateVersions`, `TemplateVariableDefinitions` |
| `EmailLayout` | Layout base que todo `EmailTemplateVersion` debe extender (no se permite HTML standalone) — mismo patron que `EmailTemplate` | `EmailLayouts`, `EmailLayoutVersions` |
| `EventTemplateMapping` | Resuelve `EventKey` → `TemplateKey` (con prioridad Tenant sobre System y locale opcional) | `EventTemplateMappings` |
| `TenantLogoRef` / `TenantLogoMissingNotification` | Logo por tenant para el header del layout (Fase 4.5, pipeline CID) + aviso cuando falta | `TenantLogoRefs`, `TenantLogoMissingNotifications` |

Cada `EmailTemplateVersion`/`EmailLayoutVersion` referencia su HTML/texto/design-JSON/preview
como blobs en CloudStorage (`HtmlStorageKey`+`HtmlFileId`, etc.) via `ITemplateStorageService`
— Scribe solo tiene subida/lectura, nunca delete (ver 36.7, retention).

## 36.2 Endpoints

- `POST /scribe/render` (permiso `scribe.render`) — HTTP. Recibe
  `{ eventKey, tenantId?, locale?, variables, logoScope? }`, devuelve
  `{ subject, html, text?, inlineAssets[] }`. Es el unico camino de render, usado por
  Notification (36.3/36.4) y por Postmaster para el path de correspondencia. Hasta la
  Fase 8 del hardening existio ademas un gRPC `scribe.TemplateService/Render`
  (`Protos/render.proto`) con el mismo query interno pero deadline duro de 200ms —
  fue la primera implementacion de gRPC de todo el monorepo, nunca tuvo un consumidor
  real (Notification siempre uso HTTP) y se retiro por completo (ver 36.7, ADR-0003).
- CRUD de templates/layouts (permisos `scribe.templates.*`/`scribe.layouts.*`):
  crear, agregar version Draft, publicar version (archiva la anterior automaticamente),
  deprecar.
- `POST /scribe/templates/versions/{versionId}/preview` — renderiza con variables de
  muestra sin tocar el estado Published (Fase 5).
- CRUD de `EventTemplateMapping` (permisos `scribe.mappings.*`).

## 36.3 Eventos consumidos por Notification (Fase 8 — migracion)

13 templates migrados desde los catalogos viejos de Notification/Signature, sembrados
al arrancar por `ScribeNotificationTemplateSeeder` (`EventTemplateMapping` + `EmailTemplate`
Published, todos sobre el layout `system-base`):

| EventKey | TemplateKey | Origen |
| --- | --- | --- |
| `auth.invitation_created.v1` | `auth.invitation` | `EmailTemplates.Invitation` (Notification) |
| `auth.password_reset_requested.v1` | `auth.password_reset` | idem |
| `auth.mfa_otp_requested.v1` | `auth.otp_code` | idem |
| `auth.email_change_requested.v1` | `auth.email_change` | idem |
| `auth.email_change_security_alert.v1` | `auth.security_alert` | idem (segundo correo del mismo evento) |
| `auth.tenant_recovery_requested.v1` | `auth.tenant_recovery` | idem |
| `auth.user_registered.v1` | `auth.welcome` | idem — sin consumer hasta Fase 9 (ver abajo) |
| `sig.signer_invited.v1` | `sig.invitation.v1` | `SignatureTemplateCatalog.Invitation` (Signature/Notification) |
| `sig.request_reminder_due.v1` | `sig.reminder.v1` | idem `.Reminder` |
| `sig.request_completed.v1` | `sig.completed.v1` | template existia, sin consumer — nuevo `SignatureRequestCompletedConsumer` |
| `sig.request_expired.v1` | `sig.expired.v1` | idem, nuevo `SignatureRequestExpiredConsumer` |
| `sig.signer_rejected.v1` | `sig.declined.v1` | idem, nuevo `SignerRejectedConsumer` |
| `sig.verification_challenge_issued.v1` | `sig.verification-challenge.v1` | rama `EmailOtp` de `SignerVerificationChallengeIssuedConsumer` (la rama `KbaQuiz` se quedo inline, nunca fue parte del catalogo) |

Los 3 ultimos `sig.*` requirieron enriquecer `SignatureRequestCompletedIntegrationEvent`,
`SignatureRequestExpiredIntegrationEvent` y `SignerRejectedIntegrationEvent` (BuildingBlocks)
con un `SignerContactSnapshot` (Email/FullName/Language) por firmante — antes solo traian
GUIDs, insuficiente para renderizar un correo sin un lookup sincrono a Signature.

`auth.welcome` (Fase 9): `UserRegisteredIntegrationEvent` se movio del namespace local de
Auth a `BuildingBlocks.Messaging.AuthIntegrationEvents` (mismo tratamiento que sus hermanos
`UserDeactivatedIntegrationEvent`/`UserRolesChangedIntegrationEvent`) para que Notification
pudiera consumirlo — `UserRegisteredConsumer` ya esta wireado. Communication (Node) seguia
consumiendolo por el nombre completo del tipo CLR (`consumer-runtime.ts`,
`CLR_TYPE_TO_EVENT_TYPE`); se actualizo esa clave al mover el evento.

## 36.4 Configuracion nueva

- Scribe (`appsettings`/user-secrets): `ConnectionStrings:Default`, `RabbitMq:Uri`,
  `Redis` (cache L2 de templates parseados), `CloudStorage:BaseUrl` +
  `ServiceAuthClient:*` (M2M contra Auth), `Scribe:Retention:Enabled/RetentionDays/BatchSize`
  (36.7).
- Notification: `ScribeClient:BaseUrl` (default `http://localhost:5340`, en Docker
  `http://scribe-api:8080`) — reusa el `IServiceTokenAcquirer`/`ServiceAuthClient:*` ya
  configurado para CloudStorage (el token M2M no esta atado a un downstream especifico).
- Auth: el cliente M2M `notification-service` necesita el permiso `scribe.render`
  agregado a su `ServiceAuth:Clients` para que las llamadas de Notification a
  `POST /scribe/render` no devuelvan 403 — paso operativo, no de codigo.

## 36.5 Migraciones

Tres migraciones EF Core (`AddTemplateAndLayoutAggregates`, `AddEventTemplateMappings`,
`AddTenantLogoRefs`) — Fase 8 (los 13 templates) y Fase 9/10 (auth.welcome, retention)
no agregaron tablas nuevas, solo filas sembradas por hosted services y un metodo nuevo
sobre el aggregate existente.

## 36.6 Guia de pruebas paso a paso

1. Levantar Scribe (`dotnet run` en `TaxVision.Scribe.Api`, puerto 5340) — al arrancar
   siembra `system-base`/`tenant-base` (layouts) y los 13 templates de 36.3.
2. `POST /scribe/render` con `Authorization: Bearer <token M2M o de usuario con scribe.render>`,
   body `{ "eventKey": "auth.password_reset_requested.v1", "tenantId": null, "variables": { "reset_link": "https://...", "expires_at": "2026-01-01 10:00", "product_name": "TaxVision" } }`
   — debe devolver `200` con `subject`/`html` no vacios.
3. Levantar Notification con `ScribeClient:BaseUrl` apuntando al Scribe local; disparar
   un `PasswordResetRequestedIntegrationEvent` (via el flujo real de Auth) y confirmar
   en los logs de Notification que el email sale con el HTML de Scribe (no el viejo
   builder en C#, que ya no existe).
4. `dotnet test deploy/tests/TaxVision.Scribe.Tests/` — incluye los 7 tests de
   `ScribeArchitectureTests` (NetArchTest, 36.7) y los de `PurgeArchivedVersionsOlderThan`.

## 36.7 Pendientes documentados

- **Campaigns (Fase 9 del plan original) — explicitamente fuera de alcance.** El senior
  no confirmo que EmailCampaigns deba pasar por Scribe; no se toco `EmailCampaign`/
  `EmailCampaignRecipient` (siguen en Notification). Queda pendiente indefinidamente
  hasta nueva instruccion.
- **gRPC retirado (Fase 8 del hardening, 2026-07-18).** El servidor gRPC existio desde
  la Fase 7 (36.2) pero nunca tuvo un consumidor real — Notification siempre uso HTTP.
  Se elimino por completo (`Protos/render.proto`, `TemplateRenderGrpcService.cs`, el
  `AddGrpc()`/`MapGrpcService<...>()` de `Program.cs`, el paquete `Grpc.AspNetCore` y
  sus tests) en vez de dejarlo "por si acaso" — mismo criterio YAGNI aplicado en el
  resto de esta sesion de hardening. Decision completa en
  `Responsabilidades/ADR-0003-scribe-grpc-retired.md`. Si en el futuro aparece un
  consumidor con un requisito real de SLA estricto, se reconstruye entonces contra
  ese caso de uso concreto.
- **Retention job no cubre `EmailLayoutVersion`.** `ScribeRetentionScheduler` (deshabilitado
  por default, `Scribe:Retention:Enabled=false`) solo purga `EmailTemplateVersion` Archived
  viejas — una version de layout Archived puede seguir "pinneada" por el
  `LayoutVersionNumber` de una version de template todavia Published (el pin es por
  numero, no "ultima version"), asi que purgarla sin verificar esa referencia cruzada
  rompiaria el render de ese template. Purgar layouts requiere ese chequeo adicional,
  no implementado todavia.
- **Retention job no borra blobs de CloudStorage.** `ITemplateStorageService` solo tiene
  subida/lectura (`UploadAsync`/`DownloadTextAsync`), no delete — al purgar una version
  de template sus blobs en CloudStorage quedan huerfanos. La limpieza de blobs huerfanos
  es responsabilidad del recycle bin de CloudStorage (seccion 27), no de Scribe.
- **ADR:** `Responsabilidades/ADR-0001-scribe-extraction.md` documenta la decision de
  extraer el templating a un microservicio propio (contexto, alternativas consideradas,
  consecuencias) — no existia convencion previa de ADR en este repo ni en la carpeta de
  planificacion; este es el primero. `Responsabilidades/ADR-0003-scribe-grpc-retired.md`
  documenta el retiro del gRPC (arriba, este mismo listado).

# 37. Connectors Service (integraciones Gmail/Graph/IMAP)

Microservicio nuevo (`src/Services/Connectors/`) dueno de OAuth, Gmail Watch API,
Microsoft Graph subscriptions e IMAP puro — nace de la separacion de responsabilidades
de `Notification` (seccion 28). Es el unico servicio que conoce las APIs de proveedores
de correo: cifra/descifra tokens per-tenant, configura push notifications, resuelve
history/delta incrementalmente y publica un evento normalizado con metadata (nunca el
body ni bytes de attachments — eso se pide bajo demanda via M2M). Diseno completo en
`Responsabilidades/Connectors_Service_Design_And_Implementation_Plan.md`. Implementadas
las Fases 0 a 11 completas (plan v1 cerrado). Post-cierre (2026-07-18): agregado
`ReconciliationJob` — safety net de Gmail/Graph detras del push y unico mecanismo de sync
para IMAP (que nunca tuvo uno hasta ahora), ver 37.8.

## 37.1 Aggregates y tablas

| Aggregate | Responsabilidad | Tabla(s) |
| --- | --- | --- |
| `TenantEmailAccount` | Cuenta de correo conectada por un tenant (Draft → Connected → Active → Disconnected \| Error) | `TenantEmailAccounts` |
| `OAuthConnection` + `OAuthToken` | Tokens OAuth cifrados AES-GCM (`EncryptedSecret`, `KeyVersion` para rotacion) | `OAuthConnections`, `OAuthTokens` |
| `ImapCredentials` | Credenciales IMAP cifradas para cuentas sin OAuth (servidor propio del tenant) | `ImapCredentials` |
| `ProviderWatchSubscription` | Suscripcion push activa (Gmail `users.watch` / Graph subscription), con expiracion y renewal | `ProviderWatchSubscriptions` |
| `ProviderSyncCursor` | Cursor durable para retomar sync incremental (`HistoryId` Gmail / `DeltaLink` Graph / `UidValidity+LastUid` IMAP) | `ProviderSyncCursors` |
| `ProviderConnectionAuditLog` | Log append-only de conexion/refresh/renewal/fetch/error, retention 90 dias (Fase 11, 37.7) | `ProviderConnectionAuditLogs` |

## 37.2 Endpoints

- `POST /connectors/accounts` (permiso `connectors.accounts.write`) — arranca el flujo
  de conectar cuenta (D3 §12.4): genera un `state` CSRF de un solo uso y devuelve
  `{ authorizationUrl }`; el frontend redirige el navegador ahi, no hace un fetch.
- `GET /connectors/accounts` (permiso `connectors.accounts.read`) — lista las cuentas
  del tenant. `GET /connectors/accounts/{id}` — detalle (nunca incluye el token).
- `DELETE /connectors/accounts/{id}` (permiso `connectors.accounts.write`) — desconecta:
  revoca la `OAuthConnection`, intenta ademas revocar el refresh token del lado de Google
  (best-effort, `POST oauth2.googleapis.com/revoke`) — Graph no tiene API equivalente, el
  usuario debe revocar el consentimiento el mismo desde `myaccount.microsoft.com/consents`.
- `POST /connectors/accounts/{id}/reauth` (permiso `connectors.accounts.write`) —
  reintenta el setup de watch/subscription tras `TenantEmailAccount.Status == Error`
  (mismo comando que el connect inicial, asi que tambien recupera una connection ya
  persistida cuyo `SetupWatchCommand` fallo antes de completar el connect).
- `GET /connectors/accounts/admin-consent-url` (permiso `connectors.accounts.write`) —
  fallback de admin-consent para Graph (D3 §12.6), solo si el connect normal fallo con
  `AADSTS90094`/`consent_required`.
- `GET /connectors/oauth/callback/{gmail,graph}` — publico, Google/Microsoft redirigen
  el navegador aca tras el consentimiento; consume el `state`, intercambia el
  `code` por tokens y redirige al frontend (`?connectors_connected=true&accountId=...`
  o `?connectors_error=...`).
- `GET /connectors/oauth/admin-consent-callback` — publico, contraparte del fallback
  de arriba.
- `POST /connectors/webhooks/gmail-push` — publico, valida el JWT del token de Google
  (`Google.Apis.Auth`). Rate limit 100 req/min por IP.
- `POST /connectors/webhooks/graph-notification` — publico, handshake `validationToken`
  + `clientState` compartido validado en tiempo constante (Fase 7, hallazgo de seguridad
  corregido — ver 37.7).
- `POST /connectors/messages/{providerMessageId}/body` — M2M (`actor_type=Service`).
  Body fetch bajo demanda (Fase 8), timeout 30s, rate limit 10 req/min por (tenant, cuenta).
- `POST /connectors/messages/{providerMessageId}/attachments/{attachmentId}` — M2M.
  Attachment fetch bajo demanda (Fase 9), rate limit 5 req/min por tenant.
- `POST /connectors/accounts/{accountId}/send` — M2M (`actor_type=Service`, D3 §3.7).
  Envio saliente vía la cuenta OAuth conectada — recibe un `OutboundMessage` normalizado
  (nunca MIME crudo, porque Graph no lo acepta para crear un envio nuevo) y lo traduce
  a la forma nativa del proveedor (`GmailApiClient`/`GraphApiClient.SendMessageAsync`).
  Rate limit 20 req/min por (tenant, cuenta) via `ISendRateLimiter`. Unico consumidor
  hoy: `ConnectorsSendClient` en Postmaster (README 35.8).

## 37.3 Eventos de integracion

- `connectors.raw_message_received.v1` — publicado al recibir un push (Fase 7), metadata
  solamente (headers, subject, snippet, `AuthenticationSignals` SPF/DKIM/DMARC) — **nunca**
  body ni bytes de attachments. Lo consume `Correspondence` (`RawMessageReceivedConsumer`,
  README seccion 38), que arma o descarta el inbox del cliente final y llama bajo demanda
  los dos endpoints M2M de arriba (body/attachment fetch) — ver 37.7.
- `connectors.oauth_refresh_failed.v1`, `connectors.watch_expired.v1` — alertas operacionales,
  publicadas por `OAuthTokenManager`/`WatchRenewalService` (Fase 4/6). **Reservados, sin
  consumidor por diseño** (Fase 6 del plan de hardening, ver detalle abajo).
- `connectors.message_body_fetched.v1` — publicado por `GetMessageBodyHandler` en cada body
  fetch M2M exitoso (Fase 8). **Reservado, sin consumidor por diseño** — no es una alerta,
  es una señal de métricas/auditoría (ver comentario en el propio DTO:
  `ConnectorsMessageBodyFetchedIntegrationEvent`, "Opcional, solo para métricas — nunca
  lleva el body en sí, solo confirma que se sirvió").
- `connectors.tenant_email_account.connected.v1` / `.disconnected.v1` — publicados por
  `CompleteOAuthConnectHandler`/`DisconnectAccountHandler` (flujo de conectar cuenta, D3
  §12.7). Consumidos por Postmaster (`TenantEmailAccountConnectedConsumer`/
  `TenantEmailAccountDisconnectedConsumer`, README 35.8) para mantener su proyeccion
  local `TenantOAuthAccount`, que el motor de envio (D3 §3) consulta para resolver el
  canal `TenantOAuth`.

**Los 3 eventos huérfanos — decisión explícita (Fase 6, 2026-07-18):** `oauth_refresh_failed`,
`watch_expired` y `message_body_fetched` se publican pero hoy nadie los consume en todo el
repo. Se evaluó construir un consumer mínimo de alerting (opción (a) del plan) contra
documentarlos como reservados (opción (b)) — se investigó primero si existía algún mecanismo
reusable de "alertar a un platform admin sobre un problema operacional" en el monorepo:
**no existe ninguno.** No hay clase `PlatformAdminAlert`/`OpsAlert`/`SystemAlert`, el rol
`PlatformAdmin` (Auth) no es destino de ninguna notificación hoy, y el único patrón de
notificación por rol que existe (`"role:TenantAdmin"` en `CloudStorageEventConsumers`,
`FileInfectedDetectedConsumer`/`StorageLimitExceededConsumer`) es tenant-scoped, no
cross-tenant — no sirve para ops. Se confirmó además que **no hay precedente en todo el
repo** de un evento operacional `*Failed`/`*Expired` con un consumidor real hacia un rol de
ops: `TenantDomainProvisioningFailedIntegrationEvent`/`TenantResolutionFailedIntegrationEvent`
(Auth) están igual de huérfanos; `SignatureRequestExpiredIntegrationEvent`/
`MeetingRecordingFailedIntegrationEvent`/`CallRecordingFailedIntegrationEvent` sí tienen
consumer, pero notifican al usuario/tenant afectado, no a ops. Construir un consumer nuevo
ahora sería inventar infraestructura de alerting de cero (nueva query cross-tenant, nuevo
destino de notificación) como efecto secundario de esta fase — viola el mismo criterio YAGNI
aplicado en el resto de este hardening (Scribe Fase 8, gRPC retirado por la misma razón).
**Decisión: (b).** `oauth_refresh_failed`/`watch_expired` quedan reservados para cuando
exista un servicio de ops/alerting real (`TenantEmailAccount.Status` ya queda en `Error` en
ambos casos — el estado es consultable hoy vía `GET /connectors/accounts`, solo falta el
push proactivo). `message_body_fetched.v1` queda reservado para un futuro consumidor de
analytics/usage-metering — es una señal de conteo, no una alerta, así que ni siquiera
comparte la misma justificación que los otros dos.

## 37.4 Configuracion nueva

- `.env`: `CONNECTORS_DB_CONNECTION`, `GMAIL_CLIENT_ID/SECRET`, `MSGRAPH_CLIENT_ID/SECRET/TENANT_ID`,
  `CONNECTORS_GMAIL_WATCH_TOPIC`, `CONNECTORS_GRAPH_NOTIFICATION_URL`,
  `CONNECTORS_GRAPH_WATCH_CLIENT_STATE`, `CONNECTORS_GMAIL_PUSH_AUDIENCE` (opcional,
  scoping del JWT de Pub/Sub).
- `Connectors:RateLimit` / `Connectors:MessageBodyRateLimit` / `Connectors:AttachmentRateLimit`
  / `Connectors:SendRateLimit` — limites per-provider/per-tenant, con defaults en codigo
  (10 req/s, 10/min, 5/min, 20/min) si la seccion no esta en `appsettings.json`.
- `Connectors:Retention` (`Enabled`, `RetentionDays` default 90, `BatchSize` default 500)
  — Fase 11, deshabilitado por default hasta autorizacion explicita (mismo patron que
  `Scribe:Retention`, 36.4).
- `Connectors:Reconciliation` (`IntervalMinutes` default 15) — cadence de `ReconciliationJob`
  (37.8). `.env`: `CONNECTORS_RECONCILIATION_INTERVAL_MINUTES` (opcional).

## 37.5 Migraciones

`InitialCreate`, `AddTenantEmailAccountAndOAuth`, `AddImapCredentials`,
`AddProviderWatchSubscriptions`, `AddProviderSyncCursors`, `AddProviderConnectionAuditLogs`,
`AddProviderConnectionAuditLogTimestampIndex` (Fase 11 — indice propio para el retention
job, que filtra solo por `Timestamp` sin `AccountId`), `AddTenantEmailAccountStatusIndex`
(37.8 — indice propio para `ReconciliationJob`, que filtra `TenantEmailAccounts` solo por
`Status` sin `TenantId`, igual razon que el indice de retention de arriba).

## 37.6 Guia de pruebas paso a paso

1. `docker compose -f deploy/docker/docker-compose.yml up connectors-api` (o `dotnet run`
   local con `Connectors:OAuth:*` en user-secrets).
2. Conectar una cuenta: `POST /connectors/accounts` (`{ providerCode: "Gmail" }`) con un
   Bearer token de tenant, abrir `authorizationUrl` en el navegador, completar el
   consentimiento — Google/Microsoft redirigen a `GET /connectors/oauth/callback/{gmail,graph}`,
   que a su vez redirige al frontend con `?connectors_connected=true&accountId=...`.
   Verificar `GET /health/ready` y `GET /connectors/accounts` (la cuenta debe listar `Status: Active`).
3. Simular un push: `curl -X POST http://localhost:5390/connectors/webhooks/gmail-push`
   con un JWT valido de Google en `Authorization: Bearer` → verificar
   `connectors.raw_message_received.v1` publicado (RabbitMQ management UI).
4. `dotnet test deploy/tests/TaxVision.Connectors.Tests/` — incluye los 7 tests de
   `ConnectorsArchitectureTests` (NetArchTest, 37.7) y los de retry/circuit breaker (37.7).
5. Reconciliación (37.8): bajar `Connectors__Reconciliation__IntervalMinutes` a 1 para no
   esperar 15 min, conectar una cuenta IMAP (no requiere OAuth) y esperar al siguiente tick
   — verificar en los logs `ReconciliationJob scanned N/M active accounts...` y, si el
   inbox IMAP tenía mail, `connectors.raw_message_received.v1` publicado sin haber pasado
   por ningún webhook.

## 37.7 Pendientes documentados

- **`Correspondence` ya existe (README seccion 38), implementado completo (Fases 0-16).**
  Nota historica corregida: esta seccion afirmaba "no existe todavia" — quedo desactualizada
  desde que Correspondence se implemento. Es el microservicio que consume
  `connectors.raw_message_received.v1` y llama los dos endpoints M2M de arriba (37.2,
  body/attachment fetch bajo demanda).
- **Flujo de "conectar cuenta" (OAuth authorization + callback) implementado.**
  `POST /connectors/accounts` (arranca el consentimiento), `GET/DELETE
  /connectors/accounts/{id}`, `GET /connectors/oauth/callback/{gmail,graph}` (publico) y
  el fallback de admin-consent para Graph — disenados y construidos en la misma sesion
  (D3 §12). Reconectar una cuenta `Revoked`/`Expired` con una grant nueva **si esta
  soportado**: `OAuthConnection.Reconnect` reutiliza la misma fila (y la misma fila de
  `OAuthToken`, vía `UpdateAccessToken`/`UpdateRefreshToken`) en vez de crear una nueva —
  `OAuthConnections` tiene un indice unico por `AccountId`, asi que un segundo
  `OAuthConnection` para la misma cuenta nunca es insertable. Si la connection existente
  sigue `Active`/`Pending`, el connect falla limpio
  (`CompleteOAuthConnectHandler.AlreadyConnected`) — el usuario debe desconectar primero.
- **D3 (enviar via la cuenta OAuth conectada) — implementado.** El motor de envio
  completo: `IOutboundEmailProviderClient`, `ISendRateLimiter`, el endpoint M2M de envio
  en Connectors, y `ProviderScope.TenantOAuth`/`IOAuthProviderResolver`/
  `IOAuthEmailSender`/`ConnectorsSendClient` en Postmaster (README 35.8). Sin threading
  ni inline assets en v1 (deliberado, mismo criterio de incrementalidad usado en otras
  fases del repo). Ver `Responsabilidades/Postmaster_OAuthEmailSend_Design_And_Implementation_Plan.md`.
- **Retry Polly (Fase 10) solo cubre Gmail/Graph.** El circuit breaker si envuelve las
  3 clients (`Gmail:messages`/`Graph:messages`/`Imap:messages`, claves separadas del
  breaker de OAuth refresh), pero el reintento automatico con backoff solo aplica a
  `HttpRequestException`/`TaskCanceledException` — MailKit (IMAP) no separa de forma
  limpia un fallo de red transitorio de uno de auth/protocolo en su superficie de
  excepciones, asi que forzar reintentos ahi seria una apuesta a ciegas.
- **Hallazgo de seguridad corregido (Fase 7, post-implementacion):** la validacion de
  `clientState` del webhook de Graph no era constant-time y aceptaba notificaciones
  forjadas si `Connectors:Watch:Graph:ClientState` quedaba sin configurar (string vacio
  matcheaba string vacio). Corregido con `CryptographicOperations.FixedTimeEquals` +
  guard explicito que rechaza si el secreto no esta configurado.
- **Jobs (`WatchRenewalJob`, `ProactiveTokenRefreshJob`, `ConnectorsRetentionScheduler`,
  `ReconciliationJob`) sin tests unitarios.** Mismo criterio que el resto del repo: los
  `BackgroundService` no se testean directamente en este monorepo (ninguno de los otros
  microservicios lo hace tampoco) — la logica que si tiene tests vive en los métodos/repos
  que estos jobs invocan (`ReconcileAccountHandlerTests`, 37.8).
- **`Correspondence` ya existe** (README seccion 38) — implementado completo, Fases 0 a 16.
  Consume `connectors.raw_message_received.v1` y llama los dos endpoints M2M de arriba
  (37.2, body/attachment fetch bajo demanda).

## 37.8 Reconciliación — safety net Gmail/Graph, único mecanismo de sync IMAP (post-cierre, 2026-07-18)

**El gap identificado:** hasta esta sesión, Gmail y Graph detectaban mail nueva
*exclusivamente* vía push (Pub/Sub / Graph subscriptions, Fase 7) — sin ningún fallback si
una notificación se perdía, un push nunca llegaba, o un watch subscription vencía
silenciosamente entre corridas de `WatchRenewalJob` (que solo renueva la suscripción, nunca
sincroniza mail). Peor aún: **las cuentas IMAP no tenían ningún mecanismo de detección de
mail nueva post-conexión.** `SetupWatchHandler` activa una cuenta IMAP directo a `Active`
sin crear ninguna `ProviderWatchSubscription` (IMAP no tiene un estándar de push genérico) —
y no existía ningún job, poller ni trigger que después le pegara a
`ImapClient.GetHistoryAsync`. Una cuenta IMAP conectada nunca volvía a sincronizar nada.

**La solución no inventa un mecanismo de sync paralelo.** Se investigó primero si
`GetHistoryAsync` (Gmail `history.list`, Graph delta query, IMAP UID search) ya soporta
catch-up incremental completo — **sí**: los tres clientes ya devuelven todo lo nuevo desde
el cursor persistido (`ProviderSyncCursor`), sin importar qué disparó la llamada. Y el delta
token de Graph ya vivía como cursor opaco (`DeltaLink` completo) desde la Fase 7 original —
no hizo falta extender el aggregate. Con eso confirmado, `ReconciliationJob` simplemente
re-invoca el mismo `RawMessageSyncOrchestrator` que los webhook handlers, a través de un
nuevo `ReconcileAccountHandler` (Application, mismo patrón `Handle` estático que
`ProcessGmailPushNotificationHandler`/`ProcessGraphNotificationHandler`) — se extrajo la
lógica de seed de cursor común a los tres en `ProviderSyncCursorSeeder` para no duplicarla.

**Cómo corre:**

| Aspecto | Decisión |
| --- | --- |
| Cadence | Un solo `Connectors:Reconciliation:IntervalMinutes` (default 15) para Gmail, Graph e IMAP — ver el WHY-comment en `ReconciliationOptions` para por qué se descartó una cadencia separada más ajustada para IMAP (complejidad real por beneficio marginal, `GetHistoryAsync` ya está protegido por rate limiter/circuit breaker). |
| Alcance por corrida | Un solo `BackgroundService` compartido que escanea TODAS las cuentas `Active` de todos los tenants (`ITenantEmailAccountRepository.ListActiveAsync`, nuevo índice `IX_TenantEmailAccounts_Status`) — **no** un loop por cuenta (el patrón de `ReactiveEmailReceivingService` del backend legacy, que no escala a muchos tenants). |
| Rate limiting / circuit breaker | Ninguno nuevo — pasa por el mismo `IEmailProviderClientFactory` → mismos `GmailApiClient`/`GraphApiClient`/`ImapClient` → mismo `IProviderRateLimiter` (Redis) + `ProviderCircuitBreaker` (Fase 10) que ya protegen esos clients. |
| Concurrencia con push | Mismo `IDistributedLock` con el mismo namespace de clave que los webhook handlers (`connectors:webhook-sync:{accountId}`, Fase 4) — un pase de reconciliación y un sync disparado por webhook para la MISMA cuenta se serializan, nunca corren en paralelo. Si el lock está tomado, el pase se salta limpio (no falla, no reintenta). TTL propio de 10 min (vs. 2 min de los webhooks) porque el primer pase de una cuenta IMAP recién activada puede traer el inbox completo. |
| Idempotencia | Verificada, no asumida: `RawMessageReceivedConsumer` en Correspondence (README 38) dedupea por `InternetMessageId` antes de crear un `IncomingEmail` — si reconciliación re-publica un mensaje que un push ya entregó (p.ej. porque el cursor no llegó a guardarse), Correspondence lo descarta como duplicado sin efecto. No hizo falta ningún fix de idempotencia. |
| Jitter | Delay aleatorio de 50-400ms entre cuentas dentro de una misma corrida — evita que todas las cuentas activas le peguen a los providers en el mismo instante en cada tick. |
| Métricas | `connectors_reconciliation_accounts_scanned_total`, `connectors_reconciliation_messages_found_total`, `connectors_reconciliation_errors_total` (todos con tag `provider`), y la que importa vigilar en producción: **`connectors_reconciliation_messages_recovered_total`** — solo cuenta mensajes encontrados en cuentas Gmail/Graph donde el cursor YA existía (no es catch-up inicial). En operación normal debería quedarse en 0; una tendencia creciente es la señal de que el push se está degradando silenciosamente. IMAP nunca aporta a esta métrica — ahí reconciliación ES el mecanismo de sync, no una recuperación de nada. Cada recuperación real además loguea un `LogWarning` estructurado (no solo se corrige en silencio). |

**Qué NO cubre:** esto no es polling de 5 minutos por mailbox (ese modelo, el del backend
legacy, se descartó explícitamente por no escalar) — es una red de seguridad de baja
frecuencia. Una cuenta Gmail/Graph con push funcionando normalmente puede tardar hasta
`IntervalMinutes` en autocorregirse si el push falla; no hay garantía de latencia menor. El
`ReconciliationJob` en sí no tiene tests unitarios directos (mismo criterio que el resto de
los `BackgroundService` del repo, ver arriba) — la lógica testeada vive en
`ReconcileAccountHandlerTests` (`deploy/tests/TaxVision.Connectors.Tests/Sync/`).

# 38. Correspondence Service (inbox del cliente final + compose/send)

Microservicio nuevo (`src/Services/Correspondence/`, puerto 5400) dueño del inbox de
correo del cliente final (empleado del tenant viendo los emails de SUS customers) y del
compose/send de correspondencia nueva o reply. Nace de la separación de responsabilidades
de `Connectors` (sección 37): Connectors solo transporta metadata normalizada de
proveedor, Correspondence es quien la convierte en un inbox real por customer, resuelve
threading, y arma el envío saliente que termina en Postmaster. Diseño completo en
`Responsabilidades/Correspondence_Service_Design_And_Implementation_Plan.md` (fusionado
con el diseño de Compose, ver §0 de ese documento). Implementadas las Fases 0 a 16
completas (plan v1 cerrado, incluyendo el hardening final).

**Diseño no negociable (§0 del plan):** el tramo de envío es una cadena HTTP síncrona y
bloqueante en cada hop — `POST /correspondence/drafts/{id}/send` → Postmaster
(`POST /postmaster/correspondence-messages`, 35) → Connectors
(`POST /connectors/accounts/{accountId}/send`, 37.2) → proveedor real (Gmail/Graph) — sin
evento, cola, ni fire-and-forget en ningún tramo. El usuario que aprieta "Enviar" espera
el resultado real (éxito o error concreto) en la MISMA request, igual que enviar desde
Gmail; el "async" lo maneja el usuario reintentando desde la UI si hace falta. Esto
**contrasta deliberadamente** con el patrón event-driven que Postmaster también sirve
para notificaciones automáticas de tenant (`notifications.email_send_requested.v1`, 35.2)
— ahí no hay un humano esperando en vivo, así que el patrón async/con reintento
automático tiene sentido; acá sí hay un humano esperando, así que agregar una cola
solo agregaría latencia y un estado "pendiente" que el usuario no puede interpretar
("¿se mandó o no?"). Ver el ADR corto en
`Responsabilidades/ADR-0002-correspondence-synchronous-send.md`.

**`CustomerId` obligatorio (diseño, no un detalle):** todo lo que persiste este servicio
—`IncomingEmail`, `EmailThread`, `Draft`— exige un `CustomerId` real y no vacío
(`Draft.ValidateIds`, mismo criterio en el resto de los aggregates). Correspondence no
tiene concepto de "correo suelto sin dueño": un mensaje entrante que no puede asociarse a
un customer conocido NUNCA se persiste como `IncomingEmail` — cae en cuarentena (ver
abajo). Esto es lo que hace posible que `GET /correspondence/customers/{customerId}/threads`
sea la puerta de entrada real del inbox (todo cuelga de un customer), no un query global.

**Cuarentena anti-spoofing:** `RawMessageReceivedConsumer` (Fase 4) nunca crea un
`IncomingEmail` a ciegas — antes de eso resuelve el remitente contra
`CustomerEmailAddresses` y valida las señales de autenticación que Connectors ya adjuntó
al evento (`SpfResult`/`DkimResult`/`DmarcResult`, 37.3). Un mensaje cuyo remitente SÍ
matchea un customer conocido pero cuyo DMARC falló (o SPF+DKIM fallaron ambos) se trata
como intento de spoofing, no como ruido: va a `UnmatchedIncomingEmail` con
`UnmatchedReason.AuthenticationFailed` en vez de aparecer en el inbox del customer
suplantado. Un remitente que directamente no matchea ningún customer usa
`UnmatchedReason.NoCustomerMatch` — mismo destino (cuarentena), motivo distinto (loggeado
distinto para poder diferenciar "ruido" de "posible ataque" en los logs).

## 38.1 Aggregates y tablas

| Aggregate | Responsabilidad | Tabla(s) |
| --- | --- | --- |
| `IncomingEmail` (+`IncomingEmailRecipient`, `IncomingEmailAttachment`) | Un correo entrante ya matcheado a un customer — metadata + estado de descarga de body/attachments bajo demanda | `IncomingEmails`, `IncomingEmailRecipients`, `IncomingEmailAttachments` |
| `EmailThread` | Hilo (thread) de un customer — agrupa `IncomingEmail`s entrantes y `Draft`s `Sent` salientes | `EmailThreads` |
| `UnmatchedIncomingEmail` | Cuarentena: mensajes que no matchearon customer o fallaron autenticación (ver arriba) | `UnmatchedIncomingEmails` |
| `CustomerEmailAddress` | Proyección local del email primario de cada customer, alimentada por 6 eventos de Customer (38.3) | `CustomerEmailAddresses` |
| `TenantBackfillState` | Marca "ya corrí el backfill inicial de `CustomerEmailAddresses` para este tenant" | `TenantBackfillStates` |
| `Draft` (+`DraftRecipient`) | Correspondencia en redacción — nueva o reply, hasta `Sent`/`Discarded`/`Failed` | `Drafts`, `DraftRecipients` |
| `CorrespondenceAuditLog` | Rastro mínimo de auditoría (solo escritura) de acciones sobre `Draft` (Send, principalmente) | `CorrespondenceAuditLogs` |

## 38.2 Endpoints

Inbox (Fases 5-9, todos requieren tenant real vía JWT — nunca M2M):

- `GET /correspondence/messages/{id}` (permiso `correspondence.read`) — metadata de UN
  mensaje, nunca llama a Connectors.
- `GET /correspondence/messages/{id}/body` (`correspondence.read`) — body fetch bajo
  demanda contra Connectors, nunca se persiste (plan §17).
- `GET /correspondence/messages/{id}/attachments` (`correspondence.read`) — attachments
  ya persistidos del mensaje.
- `POST /correspondence/messages/{id}/attachments/{attachmentId}/download`
  (`correspondence.attachment.download`) — dispara la descarga bajo demanda
  (Connectors → bucket temporal → CloudStorage). Idempotente.
- `GET /correspondence/messages/{id}/attachments/{attachmentId}/download-url`
  (`correspondence.attachment.download`) — URL presignada del attachment ya descargado.
- `GET /correspondence/customers/{customerId}/threads` (`correspondence.read`, paginado)
  — inbox de un customer, hilos más reciente primero.
- `GET /correspondence/threads/{threadId}/messages` (`correspondence.read`, paginado) —
  thread unificado inbound+outbound (Fase 15): mensajes entrantes Y `Draft`s `Sent` del
  mismo hilo, orden cronológico ascendente.
- `POST /correspondence/threads/{threadId}/archive` (`correspondence.read`) — archiva el
  hilo, idempotente.

Compose/Send (Fases 10-15):

- `POST /correspondence/drafts` (`correspondence.compose`) — correspondencia nueva.
- `GET /correspondence/drafts?customerId=` (`correspondence.compose`, paginado) —
  "retomar autoguardado": drafts `Status=Draft` del customer.
- `GET /correspondence/drafts/{id}` (`correspondence.compose`) — vista completa del
  composer.
- `PATCH /correspondence/drafts/{id}` (`correspondence.compose`) — autoguardado parcial,
  204 sin body.
- `DELETE /correspondence/drafts/{id}` (`correspondence.compose`) — `Draft → Discarded`.
- `POST /correspondence/messages/{id}/reply/draft` (`correspondence.reply`) — arranca (o
  reutiliza) un reply sobre un mensaje entrante.
- `POST /correspondence/drafts/{id}/attachments` (`correspondence.compose`) — adjunta una
  referencia a un archivo ya subido a CloudStorage (nunca bytes acá).
- `DELETE /correspondence/drafts/{id}/attachments/{fileId}` (`correspondence.compose`) —
  idempotente/tolerante.
- `POST /correspondence/drafts/{id}/send` (`correspondence.send`, permiso separado de
  `compose` a propósito — redactar es reversible, enviar no) — cierre síncrono y
  bloqueante de la cadena completa (ver arriba).

## 38.3 Eventos e integraciones M2M

**Consumidos** (2, ambos vía `RawMessageReceivedConsumer`/los 6 consumers de `Projections/CustomerEvents`):

- `connectors.raw_message_received.v1` (Connectors, 37.3) — arma o descarta un
  `IncomingEmail` (ver cuarentena arriba).
- `customer.created.v1` / `.updated.v1` / `.activated.v1` / `.deactivated.v1` /
  `.archived.v1` / `.reactivated.v1` (Customer) — mantienen `CustomerEmailAddress` al día;
  el primero de estos para un tenant nuevo dispara además el backfill completo
  (`ITenantCustomerBackfillService`, pagina `GET /customers/internal/list`).

**Publicados:**

- `correspondence.customer_email_received.v1` — al crear un `IncomingEmail` real (no
  cuarentena). Nadie lo consume todavía — mismo estado que varios eventos de Connectors
  (37.3): la infraestructura de notificar "tenés un mensaje nuevo" queda lista, conectarla
  a Communication/Notification es trabajo futuro fuera de este plan.
- `SaveFileRequestedIntegrationEvent` (a CloudStorage, vía outbox transaccional) — al
  descargar un attachment entrante bajo demanda.

**M2M salientes (4 servicios, todos vía `HttpClient` + token de servicio,
`ICorrespondenceServiceTokenAcquirer`):**

1. **Connectors** — `POST /connectors/messages/{id}/body` (body fetch) y
   `POST /connectors/messages/{id}/attachments/{attachmentId}` (attachment fetch),
   ambos bajo demanda (37.2).
2. **Customer** — `GET /customers/internal/list` (backfill inicial + reconciliación
   periódica, 38.4).
3. **Postmaster** — `POST /postmaster/correspondence-messages` (el cierre síncrono del
   envío, ver arriba) — único caller: `PostmasterClient`
   (`Infrastructure/Postmaster/`), verificado por
   `Only_PostmasterClient_should_reference_the_concrete_postmaster_http_client`
   (`CorrespondenceArchitectureTests`, 38.6).
4. **CloudStorage** — `GET` de la URL presignada de un attachment ya adoptado, más un
   chequeo de metadata best-effort (`AttachFileToDraftHandler`); la subida real de
   archivos nuevos para un draft la hace el frontend directo contra CloudStorage, acá solo
   viaja la referencia (`fileId`).

## 38.4 Jobs en segundo plano

Ambos `BackgroundService` puros (timer-tick, sin evento de entrada — guardrail de este
servicio: solo empujan una correlación NUEVA por unidad de trabajo para que sus propias
líneas de log queden agrupables, nunca propagan una correlación inbound porque no hay
ninguna):

| Job | Intervalo | Qué hace | Habilitado por default |
| --- | --- | --- | --- |
| `DraftCleanupJob` | 24h (delay inicial 15min) | `Draft`s `Status=Draft` con `UpdatedAtUtc` > `AbandonedAfterDays` (30) → `Draft.Discard()`, batcheado (200/batch, máx 25 batches/corrida) | **No** — plan §30, mismo criterio que `ConnectorsRetentionScheduler`/`ScribeRetentionScheduler`: requiere autorización explícita |
| `CustomerEmailReconciliationJob` | 24h (delay inicial 20min) | Por cada tenant ya backfilleado, pagina `ListActiveCustomersAsync` y corrige drift de `CustomerEmailAddresses` (crea faltantes, actualiza emails desactualizados, reactiva soft-deletes obsoletos) vía `ICustomerEmailReconciliationService` | **Sí** — plan §32 R1, nunca borra datos, solo corrige (mismo perfil de riesgo que el backfill, que tampoco tiene flag) |

`CustomerEmailReconciliationService` (Application, testeable sin `BackgroundService`) es
honesto sobre su límite real: `ListActiveCustomersAsync` solo devuelve customers activos,
así que esta reconciliación no puede detectar el caso inverso (un customer se desactivó
en Customer pero la proyección local sigue activa) sin un endpoint M2M nuevo que hoy no
existe — agregarlo sería scope creep de una fase de hardening. Cubre con confianza los dos
casos que el plan §32 R1 realmente le preocupa: un email que cambió sin generar un evento
limpio, y una fila que quedó soft-deleted de más.

## 38.5 Configuración nueva

- `.env`/user-secrets: `Correspondence:ServiceAuth:*` (M2M contra Auth),
  `Correspondence:Customer:BaseUrl`, `Correspondence:Connectors:BaseUrl`,
  `Correspondence:Postmaster:BaseUrl`, `Correspondence:CloudStorage:BaseUrl`,
  `Correspondence:Minio:*` (credenciales scoped `correspondence-source`, mismo patrón
  D0/D1 que Signature).
- `Correspondence:Ingest:EnableUnmatchedDebug` / `.EnableSubjectThreadingFallback` —
  flags de comportamiento del matching (Fase 4/6), off por default.
- `Correspondence:DraftCleanup` (`Enabled` default `false`, `AbandonedAfterDays` default
  30, `BatchSize` default 200) — Fase 16.
- `Correspondence:Reconciliation` (`Enabled` default `true`) — Fase 16.

## 38.6 Migraciones y arquitectura

`InitialCreate`, `AddCustomerEmailAddresses`, `AddTenantBackfillStates`,
`AddInboxAggregates`, `AddUnmatchedIncomingEmails`, `AddIncomingEmailsThreadIndex`,
`AddDraftAggregate` (incluye `IX_Drafts_Status_UpdatedAtUtc`, preparado desde Fase 10
para el `DraftCleanupJob` de Fase 16), `AddCorrespondenceAuditLog`,
`AddDraftEmailThreadId`.

`CorrespondenceArchitectureTests` (NetArchTest, Fase 16, 13 tests) — Domain sin
dependencia a Application/Infrastructure/Api/EF Core/Wolverine/`System.Net.Http`/
MailKit/Minio; Application sin dependencia a Infrastructure/Api; Infrastructure sin
dependencia a Api; solo `PostmasterClient` puede referenciar el tipo concreto
`PostmasterClient` (todo lo demás pasa por `IPostmasterClient`); y ningún tipo del
servicio referencia `TaxVision.Scribe` (Correspondence nunca renderiza — Postmaster ya
recibe el HTML final armado por el usuario).

## 38.7 Métricas

`CorrespondenceMetrics` (`System.Diagnostics.Metrics.Meter("correspondence-service")`,
registrado en OTel vía `AddMeter(serviceName)`), plan §29 — registradas dentro de
`PostmasterClient.SendAsync` (Infrastructure), no en `SendDraftHandler` (Application):
Application no puede depender de Infrastructure, así que medir en el cliente HTTP real es
la única ubicación consistente con las fronteras de 38.6.

- `correspondence_draft_send_duration_seconds{tenant}` — tramo completo
  Correspondence→Postmaster (incluye lo que Postmaster tarda con Connectors/el proveedor).
- `correspondence_draft_send_total{tenant,status}` — `status` = `sent` | `failed` |
  `suppressed` (`suppressed` solo cuando Postmaster propaga
  `SendCorrespondenceMessageHandler.AllRecipientsSuppressed` tal cual, 35.6).
- `correspondence_draft_abandoned_total{tenant}` — incrementada por `DraftCleanupJob`
  (38.4) por cada `Draft` auto-descartado.

## 38.8 Guía de pruebas paso a paso

1. `docker compose -f deploy/docker/docker-compose.yml up correspondence-api` (o
   `dotnet run` local, requiere `Connectors`/`Postmaster`/`Customer`/`CloudStorage`
   corriendo para el flujo completo).
2. Simular un mensaje entrante: publicar `connectors.raw_message_received.v1` a mano
   (RabbitMQ management UI) con un `From` que matchee un `CustomerEmailAddress`
   existente → verificar `GET /correspondence/customers/{customerId}/threads`.
3. Compose/send real: `POST /correspondence/drafts` → `PATCH .../drafts/{id}` (subject +
   body + `to`) → `POST .../drafts/{id}/send` → 200 con `sentMessageId` real si
   Postmaster/Connectors están arriba con una cuenta OAuth conectada, o el error real
   propagado si no.
4. `dotnet test deploy/tests/TaxVision.Correspondence.Tests/` — incluye
   `CorrespondenceArchitectureTests` (13 tests, 38.6) y los de
   `CustomerEmailReconciliationService`/`DraftRepository.ListAbandonedAsync` (38.4).

## 38.9 Pendientes documentados

- **Reconciliación no detecta desactivaciones fantasma** (ver el WHY-comment de 38.4) —
  requeriría un endpoint M2M nuevo en Customer.Api, fuera de alcance de una fase de
  hardening.
- **`correspondence.customer_email_received.v1` sin consumidor** — la infraestructura de
  avisar "tenés un mensaje nuevo" a Communication/Notification queda lista pero
  desconectada, mismo estado que varios eventos de Connectors (37.7).
- **Jobs sin tests unitarios directos** (`DraftCleanupJob`/`CustomerEmailReconciliationJob`)
  — mismo criterio que el resto del repo (35.7/37.7/32.5): la lógica real vive en
  `IDraftRepository.ListAbandonedAsync`/`ICustomerEmailReconciliationService`, que sí
  están testeados.

# 39. Soporte de logo por tenant (Tenant, CloudStorage, Auth)

Implementa `Tenant_Service_LogoSupport_Plan.md`: cada tenant puede subir un logo propio,
embebido por Postmaster como inline attachment CID en cada correo saliente (Scribe
`LogoResolver`/`TenantLogoRef`, Scribe Fase 4.5) en vez de caer siempre al logo del sistema
(`IsFallback: true`). Vive en **Tenant** (no en Scribe ni CloudStorage) porque el logo es un
atributo del propio tenant, igual que su nombre o subdominio — Scribe solo lo consume via
proyección (`TenantLogoUpdatedIntegrationEvent`/`TenantLogoRemovedIntegrationEvent`, ya
existentes desde la Fase 4.5, sin cambios).

## 39.1 Dominio: `Tenant.SetLogoPending` / `Tenant.ConfirmLogo`

`TenantLogo.cs` extiende `Tenant` (`partial class`) con `LogoFileId`/`LogoContentType`/
`LogoSizeBytes`/`LogoWidth`/`LogoHeight`/`LogoUpdatedAtUtc` (todos nullable). Dos métodos,
no uno solo, para representar el estado pendiente-de-escaneo vs confirmado sin una tercera
tabla ni un enum de status:

- `SetLogoPending(fileId, contentType, sizeBytes, width, height)` — llamado por
  `UploadTenantLogoHandler` de forma OPTIMISTA con los metadatos declarados por el cliente,
  antes de que CloudStorage confirme el escaneo antivirus. Deja `LogoUpdatedAtUtc` en
  `null` a propósito.
- `ConfirmLogo(fileId, contentType, sizeBytes, width, height, confirmedAtUtc)` — llamado
  por `TenantBrandingFileScanResultConsumer` al recibir `FileAvailableIntegrationEvent`, con
  los metadatos reales devueltos por CloudStorage. Es la única llamada que setea
  `LogoUpdatedAtUtc` (no nulo == confirmado).
- `DiscardPendingLogo(fileId)` — descarta un upload rechazado (`FileInfectedDetectedIntegrationEvent`/
  `FileBlockedByPolicyIntegrationEvent`), pero solo si `LogoFileId == fileId &&
  LogoUpdatedAtUtc is null`: un rechazo tardío para un fileId ya reemplazado o ya
  confirmado nunca pisa el logo actual.

**Bug real encontrado y corregido durante la implementación** (no solo documentado — ver
[[feedback_check_fixability_before_documenting_gap]]): la primera versión tenía un solo
método `SetLogo(fileId, ..., DateTime updatedAtUtc)` llamado por *ambos* sitios con
`DateTime.UtcNow`, así que `LogoUpdatedAtUtc` nunca era `null` — `DiscardPendingLogo` quedaba
muerto (un logo infectado nunca se limpiaba, dejando un `LogoFileId` colgante) y
`GetTenantLogoQuery` hubiera devuelto un download-url para un archivo todavía en escaneo.
Se detectó al escribir el test `DiscardPendingLogo_clears_matching_unconfirmed_pending_upload`
contra el código real (no contra la intención) y se corrigió dividiendo el método en dos,
no parcheando el test.

`Tenant.MaxLogoSizeBytes = 500 * 1024` (500KB) — el plan original proponía 200KB; se subió
porque dejaba muy poco margen para un PNG con transparencia en retina (2x) sin forzar
compresión agresiva. Sigue muy por debajo del cap de 5MB que Postmaster aplica a la suma de
inline assets de un mismo email (`SentMessage.MaxTotalInlineAssetsBytes`). El mismo valor se
repite en `CloudStorageOptions.BrandingPolicy()` (defensa en profundidad — dos validaciones
independientes del mismo invariante, no una fuente única compartida).

## 39.2 Upload desacoplado (patrón Fase D1, no HTTP síncrono a CloudStorage)

`TenantBrandingCloudStorageClient` (Infrastructure) sube el logo **directo a MinIO** con
credenciales IAM propias (scoped a `taxvision-temp/tenant-branding/*`, nunca las root de
CloudStorage) y publica `SaveFileRequestedIntegrationEvent` para que CloudStorage lo
catalogue y escanee (ClamAV + política de contenido) de forma asíncrona — mismo patrón que
Signature/Customer/Scribe, no el patrón HTTP+3-llamadas más viejo de Notification.
`GetDownloadUrlAsync`/`DeleteAsync` sí son llamadas HTTP+M2M síncronas a CloudStorage
(`POST storage/files/{fileId}/download-url`, `DELETE storage/files/{fileId}`) porque no
existe un evento `DeleteFileRequestedIntegrationEvent` en el catálogo.

`TenantBrandingFileScanResultConsumer` correlaciona la respuesta asíncrona por
`(evt.TenantId, Tenant.LogoFileId)` — a diferencia de Signature (`FileMetadataRef`), no hace
falta una tabla de tracking separada porque el propio row de `Tenant` es el punto de
correlación natural.

## 39.3 Endpoints HTTP (`TenantBrandingController`, bajo `/tenants/{tenantId}/logo`)

| Método | Ruta | Permiso | Rate limit | Notas |
| --- | --- | --- | --- | --- |
| `PUT` | `/tenants/{tenantId}/logo` | `branding.manage` | `tenant-logo-upload`: 10/hora por tenant | `multipart/form-data`, campo `file`. 202 Accepted — asíncrono, ver 39.1 |
| `GET` | `/tenants/{tenantId}/logo` | solo autenticación | — | 404 (`Tenant.Logo.NotFound`) si no hay logo confirmado |
| `DELETE` | `/tenants/{tenantId}/logo` | `branding.manage` | — | Idempotente, 204 |

`TryResolveTenantId` (privado, mismo patrón que Postmaster `ProvidersController`): PlatformAdmin
opera sobre cualquier tenant; el resto solo sobre el propio (`{tenantId}` de la ruta debe
coincidir con el claim `tenant_id` del JWT, nunca se confía en la ruta sola).

## 39.4 Gaps cerrados durante la implementación

1. **`FolderType.Branding` no existía en CloudStorage** — se agregó al enum
   (`FileEnums.cs`) + `CloudStorageOptions.BrandingPolicy()` (500KB, `.png/.jpg/.jpeg/.svg`,
   `image/png|jpeg|svg+xml`).
2. **Verificación de ownership tenant-ruta-vs-JWT** — implementada desde el día uno en
   `TryResolveTenantId` (39.3), no un gap post-hoc.

## 39.5 Configuración nueva

- `Tenant:Minio:{Endpoint,AccessKey,SecretKey,UseTls,TempBucket,SourcePrefix}` — credenciales
  IAM propias de Tenant (usuario `tenant-worker`, policy
  `deploy/docker/minio/policies/tenant-source.json`, scoped a
  `taxvision-temp/tenant-branding/*`).
- `ServiceAuthClient:{AuthBaseUrl,ClientId,ClientSecret}` / `CloudStorageClient:BaseUrl` —
  cliente M2M `tenant-worker` (permisos `cloudstorage.file.download` +
  `cloudstorage.file.delete` únicamente — nunca `cloudstorage.file.upload`, porque el upload
  va directo a MinIO, no vía CloudStorage HTTP).
- Permiso `branding.manage` sembrado en `PermissionCatalog` (Auth), migración
  `AddBrandingManagePermission`.
- Columnas `Logo*` en `Tenants` (migración `AddTenantLogoFields`, todas nullable — sin
  `ValueGeneratedNever()` porque `Tenant.Id` nunca se agrega vía navegación (lección de
  `[[feedback_ef_core_navigation_guid]]` no aplica aquí, `Tenant` siempre se persiste
  directo).

## 39.6 Pruebas

`deploy/tests/TaxVision.Tenant.Tests/Domain/TenantLogoTests.cs` — 21 tests de dominio
(`SetLogoPending`/`ConfirmLogo`/`RemoveLogo`/`DiscardPendingLogo`, incluyendo el caso del bug
de 39.1). `dotnet test deploy/tests/TaxVision.Tenant.Tests/` — full monorepo (1612 tests,
13 proyectos) verificado en verde tras el cambio.

## 39.7 Pendientes documentados

- **Sin Idempotency-Key persistida en el upload** — a diferencia de `CustomerImportsController`,
  una subida duplicada del logo no se deduplica con una tabla propia. Simplificación
  deliberada: el peor caso es que el escaneo que confirma último gana, y el objeto huérfano
  en `taxvision-temp` lo recicla la retención ya existente de CloudStorage — el blast radius
  no es comparable al bug de emails duplicados que motivó la regla de
  `ValueGeneratedNever()` en otros servicios.
- **SVG sin sanitización activa contra XSS** — `image/svg+xml` está en la whitelist de
  content-types (igual que el resto de logos web); un SVG puede contener `<script>`. El
  download-url es presignado a MinIO/S3, nunca servido desde un dominio de TaxVision, así
  que el riesgo de XSS persistente contra la propia app es bajo, pero si el frontend llega a
  renderizar el SVG inline (no solo como `<img src>`) debe sanitizarlo del lado cliente antes.
