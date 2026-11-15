# Investigación — Persistencia y migraciones

> Fecha: 2026-11-15. Solo lectura. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\`.
> Etiquetas: **Código verificado** · **Documentación histórica** · **Recomendación** · **Decisión pendiente**.
> No se copian cadenas de conexión: se nombran las variables (`ConnectionStrings__Default`, `<SVC>_DB_CONNECTION`).

## Estado actual

### DbContext (Código verificado)

26 DbContexts, uno por servicio .NET, todos en `src/Services/<Svc>/…Infrastructure/Persistence/<Svc>DbContext.cs`. El de referencia (`src/Services/Notes/TaxVision.Notes.Infrastructure/Persistence/NotesDbContext.cs`) fija la forma:

| Elemento | Línea | Regla |
|---|---|---|
| `sealed class NotesDbContext(DbContextOptions<…> options, ITenantContext tenantContext) : DbContext(options), IUnitOfWork` | 29-31 | El DbContext **es** la unidad de trabajo; recibe `ITenantContext` para el filtro |
| `DbSet<T> X => Set<T>()` | 33-44 | Incluye proyecciones locales y las de kits (`CustomerAssignmentProjection`) |
| `ApplyConfigurationsFromAssembly(...)` → `ApplyCustomerAssignmentProjection()` → `ApplyFailClosedTenantFilter(...)` → `base.OnModelCreating` → `UseUtcDateTimes()` | 46-54 | `UseUtcDateTimes()` **al final**, para cubrir también lo que agregan los kits |
| `EffectiveTenantId => HasTenant ? TenantId : Guid.Empty` como miembro de la instancia | 63 | Cerrar la expresión sobre `this` (EF cachea el modelo por tipo; cerrar sobre el servicio congelaría el primer tenant) |
| Filtro sobre todo `ITenantOwned` construido con `Expression` | 70-86 | **Fail-closed**: sin tenant compara con `Guid.Empty` (0 filas) |
| `SaveChangesAsync` traduce `DbUpdateConcurrencyException` → `ConflictException("Persistence.ConcurrencyConflict")` y `SqlException` 2601/2627 → `ConflictException("Persistence.UniqueConstraint")` | 88-110 | `ExceptionHandlingMiddleware` responde 409; `UseTaxVisionConflictPolicy()` decide reintento en mensajes |

Variante con eventos de dominio: Customer mapea `b.Ignore(c => c.DomainEvents)` (`src/Services/Customer/TaxVision.Customer.Infrastructure/Persistence/Configurations/CustomerConfiguration.cs:18`), requisito de `UseTaxVisionDomainEvents()` junto a `AddDbContext` normal (no `AddDbContextWithWolverineIntegration`, `src/BuildingBlocks/BuildingBlocks.Web/Hosting/DomainEventDispatchExtensions.cs:17-22`).

Registro (Código verificado, `src/Services/Notes/TaxVision.Notes.Infrastructure/DependencyInjection.cs:42-48`): `AddDbContext<NotesDbContext>(o => o.UseSqlServer(connectionString))` con `ConnectionStrings:Default` obligatorio; `AddScoped<IUnitOfWork>(p => p.GetRequiredService<NotesDbContext>())`. Ningún DbContext usa `EnableRetryOnFailure` (requisito de `TransactionalJobUnit`, `DEVS/Guia_BuildingBlocks_Ola2.md:118-121`; el libro p. 23 explica que el modo transaccional de Wolverine es incompatible con esa estrategia).

### Mappings (Código verificado)

- Un `IEntityTypeConfiguration<T>` `sealed` por entidad en `Persistence/Configurations/`.
- `NoteConfiguration.cs`: `RowVersion` con `IsRowVersion()` (:23); VOs con `OwnsOne(...)` (:26, :41, :62); colección por backing field con `SetPropertyAccessMode(PropertyAccessMode.Field)` (:75); índices nombrados (:81-83).
- `NoteAttachmentConfiguration.cs`: `ValueGeneratedNever()` (:16), enum `HasConversion<int>()` (:23), índice único `(NoteId, CloudStorageFileId)` (:27).
- Propiedades calculadas: `builder.Ignore(...)` (`src/Services/Auth/Infrastructure/Persistence/Configurations/InvitationConfiguration.cs:32`, `OffboardingRunConfiguration.cs:19-21`).
- **Lección (memoria `feedback_enum_rename_hasconversion_string_necesita_valueconverter_con_alias.md`):** renombrar un miembro de enum mapeado con `HasConversion<string>()` exige un `ValueConverter` con alias legacy; un `UPDATE` de datos no basta (una fila huérfana tumba el arranque).

### Tenant query filters e `IgnoreQueryFilters` controlado

- **Código verificado:** los 26 DbContext declaran filtro de tenant (conteo de `HasQueryFilter`/`ApplyFailClosedTenantFilter`).
- **Regla (Documentación vigente, `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md:143-201`):** `IgnoreQueryFilters()` solo si se cumple al menos una: (a) el id sale del JWT del propio actor; (b) validación post-fetch `entity.TenantId != esperado`; (c) lookup por secreto global no adivinable (hash de token, idempotency key, referencia externa); (d) operación cross-tenant deliberada con gate estricto. Si es el único control de aislamiento → **no**; agregar `tenantId` a la firma.
- **Dónde hace falta (misma guía §7.1):** handlers vía `bus.InvokeAsync`, consumers, `BackgroundService`, endpoints `[AllowAnonymous]`. Wolverine ejecuta en un scope de DI nuevo cuyo `TenantContext` puede estar vacío aunque existan `LocalCommandTenantMiddleware`/`IntegrationEventTenantMiddleware`.
- **Ejemplo verificado:** `NoteRepository.GetByIdAsync` (`src/Services/Notes/TaxVision.Notes.Infrastructure/Persistence/Repositories/NoteRepository.cs:11-16`).

### `ValueGeneratedNever` y owned types

- **`ValueGeneratedNever()`** cuando la hija recibe `Id = Guid.NewGuid()` en el dominio y se agrega a un agregado ya cargado; si no, EF emite `UPDATE` sobre una fila inexistente (`memory/feedback_ef_clave_preasignada_genera_update_no_insert.md`; incidente `CustomerAssignment.Create`). Alternativa del repo: la hija deja `Id = Guid.Empty` (`CustomerContactPoint.Create`).
- **Un owned type no se comparte entre filas** (`memory/feedback_owned_type_no_se_comparte_entre_filas_hermanas.md`): pasar la misma instancia (`Money`, `TaskReference`) a N entidades deja columnas en NULL o lanza "is part of a key". La copia va **dentro de la factory del agregado**:
  - `src/Services/Subscription/TaxVision.Subscription.Domain/Seats/SubscriptionSeat.cs:69,141` (`Money.Create(unitPrice.Amount, unitPrice.Currency)`).
  - `src/Services/Tasks/TaxVision.Tasks.Domain/Tasks/TaskItem.cs:129` (`Reference = reference.CopyForNewRow()`), `ValueObjects/TaskReference.cs:40`.
  - Probar con cantidad ≥ 2 (con una fila el defecto es invisible).

### Design-time factory (Código verificado)

26 `*DbContextFactory : IDesignTimeDbContextFactory<T>`, en Infrastructure. Patrón Notes (`NotesDbContextFactory.cs:8-28`): lee `ConnectionStrings__Default` del entorno, con un valor local por defecto (no se reproduce aquí), `UseSqlServer`, y pasa un `ITenantContext` privado con `HasTenant = false` (`DesignTimeOnlyTenantContext`). Fuente de la conexión por servicio: 24 leen la variable de entorno; **CloudStorage** lee user-secrets y **FinancialInsights** lee ambas. Sin el `ITenantContext` falso, el factory no compila al agregar el filtro (`memory/feedback_dbcontext_tenantcontext_design_time_factory.md`).

### `UseUtcDateTimes()` (Código verificado)

- `src/BuildingBlocks/EfCore/UtcDateTimeConventions.cs:30-44`: a todo `DateTime`/`DateTime?` sin conversor y sin anotación civil le pone un conversor que lee con `DateTimeKind.Utc`; `IsCivilDateTime()` excluye fechas civiles guardadas como `DateTime`; `DateTimesNotReadAsUtc(model)` alimenta las pruebas.
- Adopción: 23 DbContext. **Calendar, Signature y Tasks** usan su propio `ConfigureConventions` con un `UtcDateTimeConverter` local (`CalendarDbContext.cs:85-104`, `SignatureDbContext.cs:112-116`) y **no** tienen `UtcDateTimesTests`.
- `IsCivilDateTime()` no tiene usos en `src/Services` (las fechas civiles se modelan como `DateOnly`, regla de zonas en `Reminder/04_Guardrails...md:115`).

### Migraciones: ubicación, nombres, cifras

- **Código verificado:** migraciones en `Infrastructure/Persistence/Migrations/` o `Infrastructure/Migrations/` (Billing); `scripts/migrate-local.ps1` busca por atributo `[Migration]`+`[DbContext]`, no por carpeta ("hay migraciones fuera de la carpeta Migrations").
- Cantidad (archivos de migración sin `Designer`/`Snapshot`): Auth 145, Signature 67, Subscription 55, Tasks 43, CloudStorage 37, Correspondence 37, Notification 36, …, Wallet 4.
- Nombres: hay migraciones históricas con número de fase (`RbacFase7UserPermissionsProjection`, `RateLimitFase2TenantPlanCodeProjection`). **Regla vigente (memoria `feedback_sin_numero_de_fase_en_identificadores.md`):** nombres nuevos sin fase; los Ids ya aplicados en BD **no se tocan**.
- **Migración inicial** aunque el DbContext esté vacío (guardrail #11): Wolverine necesita la base existente para sus tablas `wolverine_*`.
- Communication (Prisma): `src/Services/Communication/prisma/schema.prisma` (`provider = "sqlserver"`, `url = env("COMMUNICATION_DB_CONNECTION")`), 56 migraciones; las nuevas usan `SYSUTCDATETIME()` (prueba `src/Services/Communication/tests/unit/prisma-utc-defaults.test.ts`).

### Cómo se crean y aplican en local

**Código verificado — `scripts/migrate-local.ps1`:**
- Sin parámetros: **solo informe** de pendientes por servicio (EF y Prisma).
- `-Apply` (pregunta), `-Yes`, `-Service Calendar,Auth`, `-SkipEf`, `-SkipPrisma`, `-AllowRemote` (si no, solo servidores locales), `-Force` (aplica aunque la base tenga migraciones que el código no conoce), `-UseEfToDetect` (detecta con `dotnet ef migrations list`, compila).
- Conexión: como ASP.NET en Development → user-secrets del Api (`<UserSecretsId>`), luego `appsettings.Development.json`, luego `appsettings.json`. Nunca imprime contraseñas.
- Detección: compara los `[Migration("…")]` del código con `__EFMigrationsHistory` (SQL directo, sin compilar).
- Aplicar: exporta temporalmente `ConnectionStrings__Default` y ejecuta `dotnet ef database update --project <Infra> --startup-project <Infra> --context <Ctx>` (`scripts/migrate-local.ps1:171-179,340-355`); Prisma con `npx --no-install prisma migrate deploy`.
- Al final: "Restart the services you migrated if the fleet is running."

**Crear una migración (Documentación vigente):** `dotnet ef migrations add <Nombre> --project <Infra.csproj> --startup-project <Api.csproj> --context <Svc>DbContext` y aplicarla **en el mismo momento** (`AGENTS.md` regla 6; `memory/feedback_aplicar_migracion_de_inmediato.md`). La cadena de desarrollo sale de los **user-secrets del servicio**, no de `.env` (`memory/feedback_dev_local_connection_string_viene_de_user_secrets.md`). Con la flota corriendo, compilar en `--configuration Release` evita chocar con los binarios Debug en uso (`memory/feedback_migration_drift_causes_gateway_load_shedding_cascade.md`).

### Cómo se aplican en producción

**Código verificado (`.github/workflows/deploy.yml`):**
1. Job `stage` (environment `production`) arma el `.env` con los `<SVC>_DB_CONNECTION`; job `deploy` (environment `production-extra`) lo completa (`:13-21`, `:559`; FinancialInsights en `:595-601`).
2. "Build images" construye **con `--profile tools`** para recompilar la imagen `migrations` desde el checkout (comentario `:668-679`: antes se reutilizaba una imagen vieja y el paso "aplicaba" sin aplicar nada).
3. "Apply .NET EF Core migrations (25 servicios)": `docker compose … --profile tools run --rm migrations` (`:766-767`). La imagen (`deploy/docker/migrations/Dockerfile`) hace `dotnet tool restore`, `restore` y `build TaxVision.slnx -c Release`; el entrypoint `apply-migrations.sh` ejecuta por servicio `dotnet ef database update --project <Infra> --startup-project <Api> --configuration Release --no-build --connection "$<SVC>_DB_CONNECTION"` con `set -eu` (falta una variable → no migra nada). FinancialInsights solo si su variable no está vacía.
4. "Apply Communication Prisma migrations": `docker compose … run --rm communication-api npx prisma migrate deploy` (`:769-770`).
5. `up -d --remove-orphans` y healthcheck a `https://<dominio>/health/ready`.

