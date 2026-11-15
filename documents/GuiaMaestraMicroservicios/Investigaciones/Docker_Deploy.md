# Docker y despliegue con Compose

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd` salvo que se indique otra raíz. Etiquetas: **[Código verificado]** (leído hoy en el repo), **[Doc. histórica]** (documentos o memoria, puede estar desfasada), **[Recomendación]**, **[Decisión pendiente]**. No se copian valores de secretos.

## 1. Estado actual

- 32 Dockerfiles en `src/**/Dockerfile`: 28 imágenes .NET (25 APIs, Gateway, MailboxListener, ContentScanner) y 3 Node (Communication, CommunicationRecorder, CommunicationTranscriptWorker). **[Código verificado]**
- Un único compose de producción: `deploy/docker/docker-compose.yml` (3 079 líneas), con dos overlays locales: `docker-compose.local.yml` (sin Caddy, PaymentApp ni Signature) y `docker-compose.local-infra.yml` (solo RabbitMQ, Redis, MinIO, ClamAV, content-scanner). Wrapper `deploy/docker/compose.ps1`. **[Código verificado]**
- SQL Server **no** está en el compose: es externo, y el contenedor `db-ready` espera su puerto. **[Código verificado]** `deploy/docker/docker-compose.yml:160-190`
- La infraestructura de despliegue es "código probado": 18 pruebas de wiring en `deploy/tests/TaxVision.BuildingBlocks.Tests/Deploy/` y otras por servicio leen el texto del compose, los Dockerfiles, el Caddyfile y `deploy.yml`. **[Código verificado]**

## 2. Evidencia

### 2.1 Estructura del Dockerfile .NET (patrón)

`src/Services/Tenant/TaxVision.Tenant.Api/Dockerfile` **[Código verificado]**:

| Línea | Qué hace |
|---|---|
| 1 | `FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build` (misma versión que `global.json`) |
| 4-9 | Contexto acotado: copia solo `global.json`, `src/BuildingBlocks/` y `src/Services/Tenant/` (antes `COPY . .` invalidaba la caché de las 19 imágenes) |
| 10-15 | `dotnet restore` + `dotnet publish -c Release -o /app/publish --no-restore /p:UseAppHost=false` |
| 17 | `FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final` |
| 19 | `ENV TZ=Etc/UTC` (la zona de negocio es la de la oficina, no la del proceso) |
| 21 | `ENV MALLOC_ARENA_MAX=2` (glibc retenía memoria nativa) |
| 23 | `EXPOSE 8080` |
| 25-29 | `apt-get install curl` **solo** si el compose le pone `healthcheck` |
| 32 | `ENTRYPOINT ["dotnet", "TaxVision.Tenant.Api.dll"]` |

Variantes verificadas:
- **Documents** usa `mcr.microsoft.com/playwright/dotnet:v1.49.0-noble` como etapa final (`src/Services/Documents/TaxVision.Documents.Api/Dockerfile:20`).
- **FinancialInsights** instala `poppler-utils tesseract-ocr tesseract-ocr-eng` (`.../FinancialInsights.Api/Dockerfile:14-15`).
- **ContentScanner**: 3 etapas (build, model, final), `USER app` porque decodifica archivos no confiables (`src/Services/ContentScanner/Dockerfile:44-45`).
- **Node** (`src/Services/Communication/Dockerfile`): `node:24.22.2-bookworm-slim` (5, 20), `ENV TZ=Etc/UTC` (22), usuario `taxvision` con `HOME=/app` (40-48), `EXPOSE 5350` (50), `HEALTHCHECK ... curl -f http://localhost:5350/health/live` (52-53).

Usuario: **ninguna imagen .NET de API declara `USER`** (corren como root); solo ContentScanner (`USER app`) y las tres Node (`USER taxvision`). **[Código verificado]** (grep de `^USER` sobre los 32 Dockerfiles).

`HEALTHCHECK` dentro del Dockerfile: solo Communication. El resto delega el healthcheck al compose. **[Código verificado]**

### 2.2 Compose de producción

**Ancla `x-platform-settings-env`** (`docker-compose.yml:1-7`): `PlatformSettings__TenantBaseUrl`, `__BaseDomain: ${TAXVISION_BASE_DOMAIN:-taxoffix.io}`, `__ApiHost: ${TAXVISION_DOMAIN:-api.taxoffix.io}`; se mezcla con `<<: *platform-settings-env` en los servicios que arman links o muestran marca (ej. gateway). **[Código verificado]**

