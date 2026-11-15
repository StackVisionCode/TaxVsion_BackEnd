# Gateway YARP

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd`. Etiquetas: **[Código verificado]**, **[Doc. histórica]**, **[Recomendación]**, **[Decisión pendiente]**.

## 1. Estado actual

- Proyecto `src/Gateway/TaxVision.Gateway` (Program.cs de 194 líneas, `appsettings.json` de 1 079). YARP carga rutas y clusters de la sección `ReverseProxy` (`Program.cs:94-96`). **[Código verificado]**
- **43 rutas y 27 clusters**, un cluster por servicio con un único destino. Todas las rutas son por prefijo `/<prefijo>/{**catch-all}`. **[Código verificado]** (lectura de `appsettings.json`)
- En local los destinos son `http://localhost:<puerto>/`; en producción el compose los pisa con `ReverseProxy__Clusters__<id>__Destinations__<id>1__Address: http://<svc>-api:8080/` (`deploy/docker/docker-compose.yml:2967-2993`). **[Código verificado]**
- El gateway **no autoriza por ruta**: ninguna ruta declara `AuthorizationPolicy`, `RateLimiterPolicy`, `CorsPolicy` ni `Timeout`. Valida el JWT si viene, aplica guardas transversales y deja la autorización fina (actor, permiso, tenant) a cada servicio. **[Código verificado]**

## 2. Evidencia

### 2.1 Pipeline (orden real, `Program.cs`) **[Código verificado]**

| Línea | Pieza | Para qué |
|---|---|---|
| 23-25 | `UseTaxVisionSerilog("gateway")`, `AddBuildingBlocks()`, `AddSqlSeatSlotGate` | Logs, base, gate de asientos (lee la base de Auth: `ConnectionStrings__Auth` en compose) |
| 37-74 | CORS `spa` | Orígenes: `Cors:Origins` (extra: localhost y apps móviles) + `PlatformSettings.WebOrigins` + cualquier subdominio de oficina HTTPS válido; `AllowCredentials`; expone `Retry-After`, `X-RateLimit-*`, `X-Seat-Slot-Gated`, `X-Result-Truncated`, `ETag` |
| 76-79 | `AddTaxVisionJwtAuthentication`, `AddTaxVisionGatewayRateLimiting`, `AddLoadShedding`, `AddTaxVisionOpenTelemetry` | |
| 86-92 | Health check `upstream-clusters` con tag `dependencies`, `failureStatus: Degraded` | GW-06: el readiness del gateway es *self* |
| 94-112 | `AddReverseProxy().LoadFromConfig(...)` + transform que re-inyecta `CF-Connecting-IP` con la IP ya resuelta | Sin esto los servicios veían la IP de Docker |
| 117-138 | `TenantHostGuardOptions`, `HostTenantResolver` con la dirección del cluster `auth`, timeout 3 s | Host ↔ tenant |
| 140, 145 | `AddTaxVisionClientIpForwarding` / `UseTaxVisionClientIp()` (primer middleware) | IP real detrás de Cloudflare |
| 147-149 | `CorrelationIdMiddleware`, `UseSerilogRequestLogging`, `ExceptionHandlingMiddleware` | |
| 151 | `SecurityHeadersMiddleware` | |
| 155 | `InternalSurfaceGuardMiddleware` | 404 a cualquier path con segmento `internal` y al grant `/auth/service-token`, antes de CORS y auth |
| 157-158 | `UseCors("spa")`, `UseAuthentication()` | |
| 161 | `MfaEnrollmentGuardMiddleware` | Un token solo-MFA no sale de `/auth/` |
| 165 | `SeatSlotGateMiddleware` | M2M exento por `ActorType.Service` |
| 166-167 | `UseRateLimiter()`, `UseAuthorization()` | |
| 172 | `LoadSheddingMiddleware` | Capa 1, excluye `/health/*` |
| 173 | `TenantPropagationMiddleware` | |
| 177 | `TenantHostGuardMiddleware` | Subdominio no registrado → 404; tenant del JWT ≠ tenant del Host → 403 |
| 179-184 | `/health/live` (sin checks), `/health/ready` (tag `ready`), `/health/dependencies` (tag `dependencies`) | |
| 190 | `UseWebSockets()` antes de `MapReverseProxy()` | Sin él socket.io cae a long-polling y se rompe detrás de Cloudflare |
| 192 | `MapReverseProxy()` | |

