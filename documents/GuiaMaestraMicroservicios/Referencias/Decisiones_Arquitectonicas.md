# Decisiones arquitectónicas — contradicciones resueltas y decisiones pendientes

> Fecha de corte: 2026-11-15. Acompaña a [GUIA_MAESTRA_CREACION_MICROSERVICIOS_TAXVISION.md](../GUIA_MAESTRA_CREACION_MICROSERVICIOS_TAXVISION.md).
> **Regla de resolución:** manda el código verificado sobre la documentación histórica, la memoria y el libro. Cuando el código no decide (o el código mismo es el riesgo), la cuestión pasa a **Decisiones pendientes** del dueño o del senior.
> "Versión del código" = estado leído en `TaxVsion_BackEnd` a la fecha de corte (WolverineFx 6.14.0, .NET 10.0.300, `deploy.yml` de dos jobs sin pruebas desde 2026-11-13). Rutas relativas a esa raíz.
> Abreviaturas de fuentes: **GCM** `DEVS/Guia_Creacion_Microservicio.md` · **BB2** `DEVS/Guia_BuildingBlocks_Ola2.md` · **IQF** `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md` · **GNE** `documents/Guia_Conectar_Microservicio_a_Notificaciones_Email.md` · **GCS** `documents/Guia_Conectar_Microservicio_a_CloudStorage.md` · **CMP** `documents/architecture/Guia_Cablear_Modulo_Plan_Microservicio.md` · **RBAC** `documents/RBAC_Endurecimiento_Defensa_Senior.md` · **REM** `Reminder/04_Guardrails_Checklist_Y_Verificacion.md` · **CONV** `DomainDrivenDesign/01-Convenciones-Pruebas.md` · **RUB** `DomainDrivenDesign/00-Rubrica.md` · **LIB** libro `Guia-DDD-EDA-NET10-Wolverine-RabbitMQ-Docker.pdf` · **MEM** memoria del proyecto · **AUD** `docs/audit/*`.

---

## 1. Contradicciones encontradas y cómo se resolvieron

