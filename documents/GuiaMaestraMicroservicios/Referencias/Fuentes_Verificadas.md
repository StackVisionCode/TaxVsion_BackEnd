# Fuentes verificadas

> Fecha de corte: 2026-11-15. Acompaña a [GUIA_MAESTRA_CREACION_MICROSERVICIOS_TAXVISION.md](../GUIA_MAESTRA_CREACION_MICROSERVICIOS_TAXVISION.md).
> Trabajo de **solo lectura**: no se compiló, no se corrieron pruebas, no se tocó Docker, bases de datos, Cloudflare ni git. No se abrieron archivos con secretos (p. ej. `GITHUBSCT/TaxVision_Secrets.xlsx`, `.env`, user-secrets).
> **Estados:** **Verificada en código** = leída en `TaxVsion_BackEnd` a la fecha de corte (por las investigaciones y, puntualmente, por la redacción de la guía) · **Histórica** = documento, memoria o libro; puede estar desfasada; cuando choca con el código, manda el código ([Decisiones_Arquitectonicas.md](Decisiones_Arquitectonicas.md)) · **Parcial** = verificada por puntos, no completa.

---

## 1. Código del backend (`C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd`)

### 1.1 Solución, raíz y despliegue

| Fuente | Qué se tomó | Estado |
|---|---|---|
| `TaxVision.slnx` | Estructura de carpetas, 31 proyectos de prueba, alta de servicios | Verificada en código |
| `global.json`, `dotnet-tools.json` | SDK 10.0.300; `dotnet-ef` por manifiesto | Verificada en código |
| `deploy/docker/docker-compose.yml` (+ `docker-compose.local.yml`, `docker-compose.local-infra.yml`, `compose.ps1`) | Anatomía de servicios, ancla `x-platform-settings-env`, `ServiceAuth__Clients__0..31`, overrides de clusters, gate de módulo por servicio, puertos publicados, healthchecks, `restart`, servicio `migrations` | Verificada en código |
| `deploy/docker/migrations/{Dockerfile,apply-migrations.sh}` | Imagen de migraciones, `set -eu`, bloques `apply_migration`, FinancialInsights condicional | Verificada en código |
| `.github/workflows/deploy.yml` | Dos jobs, environments `production`/`production-extra`, build en tandas de 3, migraciones, healthcheck, ausencia de `dotnet test` | Verificada en código |
| `deploy/docker/caddy/{Caddyfile,Dockerfile}` | Snippets `(taxvision_api)`/`(taxvision_tenant)`, orden de reglas públicas, DNS-01 | Verificada en código |
| `deploy/observability/*` | Collector OTLP → Prometheus, dashboards y alertas provisionados | Verificada en código |
| `scripts/{start-fleet,stop-fleet,restart-down,migrate-local}.ps1`, `scripts/codegen/regenerate-wolverine-code.ps1`, `scripts/generate-dev-keys.*` | Flota local, puertos, Static, migraciones locales, regeneración | Verificada en código |
| `src/Services/*/…Api/Dockerfile` (32) | Patrón de imagen, `TZ`, `MALLOC_ARENA_MAX`, `USER` | Verificada en código |

### 1.2 BuildingBlocks (`src/BuildingBlocks`)

