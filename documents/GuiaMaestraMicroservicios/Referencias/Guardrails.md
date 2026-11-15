# Guardrails — los 36 originales, explicados y verificados

> Fecha de verificación: 2026-11-15. Trabajo de solo lectura sobre `TaxVsion_BackEnd`.
> Rutas de código relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\`.
> Etiquetas: **Código verificado** (leído hoy en el repo), **Documentación histórica** (dicho por un documento, no comprobado o ya superado), **Recomendación**, **Decisión pendiente**.

## Estado actual

### De dónde salen los "36 guardrails"

- **Documentación histórica — fuente original.** El texto original de los 36 está en `C:\Users\devcacg\Desktop\Implementaciones\Signature\AUDITORIA_SIGNATURE_FASE0.md:1910-2003` ("Apéndice A — Guardrails (leer en cada fase)"). La memoria del proyecto lo cita como "Apéndice A (36 guardrails)" (`memory/project_signature_auditoria_fase0_superprompt.md:184`).
- **Copias y variantes:**
  - `DEVS/Guia_Creacion_Microservicio.md:1267-1334` (§12) tiene **38**: los mismos 36 renumerados **más** el #32 "la suite local solo es verde con `-m:1`" y el #38 "todo módulo con datos de un empleado entra al retiro (offboard)". Por eso la numeración de la guía antigua **no coincide** con la original desde el #32.
  - `Implementaciones/Reminder/04_Guardrails_Checklist_Y_Verificacion.md:8-136` los reagrupa por categoría y añade bloques propios: Quartz (Q1-Q13), tiempo y zonas horarias (10 reglas, 2026-11-15) y proceso (7 reglas).
  - Los 9 de DDD vienen de `memory/feedback_ddd_guardrails_taxvision.md` (reglas del dueño al construir Postmaster, 2026-07-17); los #1-#7 de la lista de 36 son ese mismo bloque condensado (sin el #9 "releer cada archivo" y con #4 "correlation.Push" movido a Wolverine).
- **Recomendación:** la guía maestra debe citar la numeración **original de 36** y tratar los extra (`-m:1`, offboard, Quartz, zonas horarias) como **guardrails complementarios** con su propio prefijo, para no repetir el desfase de numeración.

### Resumen de vigencia (36)

| Vigencia | Cuántos | Números |
|---|---|---|
| Vigente | 29 | 1-5, 8-25, 27-35 (salvo matices abajo) |
| Vigente con matiz / parcialmente vigente | 6 | 6, 7, 12 (contradicción documental), 13 (sin fitness general), 26, 36 (muy incumplido) |
| Obsoleto | 0 | — |
| Reemplazado | 1 parcial | 26 (el mecanismo de caché cambió a `ExpiringValueCache`) |

Ninguno de los 36 quedó contradicho por el código actual. Varios ganaron **pruebas automáticas** desde que se escribieron (ver columna "Cómo probarlo").

---

## Los 36 guardrails

### 🏛️ Diseño de dominio (DDD)

#### 1. SRP en handlers
- **Texto original:** "Todo handler que supere ~30 líneas se divide en métodos privados con nombres que dicen *qué* hacen, no *cómo*. Un método = una fase (load → validate/mutate → persist+publish)."
- **Qué significa:** el `Handle` es un índice legible; cada paso es un método privado con nombre de intención.
- **Por qué existe:** handlers largos mezclaban validación, persistencia y publicación y escondían bugs de orden (p. ej. publicar antes de guardar).
- **Dónde se aplica (Código verificado):** `src/Services/Notes/TaxVision.Notes.Application/Notes/Commands/CreateNoteCommand.cs:31-125` (`SoftVerifyCustomerReferenceAsync`, `BuildContent`, `BuildColor`). Fitness de tamaño: `deploy/tests/TaxVision.PaymentApp.Tests/Architecture/HandlerSizeFitnessTests.cs:12-13` (`MaxHandleBodyLines = 40`, `MaxHandlerFileLines = 200`) y `deploy/tests/TaxVision.Auth.Tests/Architecture/OnboardingHandlerSizeFitnessTests.cs`.
- **Qué error evita:** handlers imposibles de revisar; publicar antes de guardar; lógica de negocio fuera del agregado.
- **Cómo probarlo:** heredar/replicar `HandlerSizeFitnessTests` en el servicio; revisión de PR.
- **Vigencia:** Vigente. Solo PaymentApp y Auth.Onboarding lo vigilan con prueba.

#### 2. No sobrecargar aggregates con métodos genéricos
- **Texto original:** "Nada de `ChangeStatus(newStatus)` que esconda 5 reglas. Cada mutación es su propio método explícito, con su validación y su domain event."
- **Qué significa:** una transición de estado = un método con nombre de negocio (`Archive`, `Restore`, `SuspendForPolicyViolation`).
- **Por qué existe:** incidente `Customer.ChangeStatus(TenantSettingsStatus)` que escondía cinco reglas y hubo que partir (`memory/feedback_ddd_guardrails_taxvision.md`, regla 2).
- **Dónde se aplica (Código verificado):** `src/Services/Notes/TaxVision.Notes.Domain/Notes/Note.cs:7-11` (comentario de diseño "Sin `ChangeStatus(x)` genérico"); `Customer.cs` emite un domain event por transición (`src/Services/Customer/TaxVision.Customer.Domain/Customers/Customer.cs:143,174,182`).
- **Qué error evita:** reglas de transición saltadas o mezcladas; eventos sin significado.
- **Cómo probarlo:** T-DOM por transición (estado inválido → `Result` fallido). No hay fitness automática que prohíba un método `ChangeStatus`.
- **Vigencia:** Vigente. **Recomendación:** una fitness simple por reflexión ("ningún método público del Domain recibe un enum `*Status`").

#### 3. Value Objects, no primitivos sueltos
- **Texto original:** "Todo dato con reglas propias es un VO con validación en construcción."
- **Qué significa:** tamaño, formato, secreto cifrado, referencia polimórfica → VO inmutable con `Create()` que devuelve `Result<T>`.
- **Por qué existe:** primitivos sin validar (`int MaxPdfBytes`, `string TenantAuditSecret`) dejaban pasar estados inválidos.
- **Dónde se aplica (Código verificado):** `src/Services/Notes/TaxVision.Notes.Domain/ValueObjects/{NoteColor,NoteContent,NoteReference}.cs`.
- **Matiz (memoria):** campos tipo email validados en el agregado con `string` siguen la convención del repo (`memory/feedback_ddd_guardrails_taxvision.md`, regla 3).
- **Qué error evita:** "obsesión por primitivos" (punto C de `DomainDrivenDesign/00-Rubrica.md`).
- **Cómo probarlo:** T-DOM de cada VO (valores límite).
- **Vigencia:** Vigente.

#### 4. Cero LINQ en aggregates
- **Texto original:** "LINQ vive en repositorios o queries."
- **Qué significa:** dentro del dominio, `List<T>.Find/Exists` y `foreach`; nada de `System.Linq`.
- **Por qué existe:** mantener el agregado como lógica pura y evitar que se cuelen consultas de datos.
- **Dónde se aplica (Código verificado):** fitness en `deploy/tests/TaxVision.Calendar.Tests/Architecture/CalendarArchitectureTests.cs:47-49` (`ShouldNot().HaveDependencyOn("System.Linq")`) y `deploy/tests/TaxVision.Tasks.Tests/Architecture/TasksArchitectureTests.cs:162` (regex sobre el código fuente).
- **Qué error evita:** `IQueryable` o LINQ-to-EF en el dominio.
- **Cómo probarlo:** copiar la regla NetArchTest de Calendar al servicio nuevo.
- **Vigencia:** Vigente. Solo 2 servicios lo vigilan. **Recomendación:** subirlo a `LayeringRulesBase` como regla opcional (sería una pieza aditiva de `deploy/tests/TaxVision.BuildingBlocks.Testing`).

#### 5. Sin `virtual` ni setters públicos en aggregates
- **Texto original:** "Constructor privado + factory `Create()`."
- **Dónde se aplica (Código verificado):** `Note.cs:27-46` (`private Note() { }`, `{ get; private set; }`, colección por backing field `_attachments` + `AsReadOnly()`), `Note.Create(...)` devuelve `Result<Note>` (`Note.cs:51`).
- **Qué error evita:** lazy-loading accidental, estado mutable desde fuera.
- **Cómo probarlo:** T-DOM; revisión. No hay fitness genérica.
- **Vigencia:** Vigente.

#### 6. Un archivo, una clase
- **Texto original:** "VOs en su propio archivo bajo `Domain/ValueObjects/`."
- **Dónde se aplica (Código verificado):** VOs de Notes, uno por archivo. **Excepción real:** los contratos de integración agrupan varios `record` por archivo (`src/BuildingBlocks/Messaging/NotesIntegrationEvents/NotesIntegrationEvents.cs` contiene los 4 eventos de Notes).
- **Qué error evita:** archivos con 8 clases difíciles de encontrar y revisar.
- **Vigencia:** Parcialmente vigente: se cumple en Domain/Application; en `BuildingBlocks/Messaging` conviven ambos estilos. **Decisión pendiente:** si los contratos nuevos van un `record` por archivo.

#### 7. Repositorios devuelven `Result<T>` para lookups que pueden fallar
- **Texto original:** "`null` solo cuando 'no existe' es semánticamente distinto de 'error'."
- **Dónde se aplica (Código verificado):** `NoteRepository.GetByIdAsync` devuelve `Note?` (`src/Services/Notes/TaxVision.Notes.Infrastructure/Persistence/Repositories/NoteRepository.cs:11-16`): el repo de referencia usa `null` para "no existe" y el handler convierte a `NoteErrors.NotFound`.
- **Vigencia:** Parcialmente vigente: el uso dominante es `T?` + `Result` en el handler. **Recomendación:** documentarlo como la forma por defecto y reservar `Result<T>` en el repo para lookups con regla de negocio.

### 🗄️ EF Core / Persistencia

#### 8. `IgnoreQueryFilters()` explícito cuando el tenant ya viene validado por parámetro
- **Texto original:** "Un handler de Wolverine sin request HTTP corre en otro scope de DI y el filtro global tira aunque el `tenantId` sea válido."
- **Qué significa:** en métodos de repositorio llamados desde handlers (`bus.InvokeAsync`), consumers o jobs, se apaga el filtro global y se filtra con el `tenantId` explícito.
- **Por qué existe:** bug de CloudStorage `ScanFileHandler` (Wolverine ejecutó middleware y handler en scopes distintos) y endpoints que devolvían vacío (`memory/feedback_ef_query_filter_wolverine_scope_mismatch.md`; `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md:10-18`).
- **Dónde se aplica (Código verificado):** `NoteRepository.cs:11-16,44-51` (comentario + `.IgnoreQueryFilters()` + `TenantId == tenantId`).
- **Qué error evita:** consultas que devuelven 0 filas en silencio; 404 falsos.
- **Cómo probarlo:** prueba de repositorio con un `ITenantContext` sin tenant que aun así encuentra la fila del `tenantId` pasado; y su par negativo (otro tenant → `null`).
- **Vigencia:** Vigente. **Regla de seguridad asociada:** `IgnoreQueryFilters()` solo con una de las 4 condiciones seguras de `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md:143-201`; si es el único control de aislamiento, es un IDOR.

#### 9. Propiedades computadas necesitan `builder.Ignore()`
- **Texto original:** "Un `AsReadOnly()` se auto-descubre como navegación y rompe migraciones."
- **Dónde se aplica (Código verificado):** `src/Services/Auth/Infrastructure/Persistence/Configurations/InvitationConfiguration.cs:32`, `OffboardingRunConfiguration.cs:19-21`; `CustomerConfiguration.cs:18` (`b.Ignore(c => c.DomainEvents)`, exigido por `UseTaxVisionDomainEvents`).
- **Qué error evita:** migraciones con tablas/columnas fantasma o fallo al generar la migración.
- **Cómo probarlo:** generar la migración y revisar que no aparezcan columnas inesperadas; `dotnet ef migrations has-pending-model-changes` (**Recomendación**, no verificado en el repo).
- **Vigencia:** Vigente.

#### 10. `ValueGeneratedNever()` en PKs `Guid` de colecciones de navegación
- **Texto original:** "Sin eso EF genera `UPDATE` en vez de `INSERT`."
- **Dónde se aplica (Código verificado):** `src/Services/Notes/TaxVision.Notes.Infrastructure/Persistence/Configurations/NoteAttachmentConfiguration.cs:14-16`.
- **Por qué existe:** `CustomerAssignment.Create` con `Id = Guid.NewGuid()` tumbó la asignación de clientes en producción (`memory/feedback_ef_clave_preasignada_genera_update_no_insert.md`).
- **Qué error evita:** `DbUpdateConcurrencyException` al agregar una hija a un agregado cargado.
- **Cómo probarlo:** prueba contra DbContext real (EF InMemory basta) que afirme `EntityState.Added` y relea en un contexto nuevo. **No** ponerla en una carpeta `Persistence/`: el filtro del gate excluye `.Persistence.`.
- **Vigencia:** Vigente.

#### 11. Un DbContext vacío igual necesita migración inicial
- **Texto original:** "O el envelope-storage de Wolverine falla en el primer boot."
- **Dónde se aplica (Código verificado):** Notes nace con `20260806125110_InitialCreate` (`src/Services/Notes/TaxVision.Notes.Infrastructure/Persistence/Migrations/`).
- **Por qué existe:** Connectors arrancó sin base ("Cannot open database") porque `dotnet ef database update` sin migraciones no crea la base (`memory/feedback_ef_core_wolverine_initial_migration.md`).
- **Cómo probarlo:** arrancar el servicio contra una base nueva tras `migrate-local.ps1 -Apply`.
- **Vigencia:** Vigente.

#### 12. Filtro global de tenant: fail-closed
- **Texto original:** "Sin tenant, comparar contra `Guid.Empty` (0 filas), nunca omitir el filtro."
- **Dónde se aplica (Código verificado):** `NotesDbContext.cs:63` (`EffectiveTenantId => HasTenant ? TenantId : Guid.Empty`) y `:70-86` (`ApplyFailClosedTenantFilter` sobre todo `ITenantOwned`). Los 26 DbContext declaran filtro (conteo de `HasQueryFilter`/`ApplyFailClosedTenantFilter` por DbContext).
- **Contradicción documental:** el comentario de `src/BuildingBlocks/Tenancy/ITenantContext.cs:3-8` dice que sin tenant "los DbContexts … deben degradar a 'sin filtro'". El código y el guardrail hacen lo contrario (fail-closed). **Recomendación:** la guía debe seguir al código; el comentario de BuildingBlocks queda como deuda documental.
- **Cómo probarlo:** `deploy/tests/TaxVision.Auth.Tests/Persistence/TenantIsolationTests.cs` (patrón).
- **Vigencia:** Vigente.

### 📨 Wolverine / Mensajería

#### 13. `PublishMessage<T>().ToRabbitExchange(...)` por cada tipo de evento
- **Texto original:** "Sin esa línea, `bus.PublishAsync` lo descarta en silencio."
- **Dónde se aplica (Código verificado):** `src/Services/Notes/TaxVision.Notes.Api/Program.cs:150-156`.
- **Por qué existe:** eventos huérfanos repetidos (Correspondence, Connectors, CloudStorage; 3 eventos de Calendar sin ruta encontrados el 2026-11-13 — `memory/feedback_wolverine_evento_nuevo_necesita_ruta.md`).
- **Cómo probarlo:** prueba de rutas sobre `runtime.RoutingFor(type).RouteForPublish(...)` que exija `rabbitmq://`. **Solo Calendar la tiene** (`IntegrationEventRoutingTests`, referenciada en la memoria). Ante "no llegó", buscar "No routes can be determined" en el log del emisor.
- **Vigencia:** Vigente, **sin fitness transversal**. **Recomendación:** fitness por servicio que recorra los tipos pasados a `PublishAsync`.

