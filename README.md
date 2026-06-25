# TaxVision Backend

Backend de TaxVision construido con arquitectura de microservicios en .NET, separando responsabilidades de Tenant, Auth, Gateway e infraestructura compartida.

## Autor

Implementaciones realizadas por: **Jorge Turbi**

## Resumen de implementaciones

Este trabajo completa la base inicial del backend multitenant de TaxVision, incorporando:

- Tenant Service con API propia, dominio, aplicacion, infraestructura y persistencia.
- Auth Service con registro, login, JWT, refresh tokens y validacion real de tenants.
- API Gateway con YARP para exponer los servicios desde un unico punto de entrada.
- Mensajeria asincrona con RabbitMQ y Wolverine.
- Cache distribuida con Redis.
- Dockerizacion de Auth API, Tenant API, Gateway, RabbitMQ y Redis.
- Configuracion por variables de entorno para evitar secretos en el codigo fuente.

## Estructura principal

```text
src/
  BuildingBlocks/
  Gateway/
    TaxVision.Gateway/
  Services/
    Auth/
      Api/
      Application/
      Domain/
      Infrastructure/
    Tenant/
      TaxVision.Tenant.Api/
      TaxVision.Tenant.Application/
      TaxVision.Tenant.Domain/
      TaxVision.Tenant.Infrastructure/

deploy/
  docker/
    docker-compose.yml
```

## BuildingBlocks

Se agregaron y ajustaron componentes compartidos para soportar la arquitectura multitenant:

- `BaseEntity` como base comun de entidades.
- `ITenantOwned` como contrato para entidades que pertenecen a un tenant.
- `TenantEntity` como clase base para entidades multitenant.
- Abstracciones de cache Redis.
- Contratos compartidos de mensajeria, incluyendo `TenantCreatedIntegrationEvent`.

Esto permite que los servicios compartan contratos sin acoplar directamente sus dominios internos.

## Tenant Service

El servicio de Tenant permite crear y consultar tenants.

Funcionalidades implementadas:

- Controller canonico `TenantController`.
- Comando `CreateTenantCommand`.
- Respuesta `TenantResponse`.
- Validacion de nombre y subdominio.
- Validacion de subdominio unico.
- Persistencia con Entity Framework Core.
- Implementacion de `IUnitOfWork`.
- Publicacion de `TenantCreatedIntegrationEvent`.
- Integracion con RabbitMQ mediante Wolverine.
- Integracion con Redis.
- Swagger/OpenAPI en ambiente Development.

Endpoint principal:

```http
POST /tenants
```

Body:

```json
{
  "name": "Empresa Demo",
  "subdomain": "empresa-demo",
  "adminEmail": "admin@empresademo.com"
}
```

Respuesta esperada:

```json
{
  "id": "tenant-guid",
  "name": "Empresa Demo",
  "subdomain": "empresa-demo"
}
```

## Auth Service

El servicio de Auth permite registrar usuarios y hacer login.

Funcionalidades implementadas:

- Correccion del registro de usuarios para incluir `name`, `lastName`, `email`, `password` y `tenantId`.
- Validacion de existencia y estado activo del tenant antes de registrar usuarios.
- Registry local de tenants dentro de Auth.
- Consumidor de eventos `TenantCreatedIntegrationEvent`.
- Publicacion de `UserRegisteredIntegrationEvent`.
- Generacion de JWT.
- Emision de refresh tokens.
- Persistencia con Entity Framework Core.
- Migraciones para agregar nombre, apellido y registry local de tenants.
- Integracion con RabbitMQ mediante Wolverine.
- Integracion con Redis.

Endpoint de registro:

```http
POST /auth/register
```

Body:

```json
{
  "tenantId": "tenant-guid",
  "name": "Jorge",
  "lastName": "Turbi",
  "email": "jturbi@example.com",
  "password": "Brittany040238."
}
```

Endpoint de login:

```http
POST /auth/login
```

Body:

```json
{
  "email": "jturbi@example.com",
  "password": "Brittany040238."
}
```

Respuesta esperada:

```json
{
  "accessToken": "jwt-token",
  "refreshToken": "refresh-token"
}
```

## Validacion de tenants en Auth

Antes de esta implementacion, Auth permitia registrar usuarios usando cualquier `tenantId`.

Ahora el flujo correcto es:

1. Se crea un tenant desde Tenant Service.
2. Tenant Service guarda el tenant en su base de datos.
3. Tenant Service publica `TenantCreatedIntegrationEvent`.
4. Auth Service consume el evento.
5. Auth Service registra el tenant en su registry local.
6. Auth solo permite registrar usuarios si el tenant existe y esta activo.

Esto evita registrar usuarios asociados a tenants inexistentes.

## API Gateway

Se agrego un Gateway con YARP para enrutar los servicios desde un unico punto de entrada.

Rutas configuradas:

```text
/auth/{**catch-all}     -> Auth API
/tenants/{**catch-all}  -> Tenant API
```

En local/Docker, el Gateway queda expuesto en:

```http
http://localhost:5047
```

Ejemplos:

```http
POST http://localhost:5047/tenants
POST http://localhost:5047/auth/register
POST http://localhost:5047/auth/login
```

## Infraestructura

Se incorporaron los siguientes servicios de infraestructura:

- RabbitMQ para mensajeria asincrona.
- Redis para cache distribuida.
- SQL Server como persistencia de Auth y Tenant.
- Wolverine para mensajeria, outbox, inbox y durable messaging.
- YARP como API Gateway.

## Docker

Se agregaron Dockerfiles para:

- Tenant API.
- Auth API.
- Gateway.

Tambien se actualizo `deploy/docker/docker-compose.yml` para levantar:

- `tenant-api`
- `auth-api`
- `gateway`
- `rabbitmq`
- `redis`

Todos los servicios quedan conectados a la misma red Docker:

```text
taxvision-network
```

Solo el Gateway expone los endpoints publicos de Auth y Tenant.

## Variables de entorno

El proyecto espera un archivo `.env` en la raiz con esta estructura:

```env
JWT_SECRET=replace-with-the-same-secret-used-by-auth
AUTH_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Auth;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
TENANT_DB_CONNECTION=Server=host.docker.internal,1433;Database=TaxVision_Tenants;User Id=sa;Password=replace-with-password;TrustServerCertificate=true
```

El archivo `.env` no debe ser commiteado.

## Comandos principales

Restaurar y compilar:

```powershell
dotnet restore
dotnet build
```

Levantar infraestructura y servicios:

```powershell
docker compose -f deploy\docker\docker-compose.yml --env-file .env up -d --build
```

Ver estado de contenedores:

```powershell
docker compose -f deploy\docker\docker-compose.yml --env-file .env ps
```

Ver logs de Tenant:

```powershell
docker compose -f deploy\docker\docker-compose.yml --env-file .env logs -f tenant-api
```

Ver logs de Auth:

```powershell
docker compose -f deploy\docker\docker-compose.yml --env-file .env logs -f auth-api
```

Ver logs del Gateway:

```powershell
docker compose -f deploy\docker\docker-compose.yml --env-file .env logs -f gateway
```

## Flujo recomendado de prueba

1. Levantar los servicios con Docker Compose.
2. Crear un tenant desde Postman usando el Gateway.
3. Guardar el `tenantId` recibido en una variable de Postman.
4. Registrar un usuario usando ese `tenantId`.
5. Hacer login con el usuario registrado.
6. Guardar el `accessToken` y `refreshToken` en variables de Postman.

## Postman

Crear tenant:

```http
POST http://localhost:5047/tenants
```

Registrar usuario:

```http
POST http://localhost:5047/auth/register
```

Login:

```http
POST http://localhost:5047/auth/login
```

Script de Postman para guardar el tenant:

```javascript
const response = pm.response.json();

pm.environment.set("tenantId", response.id);
pm.environment.set("tenantName", response.name);
pm.environment.set("tenantSubdomain", response.subdomain);
```

Script de Postman para guardar tokens:

```javascript
const response = pm.response.json();

pm.environment.set("accessToken", response.accessToken);
pm.environment.set("refreshToken", response.refreshToken);
```

## Estado actual

El proyecto queda con la base de microservicios preparada para continuar las siguientes secciones de la guia:

- Auth con validacion multitenant.
- Tenant como origen canonico de tenants.
- Gateway como punto unico de entrada.
- Infraestructura Docker lista para desarrollo.
- RabbitMQ y Redis integrados.
- Servicios conectados en la misma red Docker.

##  Notas Pendientes de fecha 26-06-2026:

- Validar que un mismo correo no exista  en un mismo Tenant, pero que si pueda existir con otro Tenant
- Se realizaron pruebas de registro de Tenant.
- Crear MicroServicio de Subcripción