| Fuente | Qué se tomó | Estado |
|---|---|---|
| `BuildingBlocks.csproj` y los 9 proyectos BB | Empaquetado, dependencias, carpetas compiladas en el núcleo | Verificada en código |
| `Results/*`, `BuildingBlocks.Web/Results/ErrorHttpMapping.cs` | `Result`/`Error`, mapeo HTTP con default 400 | Verificada en código |
| `Domain/*` (`AggregateRoot`, `TenantEntity`, `UserReferenceAttribute`) | Modelo base y anotación de offboarding | Verificada en código |
| `Tenancy/*`, `BuildingBlocks.Web/Tenancy/*` | `ITenantContext`, middlewares de tenant (JWT, evento, comando local) | Verificada en código |
| `ActorTypeAuthorization/*`, `BuildingBlocks.Web/ActorTypeAuthorization/*` | Actor types, filtros, `PermissionPolicyProvider`, `ProjectionPermissionsSource`, gate de módulo | Verificada en código |
| `Authorization/PermissionModuleMap.cs`, `*Permissions.cs` | Mapa permiso→módulo, exenciones, constantes | Verificada en código |
| `BuildingBlocks.Web/Security/JwtAuthenticationRegistration.cs` | RS256/HS256, validación | Verificada en código |
| `Sessions/*`, `BuildingBlocks.Web/Session/*`, `BuildingBlocks.Infrastructure/{Sessions,Caching,Redis}/*` | Denylist, fail-open/closed, Redis | Verificada en código |
| `BuildingBlocks.Infrastructure/Security/*`, `Security/ExpiringValueCache.cs` | Adquisición y caché de tokens M2M | Verificada en código |
| `Messaging/*` (`IIntegrationEvent`, `WolverineFailurePolicies`, `WolverineConflictPolicy`, contratos) | Contratos, identidad, políticas de fallo | Verificada en código |
| `BuildingBlocks.Web/Hosting/*` (`RabbitMqTransportExtensions`, `WolverineCodeGenerationExtensions`, `DomainEventDispatchExtensions`, `DurableLocalQueueExtensions`, `OffboardingStepAcknowledgements`, `TimeZoneSelfCheck`, `ClientIpForwardingExtensions`) | Transporte, codegen, BB-1/BB-2, acuse de offboarding, self-check | Verificada en código |
| `EfCore/UtcDateTimeConventions.cs` | `UseUtcDateTimes()` | Verificada en código |
| `Jobs/TransactionalJobUnit.cs` | Unidad por transacción con outbox | Verificada en código |
| `TenantTimeZones/*`, `CustomerVisibility/*` | Kits con proyección, consumer y reconciliación; defaults de URL | Verificada en código |
| `Common/ReconciliationRetry.cs` | Reintento corto de 2 min | Verificada en código |
| `RateLimiting/*`, `BuildingBlocks.Web/RateLimiting/*`, `BuildingBlocks.Infrastructure/RateLimiting/*` | Catálogo, `[RateLimit]`, evaluador, cuotas por plan | Verificada en código |
| `Platform/PlatformSettingsSnapshot.cs`, `BuildingBlocks.Web/Platform/*` | Regla de host, hosts reservados, refresco | Verificada en código |
| `BuildingBlocks.Web/Observability/*`, `Middleware/CorrelationIdMiddleware.cs` | Serilog, OTel, correlación | Verificada en código |

### 1.3 Servicios