**Los servicios no migran al arrancar (Código verificado):** no hay `Database.Migrate`, `MigrateAsync`, `EnsureCreated` ni `ApplyAllDatabaseChangesOnStartup` en `src/`. Lo que sí ocurre al arrancar es que Wolverine crea/actualiza sus propias tablas de mensajes (por eso hace falta que la base exista, guardrail #11).

**Contradicciones:**
- `deploy/docker/docker-compose.yml:160-163` ("como los servicios migran al arrancar…") y `memory/feedback_wolverine_transaccion_antes_del_handler.md` ("los servicios migran al iniciar") contradicen el código. **Recomendación:** la guía dice "el esquema EF lo aplica el contenedor `migrations` en el deploy; al arrancar solo Wolverine prepara sus tablas".
- `DEVS/Guia_Creacion_Microservicio.md:1122` dice que `apply-migrations.sh` migra "19" servicios; hoy son 25 + FinancialInsights.
- El libro (p. 22) muestra `ApplyMigrationsAsync()` al arrancar, pero en p. 65 recomienda lo contrario para producción ("Ejecútalas como un paso del despliegue"). TaxVision sigue lo segundo.

### Seeding (Código verificado)

| Mecanismo | Dónde | Uso |
|---|---|---|
| `HasData` en configuración EF | Auth (2), Customer (2), Subscription (2), Tenant (1), Wallet (1) | Datos de referencia estables |
| `InsertData`/`UpdateData` en migraciones | Auth (71 archivos), CloudStorage (5), Subscription (3), Tenant (2), Customer, Signature, Wallet | Catálogo de permisos (`Permissions`), políticas iniciales |
| Seeders/servicios de arranque idempotentes | `Scribe…/Seed/ScribeNotificationTemplateSeeder.cs`, `ScribeBaseLayoutSeeder.cs`, `ScribeSystemAssetSeeder.cs`; `Subscription…/Persistence/Subscription{Plan,Module,AddOn}CatalogSeeder.cs`, `SubscriptionSeatPricingSeeder.cs`; `Tenant.Api/Common/PlatformSettingsSeeder.cs`; `Postmaster…/Seed/SystemEmailProviderSeeder.cs`; `Tasks…/ClientForms/LibrarySeeder.cs`; Growth `PlatformOnboardingCodeSeeder`, `DefaultReferralProgramSeeder`; Auth `PlatformAdminBootstrapService`, `SystemRolePermissionsSyncService` | Datos que dependen de configuración o de otros servicios |

**Reglas de permisos (memoria vigente):** un permiso nuevo va en `PermissionCatalog` **y** en una migración que solo hace `InsertData` en `Permissions`; **nunca** `INSERT INTO RolePermissions` para roles de sistema. `SystemRolePermissionsSyncService` (Auth, al arrancar) concede, sube `PermissionsVersion` y publica `RolePermissionsChangedIntegrationEvent`; si la migración ya concedió, el sync no ve diferencia y las proyecciones nunca se enteran (`feedback_no_conceder_permisos_sistema_por_migracion.md`, `feedback_permisos_catalogo_en_codigo_manda.md`).

### Migración vs reconciliation

| | Migración | Reconciliation (reconciliación) |
|---|---|---|
| Qué cambia | Esquema (y datos de catálogo deterministas) | Datos de una **proyección local** que deben coincidir con la fuente de verdad de otro servicio |
| Cuándo corre | En el deploy (contenedor `migrations`) o a mano en local | En runtime, periódico o al arrancar, como hosted job |
| Registro | `__EFMigrationsHistory` | Ninguno global; idempotente por diseño |
| Fuente | Código del propio servicio | HTTP M2M al dueño del dato (Tenant, Customer, CloudStorage, Auth) |
| Fallo | Corta el deploy (`set -eu`) | Reintenta: `ReconciliationRetry.NextDelay` (2 min tras fallo, `src/BuildingBlocks/Common/ReconciliationRetry.cs`) |
| Ejemplos | `AddTenantTimeZoneProjection` (crea la tabla) | `TenantTimeZoneReconciliationJob` (BB TenantTimeZones), `CustomerAssignmentReconciliationJob` (BB CustomerVisibility), `CustomerDirectoryReconciliationJob` y `PendingAttachmentReconciliationJob` (Notes, `DependencyInjection.cs:88-106,131-150`) |

Una **reposición (backfill)** de datos de negocio es una tercera cosa: la decide el dueño (`AGENTS.md`: "Las reposiciones y los ajustes de dinero los decide el dueño"; `DomainDrivenDesign/00-Rubrica.md`, "Impacto en datos").

## Evidencia

`NotesDbContext.cs`, `NotesDbContextFactory.cs`, `NoteConfiguration.cs`, `NoteAttachmentConfiguration.cs`, `src/BuildingBlocks/EfCore/UtcDateTimeConventions.cs`, `scripts/migrate-local.ps1`, `deploy/docker/migrations/{Dockerfile,apply-migrations.sh}`, `.github/workflows/deploy.yml:640-770`, memoria `index_feedback_ef_core.md` y lecciones enlazadas.

## Problemas

1. Comentarios y memoria que afirman "los servicios migran al arrancar" (falso para EF).
2. Calendar, Signature y Tasks fuera de `UseUtcDateTimes()`/`UtcDateTimesTests`.
3. `startup-project` distinto entre local (`migrate-local.ps1`: Infrastructure) y producción (`apply-migrations.sh`: Api). Ambos funcionan porque el factory vive en Infrastructure, pero la configuración que lee cada uno difiere.
4. CloudStorage es el único factory que no lee `ConnectionStrings__Default`; `migrate-local.ps1` exporta esa variable, así que en CloudStorage la conexión real sale de user-secrets (mismo valor en la práctica, pero otra ruta).
5. Un servicio nuevo exige tocar **cuatro** lugares para que su esquema llegue a producción: `apply-migrations.sh`, servicio `migrations` del compose, servicio `<svc>-api` del compose y el `.env` de `deploy.yml` (precedente documentado en el compose: Billing/Documents faltaban y el script moría con "unbound variable").
6. Migraciones con número de fase ya aplicadas (no renombrar).

## Buenas prácticas

- `UseUtcDateTimes()` como **última** línea de `OnModelCreating`.
- Filtro fail-closed sobre `this`; `IgnoreQueryFilters()` solo con una de las 4 condiciones y un comentario breve que diga cuál.
- Copias de owned types dentro de la factory del agregado; pruebas con ≥ 2 filas.
- Antes de un índice único sobre una tabla con datos: consulta previa de duplicados (`DomainDrivenDesign/01-Convenciones-Pruebas.md`). Agregar `rowversion` a una tabla grande bloquea la tabla mientras la llena: ventana de mantenimiento (`00-Rubrica.md`, hechos técnicos).

## Recomendación

- La guía maestra documenta un único flujo: crear con `dotnet ef migrations add … --project <Infra> --startup-project <Api> --context <Ctx>`, aplicar con `scripts/migrate-local.ps1 -Apply -Service <Svc>`, verificar con `scripts/migrate-local.ps1` (informe vacío).
- **Decisión pendiente:** alinear Calendar/Signature/Tasks a `UseUtcDateTimes()` + `UtcDateTimesTests` (es un ítem de código, fuera de esta investigación).
- **Decisión pendiente:** corregir el comentario del compose y la lección de memoria sobre "migran al arrancar".

## Riesgos

- Migración generada y no aplicada → `Invalid column name` lento (21-190 s) → p99 alto → **503 de load shedding en toda la flota** (`memory/feedback_migration_drift_causes_gateway_load_shedding_cascade.md`).
- Servicio nuevo sin bloque en `apply-migrations.sh` → deploy en verde sin esquema.
- Imagen `migrations` sin reconstruir → "aplica" sin aplicar nada (corregido con `--profile tools` en el build).
- Permisos concedidos por migración → proyecciones sin el permiso nuevo.

## Pruebas

| Riesgo | Prueba | Dónde |
|---|---|---|
| `DateTime` no leído como UTC | `UtcDateTimesTests.Every_datetime_column_is_read_as_utc` | `deploy/tests/TaxVision.Notes.Tests/Architecture/` (copiar) |
| Hija nueva hace `UPDATE` | DbContext real (InMemory) que afirme `EntityState.Added` y relea en un contexto nuevo — **fuera** de carpetas `Persistence/` (el gate las excluye) | `memory/feedback_ef_clave_preasignada_genera_update_no_insert.md` |
| Owned compartido | Crear ≥ 2 filas en una operación | `TaskItemOwnedReferenceTests` (Tasks) |
| Migración con datos previos | `MigrationTestDatabase` (migra hasta antes, siembra, migra el resto; usado por Calendar, Correspondence, PaymentApp, PaymentClient) | `deploy/tests/TaxVision.BuildingBlocks.Testing/Integration/MigrationTestDatabase.cs` |
| Concurrencia / índices únicos / outbox atómico | T-INT con `TemporaryTestDatabase` en namespace `.Integration.` | `deploy/tests/TaxVision.BuildingBlocks.Testing/Integration/TemporaryTestDatabase.cs`; `01-Convenciones-Pruebas.md` |
| Aislamiento de tenant | `TenantIsolationTests` | `deploy/tests/TaxVision.Auth.Tests/Persistence/` |

## Dependencias

`Investigaciones/Arquitectura.md` (registro en `Program.cs`), `Investigaciones/BuildingBlocks.md` (EfCore, TenantTimeZones, CustomerVisibility, Jobs), `Referencias/Guardrails.md` (#8-#12, GC-TZ, GC-M1), `03_INVENTARIO_ARQUITECTURA.md` (variables `<SVC>_DB_CONNECTION`).

## Material para la guía

### Paso a paso — persistencia de un servicio nuevo

1. `Persistence/<Svc>DbContext.cs`: copiar la forma de `NotesDbContext` (fragmento abajo).
2. `Persistence/<Svc>DbContextFactory.cs`: copiar `NotesDbContextFactory` (variable `ConnectionStrings__Default` + `ITenantContext` de diseño).
3. `Persistence/Configurations/`: un archivo por entidad; `ValueGeneratedNever()` en hijas con Guid de dominio; `IsRowVersion()` en raíces con escritura concurrente; `Ignore()` en calculadas y `DomainEvents`.
4. `DependencyInjection.cs`: `AddDbContext` + `IUnitOfWork`.
5. Crear `InitialCreate` (aunque esté vacío) y aplicarlo: `scripts/migrate-local.ps1 -Apply -Service <Svc>`.
6. Producción: bloque `apply_migration` en `deploy/docker/migrations/apply-migrations.sh`; `<SVC>_DB_CONNECTION` (con `:?`) en el servicio `migrations` y en `<svc>-api` de `deploy/docker/docker-compose.yml`; la misma variable en el `.env` que arma `.github/workflows/deploy.yml` y el secret en el environment de GitHub (lo carga el dueño; nunca escribir el valor).
7. Pruebas: `UtcDateTimesTests`, prueba de `EntityState.Added`, T-INT si hay concurrencia o índice único.

### Fragmento real — DbContext (Notes, comentarios recortados)

```csharp
// src/Services/Notes/TaxVision.Notes.Infrastructure/Persistence/NotesDbContext.cs
public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<NoteAttachment> NoteAttachments => Set<NoteAttachment>();
    public DbSet<CustomerAssignmentProjection> CustomerAssignmentProjections => Set<CustomerAssignmentProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.ApplyCustomerAssignmentProjection();
        ApplyFailClosedTenantFilter(modelBuilder);
        base.OnModelCreating(modelBuilder);
        // Al final: también cubre las proyecciones y tipos agregados arriba.
        modelBuilder.UseUtcDateTimes();
    }

    private Guid EffectiveTenantId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;

    private void ApplyFailClosedTenantFilter(ModelBuilder modelBuilder)
    {
        var contextConstant = Expression.Constant(this);
        var effectiveTenantIdAccess = Expression.Property(contextConstant, nameof(EffectiveTenantId));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var tenantProperty = Expression.Property(parameter, nameof(ITenantOwned.TenantId));

            var filter = Expression.Lambda(Expression.Equal(tenantProperty, effectiveTenantIdAccess), parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException(
                "Persistence.ConcurrencyConflict",
                "The note was modified by someone else. Reload and try again.",
                ex
            );
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException(
                "Persistence.UniqueConstraint",
                "A record with the same unique values already exists.",
                ex
            );
        }
    }
}
```

### Fragmento real — design-time factory (Notes; valor por defecto omitido)

```csharp
// src/Services/Notes/TaxVision.Notes.Infrastructure/Persistence/NotesDbContextFactory.cs
public sealed class NotesDbContextFactory : IDesignTimeDbContextFactory<NotesDbContext>
{
    public NotesDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "<cadena local por defecto del servicio>";

        var options = new DbContextOptionsBuilder<NotesDbContext>().UseSqlServer(connectionString).Options;

        // dotnet-ef solo inspecciona el modelo, nunca ejecuta una query real.
        return new NotesDbContext(options, new DesignTimeOnlyTenantContext());
    }

    private sealed class DesignTimeOnlyTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
```

### Fragmento real — configuración de hija (Notes)

```csharp
// src/Services/Notes/TaxVision.Notes.Infrastructure/Persistence/Configurations/NoteAttachmentConfiguration.cs:9-28
public void Configure(EntityTypeBuilder<NoteAttachment> builder)
{
    builder.ToTable("NoteAttachments");
    builder.HasKey(a => a.Id);
    builder.Property(a => a.Id).ValueGeneratedNever();

    builder.Property(a => a.NoteId).IsRequired();
    builder.Property(a => a.CloudStorageFileId).IsRequired();
    builder.Property(a => a.DisplayName).HasMaxLength(260).IsRequired();
    builder.Property(a => a.ContentType).HasMaxLength(200).IsRequired();
    builder.Property(a => a.SizeBytes).IsRequired();
    builder.Property(a => a.Status).HasConversion<int>().IsRequired();
    builder.Property(a => a.RejectionReason).HasMaxLength(500);
    builder.Property(a => a.LinkedAtUtc).IsRequired();

    builder.HasIndex(a => new { a.NoteId, a.CloudStorageFileId }).IsUnique();
}
```

### Fragmento real — bloque de producción (`apply-migrations.sh`)

```sh
apply_migration() {
  name="$1"
  project="$2"
  startup="$3"
  connection="$4"

  echo "Applying ${name} migrations..."
  dotnet ef database update \
    --project "$project" \
    --startup-project "$startup" \
    --configuration Release \
    --no-build \
    --connection "$connection"
}

apply_migration \
  "Notes" \
  "src/Services/Notes/TaxVision.Notes.Infrastructure/TaxVision.Notes.Infrastructure.csproj" \
  "src/Services/Notes/TaxVision.Notes.Api/TaxVision.Notes.Api.csproj" \
  "$NOTES_DB_CONNECTION"
```

### Comandos locales (sin valores sensibles)

```powershell
# Informe de pendientes (EF + Prisma), sin tocar nada
./scripts/migrate-local.ps1
# Aplicar solo un servicio, sin pregunta
./scripts/migrate-local.ps1 -Apply -Yes -Service Notes
```
