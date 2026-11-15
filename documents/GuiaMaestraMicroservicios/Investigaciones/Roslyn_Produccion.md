# Roslyn en producción (codegen de Wolverine) — investigación

Fecha: 2026-11-15 · Trabajo de solo lectura · Repo: `TaxVsion_BackEnd` (rutas relativas a esa raíz salvo que se indique).

Leyenda: **[Código verificado]** leído hoy en el repo · **[Documentación histórica]** memoria/planes/informes · **[Recomendación]** propuesta de esta investigación · **[Decisión pendiente]** lo decide el dueño.

---

## 1. Estado actual

- **[Código verificado]** Los 26 servicios .NET con Wolverine llaman `options.UseTaxVisionCodeGeneration(builder.Environment, builder.Configuration, typeof(Program).Assembly)` dentro de `UseWolverine`, más el worker `Connectors.MailboxListener` (`MailboxListenerProgram.cs:52`). Ejemplos: `src/Services/Tasks/TaxVision.Tasks.Api/Program.cs:131`, `src/Services/Subscription/TaxVision.Subscription.Api/Program.cs:171`, `src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/Program.cs:85`.
- **[Código verificado]** La extensión vive en `src/BuildingBlocks/BuildingBlocks.Web/Hosting/WolverineCodeGenerationExtensions.cs`:
  - clave de configuración `Wolverine:CodeGeneration:TypeLoadMode` (línea 18), por variable de entorno `Wolverine__CodeGeneration__TypeLoadMode` (doble guion bajo);
  - `options.ApplicationAssembly = applicationAssembly` (línea 30): el código generado vive en el **Api**, no en Application;
  - regla: si la clave está forzada se usa ese valor; si no, `Static` solo cuando `environment.IsProduction()`, `Dynamic` en el resto (línea 43).
- **[Código verificado]** El código pregenerado está versionado en `src/Services/<Svc>/<Svc>.Api/Internal/Generated/WolverineHandlers/*.cs`, con un `GeneratedHandlerRegistry.cs` (FinancialInsights: 31 archivos). `.csharpierignore:8` excluye `**/Internal/Generated/`.
- **[Código verificado]** Los 28 servicios del `deploy/docker/docker-compose.yml` corren con `ASPNETCORE_ENVIRONMENT: Production` (28 apariciones, ninguna distinta), así que en la VM todos quedan en `Static` sin variable extra.
- **[Código verificado]** `scripts/start-fleet.ps1:6-7` fija `Wolverine__CodeGeneration__TypeLoadMode = "Static"` para la flota local (igual que producción).
- **[Código verificado]** Las 29 imágenes con etapa final .NET tienen `ENV MALLOC_ARENA_MAX=2` (p. ej. `src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/Dockerfile:13`, `src/Services/Tasks/TaxVision.Tasks.Api/Dockerfile:21`).
- **[Código verificado]** Versión: `WolverineFx*` 6.14.0 en todos los csproj; `WolverineFx.RuntimeCompilation` 6.14.0 sigue referenciado en 30 proyectos (Roslyn sigue dentro de la imagen; en Static no se usa).
- **[Código verificado]** `options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn` en los Program.cs (p. ej. Tasks `Program.cs:141`, FinancialInsights `Program.cs:92`).
- **[Documentación histórica]** Desplegado 2026-11-13 ~17:01Z (commit del dueño "Fix ram consume and fix client forms"). Log de Tasks: "code generation mode is Static… HandlerRegistry (193 handler types)". Tras el deploy: Tasks 184 MB (antes 2.9 GB), Auth 217 MB (antes 1.7 GB), 37 contenedores ≈ 6.2 GiB. Pendiente: confirmar con uso real (`~/rss.log` con PIDs nuevos). Fuente: memoria `project_tasks_ram_3gb_produccion.md`.

## 2. Evidencia del incidente (causa confirmada)