| Servicio | Qué se tomó | Estado |
|---|---|---|
| **Notes** (referencia principal) | `Program.cs` completo y orden, `NotesDbContext`, factory, configuraciones, `CreateNoteCommand`, `NotesController`, consumers, repositorio con `IgnoreQueryFilters`, pruebas de arquitectura/integración | Verificada en código |
| **Customer** | Eventos de dominio (BB-1), colas punto a punto, `TenantTimeZones`, consumers de offboarding, endpoints de reconciliación | Verificada en código |
| **FinancialInsights** | Permisos, gate en enforce, proyecciones con `ApplyIfNewer`, pull-recovery, reconciliaciones, cuotas, módulo vendible, M2M sin caché, codegen | Verificada en código |
| **Auth** | `PermissionCatalog`, emisión JWT y M2M, `SessionAccessCutoff`, denylist, offboarding (handler, run, acuses, barrido, participantes), reconciliación de permisos, resolución de tenant por host | Verificada en código |
| **Subscription** | Catálogo de módulos/planes, revisiones, entitlements, endpoints internos, `EntitlementSnapshotReconciliationJob` | Verificada en código |
| **Tenant** | `PlatformSettings`, directorio interno, coordinador WISP | Verificada en código |
| **Calendar** | Acquirer M2M con caché, `IntegrationEventRoutingTests`, conversor UTC propio, caché de host | Verificada en código |
| **Notification** | Gateway de email, SMS, in-app, push, catálogo de kinds, `TenantEmailLinks`, consumers de offboarding | Verificada en código |
| **Scribe** | Render, seed por hash, fechas tipadas, layouts | Verificada en código |
| **Sms**, **Postmaster** | Envío SMS/correo, idempotencia, carriles | Parcial (por puntos) |
| **Connectors** | Purga de buzones en offboarding (acuse prematuro) | Verificada en código |
| **Signature**, **Tasks**, **Campaigns**, **CloudStorage**, **Correspondence**, **Reminder**, **Wallet** | Consumers de offboarding, pruebas, variantes de proyecto | Parcial (por puntos) |
| **Billing**, **Catalog**, **Documents**, **Growth**, **Inventory**, **PaymentApp**, **PaymentClient** | Adopción de piezas transversales, consumer de permisos, inventario | Parcial (conteos por búsqueda) |
| **Gateway** (`src/Gateway/TaxVision.Gateway`) | Pipeline, rutas y clusters, rate limit pre-auth, load shedding, guardas, health | Verificada en código |
| **Communication** (Node) | Denylist en handshake, watcher Pub/Sub, consumers de offboarding, in-app, Prisma | Parcial |
| **CommunicationRecorder**, **CommunicationTranscriptWorker**, **ContentScanner**, **MailboxListener** | Rol de worker, seguridad, Dockerfile | Parcial |

### 1.4 Pruebas (`deploy/tests`)

| Fuente | Qué se tomó | Estado |
|---|---|---|
| `TaxVision.BuildingBlocks.Testing/*` | `LayeringRulesBase`, `PreGeneratedCodeRules`, `SaveBeforePublishRules`, `LocalQueueDurabilityRules`, `DomainEventHandlerConventionRules`, `IntegrationIsolationRules`, `TemporaryTestDatabase`, `MigrationTestDatabase` | Verificada en código |
| `TaxVision.BuildingBlocks.Tests/{Architecture,Messaging,Offboarding,Deploy,Session,ActorTypeAuthorization,RateLimit,Common,TenantTimeZones}/*` | Fitness transversales; `ModuleGateWiringFitnessTests.cs:120` (`Assert.Equal(15, …)`, confirmado al redactar la guía); `ReconciliationRetryTests` (5 archivos) | Verificada en código |
| `TaxVision.Notes.Tests/*` | csproj de pruebas, fábricas de host, `PreGeneratedHandlerCodeTests`, `UtcDateTimesTests`, `NotesArchitectureTests`, base temporal | Verificada en código |
| `TaxVision.FinancialInsights.Tests/*`, `TaxVision.Calendar.Tests/*`, `TaxVision.Auth.Tests/*`, `TaxVision.Gateway.Tests/*`, `TaxVision.Wallet.Tests/*`, `TaxVision.Scribe.Tests/*`, `TaxVision.Notification.Tests/*`, `TaxVision.Subscription.Tests/*` | Casos citados por nombre en la guía | Verificada en código (por nombre) |
| `deploy/tests/README.md` | Lista de 6 proyectos | Histórica (desfasada) |

---

## 2. Documentos de soporte de esta guía (`GuiaMaestraMicroservicios/`)