**Anatomía de una API** (`tenant-api`, `docker-compose.yml:368-439`) **[Código verificado]**:
- `image: taxvision/<svc>-api:dev`, `container_name: taxvision-<svc>-api`, `restart: unless-stopped` (371).
- `build.context: ../..` + `dockerfile:` (la presencia de `build:` es lo que el workflow construye).
- `expose: "8080"` (375), **sin `ports:`**.
- `environment`: `TZ: Etc/UTC`, `ASPNETCORE_ENVIRONMENT: Production`, `ASPNETCORE_URLS: http://+:8080`, `ConnectionStrings__Default: ${TENANT_DB_CONNECTION:?Set ... in .env}` (`:?` = el compose no arranca si falta), `ConnectionStrings__Redis: redis:6379`, `RabbitMq__Uri: ${RABBITMQ_CONNECTION:?...}`, trío `Jwt__PublicKeyPath/Issuer/Audience`, `ServiceAuthClient__*` (M2M), URLs internas `http://<svc>-api:8080`, `OpenTelemetry__OtlpEndpoint: http://otel-collector:4317` (417).
- `volumes` (418-420): `taxvision-logs:/app/Logs` y `${TAXVISION_SECRETS_DIR:-/opt/taxvision/secrets}/jwt-public.pem:/run/secrets/jwt-public.pem:ro`.
- `healthcheck` (421-426): `curl -f http://localhost:8080/health/ready`, `interval 10s`, `retries 10`, `start_period 40s`.
- `depends_on` (427-437): `db-ready: service_completed_successfully`, `rabbitmq/redis/minio: service_healthy`, `otel-collector: service_started`.
- `networks: taxvision-network` (438).

Un servicio sin healthcheck propio (ej. `wallet-api`, `docker-compose.yml:1683-1733`) sigue el mismo patrón sin el bloque `healthcheck`.

**Healthchecks en compose**: solo 12 servicios lo tienen: rabbitmq, redis, minio, clamav, content-scanner, tenant-api, auth-api, scribe-api, connectors-api, connectors-mailbox-listener, cloudstorage-api, communication-recorder. **[Código verificado]** (awk sobre el compose). Se añade cuando otro servicio lo espera con `service_healthy` (comentario en `src/Services/Tenant/.../Dockerfile:25-27`).

**restart**: 42 apariciones de `restart:`; `db-ready` no lo lleva a propósito (termina) y los `profiles: [tools]` (`migrations`, `minio-provision`) se corren con `run --rm`. **[Código verificado]** `docker-compose.yml:160-166`, `ComposeRestartPolicyWiringTests.cs:6-36`.

**Puertos publicados** (todas las entradas `ports:` del compose, **[Código verificado]**):

| Servicio | Publicación | Línea |
|---|---|---|
| rabbitmq | `127.0.0.1:5672`, `${MGMT_BIND_IP:-127.0.0.1}:15672` | 22-24 |
| redis | `127.0.0.1:6379` | 44-45 |
| minio | `127.0.0.1:9000`, `${MGMT_BIND_IP:-127.0.0.1}:9001` | 76-78 |
| prometheus (perfil observability) | `127.0.0.1:19090:9090` | 321-322 |
| grafana (perfil observability) | `127.0.0.1:3000` | 360-361 |
| communication-api | rango SFU `40000-40003` UDP y TCP (desde variables) | 2549-2551 |
| gateway | `127.0.0.1:5047:8080` (solo dev; en prod no se usa) | 2927-2928 |
| caddy | `80:80`, `443:443` (único público) | 3051-3053 |

`otel-collector` usa solo `expose` 4317/4318/9464 a propósito: Docker escribe iptables por debajo de UFW y publicar el receptor OTLP lo dejaba abierto (`docker-compose.yml:226-239`). **[Código verificado]**

**Volúmenes con nombre** (`docker-compose.yml:3064-3075`): rabbitmq-data, redis-data, clamav-data, loki-data, tempo-data, prometheus-data, grafana-data, taxvision-logs, caddy-data, caddy-config, recorder-spool. MinIO usa bind `/srv/minio:/data` (80). Red única `taxvision-network` con `name:` fijo (3077-3079). **[Código verificado]**

**Secretos montados**: archivos en `TAXVISION_SECRETS_DIR` (`/opt/taxvision/secrets` en la VM, `dev-keys` en modo local) montados `:ro` en `/run/secrets/...`: `jwt-public.pem` (todos), `jwt-private.pem` (auth), `signature-sealing.pfx`, `signature-signer-jwt.pem`, `firebase-service-account.json`. Los escribe `deploy.yml:525-541` y `:654-678`. **[Código verificado]**