### 2.2 Rutas (prefijo → cluster) **[Código verificado]** `appsettings.json:168-445`

| Ruta(s) | Path | Cluster | Extra |
|---|---|---|---|
| auth, onboarding | `/auth/**`, `/onboarding/**` | auth | `RequestHeaderOriginalHost: true` (178, 189); auth con `MaxRequestBodySize` 6 MB |
| tenant, tenant-platform-branding/settings/tenants | `/tenants/**`, `/platform/branding|settings|tenants/**` | tenant | 2 MB en tenants y branding |
| customer | `/customers/**` | customer | 11 MB |
| financial | `/financial/**` | financial | 64 KB |
| plans, subscriptions, seats, addons, entitlements, audit, admin-subscription | `/plans`, `/subscriptions`, `/seats`, `/addons`, `/entitlements`, `/audit`, `/admin/subscription` | subscription | |
| notifications, postmaster, scribe, connectors, correspondence | un prefijo cada uno | homónimo | |
| cloudstorage | `/storage/**` | cloudstorage | |
| signature | `/signature/**` | signature | 26 MB (311-316) |
| communication | `/communication/**` | communication | incluye socket.io |
| payments-app, payments-client, growth, billing, documents, notes, sms, reminders, tasks, wallet, catalog, inventory | un prefijo cada uno | homónimo | |
| calendar, calendar-public | `/calendar/**`, `/calendar/public/**` | calendar | `calendar-public` 16 KB (378) |
| campaigns + campaigns-contacts/contact-lists/sender-profiles/settings/templates | `/campaigns`, `/contacts`, `/contact-lists`, `/sender-profiles`, `/campaign-settings`, `/campaign-templates` | campaigns | |

### 2.3 Clusters **[Código verificado]** `appsettings.json:446-…`

Todos con la misma forma (ejemplo `auth`, 447-467):
- `Destinations.<id>1.Address`
- `HealthCheck.Active`: `Enabled true`, `Interval 00:00:15`, `Timeout 00:00:05`, `Policy ConsecutiveFailures`, `Path /health/live`
- `HealthCheck.Passive.Enabled false`
- `Metadata."ConsecutiveFailuresHealthPolicy.Threshold": "3"`
- `AvailableDestinationsPolicy: HealthyOrPanic`
- `HttpRequest.ActivityTimeout` solo en cloudstorage y documents (`00:10:00`, 684 y 851) y communication (`01:00:00`, 733). El resto usa el default de YARP. **[Código verificado]**

### 2.4 Rate limiting del gateway **[Código verificado]**

- `GatewayRateLimiting` (`appsettings.json:46-111`):
  - `PreAuthByIp`: 30 por 60 s, reglas por patrón y método: `/auth/login`, `/auth/mfa/verify`, `/auth/password/forgot|reset|reset/validate`, `/auth/me/email/confirm`, `/auth/invitations/accept`, `POST /tenants`, y 7 comandos `POST /calendar/public/...`.
  - `StorageUploadByTenant`: 30 por 60 s, sin reglas hoy.
- Implementación: `src/BuildingBlocks/BuildingBlocks.Web/RateLimiting/RateLimitingRegistration.cs:18-80`: un `GlobalLimiter` particionado. Storage por `tenant_id` del JWT; pre-auth por `IP + path`; el resto `GetNoLimiter` ("su cuota real la aplica el `[RateLimit]` tiered de cada servicio", 75). Las reglas configuradas **reemplazan** a los defaults (84-98). Se releen con `IOptionsMonitor`.
- `LoadShedding` (`appsettings.json:112-167`): umbral p99 5 000 ms, 5xx 50 %, ventana 60 s, mínimo 200 muestras, `PassThroughPathPrefixes: ["/communication/socket.io"]` (120) y `Criticality` por primer segmento de ruta (`Critical` para auth, billing, onboarding, payments-*, tenants; `Background` para audit, growth, postmaster, scribe). Los escalares se repiten en el compose para ajustarlos en un incidente (`docker-compose.yml:2956-2966`).

### 2.5 Rutas públicas, JWT, M2M e internas