| Fuente | Qué se tomó | Estado |
|---|---|---|
| `03_INVENTARIO_ARQUITECTURA.md` | Cifras de la flota, matriz de servicios, adopción de piezas, stack con versiones, servicios de referencia | Verificada en código (investigación) |
| `02_COMPARATIVA_GUIAS.md` | Matriz de 76 instrucciones de guías antiguas con estado y tratamiento | Verificada en código (investigación) |
| `01_ESTADO_INVESTIGACION.md` | Estado de frentes y etapas | Documento de avance |
| `Referencias/Guardrails.md` | 36 guardrails con vigencia y pruebas; complementarios | Verificada en código (investigación) |
| `Investigaciones/*.md` (31 archivos: Arquitectura, BuildingBlocks, Migraciones, Auth_RBAC, Session_Denylist, M2M_ServiceAuth, Proyecciones, Reconciliation, RateLimit_Cuotas_Gates, Offboarding, Offboarding_Integraciones, Modulos_Planes_Addons, Wolverine_RabbitMQ, Roslyn_Produccion, Tenant_Subdomains, Tenant_Links, Scribe_Templates, Notification_Email, Notification_SMS, Notification_InApp_Push, Docker_Deploy, Scripts_Locales, Gateway_YARP, Cloudflare, CI_CD_Secrets, Postman, Observabilidad, Testing_Unit, Testing_Integration, Testing_Security, Testing_EDA) | Toda la evidencia por capítulo, fragmentos reales, errores históricos, pruebas, decisiones pendientes | Verificada en código (cada investigación marca lo histórico) |

---

## 3. Documentación histórica (`C:\Users\devcacg\Desktop\Implementaciones\` y `documents/`/`docs/` del backend)

| Fuente | Qué se tomó | Estado |
|---|---|---|
| `DEVS/Guia_Creacion_Microservicio.md` (GCM) | Estructura general, DoD de offboarding, 38 guardrails; contradicciones C-01, C-03, C-18…C-21 | Histórica — **reemplazada** por la guía maestra |
| `DEVS/Guia_BuildingBlocks_Ola2.md` (BB2) | BB-1, BB-2, BB-3, BB-4, BB-6, BB-7, BB-11, BB-14 | Histórica, mayormente vigente (estado de adopción desfasado) |
| `DEVS/Guia_Envio_Externo_Fuera_De_Transaccion.md` | Envío externo en 4 pasos, `[NonTransactional]`, sin reintento Polly en POST | Histórica vigente |
| `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md` (IQF) | 4 condiciones seguras; dónde hace falta | Histórica vigente salvo el estilo de comentarios (C-05) |
| `documents/Guia_Conectar_Microservicio_a_CloudStorage.md` (GCS) | Formas de integración con CloudStorage, enums | Histórica vigente |
| `documents/Guia_Conectar_Microservicio_a_Notificaciones_Email.md` (GNE) | Flujo de notificación | Histórica parcialmente desfasada (C-10, C-11) |
| `documents/RBAC_Endurecimiento_Defensa_Senior.md` | Modelo de 5 capas, mini-doc | Histórica parcialmente desfasada (C-12…C-16) |
| `documents/architecture/Guia_Cablear_Modulo_Plan_Microservicio.md` (CMP) | Pasos de cableado del gate | Histórica — **superada** (C-34) |
| `documents/architecture/ADR_017…023` | Criterios de bounded context (018, 021, 022), superficie interna (019), rate limit (017, 020) | Histórica vigente |
| `documents/RateLimit/*` | Taxonomía, invariantes, anti-patrones | Histórica vigente |
| `docs/audit/*` (00, 01, 08, 09, 15) | Arquitectura antigua, SEC-001, DB-002 | Histórica (cifras superadas) |
| `DomainDrivenDesign/00-Rubrica.md`, `01-Convenciones-Pruebas.md`, `<Servicio>.md`, `02-Plan-Maestro.md` | Desvíos aceptados, hechos técnicos, gate exacto, tipos de prueba | Histórica vigente (algunas cifras superadas: C-24, C-31, C-32) |
| `Reminder/04_Guardrails_Checklist_Y_Verificacion.md` (REM) | Guardrails por categoría, Quartz, zonas horarias, proceso | Histórica vigente con huecos (C-09) |
| `Signature/AUDITORIA_SIGNATURE_FASE0.md` (Apéndice A) | Texto original de los 36 guardrails | Histórica — fuente original |
| `Infra/RAM-Tasks-3GB-Produccion-2026-11-13.md` | Incidente de RAM, mediciones Dynamic vs Static, decisiones pendientes | Histórica (hechos del incidente) |
| `FinancialInsights/Revision_y_Plan_FinancialInsights.md` | Cómo entró el servicio sin ser vendible; decisiones D1–D7 | Histórica |
| `Subscriptions/Guia_Facturacion_AddOns.md` | Ciclo de add-ons | Histórica parcialmente desfasada (C-35) |
| `DEVS/Futuras Implementaciones/Offboard-Revision-2026-11-03.md`, `Fase4-Anonymize-…PLAN.md` | Revisión de offboarding, verificación 49/49, plan de anonimización | Histórica |
| `documents/Portal_Cliente_Guia_Frontend.md`, `GITHUBSCT/COTURN_turn.taxoffix.io_PASO_A_PASO.txt`, `Auth_y_CloudStorage_Plan_Completitud_v2.md` | Fragmentos del procedimiento manual de Cloudflare (hostname al túnel, DNS only, tokens, wildcard) | Histórica |
| `CommunicationCorrecciones/F11_Despliegue_Secretos_y_VM.md` | Comandos de diagnóstico en la VM | Histórica |
| `src/Services/Communication/README.md`, `src/Services/Notification/README.md`, `README.md` raíz | Datos sensibles, retención, runbook de DLQ (§48) | Histórica (README de Communication desfasado: C-21) |
| `DEVS/Guia_Integracion_*.md`, `Guia_TenantBrands_Frontend.md`, `Guia_Support_Tickets…` | Integración de frontend | Fuera de alcance (anexos) |

