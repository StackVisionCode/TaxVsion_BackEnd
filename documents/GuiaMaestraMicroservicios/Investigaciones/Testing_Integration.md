# Testing — integración (T-INT), persistencia y hosts de prueba

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd`. No se ejecutó ninguna prueba ni se tocó ninguna base. Etiquetas: **[Código verificado]**, **[Doc. histórica]**, **[Recomendación]**, **[Decisión pendiente]**.

## 1. Estado actual

- Las pruebas que tocan infraestructura real (SQL Server, RabbitMQ, Redis) viven en un namespace con `.Integration.` (o `.Persistence.`) y **no corren en el gate**; se corren a mano contra la infraestructura local: `dotnet test ... --filter "FullyQualifiedName~.Integration."`. **[Doc. histórica]** `DomainDrivenDesign/01-Convenciones-Pruebas.md:11,19`; `AGENTS.md` (punto 5)
- Unos 390 archivos de prueba declaran namespace `.Integration.` (suma de la tabla de `Testing_Unit.md` §2.1). **[Código verificado]**
- Base de las T-INT: **BB-10, base temporal por corrida** (`TemporaryTestDatabase`), creada en el SQL Server local cuya ubicación sale de una variable de entorno o de los user-secrets del Api. **No hay Testcontainers**. **[Código verificado]**
- Dos tipos de `WebApplicationFactory<Program>` por servicio: `<Svc>ApiFactory` (host real con base temporal, en `.Integration.`) y `<Svc>RoutingHostFactory` (sin base, sin Rabbit, sin jobs, entorno `Testing`, **apto para el gate**). **[Código verificado]**

## 2. Evidencia

### 2.1 Filtro del gate y por qué existe

- Filtro: `FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence.&FullyQualifiedName!~ReminderRetentionJobTests`. **[Doc. histórica]** `AGENTS.md`
- `ReminderRetentionJobTests` (`deploy/tests/TaxVision.Reminder.Tests/Scheduling/ReminderRetentionJobTests.cs:15,41`) usa `UseSqlServer(ConnectionString)` porque `ExecuteDeleteAsync` no existe en InMemory, y vive fuera de `.Integration.`: por eso se excluye por nombre. **[Código verificado]**
- Lección: el 2026-10-29 el deploy falló con 6 pruebas de Tasks ClientForms (`RabbitMq:Uri is missing`, `SqlException error 35`) que estaban fuera de `.Integration.`; se movieron a `ClientForms/Integration/`. **[Doc. histórica]** `memory/feedback_tests_con_infra_real_van_en_namespace_integration.md`
- Lección: una prueba de arquitectura que usaba la fábrica de integración (con `TemporaryTestDatabase`) pasaba en local (user-secrets) y fallaba en el runner Linux; para leer rutas de Wolverine se usa `<Svc>RoutingHostFactory`. **[Doc. histórica]** `memory/feedback_ci_linux_difiere_de_windows.md` punto 2

### 2.2 Base temporal (BB-10) **[Código verificado]**

`deploy/tests/TaxVision.BuildingBlocks.Testing/Integration/`:
- `TemporaryTestDatabase` (6-68): una base por proceso con nombre único (prefijo del servicio); la flota no la lee, así que una corrida abortada no deja filas ni sobres; borra bases huérfanas viejas del mismo prefijo; `CreateEmpty` para pruebas que crean sus tablas; `DropCreated` al final.
- `TemporaryDatabaseTestFramework` (7-11): framework de xUnit que borra las bases antes de cerrar la corrida. Se activa por ensamblado: `[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]`.
- `MigrationTestDatabase` (5-24): base propia para probar una migración con filas del esquema anterior (migra hasta antes, siembra con `InsertAsync`, migra el resto).

Uso real, Notes (`deploy/tests/TaxVision.Notes.Tests/Integration/NotesIntegrationDatabase.cs:1-48`):
```csharp
extern alias BbTesting;
using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;

[assembly: TestFramework(TemporaryDatabaseTestFramework.TypeName, TemporaryDatabaseTestFramework.AssemblyName)]