**Perfiles**: `observability` (cadvisor, node-exporter, prometheus, grafana: apagados por RAM, `docker-compose.yml:192-197`), `tools` (minio-provision 2840, migrations 2868). Loki y Tempo comentados. **[Código verificado]**

### 2.3 Overlays locales

- `docker-compose.local.yml:14-21` pone `TenantDomains__EnforceHostResolution: "false"` y `Onboarding__DevFixedOtpCode` solo en local; `:30-37` mueve `caddy`, `payment-app-api`, `signature-api` al perfil `external`; `:39-66` reemplaza el `depends_on` del gateway con `!override`. **[Código verificado]**
- `compose.ps1:31-56`: modos `full` (prod), `local` (exige `dev-keys/jwt-*.pem`, fija `TAXVISION_SECRETS_DIR=dev-keys` y `COMPOSE_PARALLEL_LIMIT=2`) e `infra`. Siempre usa el `.env` de la raíz. **[Código verificado]**
- `docker-compose.local-infra.yml` publica todo en `127.0.0.1` y no declara `restart` (memoria: pendiente proponerlo). **[Código verificado]** + **[Doc. histórica]** `memory/project_arranque_resiliente_sin_broker.md`

### 2.4 Pruebas que protegen Docker/compose (corren en el gate local)

`deploy/tests/TaxVision.BuildingBlocks.Tests/Deploy/` **[Código verificado]**:

| Prueba | Qué fija |
|---|---|
| `ComposeHealthcheckWiringTests` (21-60) | Todo `service_healthy` apunta a un servicio con healthcheck, y todo healthcheck con `curl` corre en una imagen que instala curl (incidente 2026-10-03) |
| `ComposeRestartPolicyWiringTests` (19-36) | Servicios largos con `restart`; los de una vez sin él |
| `ContainerUtcTimeZoneDeployTests` (12-33) | `ENV TZ=Etc/UTC` en la etapa final de cada imagen y `TZ` en cada servicio del compose |
| `DotnetImageMallocArenaTests` (13) | `MALLOC_ARENA_MAX=2` en toda imagen .NET |
| `ComposeInternalBaseUrlWiringTests` (16-37) | Ninguna `*__BaseUrl` apunta a `localhost` en el compose |
| `ServiceClientSecretsDeployWiringTests` (14) | Todo `*_CLIENT_SECRET` leído por el compose lo escribe `deploy.yml` |
| `ProductionExtraSecretsDeployTests` (14-37) | Secrets de `production-extra` escritos en el job `deploy`, no en `stage` |
| `PlatformDomainSingleSourceDeployTests` (21-68) | Todo host público sale de `TAXVISION_BASE_DOMAIN` / `TAXVISION_DOMAIN` |
| `CaddyPublicNavigationRouteTests` (21-53) | Rutas públicas abiertas desde el navegador van al gateway antes de `@spa_nav` |
| `AppsettingsUniqueTopLevelKeysTests` (15) | Sin claves top-level repetidas en `appsettings*.json` |
| `DevClientPortDefaultsTests` (27) | Los `BaseUrl = http://localhost:NNNN` por defecto apuntan a un puerto de `scripts/start-fleet.ps1` |
| Otras | `ModuleGateDeployWiringTests`, `WalletDeployWiringTests`, `PublicBookingDeployWiringTests`, `TenantTimeZoneReconciliationDeployTests`, `CommunicationRecorderDeployWiringTests`, `ClamAvScanLimitsDeployWiringTests`, `AssignmentVisibilityDeployWiringTests` |

Por servicio: `deploy/tests/TaxVision.Auth.Tests/SlotGating/DockerComposeNoPublicApiPortsFitnessTests.cs:18-89` (ninguna API publica `ports:`, excepción declarada: SFU de Communication; el gateway es el único entry point), y `Deploy/*WiringTests` en Auth, CloudStorage, Connectors, Notification, PaymentApp, PaymentClient, Postmaster, Signature, Subscription.

## 3. Problemas