#### 14. Consumer sin cola bindeada = handler que nunca corre
- **Dónde se aplica (Código verificado):** `Notes/Program.cs:169-171` (`ListenToRabbitQueue("notes-events", q => q.BindExchange("taxvision-events", string.Empty)).UseDurableInbox()`).
- **Matiz:** existen colas punto a punto **no** enlazadas al fanout para comandos (`customer-client-form-commands`, `cloudstorage-file-deletions`: `src/Services/Customer/TaxVision.Customer.Api/Program.cs:184,210`).
- **Vigencia:** Vigente.

#### 15. `ICorrelationContext.Push(evt.CorrelationId)` en la primera línea de todo consumer
- **Dónde se aplica (Código verificado):** `src/Services/Notes/TaxVision.Notes.Application/Notes/Consumers/NotesFileScanResultConsumer.cs:32-36` (con fallback a `EventId`).
- **Prueba relacionada:** `deploy/tests/TaxVision.BuildingBlocks.Tests/Architecture/CorrelationIdFitnessTests.cs` vigila que **los publicadores** pongan `CorrelationId` (no el `Push` del consumer).
- **Vigencia:** Vigente. El `Push` del consumer no tiene fitness.

#### 16. Orden de middleware: `JwtTenantContextMiddleware` antes de `UseAuthorization()`
- **Dónde se aplica (Código verificado):** `Notes/Program.cs:197-205`; `Customer/Program.cs` (mismo orden).
- **Por qué existe:** al revés, `[HasPermission]` en modo Projection corre sin tenant → 403/vacío (hotfix aplicado a 11 servicios, `Reminder/04_Guardrails...md:75-76`).
- **Cómo probarlo:** `deploy/tests/TaxVision.BuildingBlocks.Tests/Tenancy/JwtTenantContextMiddlewareTests.cs`; prueba E2E con login real.
- **Vigencia:** Vigente.

