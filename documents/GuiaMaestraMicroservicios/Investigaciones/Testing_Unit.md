# Testing — arquitectura general y pruebas unitarias

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd`. No se ejecutó ninguna prueba. Etiquetas: **[Código verificado]**, **[Doc. histórica]**, **[Recomendación]**, **[Decisión pendiente]**. Complementos: `Testing_Integration.md`, `Testing_Security.md`, `Testing_EDA.md`.

## 1. Estado actual

- `deploy/tests/` contiene **31 proyectos** registrados en `TaxVision.slnx:218-249`: **30 proyectos de prueba** (`TaxVision.<X>.Tests`) y **1 biblioteca** compartida (`TaxVision.BuildingBlocks.Testing`, `IsTestProject=false`). Más `deploy/tests/Shared/temporal-vectors.json` (vectores de tiempo, usado por `BuildingBlocks.Tests/TimeZones`). **[Código verificado]**
- Framework único: **xUnit 2.9.3**, `xunit.runner.visualstudio 3.1.5`, `Microsoft.NET.Test.Sdk 17.14.1`, `net10.0`. **No hay** Moq, NSubstitute, FluentAssertions, Testcontainers ni Respawn: dobles escritos a mano y `Assert` de xUnit. **[Código verificado]**
- Los servicios Node (Communication, Recorder, TranscriptWorker) no están en la solución: usan Vitest en su propia carpeta. **[Doc. histórica]** `memory/project_ci_sin_gate_de_pruebas.md`
- **El gate se corre en local**; el CI no ejecuta pruebas desde el 2026-11-13 (decisión del dueño). Gate exacto: `dotnet test TaxVision.slnx --nologo --filter "FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence.&FullyQualifiedName!~ReminderRetentionJobTests"`. **[Código verificado]** (deploy.yml sin `dotnet test`) + **[Doc. histórica]** `AGENTS.md`, `DomainDrivenDesign/01-Convenciones-Pruebas.md:19`

## 2. Evidencia

### 2.1 Inventario de los 31 proyectos **[Código verificado]**

Paquetes comunes a casi todos: xunit 2.9.3, runner 3.1.5, Test.Sdk 17.14.1. Columnas: archivos `.cs`, archivos con namespace `.Integration.` / `.Persistence.`, archivos con `WebApplicationFactory<`, paquetes adicionales.

| Proyecto | .cs | Int. | Pers. | WAF | Paquetes adicionales |
|---|---|---|---|---|---|
| Auth.Tests | 219 | 17 | 10 | 4 | EF InMemory, NetArchTest, Mvc.Testing, Jwt |
| Billing.Tests | 72 | 15 | 0 | 5 | EF InMemory, Mvc.Testing, Jwt, NetArchTest |
| BuildingBlocks.Testing (biblioteca) | 13 | – | – | – | NetArchTest, xunit.extensibility.*, xunit.assert, SqlClient 6.1.1, Pkcs 10.0.13 |
| BuildingBlocks.Tests | 144 | 10 | 0 | 1 | WolverineFx.RuntimeCompilation/SqlServer/EntityFrameworkCore 6.14.0, EF SqlServer, EF InMemory |
| Calendar.Tests | 102 | 17 | 4 | 3 | EF InMemory, NetArchTest, Mvc.Testing, Jwt, OpenTelemetry.Exporter.InMemory |
| Campaigns.Tests | 50 | 7 | 0 | 2 | Mvc.Testing, EF SqlServer, UserSecrets, Hosting, Wolverine EF/SqlServer/RuntimeCompilation |
| Catalog.Tests | 20 | 2 | 0 | 2 | Mvc.Testing |
| CloudStorage.Tests | 119 | 18 | 4 | 3 | Mvc.Testing, EF InMemory, NetArchTest, Jwt |
| Connectors.Tests | 116 | 20 | 3 | 3 | Mvc.Testing, EF InMemory, NetArchTest, Jwt |
| ContentScanner.Tests | 4 | 1 | 0 | 1 | Mvc.Testing |
| Correspondence.Tests | 145 | 21 | 9 | 4 | EF InMemory, NetArchTest, Mvc.Testing, Jwt |
| Customer.Tests | 90 | 17 | 9 | 6 | Mvc.Testing, EF InMemory, NetArchTest, Jwt |
| DataCollection.Engine.Tests | 15 | 0 | 0 | 0 | NetArchTest |
| Documents.Tests | 43 | 7 | 0 | 4 | EF InMemory, NetArchTest, Mvc.Testing, Jwt |
| FinancialInsights.Tests | 25 | 0 | 0 | 2 | EF InMemory, NetArchTest, Mvc.Testing |
| Gateway.Tests | 19 | 0 | 0 | 0 | (solo xUnit) |
| Growth.Tests | 65 | 6 | 1 | 3 | EF InMemory, NetArchTest, Mvc.Testing, Jwt |
| Inventory.Tests | 15 | 3 | 0 | 2 | Mvc.Testing, Wolverine EF/SqlServer/RuntimeCompilation |
| Notes.Tests | 39 | 7 | 1 | 3 | EF InMemory, NetArchTest, Mvc.Testing, Jwt |
| Notification.Tests | 136 | 14 | 1 | 4 | Mvc.Testing, EF InMemory, NetArchTest, Jwt |
| PaymentApp.Tests | 105 | 17 | 1 | 3 | EF InMemory, NetArchTest, Mvc.Testing, Jwt |
| PaymentClient.Tests | 91 | 12 | 1 | 3 | EF InMemory, NetArchTest, Mvc.Testing, Jwt |
| Postmaster.Tests | 86 | 12 | 1 | 3 | Mvc.Testing, EF InMemory, NetArchTest, Jwt |
| Reminder.Tests | 34 | 3 | 3 | 2 | EF InMemory, NetArchTest, Mvc.Testing, Jwt |
| Scribe.Tests | 80 | 10 | 2 | 4 | Mvc.Testing, EF InMemory, NetArchTest, Jwt |
| Signature.Tests | 149 | 25 | 8 | 3 | Mvc.Testing, EF InMemory, NetArchTest, Jwt |
| Sms.Tests | 56 | 8 | 0 | 2 | Mvc.Testing, EF InMemory |
| Subscription.Tests | 191 | 31 | 2 | 3 | EF InMemory, NetArchTest, Mvc.Testing, Jwt |
| Tasks.Tests | 190 | 15 | 5 | 6 | EF InMemory, NetArchTest, Mvc.Testing, Jwt |
| Tenant.Tests | 83 | 20 | 0 | 4 | Mvc.Testing, EF InMemory, NetArchTest, Jwt |
| Wallet.Tests | 45 | 13 | 0 | 2 | EF InMemory, NetArchTest, Mvc.Testing |

("Jwt" = `System.IdentityModel.Tokens.Jwt 8.19.1`; "Mvc.Testing" = `Microsoft.AspNetCore.Mvc.Testing 10.0.9`; "NetArchTest" = `NetArchTest.Rules 1.3.2`; "EF InMemory" = `Microsoft.EntityFrameworkCore.InMemory 10.0.9`.)

No hay `Directory.Build.props` ni `Directory.Packages.props`: cada csproj fija sus versiones. **[Código verificado]**

### 2.2 Tipos de prueba y carpetas **[Doc. histórica]** `DomainDrivenDesign/01-Convenciones-Pruebas.md:5-13`

| Código | Tipo | Carpeta |
|---|---|---|
| T-DOM | Unitaria de dominio, sin fakes ni base | `deploy/tests/TaxVision.<Svc>.Tests/Domain/<Clase>Tests.cs` |
| T-APP | Handler con repositorios falsos (publica lo que debe, no publica si falla) | `.../Application/<Handler>Tests.cs` |
| T-INT | SQL Server real, namespace `.Integration.` | `.../Integration/<Tema>IntegrationTests.cs` |
| T-ARQ | Fitness (NetArchTest, reflexión, lectura de fuente) | `.../Architecture/` o `BuildingBlocks.Tests/Architecture/` |
| T-VIVO | Flota local + Chrome del dueño | manual |

Estructura real (ejemplo Notes, **[Código verificado]**): `Api/`, `Application/`, `Architecture/`, `Bootstrap/`, `Domain/`, `Infrastructure/`, `Integration/`, `Permissions/`, `Persistence/`, `RateLimiting/`, `GlobalUsings.cs` (`global using Xunit;`). Otros servicios agregan carpetas por feature (Tasks: `ClientForms/`, `Series/`, `Templates/`...; Postmaster: `Sending/`, `Suppression/`...) y `Deploy/` para pruebas de wiring, `Fakes/` o `TestDoubles/` para dobles.

### 2.3 Patrón de prueba unitaria **[Código verificado]**

- **Dominio** (`deploy/tests/TaxVision.Notes.Tests/Domain/NoteTests.cs:6-37`): clase por agregado (`NoteTests`), helpers estáticos para VOs válidos (`NoteContent.Create(...).Value`), reloj inyectado (`DateTime` explícito en UTC), `Assert.True(result.IsSuccess)` sobre `Result`, un escenario por método (`AttachFile_UsesProvidedTimestamp`).
- **Application** (`Application/CreateNoteHandlerTests.cs:7-47` + `Application/NoteApplicationTestDoubles.cs:18-171`): handler estático llamado directo con `FakeNoteRepository`, `FakeCustomerDirectoryRepository`, `FakeOffboardedStaffRepository` (clases `internal sealed` que implementan los puertos en memoria). Nombres en inglés describiendo el escenario (`Create_fails_when_content_is_empty_after_sanitization`).
- **Persistencia con InMemory** (`Persistence/NotePersistenceTests.cs:11-32`): `UseInMemoryDatabase(nombre)` y un `ITenantContext` falso para probar mapeo y filtro por tenant. Ojo: vive en `.Persistence.` y **queda fuera del gate** (ver Problemas).

### 2.4 Fitness comunes por servicio (corren en el gate) **[Código verificado]**

| Prueba | Servicios que la tienen | Qué asegura |
|---|---|---|
| `<Svc>LayeringTests` (hereda `LayeringRulesBase`) | 26 (todos menos Gateway, ContentScanner, DataCollection) | Domain sin EF/ASP.NET/Wolverine; Application sin Infrastructure/EF (`BuildingBlocks.Testing/Architecture/LayeringRulesBase.cs:7-52`) |
| `PreGeneratedHandlerCodeTests` | 26 | Handlers cargan del código pregenerado y el código está al día (ver `Testing_EDA.md`) |
| `UtcDateTimesTests` | 23 (faltan Calendar, Signature, Tasks con ese nombre) | Todo `DateTime` del modelo EF se lee como UTC (`Notes.Tests/Architecture/UtcDateTimesTests.cs:9-21`, usa `UtcDateTimeConventions.DateTimesNotReadAsUtc`) |
| `LocalQueueDurabilityTests` | 19 | Colas locales durables |
| `DomainEventHandlerConventionTests` | 19 | Convención de handlers de eventos de dominio |
| `<Svc>ArchitectureTests` / `*ActorTypeArchitectureTests` | casi todos | Cada acción declara `[AllowActorTypes]` y `[RateLimit]`/`[RateLimitExempt]`; controllers solo dependen de Application (`Notes.Tests/Architecture/NotesArchitectureTests.cs:11-124`) |
| `HandlerSizeFitnessTests` y similares | Auth, PaymentApp, PaymentClient, Subscription, Tasks | Tope de líneas de `Handle` (CSharpier puede romperlo) |

Globales en `deploy/tests/TaxVision.BuildingBlocks.Tests/Architecture/`: `ClientIpForwardingFitnessTests`, `ConstructorBusTests`, `CorrelationIdFitnessTests`, `DomainEventEnvelopeRoundTripTests`, `DomainEventHandlerConventionTests`, `IntegrationIsolationTests`, `InternalRouteConventionFitnessTests`, `JobPublishingTests`, `LayeringRulesBaseTests`, `LocalQueueDurabilityTests`, `NamespaceAssemblyContractTests`, `NoPlanNameLogicFitnessTests`, `RepositoryInterfaceTests`, `SaveBeforePublishTests`, `TimeZoneInfraFitnessTests`; y `Deploy/` (ver `Docker_Deploy.md`).

### 2.5 Convenciones de nombres

- Clase por clase probada, sin números de fase: `SignatureRequestTests`, `SendSmsBatchHandlerTests`, `SignatureConcurrencyIntegrationTests`; un escenario por prueba con nombre descriptivo (`Sign_TwoSignersInParallel_CompletesRequest`). **[Doc. histórica]** `01-Convenciones-Pruebas.md:17-18`, `AGENTS.md`, memoria `feedback_sin_numero_de_fase_en_identificadores.md`
- Identificadores en inglés; comentarios cortos en español. **[Doc. histórica]** `AGENTS.md`
- Ciclo rojo → arreglo mínimo → verde → gate. **[Doc. histórica]** `01-Convenciones-Pruebas.md:3`

### 2.6 Alta del proyecto de pruebas de un servicio

- csproj real de Notes (`deploy/tests/TaxVision.Notes.Tests/TaxVision.Notes.Tests.csproj`): `net10.0`, `ImplicitUsings`, `Nullable`, `IsPackable=false`, `IsTestProject=true`; paquetes xunit/runner/Test.Sdk/EF InMemory/NetArchTest/Mvc.Testing/Jwt; `FrameworkReference Microsoft.AspNetCore.App`; referencias a Domain, Application, Infrastructure, Api del servicio, `BuildingBlocks.Messaging` y `TaxVision.BuildingBlocks.Testing` con `Aliases="BbTesting"` (se usa con `extern alias BbTesting;`). **[Código verificado]**
- Registro: una línea `<Project Path="deploy/tests/TaxVision.<Svc>.Tests/TaxVision.<Svc>.Tests.csproj" />` dentro de `<Folder Name="/deploy/tests/">` (`TaxVision.slnx:218-249`). Estar en el `.slnx` lo mete en el gate. **[Código verificado]**
- El Api debe terminar con `public partial class Program;` para `WebApplicationFactory<Program>` (`src/Services/Notes/TaxVision.Notes.Api/Program.cs`, última línea). **[Código verificado]**

## 3. Problemas

1. **50 de los 65 archivos con namespace `.Persistence.` usan solo EF InMemory** y quedan excluidos del gate por el filtro `!~.Persistence.`; nunca corren salvo a mano. El resto (Calendar, Customer, Reminder, Tasks) sí usa SQL real. **[Código verificado]** (clasificación por grep de `UseInMemoryDatabase` / `UseSqlServer`)
2. **Documentación desfasada**: `deploy/tests/README.md:3-13` lista 6 proyectos y dice que Billing no tiene pruebas; `01-Convenciones-Pruebas.md:24` dice "Wallet no tiene proyecto de pruebas" (hoy tiene 45 archivos). **[Doc. histórica]** vs **[Código verificado]**
3. **`UtcDateTimesTests` falta en Calendar, Signature y Tasks** con ese nombre (Calendar tiene `SeriesHaveNoStartUtcTests` en Persistence). **[Código verificado]** Puede estar cubierto de otra forma; **[Decisión pendiente]** confirmar.
4. **Versiones repetidas en 30 csproj** sin gestión central: subir xUnit exige tocar todos. **[Código verificado]**
5. **Nombres en español** en Gateway.Tests (`TodoClusterTieneActiveHealthCheckHabilitado`). **[Código verificado]**
6. Comentarios con números de fase en pruebas (`NotesArchitectureTests.cs:12` "Fase 9", `NotePersistenceTests.cs:12` "Checkpoint 2"). La regla prohíbe fases en identificadores; en comentarios no está escrito. **[Código verificado]** **[Decisión pendiente]**
7. La guía DEVS dice que la suite "solo pasa verde en serie (`-m:1`)" (Guardrail #32) (`DEVS/Guia_Creacion_Microservicio.md:337`); el gate de AGENTS.md no usa `-m:1`. **[Doc. histórica]** **[Decisión pendiente]**

## 4. Buenas prácticas ya vigentes

- Dominio probado sin infraestructura y con reloj explícito.
- Dobles a mano por puerto (sin librerías de mocks): legibles y sin magia.
- Piezas heredables en `BuildingBlocks.Testing` (capas, durabilidad, codegen) para que cada servicio solo declare sus ensamblados.
- Fitness que convierten incidentes en reglas (cada prueba explica el incidente en su `<summary>`).

## 5. Recomendación

- **[Recomendación]** Plantilla de proyecto para la guía: copiar `TaxVision.Notes.Tests.csproj` y las 4 pruebas mínimas de arquitectura (`<Svc>LayeringTests`, `<Svc>ArchitectureTests`, `PreGeneratedHandlerCodeTests` + `<Svc>RoutingHostFactory`, `UtcDateTimesTests`), más `LocalQueueDurabilityTests` si usa colas locales.
- **[Recomendación]** Pruebas con InMemory que no necesitan infraestructura: ponerlas en `Domain/`, `Application/` o `Infrastructure/`, **no** en `Persistence/`, para que corran en el gate.
- **[Decisión pendiente]** Revisar si el filtro `.Persistence.` debe seguir excluyendo pruebas InMemory.

## 6. Riesgos

- Verde local ≠ verde en Linux (nombres de archivo, fábricas con user-secrets fuera de `.Integration.`, cronómetros). **[Doc. histórica]** `memory/feedback_ci_linux_difiere_de_windows.md`
- CSharpier del dueño puede romper fitness de tamaño (`Handle ≤ 30` líneas). **[Doc. histórica]** `memory/feedback_csharpier_antes_del_gate.md`
- Pruebas que heredan la configuración del entorno miden el ambiente, no el comportamiento. **[Doc. histórica]** `memory/feedback_test_que_hereda_config_del_entorno_mide_el_ambiente.md`

## 7. Pruebas

Este documento describe la arquitectura; las pruebas concretas están en las tablas 2.1 y 2.4.

## 8. Dependencias

- `TaxVision.BuildingBlocks.Testing` (biblioteca), `BuildingBlocks.Messaging`, Api del servicio (`public partial class Program`).

## 9. Material para la guía

### 9.1 csproj de pruebas (real, Notes)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.9" />
    <PackageReference Include="NetArchTest.Rules" Version="1.3.2" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.9" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.19.1" />
  </ItemGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\src\Services\<Svc>\TaxVision.<Svc>.Domain\TaxVision.<Svc>.Domain.csproj" />
    <ProjectReference Include="..\..\..\src\Services\<Svc>\TaxVision.<Svc>.Application\TaxVision.<Svc>.Application.csproj" />
    <ProjectReference Include="..\..\..\src\Services\<Svc>\TaxVision.<Svc>.Infrastructure\TaxVision.<Svc>.Infrastructure.csproj" />
    <ProjectReference Include="..\..\..\src\Services\<Svc>\TaxVision.<Svc>.Api\TaxVision.<Svc>.Api.csproj" />
    <ProjectReference Include="..\..\..\src\BuildingBlocks\Messaging\BuildingBlocks.Messaging.csproj" />
    <ProjectReference Include="..\TaxVision.BuildingBlocks.Testing\TaxVision.BuildingBlocks.Testing.csproj" Aliases="BbTesting" />
  </ItemGroup>
</Project>
```