1. **APIs .NET corren como root**: solo ContentScanner y Node declaran `USER`. **[Código verificado]** No hay prueba que lo exija.
2. **Solo 12 servicios con healthcheck en compose**; el gateway depende de las APIs con `depends_on` simple (lista), no `service_healthy` (`docker-compose.yml:2997-3025`). Un servicio arriba pero sin base sigue recibiendo tráfico. **[Código verificado]**
3. **Volumen `taxvision-logs` montado en `/app/Logs` sin uso por defecto**: `TaxVisionLogging.cs:91-97` apaga el archivo dentro de un contenedor (`DOTNET_RUNNING_IN_CONTAINER`). El montaje sigue en cada servicio. **[Código verificado]**
4. **Comentario desactualizado** en `deploy/docker/migrations/apply-migrations.sh:169-170` ("New service is profile-gated"): `financial-insights-api` ya no tiene `profiles` en el compose (`docker-compose.yml:1626-1681`). La migración sigue siendo opcional (`if [ -n ... ]`, 171). **[Código verificado]**
5. **`deploy/tests/Dockerfile:7` corre `dotnet test TaxVision.slnx` sin filtro**: incluiría `.Integration.`/`.Persistence.` que exigen SQL real. **[Código verificado]**
6. **Imagen `minio/mc:latest`** en `minio-provision` (`docker-compose.yml:2843`): sin versión fija, a diferencia del resto. **[Código verificado]**
7. **Guía DEVS desfasada**: dice "ningún servicio declara `restart`" y "19 servicios" en migraciones (`Implementaciones/DEVS/Guia_Creacion_Microservicio.md:1118,1122`); hoy hay `restart` en 42 entradas y 26 bloques `apply_migration`. **[Doc. histórica]** vs **[Código verificado]**

## 4. Buenas prácticas ya vigentes

- Contexto de build acotado por servicio (caché estable).
- `TZ=Etc/UTC` y `MALLOC_ARENA_MAX=2` en la imagen (vale en local y en la VM) y probados.
- `:?` en variables obligatorias: el compose falla al arrancar, no el servicio en silencio.
- Nada interno publicado; lo administrativo solo en loopback o en `MGMT_BIND_IP` (nunca `0.0.0.0`).
- Base URLs internas por DNS de Docker (`http://<svc>-api:8080`), probado.
- Secretos como archivos montados `:ro`, nunca en el checkout.

## 5. Recomendación

- **[Recomendación]** La guía debe tratar el Dockerfile y el bloque de compose como plantilla fija (copiar el de Tenant o Wallet) y recordar que tocarlos exige correr el gate completo.
- **[Recomendación]** Agregar healthcheck (y `curl` en la imagen) cuando otro servicio vaya a esperar al nuevo con `service_healthy`; si no, no hace falta.
- **[Decisión pendiente]** `USER` no root en las imágenes .NET de API: hoy no existe ni se prueba.
- **[Decisión pendiente]** Quitar o justificar el volumen `taxvision-logs`.
- **[Decisión pendiente]** Fijar versión de `minio/mc`.

## 6. Riesgos

- Cambiar compose o `deploy.yml` sin correr el gate rompe pruebas de wiring que leen el texto literal (memoria `feedback_deploy_yaml_cubierto_por_fitness_tests.md`). **[Doc. histórica]**
- Publicar un puerto de API rompe `DockerComposeNoPublicApiPortsFitnessTests` y saltaría el slot gating del gateway.
- Docker escribe reglas iptables por debajo de UFW: un `ports:` sin `127.0.0.1` queda en internet aunque UFW lo niegue (`docker-compose.yml:229-232`).
- RAM de la VM como límite real (23 GiB, swap activo el 29/10): **[Doc. histórica]** `CommunicationCorrecciones/Evidencias/Registro_Evidencias.md` E-196.

## 7. Pruebas

Ver tabla 2.4. Se corren dentro del gate local (no están en `.Integration.`). No hay prueba que exija `USER` no root ni healthcheck en todas las APIs.

## 8. Dependencias

- `deploy.yml` escribe el `.env` con los nombres exactos que el compose consume (ver `CI_CD_Secrets.md`).
- Caddy y Gateway (ver `Cloudflare.md`, `Gateway_YARP.md`).
- `scripts/start-fleet.ps1` es la fuente de puertos dev (ver `Scripts_Locales.md`).

## 9. Material para la guía