### 🔐 Multi-tenancy / RBAC / Seguridad

#### 17. `Authorize(Roles=)` ≠ `AllowActorTypes`
- **Dónde se aplica (Código verificado):** `NotesController` usa `[AllowActorTypes(...)]` + `[HasPermission(...)]` (`src/Services/Notes/TaxVision.Notes.Api/Controllers/NotesController.cs:53-55,91-92`). Fitness: `NotesArchitectureTests.Controller_actions_should_declare_AllowActorTypes` y `*ActorTypeArchitectureTests` en 10 proyectos de pruebas.
- **Vigencia:** Vigente.

#### 18. God-role explícito, no implícito
- **Dónde se aplica (Código verificado):** flag `IsDangerous` en `src/Services/Auth/Domain/Roles/PermissionCatalog.cs` (7 permisos marcados; comentarios en `:324-334`).
- **Vigencia:** Vigente.

#### 19. M2M no usa la staleness de humanos
- **Dónde se aplica (Código verificado):** `src/BuildingBlocks/BuildingBlocks.Web/ActorTypeAuthorization/ProjectionPermissionsSource.cs:56` (`if (user.GetActorType() == ActorType.Service)` → lee el claim directo).
- **Vigencia:** Vigente.

#### 20. Todo lector pasa por `IUserPermissionsSource`
- **Dónde se aplica (Código verificado):** `Notes/Program.cs:69-72` (`AddUserPermissionsSource(...)` revienta al arrancar si hay `[HasPermission]` y la fuente no es `Projection`).
- **Vigencia:** Vigente.