**[Documentación histórica]** `Implementaciones/Infra/RAM-Tasks-3GB-Produccion-2026-11-13.md` + memoria `project_tasks_ram_3gb_produccion.md`:

| Hecho | Dato |
|---|---|
| Síntoma | Tasks llegó a 3.5 GB (2.9 GB retenidos) en prod el 13-nov, tras reinicio forzado de Proxmox y un deploy |
| Heap .NET | 50–130 MB todo el tiempo: no eran objetos .NET |
| Patrón | +500/+900 MB de working set en el **primer** uso de cada endpoint; primera llamada lenta (tablero 6 s, reabrir 1.5 s), `assembly.count` +1..5, miles de métodos JIT; la segunda llamada en ms y sin salto |
| Causa | Wolverine en modo `Dynamic` compilaba cada handler con Roslyn dentro del proceso al primer uso. Ningún servicio usaba código pregenerado |
| Por qué no bajaba | glibc en Linux retiene la memoria nativa liberada (dotnet/runtime #122027, #121827) |
| Descartado con mediciones | avalancha de mensajes, job de clientes (18 clientes), cola Serilog→OTel (el colector descarta con `nop`) |
| Otro factor (aparte) | `deploy.yml` corre `runs-on: self-hosted` y compila las imágenes en la misma VM, de 3 en 3. El dueño decidió dejarlo así |

Medición local, mismo recorrido en el CRM:

| | Dynamic | Static |
|---|---|---|
| Pico del proceso Tasks | 1 479 MB | 333 MB |
| Reposo | 309 MB | 270 MB |
| Ensamblados cargados en el uso | 232 → 357 | 242 → 245 |
| Heap .NET máximo | 134 MB | 50 MB |
| Avisos "Utilizing service location" | sí | 0 |
| Reabrir/iniciar tarea la primera vez | 1.5 s | 0.15 s |

**[Documentación histórica]** El libro `DomainDrivenDesign/Guia-DDD-EDA-NET10-Wolverine-RabbitMQ-Docker.pdf` ya lo advertía: §1.7 "El coste de Wolverine: genera y compila código en tiempo de ejecución…" y §10.3 recomienda evaluar `codegen write` + `TypeLoadMode.Static` antes de producción.

## 3. Corrección aplicada (seis piezas)

| # | Pieza | Dónde | Tipo |
|---|---|---|---|
| 1 | Static solo en Production, forzable por configuración | `WolverineCodeGenerationExtensions.cs` + 1 línea en cada `UseWolverine` | [Código verificado] |
| 2 | Guardarraíl por servicio: todos los handlers cargan de código pregenerado y el código está al día | `deploy/tests/TaxVision.BuildingBlocks.Testing/Architecture/PreGeneratedCodeRules.cs` + `deploy/tests/TaxVision.<Svc>.Tests/Architecture/PreGeneratedHandlerCodeTests.cs` (26 servicios) | [Código verificado] |
| 3 | Regeneración | `scripts/codegen/regenerate-wolverine-code.ps1` (todos o `-Service Tasks,Auth`) | [Código verificado] |
| 4 | Logs: Serilog respeta `Logging:LogLevel`; EF y ASP.NET en Warning; archivo y OTLP apagados por defecto | `TaxVisionLogging.cs`, `OpenTelemetryRegistration.cs` | [Documentación histórica] (no releído hoy) |
| 5 | `ENV MALLOC_ARENA_MAX=2` en la etapa final de cada imagen .NET | Dockerfiles + `deploy/tests/TaxVision.BuildingBlocks.Tests/Deploy/DotnetImageMallocArenaTests.cs` | [Código verificado] |
| 6 | Job de clientes de Tasks con un scope (DbContext) por página | `TenantCustomerFullReconciliationJob.cs` | [Documentación histórica] |

Ajustes colaterales [Documentación histórica]: la política de offboarding ordena sus pasos para que el código generado sea estable; las reglas `SaveBeforePublish` y de acoplamiento de Calendar excluyen `Internal/Generated` (confirmado en `SaveBeforePublishRules.cs`: `.Where(path => !IsGenerated(path))`); dos clases de prueba aisladas con `DisableParallelization`.

### 3.1 Cómo funciona el guardarraíl — [Código verificado]

`PreGeneratedCodeRules` (`deploy/tests/TaxVision.BuildingBlocks.Testing/Architecture/PreGeneratedCodeRules.cs`):
- `AssertAllHandlersArePreGenerated` (líneas 29-48): exige `TypeLoadMode.Static`, llama `AssertPreBuildTypesExist` en cada `ICodeFileCollection` y falla si algún `HandlerChain` resuelve a un tipo fuera del ensamblado del Api. Mensaje: "Handlers sin código pregenerado (regenerar con scripts/codegen/regenerate-wolverine-code.ps1)".
- `AssertGeneratedCodeIsCurrent` (líneas 57-103): genera en un directorio temporal con `DynamicCodeBuilder` (con `ServiceVariableSource` del contenedor, "sin esto no resuelve los IDomainEventScraper") y compara con lo versionado. Reporta `falta:`, `sobra:`, `desactualizado:`. Con `TAXVISION_CODEGEN_WRITE=1` reescribe la carpeta (así regenera el script).
- Comparación tolerante (líneas 109-126): líneas ordenadas, sin vacías y con locales numeradas normalizadas (`outgoing1` → `outgoing#`), porque Wolverine no genera igual entre corridas.

Prueba por servicio (ejemplo real, `deploy/tests/TaxVision.FinancialInsights.Tests/Architecture/PreGeneratedHandlerCodeTests.cs`):
- `Every_handler_loads_from_pre_generated_code_in_production`
- `Pre_generated_code_matches_the_current_handlers`
- Host `FinancialInsightsRoutingHostFactory` con `UseEnvironment("Production")`, transportes y persistencia de Wolverine anulados (`DisableAllExternalWolverineTransports`, `DisableAllWolverineMessagePersistence`) y sin `IHostedService` propios.
- Colección `[CollectionDefinition(nameof(PreGeneratedHandlerCodeTests), DisableParallelization = true)]`: generar código prende una bandera estática de JasperFx (`DynamicCodeBuilder.WithinCodegenCommand`) que deja sin transportes a cualquier host que arranque en paralelo en el mismo proceso.

Pruebas de la extensión: `deploy/tests/TaxVision.BuildingBlocks.Tests/Messaging/WolverineCodeGenerationExtensionsTests.cs` → `Only_production_loads_pre_generated_code` (Theory) y `The_mode_can_be_forced_by_configuration`.

### 3.2 Regeneración — [Código verificado]

`scripts/codegen/regenerate-wolverine-code.ps1`:
- lista fija `$all` de 26 servicios (incluye FinancialInsights);
- pone `TAXVISION_CODEGEN_WRITE=1` y **fuerza `Wolverine__CodeGeneration__TypeLoadMode=Dynamic`** mientras regenera: "En Static Wolverine solo descubre los handlers del GeneratedHandlerRegistry existente: un handler nuevo quedaría fuera sin aviso";
- corre `dotnet test deploy\tests\TaxVision.<Svc>.Tests\... --filter "FullyQualifiedName~PreGeneratedHandlerCodeTests.Pre_generated_code_matches_the_current_handlers"`.

**[Documentación histórica]** (memoria `project_financial_insights_local.md`, 2026-11-14): "Script de codegen ahora en Dynamic: antes omitía handlers nuevos sin avisar".

## 4. Problemas y contradicciones

1. **[Código verificado + Documentación histórica] El guardarraíl ya no corre en el CI.** El informe de RAM dice "Guardarraíl en el gate del CI", pero desde el commit `b7cd9613` (2026-11-13) `deploy.yml` no corre pruebas (memoria `project_ci_sin_gate_de_pruebas.md`; `grep -i test .github/workflows/deploy.yml` no encuentra ningún `dotnet test`). Hoy la protección depende de que alguien corra el gate local antes de subir. El dueño lo decidió a propósito (saturaba la VM).
2. **[Código verificado] Static no valida al arrancar en Wolverine 6.14.** Comentario de `PreGeneratedCodeRules.cs:14-15`: "si falta el tipo de un handler, el primer mensaje de ese tipo revienta (6.14 no lo valida al arrancar)". Y según el script, en Static un handler nuevo sin regenerar puede quedar **fuera del registro sin aviso**. Riesgo: un servicio arranca "sano" y falla (o ignora) el primer mensaje.
3. **[Código verificado] La flota local también corre en Static** (`start-fleet.ps1:7`): si alguien agrega un handler y no regenera, en local tampoco lo verá funcionar; la prueba E2E en local puede engañar en ambos sentidos.
4. **[Documentación histórica] `codegen preview`/`codegen write` no sirven en TaxVision.** Los servicios terminan en `app.Run()`: el argumento se ignora y el host arranca entero (escucha Rabbit, procesa pendientes). Memoria `feedback_no_codegen_preview_arranca_servicio.md`. Además `DynamicCodeBuilder.GenerateAllCode()` falla en estático por `IDomainEventScraper`. Contradice la recomendación genérica del libro (§10.3 `dotnet run -- codegen write`).
5. **[Documentación histórica] Variable mal escrita = Dynamic en silencio.** `Wolverine_CodeGeneration_TypeLoadMode` con un guion bajo no se lee (memoria `feedback_codegen_static_variable_doble_guion.md`, 2026-11-14).
6. **[Documentación histórica] Memorias viejas desactualizadas:** `project_a6_module_gate_estado_medido.md` y `feedback_codegen_dynamic_oculta_di_roto_hasta_el_primer_mensaje.md` dicen "la codegen es Dynamic". Hoy es Static en Production; la lección (un DI roto no se ve al arrancar) sigue valiendo en Development.
7. **[Código verificado] Roslyn sigue en las imágenes** (`WolverineFx.RuntimeCompilation` en 30 proyectos). No compila en Static, pero ocupa disco (~100 MB según el informe) y permite volver a Dynamic con solo cambiar la variable.
8. **[Documentación histórica] La compilación de imágenes en la VM de producción** sigue (self-hosted, tandas de 3). Es otro consumo de RAM durante el deploy, separado del incidente.
9. **[Documentación histórica] Primera carga del tablero ~6 s** sigue: no es Wolverine (sin ensamblados nuevos), es arranque en frío de EF Core/JIT/SQL. Sin investigar.

## 5. Buenas prácticas (derivadas de lo verificado)

- Toda línea `UseWolverine(options => …)` empieza con `options.UseTaxVisionCodeGeneration(builder.Environment, builder.Configuration, typeof(Program).Assembly);`.
- Todo cambio que agrega, quita o cambia la firma de un handler (parámetros, dependencias, middleware, políticas) va con el código regenerado en el mismo cambio.
- Regenerar siempre con el script (que fuerza Dynamic), nunca a mano ni con `codegen write`.
- Probar en local en Static cuando se toca un handler: `$env:Wolverine__CodeGeneration__TypeLoadMode = "Static"` (doble guion bajo).
- Tras el deploy, mirar en el log de cada servicio "code generation mode is Static" y "Using pre-generated Wolverine HandlerRegistry".
- Imagen .NET nueva: `ENV MALLOC_ARENA_MAX=2` en la etapa final (la prueba `DotnetImageMallocArenaTests` lo exige para todo Dockerfile bajo `src/` cuyo último `FROM` sea `dotnet/aspnet` o `playwright/dotnet`).

## 6. Recomendación

- **[Recomendación]** La guía debe tratar el codegen Static como requisito de "servicio terminado" (no opcional) y decir explícitamente que, sin CI de pruebas, el gate local es la única barrera.
- **[Recomendación]** Que la checklist de servicio nuevo incluya: agregar el servicio a `$all` del script, crear `Internal/Generated` regenerando, y copiar `PreGeneratedHandlerCodeTests` con su host de rutas.
- **[Decisión pendiente]** (del informe, no hechas) límites de memoria por contenedor tras una semana en Static (pico × 1.3–1.5); subir Wolverine a ≥ 6.30 (valida el código pregenerado al arrancar); quitar `WolverineFx.RuntimeCompilation` de las imágenes de producción; volver a encender OTLP/archivo de logs (`OpenTelemetry__ExportLogs`, `OpenTelemetry__ExportTraces`, `Observability__Logging__File`).
- **[Decisión pendiente]** Si se quiere una barrera fuera del gate local: no hay hoy ninguna prueba que verifique que **todo** `Program.cs` con `UseWolverine` llame `UseTaxVisionCodeGeneration` (solo existe la prueba de la extensión). Un servicio nuevo sin la línea y sin `PreGeneratedHandlerCodeTests` volvería a Dynamic en producción sin que nada falle.

## 7. Riesgos

| Riesgo | Efecto | Detección actual |
|---|---|---|
| Handler nuevo/cambiado sin regenerar | En Static: falta del tipo → excepción al primer mensaje, o el handler queda fuera del registro sin aviso | `PreGeneratedHandlerCodeTests` (solo gate local) |
| Servicio nuevo sin `UseTaxVisionCodeGeneration` | Vuelve a compilar con Roslyn en prod → incidente de RAM | Ninguna automática (hueco) |
| Servicio nuevo sin `PreGeneratedHandlerCodeTests` | Nada vigila su código generado | Ninguna (hueco) |
| Servicio nuevo fuera de `$all` del script | El script no lo regenera | Ninguna |
| `ASPNETCORE_ENVIRONMENT` distinto de `Production` en un contenedor | Cae a Dynamic | Ninguna; hoy los 28 están en Production |
| Variable con un solo guion bajo | Queda en Dynamic | Log de arranque |
| Pruebas de codegen en paralelo con otros hosts | Hosts sin transportes, fallos raros | `DisableParallelization` en la colección |
| Imagen sin `MALLOC_ARENA_MAX=2` | Más memoria nativa retenida | `DotnetImageMallocArenaTests` |

## 8. Pruebas

Existentes:
- `deploy/tests/TaxVision.<Svc>.Tests/Architecture/PreGeneratedHandlerCodeTests.cs` (26: Auth, Billing, Calendar, Campaigns, Catalog, CloudStorage, Connectors, Correspondence, Customer, Documents, FinancialInsights, Growth, Inventory, Notes, Notification, PaymentApp, PaymentClient, Postmaster, Reminder, Scribe, Signature, Sms, Subscription, Tasks, Tenant, Wallet).
- `deploy/tests/TaxVision.BuildingBlocks.Testing/Architecture/PreGeneratedCodeRules.cs` (reglas compartidas).
- `deploy/tests/TaxVision.BuildingBlocks.Tests/Messaging/WolverineCodeGenerationExtensionsTests.cs`.
- `deploy/tests/TaxVision.BuildingBlocks.Tests/Deploy/DotnetImageMallocArenaTests.cs` (`Every_dotnet_image_limits_glibc_arenas`).
- `WolverineFailurePoliciesTests` corre aislada por la misma bandera estática de JasperFx [Documentación histórica].

Las que debe tener un servicio nuevo:
1. `Architecture/<Svc>RoutingHostFactory.cs` (host Testing sin infraestructura: cadenas de conexión a `127.0.0.1:1`, transportes y persistencia de Wolverine anulados, sin hosted services propios).
2. `Architecture/PreGeneratedHandlerCodeTests.cs` con las dos pruebas y la colección `DisableParallelization`.
3. Verificarla en rojo: borrar un archivo de `Internal/Generated` o agregar un handler sin regenerar.

Comando del gate local (el filtro no excluye `.Architecture.`, así que estas pruebas sí corren):
`dotnet test TaxVision.slnx --nologo --filter "FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence.&FullyQualifiedName!~ReminderRetentionJobTests"`

## 9. Dependencias

- Wolverine/JasperFx 6.14.0 (`TypeLoadMode`, `DynamicCodeBuilder`, `ICodeFileCollection`, `HandlerGraph`).
- `ASPNETCORE_ENVIRONMENT=Production` en compose.
- Host de pruebas por servicio que arranque sin SQL/Rabbit/Redis.
- `scripts/start-fleet.ps1` (local en Static).
- Docker: imagen `aspnet` final con `MALLOC_ARENA_MAX`.

## 10. Material para la guía (pasos y fragmentos reales)

**Paso 1 — Program.cs** (real, `FinancialInsights/.../Program.cs:83-86`):
```csharp
builder.Host.UseWolverine(options =>
{
    options.UseTaxVisionCodeGeneration(builder.Environment, builder.Configuration, typeof(Program).Assembly);
    options.Discovery.IncludeAssembly(typeof(AssemblyMarker).Assembly);
```

**Paso 2 — Regla del modo** (real, `WolverineCodeGenerationExtensions.cs:37-44`):
```csharp
var forced = configuration[TypeLoadModeKey];
if (!string.IsNullOrWhiteSpace(forced))
    return Enum.Parse<TypeLoadMode>(forced, ignoreCase: true);
return environment.IsProduction() ? TypeLoadMode.Static : TypeLoadMode.Dynamic;
```

**Paso 3 — Agregar el servicio al script** (`scripts/codegen/regenerate-wolverine-code.ps1`, arreglo `$all`) y regenerar:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\codegen\regenerate-wolverine-code.ps1 -Service <Svc>
```

**Paso 4 — Prueba del servicio** (real, FinancialInsights):
```csharp
[CollectionDefinition(nameof(PreGeneratedHandlerCodeTests), DisableParallelization = true)]
public sealed class PreGeneratedHandlerCodeCollection;

[Collection(nameof(PreGeneratedHandlerCodeTests))]
public sealed class PreGeneratedHandlerCodeTests
{
    private const string ApiProject = "src/Services/FinancialInsights/TaxVision.FinancialInsights.Api";

    [Fact]
    public void Every_handler_loads_from_pre_generated_code_in_production()
    {
        using var factory = ProductionHost(_ => { });
        PreGeneratedCodeRules.AssertAllHandlersArePreGenerated(factory.Services, typeof(Program).Assembly);
    }
    // Pre_generated_code_matches_the_current_handlers: igual, con GeneratedCodeOutputPath = scratch
    // y PreGeneratedCodeRules.AssertGeneratedCodeIsCurrent(...)

    private static WebApplicationFactory<Program> ProductionHost(Action<WolverineOptions> configure) =>
        new FinancialInsightsRoutingHostFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services => services.ConfigureWolverine(configure));
        });
}
```

**Paso 5 — Dockerfile** (real, etapa final de FinancialInsights):
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS final
ENV TZ=Etc/UTC
ENV MALLOC_ARENA_MAX=2
```

**Paso 6 — Probar Static en local:** `$env:Wolverine__CodeGeneration__TypeLoadMode = "Static"` (doble guion bajo) y usar cada handler una vez.

**Paso 7 — Verificar en producción:** log "code generation mode is Static" y "Using pre-generated Wolverine HandlerRegistry"; `docker stats` / `~/rss.log` sin saltos al primer uso.

**Cuándo regenerar:** al agregar/quitar/renombrar un handler o consumer, al cambiar sus parámetros (servicios inyectados, `Envelope`, `CancellationToken`), al agregar middleware/políticas (`AddMiddleware`, `UseTaxVisionDomainEvents`, `ListenForPlatformSettings`, `Discovery.IncludeType`), o al cambiar la política de transacciones.