- **Públicas/anónimas**: el gateway no bloquea anónimos; cada servicio decide con `[AllowAnonymous]`. Las públicas sensibles entran a `PreAuthByIp`. **[Código verificado]**
- **JWT**: validado en el gateway (`Jwt` en `appsettings.json:9-17`, audiencias `TaxVision.Services`, `TaxVision.Account`, `TaxVision.MfaEnrollment`) y otra vez en cada servicio. **[Código verificado]**
- **M2M e internas**: los controllers M2M viven bajo un segmento `internal` y, como el grant de tokens de servicio (`/auth/service-token`), se llaman contenedor a contenedor (`http://<svc>-api:8080/...`), **nunca** por el gateway: `InternalSurfaceGuardMiddleware.cs:5-55` responde 404 (no 403, para no confirmar existencia) y cuenta la métrica `InternalSurfaceProbesBlocked`. **[Código verificado]**
- **WebSockets**: `UseWebSockets()` (190) + cluster communication con `ActivityTimeout` 1 h + `PassThroughPathPrefixes` del load shedder.

### 2.6 Pruebas del gateway **[Código verificado]**

`deploy/tests/TaxVision.Gateway.Tests` (19 archivos; xUnit 2.9.3; referencia solo el proyecto Gateway):

| Prueba | Qué protege |
|---|---|
| `Health/GatewayHealthConfigurationTests` (28-75) | Todo cluster con health check activo a `/health/live` (no readiness), pasivo apagado, `HealthyOrPanic` |
| `LoadShedding/LoadSheddingCriticalityCoverageTests` (41-81) | **Toda ruta nueva necesita entrada en `LoadShedding:Criticality`** (y no sobran entradas) |
| `Pipeline/GatewayPublicBookingRouteTests` (29-84) | Tope 16 KB en `calendar-public` con Kestrel real, comandos públicos en el gate pre-auth, 429 tras 30 por minuto |
| `Pipeline/GatewayWebSocketPipelineTests` (26-32) | `UseWebSockets` existe y va antes de `MapReverseProxy` |
| `Pipeline/GatewayCorsPolicyTests` (35-70) | Headers expuestos y orígenes extra iguales a los de Communication |
| `Middleware/*Tests` | InternalSurfaceGuard, MfaEnrollmentGuard, SeatSlotGate, SecurityHeaders, TenantHostGuard, TenantPropagation, HostTenantResolver |
| `LoadShedding/*` | LoadShedder, middleware, Kestrel pipeline, ventana, señal, consumo por tenant |

Fuera del proyecto:
- `deploy/tests/TaxVision.BuildingBlocks.Tests/Architecture/InternalRouteConventionFitnessTests.cs:7-18`: ninguna ruta del gateway menciona `internal` y ningún controller interno lleva el prefijo del servicio delante.
- `deploy/tests/TaxVision.Auth.Tests/SlotGating/DockerComposeNoPublicApiPortsFitnessTests.cs:89`: el gateway es el único entry point con puertos.

## 3. Problemas

1. **No hay prueba que exija el override de destino en el compose** para cada cluster de `appsettings.json`. `ComposeInternalBaseUrlWiringTests` solo mira claves `*__BaseUrl`. Un cluster nuevo sin `ReverseProxy__Clusters__...__Address` en compose apuntaría a `localhost` dentro del contenedor. **[Código verificado]** (grep de `ReverseProxy__Clusters` en `deploy/tests`: sin coincidencias)
2. **`/health/ready` del gateway no tiene checks con tag `ready`** (solo `dependencies`): responde 200 si el proceso vive, sin importar los servicios (decisión GW-06, `Program.cs:81-85`). **[Código verificado]**
3. **El health check activo de cada cluster pega a `/health/live`**, que en los servicios es `Predicate = _ => false` (ej. `src/Services/Notes/TaxVision.Notes.Api/Program.cs:207`): solo prueba que el proceso responde, no su base. Es decisión explícita (GW-07). **[Código verificado]**
4. **Pruebas del gateway nombradas en español** (`TodoClusterTieneActiveHealthCheckHabilitado`, `UseWebSocketsEstaEnElPipeline`), contra la convención "identificadores en inglés" del proyecto. **[Código verificado]** vs `AGENTS.md`
5. Comentario de la guía DEVS: "split SPA app.* + API a.*" (`Implementaciones/DEVS/Guia_Creacion_Microservicio.md:40`): "a.*" no aparece en el Caddyfile. **[Doc. histórica]**

## 4. Buenas prácticas ya vigentes

- Un prefijo por servicio, un cluster por servicio, destino sobreescrito por compose (la imagen es la misma en local y prod).
- `MaxRequestBodySize` por ruta cuando el cuerpo es grande o debe ser pequeño (público).
- Superficie interna cerrada por comportamiento (404) y por estructura (fitness).
- Criticidad del load shedder obligatoria por ruta (fitness).
- `RequestHeaderOriginalHost` solo donde el servicio resuelve tenant por Host (Auth).