#### 21. Session denylist con Redis en todos los servicios
- **Dónde se aplica (Código verificado):** `AddSessionDenylist` en 25 `Program.cs`; Auth registra su propio lector y monta `SessionDenylistMiddleware` (`src/Services/Auth/Api/Program.cs:101-103,661`). El texto original decía "14+"; hoy son los 26 .NET.
- **Cómo probarlo:** `deploy/tests/TaxVision.BuildingBlocks.Tests/Session/SessionDenylistMiddlewareTests.cs`.
- **Vigencia:** Vigente.

#### 22. Endpoints M2M+admin necesitan `PlatformOnly` explícito
- **Dónde se aplica (Código verificado):** flag `PlatformOnly` en `PermissionCatalog.cs` (p. ej. `:72,76,105`).
- **Vigencia:** Vigente. Complemento: ADR-019 (`documents/architecture/ADR_019_Internal_Surface_Not_Exposed_At_Gateway.md:33-64`) — la superficie `internal/*` no se enruta en el Gateway.

#### 23. Webhooks: comparación en tiempo constante + rechazo si no hay secreto
- **Dónde se aplica (Código verificado):** `src/BuildingBlocks/BuildingBlocks.Web/Webhooks/SharedSecretWebhookVerifier.cs:36` (`CryptographicOperations.FixedTimeEquals`).
- **Vigencia:** Vigente.