internal static class NotesIntegrationDatabase
{
    private static readonly TemporaryTestDatabase Database = new(
        "not_tint",
        () =>
            Environment.GetEnvironmentVariable("NOTES_DB_CONNECTION")
            ?? new ConfigurationBuilder().AddUserSecrets("<UserSecretsId del Api>").Build().GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set NOTES_DB_CONNECTION or the Api user-secrets to run this test."),
        connection =>
        {
            using var db = new NotesDbContext(new DbContextOptionsBuilder<NotesDbContext>().UseSqlServer(connection).Options, new NoTenant());
            db.Database.Migrate();
        }
    );
    public static string ConnectionString => Database.ConnectionString;
}
```
Lo usan 22 proyectos (incluido BuildingBlocks.Tests; todos salvo Billing, Catalog, ContentScanner, DataCollection, FinancialInsights, Gateway, Growth, Postmaster). **[Código verificado]**

### 2.3 Fábricas de host **[Código verificado]**

**Host real (T-INT)** — `deploy/tests/TaxVision.Notes.Tests/Integration/NotesApiFactory.cs:22-65`:
- `UseEnvironment("Development")`, `UseSetting("ConnectionStrings:Default", NotesIntegrationDatabase.ConnectionString)`.
- Sustituye solo la firma JWT: `Jwt:PublicKeyPath`/`Jwt:PublicKeyPem` vacíos y `Jwt:Secret` HS256 aleatorio en memoria (nunca leído de un secreto real).
- `ConfigureTestServices`: `services.DisableAllExternalWolverineTransports()` ("la cola notes-events es la de la flota y este host le quitaría mensajes") y quita los `IHostedService` de jobs que llaman a otros servicios.

**Host de rutas (gate)** — `deploy/tests/TaxVision.Notes.Tests/Architecture/NotesRoutingHostFactory.cs:14-33`:
- `UseEnvironment("Testing")`, cadenas falsas (`Server=127.0.0.1,1;...`, Redis `127.0.0.1:1,abortConnect=false`, Rabbit `amqp://guest:guest@127.0.0.1:1`).
- `DisableAllExternalWolverineTransports()`, `DisableAllWolverineMessagePersistence()`, quita todos los `IHostedService` cuyo namespace empieza por `TaxVision.`.
- "No se conecta a nada, así que corre en el gate del CI" (11-12).

### 2.4 Clasificación por base (fitness) **[Código verificado]**

`BuildingBlocks.Testing/Architecture/IntegrationIsolationRules.cs:28-96`: clasifica cada archivo de prueba por la base que usa (la de la flota vía user-secrets sin cambiar catálogo, o una temporal) y reporta hallazgos: host real sobre la base de la flota con durabilidad activa, host con Rabbit encendido, jobs propios encendidos, sobres en el esquema que lee la flota, filas escritas en la base de la flota (BIL-14). Lo aplican `BuildingBlocks.Tests/Architecture/IntegrationIsolationTests.cs`, `FinancialInsightsIntegrationIsolationTests`, `BillingIntegrationTestIsolationTests`.

### 2.5 Patrón T-INT de concurrencia y atomicidad

- Notes (`Integration/NoteConcurrencyIntegrationTests.cs:18-99`): `IAsyncLifetime`, tenant propio `Guid.NewGuid()`, base propia con prefijo, escenarios `TwoAttachmentsOnNoteWith19_SecondConflicts`, `ConcurrentContentEdits_SecondConflicts`. **[Código verificado]**
- Wallet (`Integration/WalletDomainEventsIntegrationTests.cs:31-108`): host mínimo de Wolverine contra el SQL local, sobres en un esquema propio que se borra al terminar y transporte a un puerto TCP cerrado; afirma `ExpireReservation_EnvelopeStoredWithRow` y `ExpireReservation_FailureAfterSave_LeavesNoRowNorEnvelope` contando filas en `wolverine_outgoing_envelopes`. **[Código verificado]**
- Regla: "Nada de RabbitMQ real en las pruebas. La atomicidad del outbox se prueba en SQL" (`01-Convenciones-Pruebas.md:20`). **[Doc. histórica]** Excepción explícita: pruebas de caída en `BuildingBlocks.Tests/Integration/BrokerOutageIntegrationTests.cs:14-18` y `RedisOutageIntegrationTests` usan RabbitMQ/Redis locales a través de `TcpChaosProxy`, con exchange, cola y esquema propios borrados al terminar. **[Código verificado]**

### 2.6 `.Persistence.` **[Código verificado]**

65 archivos con namespace `.Persistence.`: 50 solo EF InMemory (no necesitan infraestructura), 11 con SQL Server real (Calendar `SqlServerFixture`, Customer, Reminder, Tasks), el resto sin base. Todos excluidos del gate.

### 2.7 Ejecución

- Local (con SQL Server, RabbitMQ y Redis locales arriba; no hace falta la flota):
  ```powershell
  dotnet test deploy/tests/TaxVision.<Svc>.Tests/TaxVision.<Svc>.Tests.csproj --nologo --filter "FullyQualifiedName~.Integration."
  ```
  **[Doc. histórica]** `01-Convenciones-Pruebas.md:19`
- Si un handler recibe una dependencia nueva, correr las `.Integration.` del servicio: los hosts se arman a mano y el gate no lo detecta. **[Doc. histórica]** `MEMORY.md` (índice: `feedback_dependencia_nueva_correr_integration_del_servicio.md`)
- CI: no se ejecutan (el CI no corre pruebas). `deploy/tests/Dockerfile:7` corre toda la solución sin filtro y fallaría sin SQL. **[Código verificado]**