## 5. Recomendación

- **[Recomendación]** La guía debe pedir, por servicio nuevo: ruta(s), cluster con el bloque de health check idéntico, entrada en `Criticality`, override en compose y `depends_on`, y nada bajo `internal`.
- **[Decisión pendiente]** Prueba que compare clusters de `appsettings.json` contra overrides del compose (hoy no existe).
- **[Decisión pendiente]** Si un servicio necesita timeouts largos (streaming, archivos), fijar `HttpRequest.ActivityTimeout` en su cluster como cloudstorage/documents; no hay regla escrita.

## 6. Riesgos

- Ruta sin criticidad: rompe el gate (bueno). Ruta sin override en compose: se descubre en producción (502).
- Un prefijo nuevo que choque con uno existente o con los buckets de MinIO (`/taxvision-storage`, `/taxvision-temp`, `/taxvision-quarantine`, que Caddy manda a MinIO antes del gateway).
- Un endpoint público que el usuario **abre en el navegador** en un host de oficina necesita además regla en Caddy (ver `Cloudflare.md`).
- Endpoints anónimos sensibles sin entrada en `PreAuthByIp` solo quedan con el `[RateLimit]` del servicio.

## 7. Pruebas

Tabla 2.6. Todas corren en el gate local (no hay `.Integration.` en Gateway.Tests).

## 8. Dependencias

- Auth (cluster `auth` también usado por `HostTenantResolver`), Tenant (PlatformSettings para CORS y hosts), base de Auth (seat gate).
- Compose (overrides de destino), Caddy (Host original), Cloudflare (IP real `CF-Connecting-IP`).

## 9. Material para la guía

### 9.1 Ruta y cluster nuevos (forma real de `notes`)

```json
"ReverseProxy": {
  "Routes": {
    "<svc>": {
      "ClusterId": "<svc>",
      "Match": { "Path": "/<prefijo>/{**catch-all}" }
    }
  },
  "Clusters": {
    "<svc>": {
      "Destinations": { "<svc>1": { "Address": "http://localhost:<puerto>/" } },
      "HealthCheck": {
        "Active": {
          "Enabled": true,
          "Interval": "00:00:15",
          "Timeout": "00:00:05",
          "Policy": "ConsecutiveFailures",
          "Path": "/health/live"
        },
        "Passive": { "Enabled": false }
      },
      "Metadata": { "ConsecutiveFailuresHealthPolicy.Threshold": "3" },
      "AvailableDestinationsPolicy": "HealthyOrPanic"
    }
  }
}
```

Opcional por ruta: `"MaxRequestBodySize": <bytes>`. Opcional por cluster: `"HttpRequest": { "ActivityTimeout": "00:10:00" }`.

### 9.2 Criticidad (obligatoria)

```json
"LoadShedding": { "Criticality": { "<prefijo>": "Standard" } }
```
Valores usados: `Critical`, `Standard`, `Background`. La clave es el primer segmento del `Match.Path` (`LoadSheddingCriticalityCoverageTests.cs:29-41`).

### 9.3 Producción (compose, bloque `gateway`)

```yaml
      ReverseProxy__Clusters__<svc>__Destinations__<svc>1__Address: http://<svc>-api:8080/
    depends_on:
      - <svc>-api
```
Y lo mismo en `deploy/docker/docker-compose.local.yml:39-66` (lista `!override`).

### 9.4 Endpoint público con riesgo de abuso

Agregar regla a `GatewayRateLimiting:PreAuthByIp:Rules` (`{ "Pattern": "/<prefijo>/public/...", "Method": "POST" }`) y, si el cuerpo es pequeño, `MaxRequestBodySize` en una ruta propia (patrón `calendar-public`).

### 9.5 Endpoints M2M

Siempre bajo `.../internal/...` en el servicio, llamados por DNS de Docker desde otro servicio. No se registran en el gateway (el guard los devuelve 404 y el fitness lo vigila).

### 9.6 Verificación

- Gate local: `LoadSheddingCriticalityCoverageTests`, `GatewayHealthConfigurationTests`, `InternalRouteConventionFitnessTests`.
- Local: `http://localhost:5047/<prefijo>/...` sin token debe dar 401 del servicio (no 404 del gateway, salvo `internal`).
- `GET /health/dependencies` del gateway muestra `Degraded` si un cluster cae.