### 🔑 M2M / ServiceAuth / Config de despliegue

#### 24. Un permiso nuevo para un M2M va en 3 lugares
- **Texto original:** "`dotnet user-secrets`, `deploy/docker/docker-compose.yml` y, si el endpoint es nuevo, el catálogo de Auth."
- **Estado actual (Código verificado):** además del compose hay que añadir el secret al `.env` que arma `.github/workflows/deploy.yml`, que ahora tiene **dos jobs y dos environments** (`production` y `production-extra`, `.github/workflows/deploy.yml:13-21,559`).
- **Vigencia:** Vigente, con más lugares de los que dice el texto.

#### 25. Las claves de config M2M calzan EXACTO con el `SectionName`
- **Evidencia:** memoria `feedback_m2m_config_key_binding.md` (índice `index_feedback_secretos_cripto.md`); `Reminder/04_Guardrails...md:90-91`.
- **Vigencia:** Vigente.

#### 26. Token M2M cacheado en memoria: reiniciar Auth NO alcanza
- **Estado actual (Código verificado):** la adquisición de tokens vive en `src/BuildingBlocks/BuildingBlocks.Infrastructure/Security/ServiceTokenHttpAcquisition.cs` con `ExpiringValueCache` (`src/BuildingBlocks/Security/ExpiringValueCache.cs`); el token sigue cacheado en el proceso consumidor.
- **Vigencia:** Vigente con matiz: el "static `ConcurrentDictionary`" del texto fue **reemplazado** por la pieza común; la consecuencia (reiniciar también el consumidor o esperar la expiración) se mantiene.