---

## 4. Vault (Obsidian) y evidencias

| Fuente | Qué se tomó | Estado |
|---|---|---|
| `CommunicationCorrecciones/Investigaciones/INV-09_Sesiones_Seguridad.md` | Estado anterior de sesiones en sockets | Histórica (corregido, C-44) |
| `CommunicationCorrecciones/Proyectos/Calendar/Evidencias/E-12_…` | Incidente del secreto M2M de Calendar | Histórica (hecho del incidente) |
| `CommunicationCorrecciones/Proyectos/TenantTimeZones/Decisiones/Aprobadas.md`, `Despliegue_Checklist.md` | P14, P15, P18; orden Scribe → Notification | Histórica (decisiones aprobadas) |
| `CommunicationCorrecciones/Proyectos/PublicDemoScheduling/Evidencias/E-14_*` | Turnstile | Histórica |
| `CommunicationCorrecciones/Evidencias/Registro_Evidencias.md` (E-196) | RAM de la VM, SQL por túnel | Histórica |
| `RABC/Guia_Arquitectura_de_Accesos.md`, `RateLimit/PLAN_RateLimit_Flexibilizacion.md` | Contexto de RBAC y rate limit | Histórica |

---

## 5. Memoria del proyecto (`memory/*.md`)

| Fuente (índices y lecciones citadas) | Qué se tomó | Estado |
|---|---|---|
| `project_ci_sin_gate_de_pruebas.md` | El CI no corre pruebas desde 2026-11-13 | Histórica, confirmada por `deploy.yml` |
| `project_tasks_ram_3gb_produccion.md`, `feedback_codegen_static_variable_doble_guion.md`, `feedback_no_codegen_preview_arranca_servicio.md` | Incidente de RAM, variable con doble guion, codegen write | Histórica, confirmada en código |
| `feedback_wolverine_evento_nuevo_necesita_ruta.md`, `feedback_wolverine_reintento_en_linea_filtra_mensajes.md`, `feedback_wolverine_no_descubre_sufijo_plural.md`, `feedback_fanout_rabbit_sin_dueno_ruido_no_known_handler.md` | Reglas de mensajería | Histórica vigente (salvo la primera parte del fanout: C-04) |
| `feedback_ef_clave_preasignada_genera_update_no_insert.md`, `feedback_owned_type_no_se_comparte_entre_filas_hermanas.md`, `feedback_enum_rename_hasconversion_string_…`, `feedback_ef_core_wolverine_initial_migration.md`, `feedback_ef_query_filter_wolverine_scope_mismatch.md` | Lecciones de EF Core | Histórica vigente |
| `feedback_migration_drift_causes_gateway_load_shedding_cascade.md`, `feedback_aplicar_migracion_de_inmediato.md`, `feedback_dev_local_connection_string_viene_de_user_secrets.md` | Migraciones y entorno local | Histórica vigente |
| `feedback_no_conceder_permisos_sistema_por_migracion.md`, `feedback_permisos_catalogo_en_codigo_manda.md`, `feedback_permissionmodulemap_cross_service_no_duplicate.md`, `feedback_comprobacion_de_recurso_no_es_comprobacion_de_permiso.md` | Permisos | Histórica vigente |
| `feedback_m2m_config_key_binding.md`, `project_subscription_m2m_serviceauth_and_pullrecovery_gap.md` | M2M | Histórica vigente |
| `project_tenant_employee_offboarding_3_2_analysis.md` | Offboarding | Histórica |
| `project_a6_module_gate_estado_medido.md`, `project_catalogo_planes_addons_modulos_reales.md` | Gate y catálogo | Histórica parcialmente desfasada (C-34, C-42) |
| `feedback_scribe_subir_version_plantilla.md`, `feedback_gate_de_preferencias_no_cubre_email.md`, `feedback_email_idempotency_key_must_include_recipient.md` | Notificaciones | Histórica desfasada (C-10, C-40, C-41) |
| `feedback_tests_con_infra_real_van_en_namespace_integration.md`, `feedback_ci_linux_difiere_de_windows.md`, `feedback_dependencia_nueva_correr_integration_del_servicio.md`, `feedback_csharpier_antes_del_gate.md`, `feedback_tz_env_windows_pruebas.md` | Pruebas | Histórica vigente |
| `feedback_sin_numero_de_fase_en_identificadores.md`, `feedback_ddd_guardrails_taxvision.md` | Convenciones | Histórica vigente |
| `project_real_client_ip_forwarding_cloudflare_docker_fix.md`, `project_financial_insights_local.md`, `project_tenant_timezones_auditoria.md` | IP real, FinancialInsights local, auditoría de zonas | Histórica |
| `feedback_reiniciar_servicio_tocado_release.md`, `feedback_reinicio_flota_no_encadenar_en_background.md` | Operación de la flota local | Histórica vigente |