### 9.2 Registro en la solución

```xml
<Folder Name="/deploy/tests/">
  <Project Path="deploy/tests/TaxVision.<Svc>.Tests/TaxVision.<Svc>.Tests.csproj" />
</Folder>
```

### 9.3 Prueba de capas (heredada)

```csharp
extern alias BbTesting;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;

public sealed class <Svc>LayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(<AgregadoRaiz>).Assembly;
    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
```
Fuente: ejemplo del `<summary>` de `BuildingBlocks.Testing/Architecture/LayeringRulesBase.cs:10-16`.

### 9.4 Prueba UTC del modelo

Copiar `deploy/tests/TaxVision.Notes.Tests/Architecture/UtcDateTimesTests.cs` cambiando el `DbContext` (usa `UseSqlServer("Server=unused;Database=unused")` sin conectar, por eso corre en el gate).

### 9.5 Ejecución local

```powershell
# Gate exacto (antes de pedir push)
dotnet test TaxVision.slnx --nologo --filter "FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence.&FullyQualifiedName!~ReminderRetentionJobTests"
# Solo un proyecto
dotnet test deploy/tests/TaxVision.<Svc>.Tests/TaxVision.<Svc>.Tests.csproj --nologo --filter "FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence."
# Una clase
dotnet test deploy/tests/TaxVision.<Svc>.Tests/TaxVision.<Svc>.Tests.csproj --filter "FullyQualifiedName~<Clase>Tests"
```
Antes: `dotnet csharpier format <archivos tocados>`. Después: cerrar `VBCSCompiler`/`testhost` que queden vivos (pedido del dueño, memoria `feedback_codegen_static_variable_doble_guion.md`).