### 🐳 Docker / CI-CD / Infraestructura

#### 27. Git Bash en Windows mangla paths Unix → `MSYS_NO_PATHCONV=1`
- **Vigencia:** Vigente para quien opere Docker a mano. **Nota de proceso:** las reglas de este repo prohíben que un agente toque Docker (`AGENTS.md`).

#### 28. Runner CI compartiendo host con producción = riesgo de OOM
- **Estado actual (Código verificado):** `deploy.yml` construye imágenes en tandas de 3 (`.github/workflows/deploy.yml`, paso "Build images", comentario sobre OOM/exit 137). Desde 2026-11-13 el deploy **no corre el gate de pruebas** por la misma causa (`memory/project_ci_sin_gate_de_pruebas.md`).
- **Vigencia:** Vigente; el gate se corre en local.

#### 29. PFX en Windows necesita `X509KeyStorageFlags.Exportable`
- **Dónde se aplica (Código verificado):** `src/Services/Signature/TaxVision.Signature.Infrastructure/Sealing/Cms/BouncyCastleCmsPdfSigner.cs:104`, `Sealing/Pades/PadesCmsSigner.cs:147`.
- **Vigencia:** Vigente (solo servicios que firman).

### 🧪 Testing

#### 30. `MeterListener` + xUnit en paralelo necesitan `[Collection]` compartida
- **Dónde se aplica (Código verificado):** `deploy/tests/TaxVision.BuildingBlocks.Tests/ActorTypeAuthorization/AuthorizationMetricsCollection.cs`.
- **Vigencia:** Vigente.