---

## 6. Libro

| Fuente | Qué se tomó | Estado |
|---|---|---|
| `DomainDrivenDesign/Guia-DDD-EDA-NET10-Wolverine-RabbitMQ-Docker.pdf` (páginas leídas por las investigaciones: 1-9, 20-24, 30-32, 37-39, 42-44, 64-66) | Principios de capas, outbox "al menos una vez", consumidor idempotente, "un fanout sin cola pierde el mensaje", vigilar la DLQ, migraciones en el deploy (p. 65), codegen Static antes de producción (§10.3), coste de Roslyn (§1.7), monolito modular como punto de partida (cap. 10.3) | Histórica. **Desvíos aceptados** (Application con Wolverine, `Result` en vez de excepciones); **descartado** lo inseguro o reemplazado (credenciales en `appsettings`, `ApplyMigrationsAsync` al arrancar, `AddDbContextWithWolverineIntegration`, exchange por servicio, `codegen write`) |

---

## 7. Huecos de evidencia reconocidos

- No se verificó nada **en vivo** (rutas, Cloudflare, secrets de GitHub, estado de producción).
- Conteos de consumidores y de adopción obtenidos por búsqueda de patrones (aproximados; Customer subcontado).
- Postmaster, Sms, Signature, Tasks, Campaigns, CloudStorage y Communication se verificaron por puntos, no flujo por flujo.
- La configuración de Cloudflare no está en el repo: el procedimiento manual se reconstruyó de fragmentos documentales.
- Las páginas del libro no listadas arriba no se leyeron.