## 3. Problemas

1. **InMemory en `.Persistence.` fuera del gate** (50 archivos): no detecta regresiones en el gate y tampoco prueba SQL real. **[Código verificado]**
2. **Convención doble para la base**: el documento de convenciones aún describe "tenant propio y limpieza en `DisposeAsync`" sobre la base local (`01-Convenciones-Pruebas.md:11`), mientras el código migró a BB-10 (base temporal). **[Doc. histórica]** vs **[Código verificado]**
3. **5 proyectos con `.Integration.` y sin `TemporaryTestDatabase`** (Billing, Catalog, ContentScanner, Growth, Postmaster): sus T-INT pueden escribir en la base de la flota; `IntegrationIsolationRules` es la red de seguridad. **[Código verificado]** **[Decisión pendiente]** revisar caso por caso.
4. **Sin ejecución periódica** de las T-INT: dependen de que alguien las corra. **[Código verificado]**
5. Nombres de UserSecretsId embebidos en las pruebas (acopla la prueba al csproj del Api). **[Código verificado]**

## 4. Buenas prácticas ya vigentes

- La flota local nunca comparte sobres ni filas con una T-INT (base temporal + transportes apagados).
- Firma JWT de prueba generada en memoria; ningún secreto real.
- Atomicidad del outbox medida en SQL, sin broker.
- Fábrica separada para el gate (sin infraestructura).

## 5. Recomendación

- **[Recomendación]** Servicio nuevo: `<Svc>IntegrationDatabase` con `TemporaryTestDatabase` + atributo de ensamblado; `<Svc>ApiFactory` en `Integration/`; `<Svc>RoutingHostFactory` en `Architecture/` para todo lo que corra en el gate.
- **[Recomendación]** Variable de entorno `<SVC>_DB_CONNECTION` como primera fuente (permite correr sin user-secrets) y user-secrets del Api como respaldo.
- **[Decisión pendiente]** Mover las pruebas InMemory de `Persistence/` a una carpeta que entre al gate, o cambiar el filtro.

## 6. Riesgos

- Prueba con infraestructura fuera de `.Integration.` → gate rojo en Linux / deploy roto (pasó). **[Doc. histórica]**
- Host real con Rabbit encendido roba mensajes de la cola de la flota (`NotesApiFactory.cs:48-49`). **[Código verificado]**
- Migración probada solo con base vacía: usar `MigrationTestDatabase` cuando hay datos previos.
- Índice único nuevo sin consulta previa de duplicados (`01-Convenciones-Pruebas.md:22`). **[Doc. histórica]**

## 7. Pruebas

Ver 2.2-2.6. Las de arquitectura (`IntegrationIsolationTests`) sí corren en el gate.

## 8. Dependencias

- SQL Server local + user-secrets del Api; RabbitMQ/Redis locales solo para pruebas de caída.
- `TaxVision.BuildingBlocks.Testing` (alias `BbTesting`).

## 9. Material para la guía

### 9.1 Archivos mínimos de integración de un servicio nuevo

| Archivo | Namespace | Base |
|---|---|---|
| `Integration/<Svc>IntegrationDatabase.cs` | `TaxVision.<Svc>.Tests.Integration` | `TemporaryTestDatabase("<prefijo>_tint", ...)` + `[assembly: TestFramework(...)]` |
| `Integration/<Svc>ApiFactory.cs` | idem | host real, `Development`, JWT HS256 en memoria, transportes externos apagados, jobs quitados |
| `Integration/<Tema>IntegrationTests.cs` | idem | concurrencia, índices únicos, atomicidad |
| `Architecture/<Svc>RoutingHostFactory.cs` | `TaxVision.<Svc>.Tests.Architecture` | ninguna (apto para el gate) |

### 9.2 Prueba de atomicidad (forma)

1. Host mínimo de Wolverine con `PersistMessagesWithSqlServer` sobre la base temporal y transporte a un puerto cerrado.
2. Ejecutar el caso de uso → afirmar fila de negocio **y** un sobre del evento en `wolverine_outgoing_envelopes`.
3. Repetir con un fallo después de guardar → afirmar **ni fila ni sobre**.

Fuente: `deploy/tests/TaxVision.Wallet.Tests/Integration/WalletDomainEventsIntegrationTests.cs:79-108`.

### 9.3 Comandos

```powershell
# Integración de un servicio (local)
$env:<SVC>_DB_CONNECTION = "<cadena local, nunca commiteada>"   # opcional si hay user-secrets
dotnet test deploy/tests/TaxVision.<Svc>.Tests/TaxVision.<Svc>.Tests.csproj --nologo --filter "FullyQualifiedName~.Integration."
```
Reportar el resultado junto al del gate (las T-INT no corren solas en ningún lado).