#### 31. Llamada bare a un extension method requiere `this.`
- **Dónde se aplica (Código verificado):** `this.TryGetTenantAndUser(...)` en `NotesController.cs:96`.
- **Vigencia:** Vigente (regla del lenguaje C#).

### 📄 Integraciones de terceros

#### 32. PdfSharp 6.x necesita `IFontResolver` registrado al boot
- **Dónde se aplica (Código verificado):** `src/Services/Signature/TaxVision.Signature.Infrastructure/DependencyInjection.cs:254-255`. Lección relacionada: el CI Linux no trae cursivas (`memory/feedback_ci_linux_sin_cursivas_fuentes_pdf.md`).
- **Vigencia:** Vigente (Signature, Documents usan PdfSharp 6.2.0).

#### 33. RFC 3161 (TSA) espera un digest SHA-256
- **Dónde se aplica (Código verificado, sin línea):** `src/Services/Signature/TaxVision.Signature.Infrastructure/Sealing/Cms/FreeTsaClient.cs`.
- **Vigencia:** Vigente (solo Signature).

### 🧭 Disciplina de proceso

#### 34. No documentar un gap como "conocido, no arreglado" sin revisar si es arreglable
- **Vigencia:** Vigente. Refuerzos en memoria: "medir el coste antes de diferir" (`index_feedback_metodo.md`).

#### 35. No acoplar a un vendor por un ejemplo ilustrativo
- **Vigencia:** Vigente (`index_feedback_proceso.md` → "Sin acoplamiento especulativo").

#### 36. Comentarios: sintetizar la razón técnica, no narrar el historial del bug
- **Texto original:** "Nada de 'Bug real de producción (fecha)…' — eso va en el commit. El comentario explica el invariante o el porqué no obvio, sin fecha, sin narrativa, sin referencias cruzadas a otras fases."
- **Estado actual (Código verificado):** **muy incumplido** en el código de referencia: `NotesDbContext.cs:19-28` ("Fase 2 agregó…", "RBAC Fase 5…"), `Notes/Program.cs:60-172`, `NotesController.cs:23-49`, `ErrorHttpMapping.cs:47-49,69-71`.
- **Contradicción documental:** `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md:303-315` exige un "estilo de comentario obligatorio" que **cita el bug** y otros archivos. Choca con este guardrail y con `AGENTS.md` ("Comentarios cortos y en español. No apilar comentarios").
- **Recomendación:** la guía maestra adopta el #36 + `AGENTS.md` como regla vigente; el formato del documento de `IgnoreQueryFilters` se reduce a "decir cuál de las 4 condiciones seguras aplica", sin historia.
- **Vigencia:** Vigente (incumplido por código heredado; no tocar en masa: viola "sin refactors extra").

---

## Guardrails complementarios (no forman parte de los 36)

| Prefijo propuesto | Fuente | Contenido | Vigencia |
|---|---|---|---|
| GC-01 | `DEVS/Guia_Creacion_Microservicio.md:1322` (#32 de esa guía) | Suite local verde solo con `-m:1` | Parcialmente vigente: las pruebas con infra real se aíslan en `.Integration.`/`.Persistence.`; el gate del CI excluye esas carpetas (`01-Convenciones-Pruebas.md`). |
| GC-02 | `DEVS/Guia_Creacion_Microservicio.md:1334` (#38) | Todo módulo con datos de empleado entra al retiro (`[UserReference]`, `UserReferenceContractTests`) | Vigente (Código verificado: `[UserReference]` en `Note.cs`). |
| GC-Q1…Q13 | `Reminder/04_Guardrails...md:53-69` | Quartz: `AdoJobStore`, misfire explícito, tablas `QRTZ_*` por migración EF, `Quartz.Serialization.SystemTextJson` | Vigente para servicios con Quartz (hoy solo Reminder, `Quartz 3.15.1`). |
| GC-TZ1…TZ10 | `Reminder/04_Guardrails...md:110-121` | Job técnico = UTC; job de negocio = zona del dueño; `UseUtcDateTimes()` al final de `OnModelCreating`; `AddTimeZoneSelfCheck()`; `TZ=Etc/UTC` en contenedores; Prisma con `SYSUTCDATETIME()` | Vigente con huecos: Calendar, Signature y Tasks usan su propio conversor UTC (`ConfigureConventions`) y no tienen `UtcDateTimesTests` (ver `Investigaciones/Migraciones.md`). |
| GC-W1 | `memory/feedback_wolverine_reintento_en_linea_filtra_mensajes.md`; `DEVS/Guia_BuildingBlocks_Ola2.md:437-486` (BB-14) | **Guardar antes de publicar** en handlers invocados con `InvokeAsync` | Vigente; fitness `deploy/tests/TaxVision.BuildingBlocks.Tests/Architecture/SaveBeforePublishTests.cs`. |
| GC-W2 | `memory/feedback_fanout_rabbit_sin_dueno_ruido_no_known_handler.md` (corrección del dueño 2026-10-21) | Publicar **siempre** a `taxvision-events`; no inventar exchanges/colas de destino | Vigente (ver contradicción en `02_COMPARATIVA_GUIAS.md`). |
| GC-T1 | `memory/feedback_tests_con_infra_real_van_en_namespace_integration.md` | Prueba con SQL/Rabbit real → namespace `.Integration.` | Vigente. |
| GC-M1 | `memory/feedback_permisos_catalogo_en_codigo_manda.md`, `feedback_no_conceder_permisos_sistema_por_migracion.md` | Permiso nuevo: `PermissionCatalog` + `InsertData` en `Permissions`; nunca `INSERT INTO RolePermissions` | Vigente. |

## Evidencia

Ver las rutas citadas en cada guardrail. Archivos principales: `Signature/AUDITORIA_SIGNATURE_FASE0.md:1910-2003` (texto original), `DEVS/Guia_Creacion_Microservicio.md:1267-1334`, `Reminder/04_Guardrails_Checklist_Y_Verificacion.md`, `memory/feedback_ddd_guardrails_taxvision.md`.

## Problemas

1. Tres numeraciones distintas (36 original, 38 en la guía antigua, categorías en Reminder).
2. #13 y #15 no tienen fitness transversal: dependen de revisión humana salvo en Calendar.
3. #36 está incumplido en casi todo el código de referencia y contradicho por una guía de `documents/`.
4. #12 contradicho por el comentario de `ITenantContext` en BuildingBlocks.

## Buenas prácticas

- Cada guardrail nuevo entra con su prueba (fitness, T-DOM o T-INT) y con el error real que lo motivó, en la memoria; el comentario de código no narra el error (#36).
- Una fitness que nunca se vio fallar no prueba nada: romperla a propósito, ver rojo, revertir (`Reminder/04_Guardrails...md:107-108`).

## Recomendación

- Publicar en la guía maestra **la tabla de 36 con la numeración original** + los complementarios con prefijo.
- Para un servicio nuevo, convertir en checklist de PR los guardrails 1-23 (los que tocan código) y GC-W1, GC-T1, GC-TZ.

## Riesgos

- Renumerar en la guía maestra rompe referencias cruzadas existentes ("guardrail 10" en comentarios de código como `NoteAttachmentConfiguration.cs:14`). **Decisión pendiente:** mantener la numeración original evita ese riesgo.

## Pruebas

| Guardrail | Prueba existente reutilizable |
|---|---|
| 1 | `HandlerSizeFitnessTests` (PaymentApp), `OnboardingHandlerSizeFitnessTests` (Auth) |
| 4 | regla `System.Linq` de `CalendarArchitectureTests` |
| 12 | `TenantIsolationTests` (Auth) |
| 13 | `IntegrationEventRoutingTests` (Calendar) |
| 15 (publicador) | `CorrelationIdFitnessTests` |
| 16 | `JwtTenantContextMiddlewareTests` |
| 17 | `*ActorTypeArchitectureTests`, `NotesArchitectureTests` |
| 21 | `SessionDenylistMiddlewareTests` |
| 30 | `AuthorizationMetricsCollection` |
| GC-W1 | `SaveBeforePublishTests` |

## Dependencias

`Investigaciones/Arquitectura.md` (orden de middleware, Program.cs), `Investigaciones/Migraciones.md` (#9-#12, UTC), `Investigaciones/BuildingBlocks.md` (piezas que implementan #13-#26).

## Material para la guía

**Checklist de PR (copiar a la guía):**

```
[ ] G1  Handle ≤ ~30 líneas; métodos privados por fase.
[ ] G2  Ningún ChangeStatus genérico; una transición = un método + su evento.
[ ] G3  Conceptos con reglas como VO (Create() → Result<T>).
[ ] G4  Sin System.Linq en Domain.
[ ] G5  Agregado sealed, ctor privado, private set, colecciones por backing field.
[ ] G8  IgnoreQueryFilters() solo con tenantId explícito o una de las 4 condiciones seguras.
[ ] G9  Propiedades calculadas con builder.Ignore().
[ ] G10 Hijas con Guid generado en dominio → ValueGeneratedNever().
[ ] G11 Migración InitialCreate aunque el DbContext esté vacío.
[ ] G12 Filtro de tenant fail-closed (Guid.Empty sin tenant).
[ ] G13 Un PublishMessage<T>().ToRabbitExchange("taxvision-events") por evento publicado.
[ ] G14 Cola <svc>-events enlazada al exchange con UseDurableInbox().
[ ] G15 using (correlation.Push(...)) primera línea de cada consumer.
[ ] G16 UseAuthentication → JwtTenantContextMiddleware → SessionDenylistMiddleware → UseAuthorization.
[ ] G17 [AllowActorTypes] + [HasPermission]; nunca [Authorize(Roles=...)].
[ ] G36 Comentarios cortos, en español, con el porqué; sin fases ni fechas.
[ ] GC-W1 Guardar antes de publicar.
```

**Fragmento real (G10 + G12), copiado de Notes:**

```csharp
// src/Services/Notes/TaxVision.Notes.Infrastructure/Persistence/Configurations/NoteAttachmentConfiguration.cs:16
builder.Property(a => a.Id).ValueGeneratedNever();

// src/Services/Notes/TaxVision.Notes.Infrastructure/Persistence/NotesDbContext.cs:63
private Guid EffectiveTenantId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;
```
