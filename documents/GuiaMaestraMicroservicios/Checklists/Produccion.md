# Checklist — Criterios de aceptación en producción

> Propósito: dar por "desplegado y sano" un servicio solo con evidencia: esquema aplicado, secrets presentes, consumers conectados, proyecciones pobladas, reconciliación terminada y codegen Static sin Roslyn.
> `/health/ready` en verde **no** prueba producción: el del Gateway responde 200 con la flota caída detrás.

**Cuándo aplica:** antes de pedir el push (lo hace el dueño; el push a `main` despliega) y justo después de cada deploy que incluya el servicio.

**Formato:** *Verificación* = comando, prueba o archivo; *Origen* = investigación. Condicionales entre corchetes. En la VM no queda `.env`: los comandos van contra contenedores con `sudo docker ...`.

---

## 1. Antes del push (Definition of Done local)

- [ ] **Gate exacto verde**: `dotnet test TaxVision.slnx --nologo --filter "FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence.&FullyQualifiedName!~ReminderRetentionJobTests"`.
  - Verificación: salida del comando. · Origen: [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §2.6 y §5.
- [ ] **`.Integration.` del servicio verdes** contra la base local si se tocó persistencia o handlers.
  - Verificación: `dotnet test deploy/tests/TaxVision.<Svc>.Tests/TaxVision.<Svc>.Tests.csproj --nologo --filter "FullyQualifiedName~.Integration."`. · Origen: [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §5.
- [ ] **CSharpier sobre lo tocado** y migración aplicada en local (`scripts/migrate-local.ps1` sin pendientes).
  - Verificación: `dotnet csharpier format <archivos>`; informe de `migrate-local.ps1` vacío. · Origen: [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §5; [Migraciones](../Investigaciones/Migraciones.md) "Recomendación".
- [ ] **Vitest** de Communication, Recorder y TranscriptWorker [si se tocó Node].
  - Verificación: salida de Vitest. · Origen: [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §2.6.

## 2. Migraciones aplicadas

- [ ] **Bloque `apply_migration`** del servicio en `deploy/docker/migrations/apply-migrations.sh` y `<SVC>_DB_CONNECTION` (con `:?`) en el servicio `migrations` y en `<svc>-api` del compose; proyectos en `TaxVision.slnx` (la imagen `migrations` compila la solución).
  - Verificación: revisión de los tres archivos; sin el bloque el deploy queda en verde sin esquema. · Origen: [Migraciones](../Investigaciones/Migraciones.md) problema 5 y "Paso a paso" 6; [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §9.1.
- [ ] **El contenedor `migrations` corrió en el deploy** (`--profile tools run --rm migrations`, imagen reconstruida con `--profile tools`). Los servicios no migran al arrancar; Wolverine solo crea sus tablas.
  - Verificación: log del job `deploy` de GitHub Actions (paso "Apply .NET EF Core migrations"). · Origen: [Migraciones](../Investigaciones/Migraciones.md) "Cómo se aplican en producción".
- [ ] **Sin `Invalid column name` en los logs** (una migración sin aplicar no tumba el health pero dispara load shedding en toda la flota).
  - Verificación: `sudo docker logs --tail 200 taxvision-<svc>-api`. · Origen: [Migraciones](../Investigaciones/Migraciones.md) "Riesgos"; [Observabilidad](../Investigaciones/Observabilidad.md) §2.4.

## 3. Secrets

- [ ] **Secrets nuevos en el environment `production-extra`** (el `production` está lleno), escritos en el paso *Append production-extra secrets to .env* del job `deploy` y listados en `ProductionExtraSecretsDeployTests`.
  - Verificación: `ProductionExtraSecretsDeployTests` en el gate; el dueño carga los valores en GitHub (nunca escribirlos en el repo ni en chat). · Origen: [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §5 y §9.1.
- [ ] **Todo `*_CLIENT_SECRET` del compose escrito en `deploy.yml`** (si falta, el M2M se apaga en silencio).
  - Verificación: `ServiceClientSecretsDeployWiringTests`. · Origen: [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §6; [M2M_ServiceAuth](../Investigaciones/M2M_ServiceAuth.md) §6.
- [ ] **Cliente M2M registrado en `auth-api`** (`ServiceAuth__Clients__<N>__*`) [si el servicio usa M2M].
  - Verificación: bloque `auth-api` del compose. · Origen: [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §9.1.

## 4. Consumers conectados

- [ ] **Cola `<svc>-events` declarada y enlazada a `taxvision-events`** en RabbitMQ.
  - Verificación: consola de RabbitMQ (management) o log de arranque del servicio. · Origen: [Observabilidad](../Investigaciones/Observabilidad.md) §9.3; [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §4.
- [ ] **Ninguna línea "No routes can be determined"** en el log del emisor (evento sin `PublishMessage`, descartado) y ningún "No known handler" para un handler que sí existe (nombre plural o fuera de `Discovery`).
  - Verificación: `sudo docker logs taxvision-<svc>-api 2>&1 | grep -E "No routes can be determined|No known handler"`. · Origen: [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §4 y §10.
- [ ] **Sin mensajes acumulados** en `wolverine_outgoing_envelopes` del emisor ni en `wolverine_dead_letters` del receptor (consultas solo `SELECT`).
  - Verificación: SQL de solo lectura sobre esas tablas. · Origen: [Observabilidad](../Investigaciones/Observabilidad.md) §2.7 y §9.3.
- [ ] [si participa en offboarding] El paso nuevo acusa en "Handover status" (N/N) en una prueba manual.
  - Verificación: CRM `/company/users?handover=<userId>`. · Origen: [Offboarding_Integraciones](../Investigaciones/Offboarding_Integraciones.md) paso 15.

## 5. Proyecciones pobladas

- [ ] **Cada proyección local tiene filas para todos los tenants/usuarios existentes.** Ejemplo real de consulta de solo lectura que recorre todas las bases: `Implementaciones/CommunicationCorrecciones/Proyectos/TenantTimeZones/Evidencias/Despliegue_Proyecciones_Zona.sql` → columna `faltan` = 0 en todas las bases, `en_utc` muestra oficinas que quedaron en `Etc/UTC`, `ultima_lectura_utc` la última reconciliación. Adaptar el nombre de tabla para otras proyecciones (permisos, plan, asignaciones).
  - Verificación: correr el SQL (solo `SELECT`). · Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §6 y §9.
- [ ] **Permisos**: login real + endpoint `[HasPermission]` → 200; usuario sin el permiso → 403 `Authz.PermissionDenied`.
  - Verificación: request real por el Gateway. · Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §10 paso 9.
- [ ] [si vende módulo] Log `Module gate registered: ENFORCING (modules: ...)` o `log-only`; oficina sin el módulo → 403 `Authz.ModuleUnavailable`.
  - Verificación: log de arranque y request real. · Origen: [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §10 paso 10.

## 6. Reconciliación completada

- [ ] **Log de la primera pasada con conteos** (sin "warning" de fuente que no responde); si falló, reintento a los 2 min (`ReconciliationRetry`), no a las 12 h.
  - Verificación: `sudo docker logs taxvision-<svc>-api 2>&1 | grep -i reconcil`. · Origen: [Reconciliation](../Investigaciones/Reconciliation.md) §1.4 y §4.
- [ ] **URLs internas a `http://<servicio>-api:8080`**, nunca `localhost` (si no, la reconciliación nunca llega y solo deja warnings).
  - Verificación: `ComposeInternalBaseUrlWiringTests`, `TenantTimeZoneReconciliationDeployTests` en el gate. · Origen: [Reconciliation](../Investigaciones/Reconciliation.md) §1.5.

## 7. Codegen Static sin Roslyn

- [ ] **Log de arranque con "code generation mode is Static" y "Using pre-generated Wolverine HandlerRegistry".**
  - Verificación: `sudo docker logs --tail 200 taxvision-<svc>-api`. · Origen: [Roslyn_Produccion](../Investigaciones/Roslyn_Produccion.md) §5 y §10 paso 7.
- [ ] **`UseTaxVisionCodeGeneration` en `UseWolverine`, `Internal/Generated` regenerado y `PreGeneratedHandlerCodeTests` verdes** (ninguna prueba vigila que todo `Program.cs` llame la extensión).
  - Verificación: `Program.cs`; gate. · Origen: [Roslyn_Produccion](../Investigaciones/Roslyn_Produccion.md) §6 y §7.
- [ ] **`ASPNETCORE_ENVIRONMENT=Production`** en el contenedor (otro valor cae a Dynamic) y, si se fuerza el modo, la variable con **doble** guion bajo `Wolverine__CodeGeneration__TypeLoadMode`.
  - Verificación: compose del servicio. · Origen: [Roslyn_Produccion](../Investigaciones/Roslyn_Produccion.md) §7.
- [ ] **Memoria estable al primer uso** de cada handler.
  - Verificación: `docker stats` / `~/rss.log` sin saltos. · Origen: [Roslyn_Produccion](../Investigaciones/Roslyn_Produccion.md) §10 paso 7.

## 8. Health no basta: verificación funcional

- [ ] **Petición real por el Gateway** (no solo `/health`): `/health/live` de los servicios no ejecuta checks y `/health/ready` solo mide la base y un TCP a RabbitMQ.
  - Verificación: request autenticada a un endpoint del servicio. · Origen: [Observabilidad](../Investigaciones/Observabilidad.md) §2.4 y §5.
- [ ] **Logs sin `Error` al arrancar** y línea del `TimeZoneSelfCheck` presente.
  - Verificación: `sudo docker logs --tail 200 taxvision-<svc>-api`. · Origen: [Observabilidad](../Investigaciones/Observabilidad.md) §5.
- [ ] [si agrega plantillas] Scribe desplegado antes que Notification; plantilla republicada (por hash) al arrancar Scribe.
  - Verificación: orden del deploy y log de Scribe. · Origen: [Scribe_Templates](../Investigaciones/Scribe_Templates.md) §5.

## 9. Logs y diagnóstico

| Síntoma | Dónde mirar | Origen |
|---|---|---|
| Producción | `sudo docker logs --tail 200 taxvision-<svc>-api`; `sudo docker ps` | [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §9.2 |
| 503 "Fleet is overloaded" | Log del Gateway (`Load shedding activated: p99=...`), luego `Invalid column name` | [Observabilidad](../Investigaciones/Observabilidad.md) §6 y §9.3 |
| Evento que no llega | `wolverine_outgoing_envelopes` del emisor, cola `<svc>-events`, `wolverine_dead_letters` del receptor | [Observabilidad](../Investigaciones/Observabilidad.md) §9.3 |
| Job a mano | `sudo docker exec -it taxvision-<svc>-api ...` (las variables están dentro del contenedor) | [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §9.2 |
| Reencender telemetría apagada | `OpenTelemetry__ExportLogs=true`, `OpenTelemetry__ExportTraces=true`, `Observability__Logging__File=true` | [Observabilidad](../Investigaciones/Observabilidad.md) §2.7 |

- [ ] **No bajar EF a `Information` en producción** salvo diagnóstico puntual (llena los logs).
  - Origen: [Observabilidad](../Investigaciones/Observabilidad.md) §6.

## 10. Rollback

- [ ] **Saber que no hay rollback automatizado** ni imágenes por commit (todo `taxvision/<svc>:dev`): volver atrás = revertir el commit (lo hace el dueño) y redeploy. Una migración aplicada no se revierte sola.
  - Verificación: `.github/workflows/deploy.yml` (sin paso de rollback). · Origen: [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) problema 7.
- [ ] **Palanca de codegen**: `WolverineFx.RuntimeCompilation` sigue en las imágenes; forzar `Wolverine__CodeGeneration__TypeLoadMode=Dynamic` vuelve a compilar en runtime (con el coste de RAM que motivó Static). Solo como medida de emergencia.
  - Verificación: compose del servicio. · Origen: [Roslyn_Produccion](../Investigaciones/Roslyn_Produccion.md) §4.7.
- [ ] [si el servicio enforza un gate por secret] Palanca `${<SVC>_MODULE_GATE_ENFORCE:-false}`; con `"true"` literal no hay palanca sin redeploy.
  - Verificación: compose. · Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) problema 4.

## 11. Runbook

- [ ] **Escribir el runbook del servicio** (no existe un runbook general del backend; solo puntuales como `DEVS/Runbook_Resincronizar_Buzones.md`): cómo verificar los puntos 2-8, qué reiniciar, qué jobs de reconciliación existen y sus intervalos, y las ventanas conocidas (p. ej. qué sigue pasando en nombre de un empleado retirado hasta que el consumer corre).
  - Verificación: documento del servicio revisado por el dueño. · Origen: [Observabilidad](../Investigaciones/Observabilidad.md) §2.7 y problema 5; [Offboarding](../Investigaciones/Offboarding.md) §13.10.

## Decisiones pendientes

- Etiquetar imágenes por SHA y documentar rollback manual ([CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §5).
- Healthcheck final del deploy que detecte servicios caídos ([CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) problema 4).
- Backend de logs/métricas en producción ([Observabilidad](../Investigaciones/Observabilidad.md) §5).
- Subir Wolverine a ≥ 6.30 para validar el código pregenerado al arrancar ([Roslyn_Produccion](../Investigaciones/Roslyn_Produccion.md) §6).
