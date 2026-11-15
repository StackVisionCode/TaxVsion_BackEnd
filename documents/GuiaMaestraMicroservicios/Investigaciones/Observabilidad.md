# Observabilidad

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd`. Etiquetas: **[Código verificado]**, **[Doc. histórica]**, **[Recomendación]**, **[Decisión pendiente]**.

## 1. Estado actual

- **Logs**: Serilog compartido (`UseTaxVisionSerilog`) a consola; archivo solo fuera de contenedor; OTLP de logs apagado por defecto. En producción la fuente de logs es `docker logs`. **[Código verificado]**
- **Métricas**: OpenTelemetry siempre exporta métricas por OTLP al `otel-collector`, que las deja en un endpoint Prometheus `:9464` (pull). Prometheus y Grafana existen pero están **apagados** (perfil `observability`) por RAM. **[Código verificado]**
- **Trazas**: apagadas por defecto (`OpenTelemetry:ExportTraces=false`); el collector las descarta con `nop`. Loki y Tempo comentados en el compose. **[Código verificado]**
- **Health**: `/health/live` (proceso) y `/health/ready` (base + RabbitMQ con tag `ready`) en cada servicio; el gateway además `/health/dependencies`. **[Código verificado]**
- Wiring presente en los Program.cs: `UseTaxVisionSerilog` 27, `AddTaxVisionOpenTelemetry` 27, `CorrelationIdMiddleware` 27, `AddTimeZoneSelfCheck` 26, `/health/ready` 28. **[Código verificado]** (conteo por grep en `src/**/Program.cs`)

## 2. Evidencia

### 2.1 Serilog **[Código verificado]** `src/BuildingBlocks/BuildingBlocks.Web/Observability/TaxVisionLogging.cs`

- Plantilla (15-17): `[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{Service}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}`.
- Niveles (45-68): base Information; `Microsoft` Warning, `Microsoft.Hosting.Lifetime` Information, `Microsoft.EntityFrameworkCore` Warning, `System.Net.Http.HttpClient` Warning; después aplica `Logging:LogLevel` y al final la sección `Serilog` (gana).
- Request logging (70-77): `/health/*` en Verbose (no se ve), 5xx o excepción en Error.
- Sinks (79-127): consola siempre; archivo JSON compacto `Logs/<servicio>-.log`, diario, 30 archivos, **solo si no está en contenedor** (`DOTNET_RUNNING_IN_CONTAINER`) o si `Observability:Logging:File=true`; OTLP si `OpenTelemetry:ExportLogs=true` y hay endpoint, con cola acotada (500 por lote, 10 000 en cola, 1 min de reintento).
- Uso: `builder.Host.UseTaxVisionSerilog("notes-service");` (`src/Services/Notes/TaxVision.Notes.Api/Program.cs:40`).

### 2.2 OpenTelemetry **[Código verificado]** `src/BuildingBlocks/BuildingBlocks.Web/Observability/OpenTelemetryRegistration.cs`

- Recurso con `serviceName` (26-28).
- Trazas (30-47) solo si `OpenTelemetry:ExportTraces=true`: ASP.NET Core (sin `/health`), HttpClient, `AddSource(serviceName)`.
- Métricas (49-71) siempre: ASP.NET Core, HttpClient, Runtime, `AddMeter(serviceName)` (meter propio con el mismo nombre del servicio), `AuthorizationMetrics`, `SeatSlotGateMetrics`, `RateLimitMetrics`, más `additionalMeterNames`.
- Endpoint: `OpenTelemetry__OtlpEndpoint: http://otel-collector:4317` en cada servicio del compose; local `http://localhost:4317` (gateway `appsettings.json:43-45`).

Collector `deploy/observability/otel-collector.yml`: receptores OTLP gRPC 4317 y HTTP 4318 (1-7), `memory_limiter` 256 MiB y `batch` (9-14), exporter `prometheus` en `0.0.0.0:9464` y `nop` (21-24), pipelines traces→nop, metrics→prometheus, logs→nop (34-47). En compose solo `expose` (sin `ports`), `mem_limit: 512m` (`deploy/docker/docker-compose.yml:219-246`).

Prometheus (`deploy/observability/prometheus.yml`): jobs `taxvision-otel-collector` (`otel-collector:9464`), `communication` (`communication-api:5350`), `cadvisor`, `node-exporter`, `rabbitmq` (`rabbitmq:15692`). Grafana provisiona 12 dashboards (`overview`, `service-detail`, `eventbus`, `authorization`, `ratelimit-*`, `payment-*`, `reminder`, `communication`, `infrastructure`) y alertas `eventbus-alerts.yml`, `ratelimit-alerts.yml` en `deploy/observability/grafana/provisioning/`.

### 2.3 Health checks **[Código verificado]**

Servicio (Notes, `Program.cs:114-117, 207-208`):
```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<NotesDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
```
Gateway (`src/Gateway/TaxVision.Gateway/Program.cs:86-92,179-184`): solo `upstream-clusters` con tag `dependencies` y `failureStatus: Degraded`; `/health/ready` no tiene checks `ready`, así que responde 200 si el proceso vive (GW-06: un servicio caído no debe sacar al gateway del balanceador).

Quién consume cada uno:
- Compose `healthcheck` → `/health/ready` del contenedor (12 servicios, ver `Docker_Deploy.md`).
- YARP health check activo → `/health/live` de cada cluster cada 15 s (`appsettings.json` de Gateway).
- `deploy.yml:786-819` → `https://${TAXVISION_DOMAIN}/health/ready` (gateway).

### 2.4 Por qué `/health` no prueba producción **[Código verificado]**

1. El `/health/ready` público es el del gateway, sin checks de servicios: 200 con toda la flota caída detrás.
2. `/health/live` de los servicios no ejecuta ningún check (`Predicate = _ => false`).
3. `/health/ready` de un servicio solo mide su base (conexión EF) y un **TCP** a RabbitMQ; no prueba migraciones aplicadas, consumidores activos, M2M, MinIO ni proveedores externos.
4. Una migración sin aplicar no tumba el health: el `Invalid column` aparece en la primera consulta. **[Doc. histórica]** `memory/feedback_migration_drift_causes_gateway_load_shedding_cascade.md`

### 2.5 CorrelationId **[Código verificado]** `src/BuildingBlocks/BuildingBlocks.Web/Middleware/CorrelationIdMiddleware.cs`

- Header `X-Correlation-Id`, validado contra `^[A-Za-z0-9._-]{1,128}$`; si falta o no es válido genera `Guid.NewGuid().ToString("N")` (12-26).
- Lo pone en request y response, en `Activity` como tag y baggage `taxvision.correlation_id` (28-36) y en `ICorrelationContext` (que usa la plantilla de Serilog) (41-44); con `UseTaxVisionDomainEvents()` también en el sobre de eventos (38-39).
- Fitness: todo evento de integración sale con `CorrelationId` (`deploy/tests/TaxVision.BuildingBlocks.Tests/Architecture/CorrelationIdFitnessTests.cs:6-15`).

### 2.6 TimeZoneSelfCheck **[Código verificado]** `src/BuildingBlocks/BuildingBlocks.Web/Hosting/TimeZoneSelfCheck.cs`

`IHostedService` que al arrancar comprueba `America/New_York` y `Asia/Tokyo` y si la globalización es invariante; registra Warning si falta algo o Information si todo resuelve; **nunca detiene el arranque** (7-36). Se registra con `builder.Services.AddTimeZoneSelfCheck();` (`Notes/Program.cs:53`).

### 2.7 Logs en la VM y runbooks

- En la VM no queda `.env`; los comandos van contra contenedores con `sudo docker ...` (`sudo docker exec -it taxvision-communication-api ...`). **[Doc. histórica]** `CommunicationCorrecciones/F11_Despliegue_Secretos_y_VM.md:45-60`
- Compose: `docker compose ... logs -f <svc>` (`README.md:1462-1470`). **[Doc. histórica]**
- Tras el cambio a codegen Static, cada servicio debe loguear "code generation mode is Static" y "Using pre-generated Wolverine HandlerRegistry"; para reencender lo apagado: `OpenTelemetry__ExportLogs=true`, `OpenTelemetry__ExportTraces=true`, `Observability__Logging__File=true`, `Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore.Database.Command=Information`. **[Doc. histórica]** `Implementaciones/Infra/RAM-Tasks-3GB-Produccion-2026-11-13.md:51-58`
- Outbox/inbox/DLQ por SQL: `wolverine_outgoing_envelopes`, `wolverine_incoming_envelopes`, `wolverine_dead_letters` (`README.md:1478-1486`). **[Doc. histórica]**
- Runbooks existentes, puntuales: `Implementaciones/DEVS/Runbook_Resincronizar_Buzones.md`, `Implementaciones/BUILDER_TEMPLATE/Runbook_ClientForms_X8.md`. No hay runbook general de despliegue/incidente del backend. **[Doc. histórica]**
- Métricas de negocio de Communication en `/communication/admin/analytics/*` (comentario `docker-compose.yml:192-197`). **[Código verificado]**

### 2.8 Pruebas **[Código verificado]**

`deploy/tests/TaxVision.BuildingBlocks.Tests/Observability/TaxVisionLoggingTests.cs`, `OpenTelemetryRegistrationTests.cs`; `Architecture/CorrelationIdFitnessTests.cs`; `Architecture/TimeZoneInfraFitnessTests.cs`; carpetas `Observability/` en Calendar, Reminder, Scribe, Subscription y Tasks Tests (métricas propias); Calendar usa `OpenTelemetry.Exporter.InMemory 1.16.0`.

## 3. Problemas

1. **Sin backend de logs ni trazas en producción**: logs solo en `docker logs` (rotación según el daemon, no verificada); trazas descartadas. **[Código verificado]** **[Decisión pendiente]** (Loki/Tempo comentados por RAM).
2. **Métricas recolectadas pero no leídas**: Prometheus/Grafana apagados; las alertas provisionadas no corren. **[Código verificado]**
3. **Volumen `taxvision-logs`** montado en todos los servicios aunque el sink de archivo está apagado en contenedor. **[Código verificado]**
4. **El healthcheck del deploy no detecta servicios caídos** (2.4). **[Código verificado]**
5. **Sin runbook general** (diagnóstico de 502/503, load shedding, DLQ, reinicio de un servicio). **[Doc. histórica]**

## 4. Buenas prácticas ya vigentes

- Una pieza compartida para logs y otra para OTel: un servicio nuevo solo las llama con su nombre.
- Health checks fuera del log de requests y de las trazas.
- Collector sin puertos publicados y sin depender de backends.
- Meter con el nombre del servicio por convención.
- CorrelationId de punta a punta, probado.

## 5. Recomendación

- **[Recomendación]** La guía debe exigir en el Program.cs del servicio nuevo: `UseTaxVisionSerilog("<svc>-service")`, `AddTimeZoneSelfCheck()`, `AddTaxVisionOpenTelemetry(config, "<svc>-service")`, `CorrelationIdMiddleware` antes del request logging, health checks con tag `ready` para su base y RabbitMQ, y `/health/live` + `/health/ready`.
- **[Recomendación]** Verificación post-deploy manual por servicio: logs sin `Error` al arrancar, línea del TimeZoneSelfCheck, línea de codegen Static, y una petición real por el gateway (no solo `/health`).
- **[Decisión pendiente]** Encender Prometheus/Grafana o un backend de logs cuando la RAM lo permita.

## 6. Riesgos

- Interpretar un `/health/ready` 200 como "producción sana".
- Load shedding (503 "Fleet is overloaded") disparado por latencia de otro servicio: mirar el log del gateway (`Load shedding activated: p99=...`). **[Doc. histórica]**
- Bajar el nivel de EF a Information en producción llena los logs.

## 7. Pruebas

Ver 2.8. Todas en el gate local.

## 8. Dependencias

- `otel-collector` (compose), Wolverine (tablas de sobres), RabbitMQ management (métricas `:15692`).

## 9. Material para la guía

### 9.1 Fragmento de Program.cs (real, Notes)

```csharp
builder.Host.UseTaxVisionSerilog("<svc>-service");
builder.Services.AddTimeZoneSelfCheck();
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "<svc>-service");

builder.Services.AddHealthChecks()
    .AddDbContextCheck<<Svc>DbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);

// ...
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
// ...
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
```
Fuente: `src/Services/Notes/TaxVision.Notes.Api/Program.cs:40,53,58,114-117,188-190,207-208`.

### 9.2 Métrica propia

Crear un `Meter` con el **mismo nombre** que se pasa a `AddTaxVisionOpenTelemetry` (ej. `"notes-service"`); se exporta sin tocar el builder compartido (`OpenTelemetryRegistration.cs:51-57`).

### 9.3 Diagnóstico rápido

| Síntoma | Dónde mirar |
|---|---|
| 502 en local | Puerto del servicio caído: `scripts/restart-down.ps1`, `logs/<Svc>.err.log` |
| 503 "Fleet is overloaded" | `logs/Gateway.log` (`Load shedding activated`), luego `Invalid column name` en `logs/*.log` |
| Evento que no llega | `wolverine_outgoing_envelopes` del emisor, cola `<svc>-events` en RabbitMQ, `wolverine_dead_letters` del receptor |
| Producción | `sudo docker logs --tail 200 taxvision-<svc>-api` |