### 9.1 Dockerfile de un servicio .NET nuevo (copiar del real, cambiar `<Svc>`)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src
COPY global.json ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/<Svc>/ src/Services/<Svc>/
RUN dotnet restore "src/Services/<Svc>/TaxVision.<Svc>.Api/TaxVision.<Svc>.Api.csproj"
RUN dotnet publish "src/Services/<Svc>/TaxVision.<Svc>.Api/TaxVision.<Svc>.Api.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
# Contenedor en UTC declarado; la zona de negocio es la de la oficina, no la del proceso.
ENV TZ=Etc/UTC
# glibc: 2 arenas de malloc en vez de 8 por nucleo.
ENV MALLOC_ARENA_MAX=2
WORKDIR /app
EXPOSE 8080
# Solo si el compose le pone healthcheck:
# RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaxVision.<Svc>.Api.dll"]
```

Fuente: `src/Services/Tenant/TaxVision.Tenant.Api/Dockerfile:1-32`, `src/Services/FinancialInsights/.../Dockerfile:1-19`.

### 9.2 Bloque de compose (mínimo real, de `wallet-api`)

```yaml
  <svc>-api:
    image: taxvision/<svc>-api:dev
    container_name: taxvision-<svc>-api
    restart: unless-stopped
    build:
      context: ../..
      dockerfile: src/Services/<Svc>/TaxVision.<Svc>.Api/Dockerfile
    expose:
      - "8080"
    environment:
      TZ: Etc/UTC
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:8080
      ConnectionStrings__Default: ${<SVC>_DB_CONNECTION:?Set <SVC>_DB_CONNECTION in .env}
      ConnectionStrings__Redis: redis:6379
      RabbitMq__Uri: ${RABBITMQ_CONNECTION:?Set RABBITMQ_CONNECTION in .env}
      Jwt__PublicKeyPath: /run/secrets/jwt-public.pem
      Jwt__Issuer: TaxVision.Auth
      Jwt__Audience: TaxVision.Services
      Authorization__PermissionsSource: Projection
      ServiceAuthClient__AuthBaseUrl: http://auth-api:8080
      ServiceAuthClient__ClientId: ${<SVC>_SERVICE_CLIENT_ID:-<svc>-worker}
      ServiceAuthClient__ClientSecret: ${<SVC>_SERVICE_CLIENT_SECRET:-}
      OpenTelemetry__OtlpEndpoint: http://otel-collector:4317
    volumes:
      - taxvision-logs:/app/Logs
      - ${TAXVISION_SECRETS_DIR:-/opt/taxvision/secrets}/jwt-public.pem:/run/secrets/jwt-public.pem:ro
    depends_on:
      db-ready:
        condition: service_completed_successfully
      rabbitmq:
        condition: service_healthy
      redis:
        condition: service_healthy
      otel-collector:
        condition: service_started
    networks:
      - taxvision-network
```

Fuente: `deploy/docker/docker-compose.yml:1683-1733`.

### 9.3 Checklist Docker/compose de un servicio nuevo

1. Dockerfile en `src/Services/<Svc>/TaxVision.<Svc>.Api/Dockerfile` con `TZ` y `MALLOC_ARENA_MAX` (lo exigen `ContainerUtcTimeZoneDeployTests` y `DotnetImageMallocArenaTests`).
2. Bloque `<svc>-api` en `docker-compose.yml` con `restart: unless-stopped`, `expose: "8080"`, **sin `ports:`**, `TZ: Etc/UTC`.
3. Toda URL de otro servicio como `http://<otro>-api:8080`, nunca `localhost`.
4. Si otro servicio lo esperará con `service_healthy`: `healthcheck` + `curl` en la imagen.
5. Si se agrega al gateway: `ReverseProxy__Clusters__<id>__Destinations__<id>1__Address: http://<svc>-api:8080/` en el bloque `gateway` y el servicio en su `depends_on` (`docker-compose.yml:2967-2993`, `2997-3025`); repetir en `docker-compose.local.yml:39-66` si el servicio corre en local.
6. Variables M2M: entrada `ServiceAuth__Clients__<N>__*` en `auth-api` (ej. `docker-compose.yml:713-715`) y el `*_CLIENT_SECRET` escrito en `deploy.yml`.
7. `*_DB_CONNECTION` también en el servicio `migrations` (`:2868-2913`) y un `apply_migration` en `apply-migrations.sh`.
8. Correr el gate completo: las pruebas de wiring leen estos archivos.

### 9.4 Cómo evitar colisiones de puertos

- Dentro de Docker todos los .NET escuchan en 8080: no hay colisión porque nada se publica.
- En local cada servicio tiene su puerto en `scripts/start-fleet.ps1:9-39` (fuente de verdad, la usa `DevClientPortDefaultsTests`). Elegir uno libre de esa tabla y del `stop-fleet.ps1:12-15`.
- Ojo: Communication Recorder usa 5360 en contenedor pero 5365 en local porque 5360 es de Subscription (`start-fleet.ps1:107-108`).