| # | Tema | Fuente(s) que dicen otra cosa | Lo que dice el código (versión de corte) | Conclusión adoptada en la guía |
|---|---|---|---|---|
| C-01 | Orden guardar/publicar | GCM §3.3 (Tasks publica **antes** de `SaveChangesAsync` "en la misma transacción") | BB-14; `SaveBeforePublishTests`; `CreateNoteCommand.cs:73-88` guarda y después publica | **Guardar antes de publicar** (y en jobs, `TransactionalJobUnit`). GCM §3.3 incorrecta. |
| C-02 | Quién aplica migraciones | Comentario `deploy/docker/docker-compose.yml:160-163` ("como los servicios migran al arrancar"); MEM `feedback_wolverine_transaccion_antes_del_handler.md`; LIB p. 22 (`ApplyMigrationsAsync`) | No hay `Database.Migrate`/`MigrateAsync`/`EnsureCreated` en `src/`; contenedor `migrations` (`apply-migrations.sh`) en `deploy.yml:766-767`; local `scripts/migrate-local.ps1` | **Migra el contenedor `migrations` del deploy**; en local `migrate-local.ps1`; al arrancar solo Wolverine crea sus tablas. LIB p. 65 coincide con el código. |
| C-03 | Cuántos servicios migra el deploy | GCM:1122 ("19") | 25 bloques + FinancialInsights condicional | 25 + FI condicional; alta en 4 lugares. |
| C-04 | Exchange por destino | MEM `feedback_fanout_rabbit_sin_dueno…` (primera parte: exchange dedicado `taxvision-<dominio>`); LIB p. 37 (exchange por servicio) | 294 rutas a `taxvision-events`; excepciones `taxvision-catalog` y `taxvision-catalog-items`; corrección del dueño (2026-10-21): publicar siempre a `taxvision-events` | **Servicios nuevos publican a `taxvision-events`**; los dedicados son excepción existente; uno nuevo requiere aprobación. |
| C-05 | Estilo de comentarios | IQF §7.3 ("estilo obligatorio": citar el bug, listar callers, referenciar archivos) | Guardrail #36 y `AGENTS.md` (cortos, en español, sin historia) | **Guardrail #36 + AGENTS.md.** Para `IgnoreQueryFilters()`, comentario breve que diga cuál de las 4 condiciones aplica. |
| C-06 | Filtro de tenant sin tenant | Comentario `src/BuildingBlocks/Tenancy/ITenantContext.cs:3-8` ("degradar a sin filtro") | 26 DbContext fail-closed (`EffectiveTenantId` → `Guid.Empty`) | **Fail-closed.** El comentario es deuda documental. |
| C-07 | Contador del gate de módulo | GCM:904 y CMP (`The_thirteen_…`, 13); nombre de la prueba `The_fourteen_gated_services_are_still_wired` (14); `ModuleGateDeployWiringTests` ("14 .NET + Communication") | `ModuleGateWiringFitnessTests.cs:120` → `Assert.Equal(15, …)` (15 `Program.cs` con `AddModuleGate`, incluido FinancialInsights) | **La fuente del conteo es la prueba de fitness: 15.** Un servicio nuevo con gate la sube a 16 en el mismo cambio. |
| C-08 | Numeración de guardrails | GCM §12 (38, renumerados desde el #32) | Original de 36 (Apéndice A de `Signature/AUDITORIA_SIGNATURE_FASE0.md`); comentarios de código citan la numeración original ("guardrail 10") | **Numeración original de 36** + complementarios con prefijo (GC-xx). |
| C-09 | UTC en todos los DbContext | REM:117 ("cada DbContext llama `UseUtcDateTimes()`… lo prueba `UtcDateTimesTests` en cada servicio") | 23/26; Calendar, Signature y Tasks con conversor propio y sin la prueba | Regla vigente para servicios nuevos: `UseUtcDateTimes()` + `UtcDateTimesTests`. Los 3 → decisión pendiente D-28. |
| C-10 | Republicación de plantillas Scribe | MEM `feedback_scribe_subir_version_plantilla.md`; GNE Paso 4a ("si el TemplateKey existe, lo salta"; hay que subir `ContentVersion`) | `ScribeNotificationTemplateSeeder.cs:241` republica por `ContentHash`; prueba `Changed_html_without_bumping_content_version_is_republished` | **Se republica por hash de contenido**; `ContentVersion` es etiqueta (se sigue subiendo por convención). |
| C-11 | EventKey de Scribe | GNE ("el `event_key` debe ser exactamente el mismo string que `[MessageIdentity]`") | `OfficeWispReviewDueIntegrationEvent` con identidad CLR y EventKey `tenant.wisp_review_due.v1` | **EventKey ≠ MessageIdentity.** Deben coincidir constante del consumer, seed y mapping. |
| C-12 | Ruta de revocación de sesión | RBAC §6.7 (`POST /auth/sessions/{sid}/revoke`) | `SessionsController.cs:55` `DELETE auth/sessions/{sessionId}`; `:77` `DELETE auth/sessions` | **`DELETE auth/sessions/{id}`.** |
| C-13 | Recomposición de permisos | RBAC §6.5 paso 3; cabecera de `CloudStorage…/PermissionsProjectionConsumers.cs:10-15` ("union-recompute en `RolePermissionsChanged`") | `RolePermissionsFanOut.cs:9-122` (Auth publica un `UserRolesChanged` por titular); consumers de CloudStorage y FI no recomponen | **No recomponer**; solo `UserRolesChanged` con códigos efectivos (recomponer resucitaba denies). |
| C-14 | Regla de versión en proyecciones | RBAC §6.5 ("no-op si version <= actual") | `ApplyIfNewer`: `version < actual → return` (igual se re-aplica) | Regla única: **descartar menor, re-aplicar igual** (idempotente). |
| C-15 | Orden de `JwtTenantContextMiddleware` | RBAC §6.6 (antes de `UseAuthentication`) | FI `Program.cs:148-151`: después de `UseAuthentication`, antes de denylist y `UseAuthorization` | **Después de autenticación.** |
| C-16 | Selección de la fuente de permisos | RBAC §6.5 paso 5 (`if/else` manual) | `AddUserPermissionsSource` revienta al arrancar si hay `[HasPermission]` sin `Projection` | **`AddUserPermissionsSource`** y modo `Projection` obligatorio. |
| C-17 | Pull-recovery | GCM:679 ("solo en el servicio que lo necesita; Notes único"); comentario de Notes `DependencyInjection.cs:108-113` | 16 servicios registran `IPermissionsSnapshotClient` | Práctica por defecto para todo servicio con humanos (obligatoriedad formal: D-04). |
| C-18 | CI y gate de pruebas | GCM §9.1 (job único con gate .NET y Node, Node 22); informe RAM ("guardarraíl en el gate del CI"); REM:97-98 (`-m:1` y CI en paralelo) | `deploy.yml`: dos jobs (`stage`/`deploy`), **sin `dotnet test`** desde 2026-11-13 (decisión del dueño); Node 24.22.2 | **Gate local exacto obligatorio** + `.Integration.` + CSharpier; el CI solo despliega. |
| C-19 | Environment de secrets | GCM:1184,1208 (al environment `production`) | `production` en el tope de 100; nuevos en `production-extra` leídos por el job `deploy` (`ProductionExtraSecretsDeployTests`) | **Secrets nuevos en `production-extra`** + línea en el job `deploy` + prueba. |
| C-20 | `restart` en compose | GCM:1118 ("ningún servicio declara `restart`") | 42 entradas `restart:`; `ComposeRestartPolicyWiringTests` | `restart: unless-stopped` en servicios largos. |
| C-21 | Número de servicios Node | GCM:32 ("2 servicios Node"); README de Communication ("único servicio Node, Node ≥ 20.11") | 3 servicios Node; `engines.node >= 24.0.0`; imagen `node:24.22.2-bookworm-slim` | 3 servicios Node, Node 24. README → D-60. |
| C-22 | Transporte RabbitMQ | GCM §3.1 y LIB p. 37 (`UseRabbitMq(uri).AutoProvision()`) | `UseTaxVisionRabbitMq` en 27 Program.cs (confirmaciones del publicador + acuse de offboarding) | **`UseTaxVisionRabbitMq`.** |
| C-23 | Integración EF de Wolverine | LIB p. 37 (`AddDbContextWithWolverineIntegration` + `PublishDomainEventsFromEntityFrameworkCore`); RUB ("nadie lo usa") | BB-1: `AddDbContext` normal + `UseTaxVisionDomainEvents()` (17 servicios); con 6.14 la integración no deja el evento en el inbox durable | **BB-1.** |
| C-24 | Colas locales durables | RUB ("ningún `Program.cs` activa `UseDurableLocalQueues`") | `UseTaxVisionDurableLocalQueues()` en 19 | Recomendada para servicios nuevos (obligatoriedad: D-02). |
| C-25 | Alias de eventos | GCM §3.2 (alias `.v1` en el docstring) | BB-4: `[MessageIdentity]` obligatorio; 54 con `.v1`, 285 con nombre CLR | **Atributo `[MessageIdentity]`**; contratos nuevos `.v1` (convención formal: D-01). |
| C-26 | Mapa CLR→alias en Node | GCM §3.5 (siempre `CLR_TYPE_TO_EVENT_TYPE`) | Eventos con alias llegan con el alias en `type`; solo los legacy necesitan la tabla | Tabla solo para identidades CLR. |
| C-27 | Redis | GCM §6.1 (`AddSingleton<IConnectionMultiplexer>(…Connect…)`) | `AddTaxVisionRedis(...)` (`AbortOnConnectFail=false`, `FailFast`) | **`AddTaxVisionRedis`.** |
| C-28 | Ubicación de OTel | GCM:64 (`BuildingBlocks.Infrastructure` = "Redis, HTTP readers, OTel") | `BuildingBlocks.Web/Observability/OpenTelemetryRegistration.cs` | OTel en `BuildingBlocks.Web`. |
| C-29 | Uso de `BuildingBlocks.Jobs` | BB2:73 ("ningún servicio lo referencia todavía") | 12 servicios | API vigente; estado histórico. |
| C-30 | Suite verde solo en serie | GCM §2.7 y guardrail #32 de GCM (`-m:1`) | Gate con filtro `!~.Integration.&!~.Persistence.`; T-INT con bases temporales (BB-10) | Histórico; regla vigente = filtro + `.Integration.`. |
| C-31 | Proyecto de pruebas de Wallet | CONV ("Wallet no tiene proyecto de pruebas"); `deploy/tests/README.md` (6 proyectos) | `deploy/tests/TaxVision.Wallet.Tests` (46 archivos) en `.slnx`; 30 proyectos de prueba | Descartado. |
| C-32 | Base de las T-INT | CONV:11 ("tenant propio y limpieza en `DisposeAsync`" sobre la base local) | `TemporaryTestDatabase` (BB-10) en 22 proyectos | **BB-10.** |
| C-33 | Enums de CloudStorage | GCM §7 (corrige a GCS) | GCS §5 al día con `FileEnums.cs`; GCM desfasada | Remitir a GCS. |
| C-34 | Modelo de catálogo de módulos | CMP:32-33,65-70 (`PlanModuleCatalog`, `ModuleAddOnCatalog`, `MinPlanTier`); MEM F1 (`PlanModule` de Auth vigente) | Tabla `Modules` de Subscription; `MinPlanTier` eliminado (`DropPermissionMinPlanTier`); `PlanModules` de Auth borrada (`RemoveDuplicatePlanModulesCatalog`) | **Modelo de Subscription + revisiones de catálogo.** CMP superada. |
| C-35 | Precios y módulos | `Subscriptions/Guia_Facturacion_AddOns.md` §9 (Starter $49/Pro $129/Enterprise $299; módulos reports/marketing/builder/irs/miles) | R2 `2026-11-11-r2-single-enterprise` ($39/$69/$99; solo Enterprise a la venta); 10 módulos | Precios de las revisiones; el seeder **no** es fuente de precios. |
| C-36 | Rate limit por plan | ADR-017 §2.1 (sin implementación de `IPlanRateLimitReader`) | `HttpPlanRateLimitReader` compartido | Citar el código. |
| C-37 | `EnforceTierQuotas` por defecto | `postman/README.md:101-105` ("OFF por default") | Compose con `"true"` en varios servicios | Citar compose; patrón único → D-26. |
| C-38 | Prefijos del gateway | `postman/README.md:109-110` (`/subscription`, `/tenant`) | `/subscriptions`, `/plans`, `/tenants`… | Prefijos del `appsettings.json` del Gateway. |
| C-39 | Flag de push FCM | Comentario `deploy.yml:419-420` (default `false`); auditoría de notificaciones ("FASE 9 Push diferida") | Compose `${NOTIFICATION_USE_FCM_PUSH:-true}`; push implementado | Push implementado; flag por compose (aclaración → D-41). |
| C-40 | Gate de preferencias y email | MEM `feedback_gate_de_preferencias_no_cubre_email.md` ("el correo esquiva el gate") | El gateway lo aplica si vienen `RecipientUserId` + `Category` | Aplicado pero **opt-in**: pasarlos siempre para staff (obligatoriedad → D-38). |
| C-41 | Idempotencia de correo | MEM `feedback_email_idempotency_key_must_include_recipient.md` (bug de fan-out) | Corregido: chequeo y clave incluyen `To` | Corregido; no pasar `IdempotencyKey` propio sin destinatario. |
| C-42 | Modo de codegen | MEM `project_a6_module_gate_estado_medido.md`, `feedback_codegen_dynamic_oculta_di_roto…` ("la codegen es Dynamic") | Static en Production (`WolverineCodeGenerationExtensions.cs:43`); flota local en Static | **Static obligatorio**; la lección de DI roto vale en Development. |
| C-43 | `codegen write` | LIB §10.3 (`dotnet run -- codegen write`) | Los servicios terminan en `app.Run()`; MEM `feedback_no_codegen_preview_arranca_servicio.md` | Regenerar **solo** con `scripts/codegen/regenerate-wolverine-code.ps1`. |
| C-44 | Sockets tras el handshake | Vault `INV-09_Sesiones_Seguridad.md` (nada re-validaba el socket) | `session-handlers.ts` cierra al `exp` y expulsa por Pub/Sub | Corregido. |
| C-45 | Consumers de `TenantSubdomainChanged` | MEM 2026-10-13 ("cero consumers", `InvalidateAsync` sin llamadas) | 7 consumers; Change/Disable invalidan | Corregido. |
| C-46 | Dónde corre `cloudflared` | GCM:39 ("vm-frontend, remotely-managed") | Caddyfile:15-18 ("el túnel entrega a `http://localhost:80`") | Sin resolución en código → **D-52**. |
| C-47 | Split SPA/API | GCM:40 ("app.* + API a.*") | Caddyfile sin `a.*` | Citar el Caddyfile. |
| C-48 | Comentario de servicio "profile-gated" | `apply-migrations.sh:169-170` | `financial-insights-api` sin `profiles` en compose | Comentario desfasado; la migración sigue condicional. |
| C-49 | Communication fuera de start-fleet | `DevClientPortDefaultsTests.cs:22-23` | `start-fleet.ps1:103-108` sí lo arranca | Comentario desfasado. |
| C-50 | Dominios | Documentación vieja (`taxprocore.com`, `taxproffice.com`) | Todo derivado de `TAXVISION_BASE_DOMAIN`/`TAXVISION_DOMAIN`; default `taxoffix.io` | Variables; sin hosts literales. |
| C-51 | Application y Wolverine | LIB p. 8, 20 (Application no referencia Wolverine) | Application referencia `WolverineFx` (`IMessageBus`) | **Desvío aceptado** (RUB). |
| C-52 | Excepciones de negocio | LIB p. 42-43 (`GlobalExceptionHandler`) | `Result`/`Error` + `ErrorHttpMapping` | **Desvío aceptado** (RUB). |
| C-53 | Credenciales en `appsettings.Development.json` | LIB p. 43 | User-secrets en local; `.env` generado por `deploy.yml` | **Inseguro: descartado.** |
| C-54 | JWT HS256 | GCM §10.1 (`Jwt:Secret`; "el secret debe coincidir entre Auth, Tenant y Gateway") | RS256 con `Jwt__PublicKeyPath` en compose; HS256 solo como alternativa de código | RS256 por defecto; fallback silencioso → **D-08**. |
| C-55 | Cifras de alcance | IQF ("7 microservicios", "1963/1963 tests"); RBAC §3 ("13/14 servicios"); AUD 01 ("18 áreas", SQL/Loki/Tempo en compose) | 26 servicios con filtro y denylist; SQL externo; Loki/Tempo comentados | Cifras históricas. |
| C-56 | Reconciliación con reintento | Encargo de la investigación ("usar `ReconciliationRetry`") | Solo 5 jobs lo usan (`ReconciliationRetryTests`); ≥ 10 sin reintento corto | Regla para servicios nuevos: `ReconciliationRetry` obligatorio. Los existentes → **D-18**. |
| C-57 | Pre-flight de Tenant | Retiro exige `TenantAdmin`/`PlatformAdmin` | `TenantOffboardingImpactController.cs:21-23` admite `TenantEmployee` con `users.manage` | Sin resolución → **D-32**. |
| C-58 | Wallet: módulo o transversal | Backend: `PermissionModuleMap.cs:68` y gate en compose | CRM `features.ts`: transversal (`module: null`) | Sin resolución → **D-23**. |

---

<a id="decisiones-pendientes"></a>
## 2. Decisiones pendientes

Columnas: **Riesgo** = efecto si no se decide (Alto / Medio / Bajo, estimado por la investigación) · **Origen** = investigación con la evidencia. Ninguna se implementó como parte de esta guía.

### 2.1 Seguridad (a confirmar con prioridad)

| ID | Decisión pendiente | Riesgo | Origen |
|---|---|---|---|
| D-SEC-01 | **`ServiceOnly` sin allowlist de clientes/scopes**: cualquier secreto M2M filtrado abre todos los `internal/*`; Auth emite token de **cualquier tenant** (incluido PlatformTenant) a cualquier cliente registrado; endpoints internos (`permissions-snapshot`, `entitlements`, `tenants/directory`) aceptan cualquier token de servicio para cualquier tenant (los dos primeros usan el `tenantId` de la ruta). ¿Allowlist por `client_id`/scope y PlatformTenant obligatorio en endpoints cross-tenant? | Medio | [M2M_ServiceAuth](../Investigaciones/M2M_ServiceAuth.md) §3.1-3.3; [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §3.6; [Reconciliation](../Investigaciones/Reconciliation.md) §3.6; AUD SEC-001 · Plan: [M2M_Autorizacion_Por_Cliente_PLAN](../../Seguridad/M2M_Autorizacion_Por_Cliente_PLAN.md) |
| D-SEC-02 | **Denylist fail-open en .NET** si cae Redis (default implícito, sin config en ningún sitio) frente a Communication **fail-closed**. ¿Una sola política, `FailClosed` en servicios con datos sensibles (Customer, CloudStorage, Signature), y `FailureMode` explícito en compose? (ventana V1 del offboarding) | Medio | [Session_Denylist](../Investigaciones/Session_Denylist.md) §3.1; [Offboarding](../Investigaciones/Offboarding.md) V1/P-5 |
| D-SEC-03 | **Fallback silencioso a HS256** sin clave pública: un servicio desplegado sin `Jwt__PublicKeyPath` pero con `Jwt:Secret` aceptaría tokens firmados con el secreto compartido. No hay prueba de compose que exija el PEM. ¿Fitness de despliegue o quitar el modo dual fuera de pruebas? | Medio | [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §3.9 |
| D-SEC-04 | **Reconciliación de entitlements que no repara consumidores** (`OnlyIfChanged`): un servicio nuevo con `TenantPlanCodeProjection` no tiene fila para los tenants existentes → **gate abierto (fail-open)** y cuota base hasta que el plan cambie. ¿Re-publicación forzada en Subscription o pull obligatorio en cada consumidor? | Alto (comercial) | [Proyecciones](../Investigaciones/Proyecciones.md) §3.3; [Reconciliation](../Investigaciones/Reconciliation.md) §3.5; [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §3.7 |
| D-SEC-05 | **~10 jobs de reconciliación sin reintento corto** (FI clientes/nombres/entitlements, Customer empleados, Notification destinatarios y directorio, Correspondence/Signature clientes, Sms política de contacto, Calendar nombres): si corren antes que su fuente al desplegar, se repite el incidente de 12 h. ¿Adoptar `ReconciliationRetry` y ampliar `ReconciliationRetryTests`? | Medio | [Reconciliation](../Investigaciones/Reconciliation.md) §3.1-3.2 |
| D-SEC-06 | **Healthcheck del deploy no detecta servicios caídos**: `/health/ready` del gateway no tiene checks `ready` (GW-06) y es lo único que consulta `deploy.yml`. ¿Healthcheck por servicio en el deploy o en compose? | Medio | [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §3.4; [Observabilidad](../Investigaciones/Observabilidad.md) §2.4; [Docker_Deploy](../Investigaciones/Docker_Deploy.md) §3.2 |
| D-SEC-07 | **Ninguna prueba exige codegen Static en un `Program.cs` nuevo** (ni que exista su `PreGeneratedHandlerCodeTests` ni que esté en `$all` del script): un servicio nuevo sin la línea volvería a Dynamic en producción (incidente de RAM). ¿Fitness transversal? | Alto | [Roslyn_Produccion](../Investigaciones/Roslyn_Produccion.md) §6-7 |
| D-SEC-08 | **Ninguna prueba exige el override de destino por cluster en compose**: un cluster nuevo sin `ReverseProxy__Clusters__…__Address` apuntaría a `localhost` dentro del contenedor (502 en producción). ¿Fitness que compare `appsettings.json` del Gateway con el compose? | Medio | [Gateway_YARP](../Investigaciones/Gateway_YARP.md) §3.1 |
| D-SEC-09 | **Pruebas InMemory dentro de `.Persistence.` que nunca corren** (50 de 65 archivos, incluidas las 13 `TenantIsolationTests`): el aislamiento de tenant no está en el gate. ¿Moverlas o cambiar el filtro? | Medio | [Testing_Unit](../Investigaciones/Testing_Unit.md) §3.1; [Testing_Integration](../Investigaciones/Testing_Integration.md) §3.1; [Testing_Security](../Investigaciones/Testing_Security.md) §3.1 |
| D-SEC-10 | **Push sin revocación al cerrar sesión**: solo `UserDeactivated`/`UserOffboarded` revocan dispositivos; el mismo token puede quedar activo en dos oficinas (reuso por tenant). ¿Evento de logout de Auth y política de token compartido? | Medio (datos de clientes en un teléfono) | [Notification_InApp_Push](../Investigaciones/Notification_InApp_Push.md) §3.3 |
| D-SEC-11 | **SMS sin estado real de entrega** en Notification (marca "Sent" al publicar; supresión por STOP o E.164 inválido no vuelve). ¿Cerrar el ciclo Sms → Notification? | Bajo-Medio | [Notification_SMS](../Investigaciones/Notification_SMS.md) §3.1-3.2 |
| D-SEC-12 | **Ventanas de offboarding V1–V5**: V1 denylist fail-open; V2 Pub/Sub perdido; V3 acciones en nombre del retirado mientras un consumer está caído; **V4 inbox de Node marcado antes del trabajo**; **V5 `ApplyIfNewer` reactiva proyecciones y Auth acepta cambios de roles sobre un retirado**. ¿Corregir V4/V5 y escalar el aviso de incompleto? | Medio | [Offboarding](../Investigaciones/Offboarding.md) §3.3, §7; [Offboarding_Integraciones](../Investigaciones/Offboarding_Integraciones.md) §4 |
| D-SEC-13 | **Imágenes .NET de API corriendo como root** (solo ContentScanner y Node declaran `USER`); ninguna prueba lo exige. ¿`USER` no root + prueba? | Medio | [Docker_Deploy](../Investigaciones/Docker_Deploy.md) §3.1 |
| D-SEC-14 | Render de Scribe confía en el `TenantId` del cuerpo: cualquier servicio con `scribe.render` puede pedir el render de otra oficina. ¿Aceptado o restringido por tenant del token? | Bajo | [Scribe_Templates](../Investigaciones/Scribe_Templates.md) §3.6 |
| D-SEC-15 | Fail-open del guard de host del Gateway si Auth no responde (JWT de A pasa por el host de B). ¿Cerrar para rutas sensibles? | Bajo (datos aislados por JWT) | [Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §3.1 |
| D-SEC-16 | PII en URLs: `?email=` en links de share; tokens de cuenta en query. ¿Mover a fragmento/cuerpo? | Medio | [Tenant_Links](../Investigaciones/Tenant_Links.md) §3.3 |
| D-SEC-17 | `Email` del empleado viaja en `UserOffboardedIntegrationEvent` y queda en outbox/inbox de todos los servicios. | Bajo-Medio | [Offboarding](../Investigaciones/Offboarding.md) P-7 |
| D-SEC-18 | Clave de firma JWT única sin rotación (un solo `kid`): rotar exige corte coordinado. | Medio (operacional) | [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §1.2, §6 |
| D-SEC-19 | Tokens M2M no revocables (sin `sid`): el corte es rotar el secreto en Auth y en el llamador a la vez. | Medio | [M2M_ServiceAuth](../Investigaciones/M2M_ServiceAuth.md) §3.6 |
| D-SEC-20 | `ProjectionPermissionsSource` no conoce `IsActive`; depende de que cada lector filtre inactivos (verificado solo en FI). | Medio | [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §3.5; [Proyecciones](../Investigaciones/Proyecciones.md) §3.5 |

### 2.2 Arquitectura, mensajería y datos

| ID | Decisión pendiente | Riesgo | Origen |
|---|---|---|---|
| D-01 | Convención de `[MessageIdentity]` para eventos nuevos (`.v1` como hacen los recientes) escrita como regla. | Bajo | [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §3.2 |
| D-02 | `UseTaxVisionDomainEvents()` (BB-1) y `UseTaxVisionDurableLocalQueues()` (BB-2) obligatorios para servicios nuevos (hoy 17 y 19 de 26). Sin BB-1, cada camino de una transición publica a mano y un camino nuevo lo puede olvidar (checkout de Subscription). | Bajo | [BuildingBlocks](../Investigaciones/BuildingBlocks.md); [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §2 y §6 |
| D-03 | Regla escrita de exchanges dedicados ("siempre `taxvision-events`" vs "dedicado solo si el consumidor único ya declara el binding"). | Bajo | [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §3.1 |
| D-04 | Pull-recovery de permisos obligatorio en todo servicio con humanos. | Medio | [02_COMPARATIVA_GUIAS](../02_COMPARATIVA_GUIAS.md) fila 28 |
| D-05 | 8 eventos de PaymentClient sin ruta ni consumidor (`KnownUnroutedPendingOwner`). | Bajo | [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §3.4 |
| D-06 | Prueba de rutas en runtime (estilo Calendar) obligatoria por servicio (la de texto no ve eventos en cascada). | Medio | [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §3.3; [Testing_EDA](../Investigaciones/Testing_EDA.md) §3.1 |
| D-07 | Si basta la regla global `SaveBeforePublishTests` o cada servicio debe heredarla (hoy 4). | Bajo | [Testing_EDA](../Investigaciones/Testing_EDA.md) §3.3 |
| D-08 | Ver D-SEC-03 (HS256). | — | — |
| D-09 | Códigos de error nuevos: registro central en `ErrorHttpMapping` (hoy) vs convención por sufijo (cambiaría comportamiento de BuildingBlocks). | Bajo | [Arquitectura](../Investigaciones/Arquitectura.md) |
| D-10 | Idioma de `Error.Message` cuando llega a la UI (`NoteErrors` en español; regla "copy de UI en inglés"). | Bajo | [03_INVENTARIO_ARQUITECTURA](../03_INVENTARIO_ARQUITECTURA.md) §5 |
| D-11 | Contratos de integración nuevos: un `record` por archivo o agrupados (guardrail #6). | Bajo | [Guardrails](Guardrails.md) #6 |
| D-12 | Mantener la numeración original de guardrails (evita romper referencias como "guardrail 10"). | Bajo | [Guardrails](Guardrails.md) §Riesgos |
| D-13 | Gestión central de versiones (`Directory.Packages.props`); divergencia Fluid.Core 2.14.0 vs 2.31.0. | Bajo | [03_INVENTARIO_ARQUITECTURA](../03_INVENTARIO_ARQUITECTURA.md) §8 |
| D-14 | Corregir el comentario del compose y la memoria "los servicios migran al arrancar". | Bajo | [Migraciones](../Investigaciones/Migraciones.md) |
| D-15 | Re-publicación forzada de entitlements en Subscription (ver D-SEC-04). | — | — |
| D-16 | Unificar semántica de paginación (`PagedResult` con total) en endpoints de reconciliación. | Bajo | [Reconciliation](../Investigaciones/Reconciliation.md) §3.7 |
| D-17 | Defaults de URL a `localhost` en código de producción (`TenantBaseUrl`, `CustomerBaseUrl`): ¿solo en Development? | Medio | [Reconciliation](../Investigaciones/Reconciliation.md) §3.3 |
| D-18 | Ver D-SEC-05 (reintento corto en jobs existentes). | — | — |
| D-19 | Carrera posible en la reconciliación de zona (inferida, no reproducida). | Bajo | [Reconciliation](../Investigaciones/Reconciliation.md) §3.4 |
| D-20 | Claves de configuración M2M heterogéneas (`ServiceAuthClient:*` vs `{Servicio}:ServiceAuth:*`). | Bajo | [M2M_ServiceAuth](../Investigaciones/M2M_ServiceAuth.md) §3.4 |

### 2.3 Comercialización y límites

| ID | Decisión pendiente | Riesgo | Origen |
|---|---|---|---|
| D-21 | Modo inicial del gate para módulos nuevos: `"true"` literal (FinancialInsights) vs secret `${VAR:-false}` (los otros 14); dejar escrito si lo de FI es intencional. | Medio | [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §3.4; [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §3.4 |
| D-22 | Oficinas reales sin suscripción con el gate en enforce (fail-open sin proyección). | Medio | [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §3.7 |
| D-23 | `wallet`: módulo (backend) o transversal (CRM). | Bajo | [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §3.5 |
| D-24 | A6: casos 2/3/8 del Anexo C (tenant Starter en vivo) y `call.record`/`meeting.record` fuera de bundles. | Bajo | [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §3.8 |
| D-25 | `TenantAdminCommunicationExtras` como excepción en el bundle del admin. | Bajo | [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §3.10 |
| D-26 | Patrón único para `RateLimit:EnforceTierQuotas` en compose. | Bajo | [RateLimit_Cuotas_Gates](../Investigaciones/RateLimit_Cuotas_Gates.md) §3.3 |
| D-27 | Marcar formalmente como superada `Guia_Cablear_Modulo_Plan_Microservicio.md`. | Bajo | [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §6 |

### 2.4 Persistencia, zonas y offboarding

| ID | Decisión pendiente | Riesgo | Origen |
|---|---|---|---|
| D-28 | Alinear Calendar, Signature y Tasks a `UseUtcDateTimes()` + `UtcDateTimesTests`. | Bajo | [Migraciones](../Investigaciones/Migraciones.md) |
| D-29 | Re-ejecución segura del handover ("republicar el retiro"). | Medio | [Offboarding](../Investigaciones/Offboarding.md) P-3 |
| D-30 | Escalado del aviso de offboarding incompleto (hoy uno solo). | Bajo | [Offboarding](../Investigaciones/Offboarding.md) P-5 |
| D-31 | Signature D-2: si el documento firmado muestra nombre/PTIN del preparador (criterio contable). | Bajo | [Offboarding_Integraciones](../Investigaciones/Offboarding_Integraciones.md) |
| D-32 | Actores del pre-flight de Tenant (admite `TenantEmployee` con `users.manage`). | Bajo | [Offboarding_Integraciones](../Investigaciones/Offboarding_Integraciones.md) §6 |
| D-33 | Migraciones del retiro desplegadas en producción (memoria dice "falta desplegar"). | Medio | [Offboarding](../Investigaciones/Offboarding.md) §11 |
| D-34 | Respuesta HTTP ante dos retiros simultáneos del mismo usuario (índice único lo protege, sin prueba). | Bajo | [Offboarding](../Investigaciones/Offboarding.md) §4.2 |

### 2.5 Tenant, enlaces y notificaciones

| ID | Decisión pendiente | Riesgo | Origen |
|---|---|---|---|
| D-35 | Soporte de custom hostnames en Gateway/CORS si se van a vender. | Bajo | [Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §3.2 |
| D-36 | Pieza BB compartida para validación Host↔token en rutas públicas (hoy dos patrones). | Bajo | [Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §3.5 |
| D-37 | Qué hacer si no hay host al armar un link (enviar con base fija, reintentar o fallar) y si `client.{dominio}` sigue siendo destino válido. | Medio | [Tenant_Links](../Investigaciones/Tenant_Links.md) §3.1-3.2 |
| D-38 | Gate de email obligatorio (no opt-in) para destinatarios staff. | Bajo-Medio | [Notification_Email](../Investigaciones/Notification_Email.md) §3.1 |
| D-39 | Override de oficina sin versión publicada: ¿cae a la plantilla de sistema? | Bajo | [Scribe_Templates](../Investigaciones/Scribe_Templates.md) §3.3 |
| D-40 | Estrategia de idioma para plantillas al cliente (mapping por locale vs ramas). | Bajo | [Scribe_Templates](../Investigaciones/Scribe_Templates.md) §3.5 |
| D-41 | Aclarar el default real de `NOTIFICATION_USE_FCM_PUSH` (compose `true` vs comentario de `deploy.yml`). | Bajo | [Notification_InApp_Push](../Investigaciones/Notification_InApp_Push.md) §3.4 |
| D-42 | Unificar la fuente in-app (retirar handlers Node legacy tras E2E por grupo). | Bajo | [Notification_InApp_Push](../Investigaciones/Notification_InApp_Push.md) §3.1 |
| D-43 | Ventana horaria y consentimiento (TCPA/10DLC) para SMS transaccional. | Medio (regulatorio) | [Notification_SMS](../Investigaciones/Notification_SMS.md) §5 |
| D-44 | Identidad de remitente SMS por oficina (hoy proveedor global). | Bajo | [Notification_SMS](../Investigaciones/Notification_SMS.md) §3.5 |
| D-45 | Calendar cachea el host 30 min sin invalidación por rename. | Bajo | [Tenant_Links](../Investigaciones/Tenant_Links.md) §3.4 |
| D-46 | Preferencias de notificación sin pantalla (endpoint dormido). | Bajo | [Notification_Email](../Investigaciones/Notification_Email.md) §3.5 |

### 2.6 Operación, despliegue y pruebas

| ID | Decisión pendiente | Riesgo | Origen |
|---|---|---|---|
| D-47 | Etiquetado de imágenes por SHA y rollback documentado (hoy todo `:dev`). | Medio | [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §3.7 |
| D-48 | Confirmar en GitHub el nivel de cada secret (223 referencias en `stage` frente al tope de 100) y dónde vive `TAXVISION_DOMAIN` (usado en el job `deploy`). | Medio | [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §3.2-3.3 |
| D-49 | Quitar o justificar el volumen `taxvision-logs` (sink de archivo apagado en contenedor). | Bajo | [Docker_Deploy](../Investigaciones/Docker_Deploy.md) §3.3 |
| D-50 | Fijar versión de `minio/mc` (hoy `latest`) y de dependencias Node con rango. | Bajo | [Docker_Deploy](../Investigaciones/Docker_Deploy.md) §3.6 |
| D-51 | `ActivityTimeout` por cluster: regla escrita para servicios con streaming o archivos. | Bajo | [Gateway_YARP](../Investigaciones/Gateway_YARP.md) §5 |
| D-52 | Cloudflare: plantilla/canal de solicitud al senior, dónde corre `cloudflared`, existencia de reglas WAF/caché por path. | Medio | [Cloudflare](../Investigaciones/Cloudflare.md) §3.1-3.3 |
| D-53 | Igualar `restart-down.ps1` con `start-fleet.ps1` (Static y espera de Scribe) y una sola tabla de flota. | Bajo | [Scripts_Locales](../Investigaciones/Scripts_Locales.md) §3.1-3.3 |
| D-54 | Postman: una sola convención (`Postman_Collection/` mayoritaria) y un solo generador OpenAPI (hoy OpenApi + Swashbuckle). | Bajo | [Postman](../Investigaciones/Postman.md) §3.1, §3.6 |
| D-55 | Encender Prometheus/Grafana o un backend de logs/trazas cuando la RAM lo permita. | Medio | [Observabilidad](../Investigaciones/Observabilidad.md) §3.1-3.2 |
| D-56 | Subir Wolverine a ≥ 6.30 (valida el código pregenerado al arrancar), quitar `WolverineFx.RuntimeCompilation` de producción, límites de memoria por contenedor, reencender OTLP/archivo de logs. | Medio | [Roslyn_Produccion](../Investigaciones/Roslyn_Produccion.md) §6 |
| D-57 | 5 proyectos con `.Integration.` sin `TemporaryTestDatabase` (Billing, Catalog, ContentScanner, Growth, Postmaster): revisar que no escriban en la base de la flota. | Medio | [Testing_Integration](../Investigaciones/Testing_Integration.md) §3.3 |
| D-58 | Prueba de aislamiento de tenant por HTTP de punta a punta (token de A, recurso de B → 404). | Medio | [Testing_Security](../Investigaciones/Testing_Security.md) §3.3 |
| D-59 | Comentarios con números de fase en pruebas y `-m:1` (histórico). | Bajo | [Testing_Unit](../Investigaciones/Testing_Unit.md) §3.6-3.7 |
| D-60 | Actualizar `src/Services/Communication/README.md` (3 servicios Node, Node 24). | Bajo | [02_COMPARATIVA_GUIAS](../02_COMPARATIVA_GUIAS.md) fila 74 |
| D-61 | `deploy/tests/Dockerfile` corre la solución sin filtro (incluiría `.Integration.`). | Bajo | [Docker_Deploy](../Investigaciones/Docker_Deploy.md) §3.5 |
| D-62 | Runbook general de incidentes del backend (502/503, load shedding, DLQ, reinicio). | Medio | [Observabilidad](../Investigaciones/Observabilidad.md) §3.5 |

---

## 3. Cómo usar este documento

- Antes de implementar una capacidad, busca su tema en la tabla 1: la columna "Conclusión" es la regla de la guía.
- Si tu servicio toca una decisión de la sección 2, pide la respuesta al dueño **antes** de escribir código y regístrala junto al ítem (criterio de aceptación del [cap. 37](../GUIA_MAESTRA_CREACION_MICROSERVICIOS_TAXVISION.md#cap-37)).
- Este documento no autoriza cambios: las correcciones de las decisiones pendientes son ítems propios con su ciclo rojo → verde → gate.
