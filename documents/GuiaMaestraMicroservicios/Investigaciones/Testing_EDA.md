# Testing — EDA (Wolverine, RabbitMQ, outbox, eventos)

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd`. No se ejecutó ninguna prueba. Etiquetas: **[Código verificado]**, **[Doc. histórica]**, **[Recomendación]**, **[Decisión pendiente]**.

## 1. Estado actual

- Bus: un exchange fanout `taxvision-events` y una cola `<servicio>-events` por servicio; Wolverine 6.14 con outbox/inbox en SQL Server. **[Doc. histórica]** `Implementaciones/DEVS/Guia_Creacion_Microservicio.md:47-49`
- Las pruebas de EDA cubren cuatro riesgos que ya pasaron en producción o local **[Código verificado]**:
  1. **Evento sin ruta** (Wolverine lo entrega en proceso y nunca sale a RabbitMQ, sin error).
  2. **Código pregenerado desactualizado** (producción corre en `Static`; un handler nuevo sin regenerar revienta al primer mensaje).
  3. **Atomicidad**: publicar antes de guardar, o fallo después de guardar que deja sobre o fila.
  4. **Contrato de cable**: nombres de mensaje, interoperabilidad con Node, serialización del sobre.
- "Nada de RabbitMQ real en las pruebas; la atomicidad del outbox se prueba en SQL". **[Doc. histórica]** `DomainDrivenDesign/01-Convenciones-Pruebas.md:20`

## 2. Evidencia

### 2.1 Rutas de eventos **[Código verificado]**

- `deploy/tests/TaxVision.BuildingBlocks.Tests/Messaging/EventRoutingContractTests.cs:7-22` (H-12): por servicio compara lo que el código **publica** contra lo que `Program.cs` **rutea** (`PublishMessage<T>()`), leyendo el fuente porque las rutas viven en el lambda de `UseWolverine`. Motivo: cuatro incidentes (CloudStorage, Auth ToS/Privacy, 8 en PaymentClient...).
- `deploy/tests/TaxVision.Calendar.Tests/Architecture/IntegrationEventRoutingTests.cs:10-37`: mide sobre el **runtime real** (host de rutas) que todo evento de integración de Calendar va al broker (`Every_calendar_integration_event_is_routed_to_the_broker`). Solo Calendar la tiene con ese nombre; CloudStorage y Signature tienen `Contracts/EventRoutingContractTests`, Catalog `Architecture/CatalogRoutingTests`.
- Lección: "evento sin `PublishMessage` se descarta". **[Doc. histórica]** índice `index_feedback_wolverine.md` en `MEMORY.md`.

### 2.2 Código pregenerado (Static) **[Código verificado]**

- `deploy/tests/TaxVision.BuildingBlocks.Testing/Architecture/PreGeneratedCodeRules.cs:13-57`: `AssertAllHandlersArePreGenerated` (en Static todos los handlers salen de tipos compilados del Api) y `AssertGeneratedCodeIsCurrent` (regenera en un directorio temporal y compara con `Internal/Generated` del Api, línea a línea sin importar el orden ni el número de variables locales). Con `TAXVISION_CODEGEN_WRITE=1` reescribe el código versionado.
- Uso, Notes (`deploy/tests/TaxVision.Notes.Tests/Architecture/PreGeneratedHandlerCodeTests.cs:13-65`): colección con `DisableParallelization = true` (generar código prende una bandera estática de JasperFx que deja sin transportes a otros hosts del proceso); host `Production` sobre `NotesRoutingHostFactory` con `Encryption:MasterKey` aleatoria; dos pruebas: `Every_handler_loads_from_pre_generated_code_in_production` y `Pre_generated_code_matches_the_current_handlers`.
- Presente en 26 servicios. Regeneración: `scripts/codegen/regenerate-wolverine-code.ps1 -Service <Svc>` (ver `Scripts_Locales.md`).
- Modo: `Wolverine:CodeGeneration:TypeLoadMode` (`src/BuildingBlocks/BuildingBlocks.Web/Hosting/WolverineCodeGenerationExtensions.cs:18-42`); Production = Static, Development/Testing = Dynamic. **[Código verificado]** + **[Doc. histórica]** `Implementaciones/Infra/RAM-Tasks-3GB-Produccion-2026-11-13.md:19-34`

### 2.3 Durabilidad y transacción **[Código verificado]**

| Pieza (BuildingBlocks.Testing) | Qué exige | Servicios que la usan |
|---|---|---|
| `Messaging/LocalQueueDurabilityRules.cs:9-47` | Todo mensaje con handler propio ruteado a cola local va a cola durable (medido en el runtime) | 19 (`LocalQueueDurabilityTests`) |
| `Architecture/SaveBeforePublishRules.cs:5-43` (BB-16) | No publicar antes de un guardado en el mismo camino (con `InvokeAsync` lo publicado sale igual en el reintento) | BuildingBlocks (global), Auth, CloudStorage, Notification, Tasks |
| `Architecture/JobPublishingRulesBase.cs` (BB-3) | Ningún job publica por un `IMessageBus` suelto | BuildingBlocks (global), Campaigns, CloudStorage, Documents, Notification, Postmaster |
| `Architecture/ConstructorBusRules.cs` (NOT-22) | Nadie recibe el bus por constructor si un handler lo usa | BuildingBlocks (global) |
| `Messaging/DomainEventHandlerConventionRules.cs` | Convención de handlers de eventos de dominio | 19 |
| `Messaging/HandlerOutboxProbes.cs:3-50` (BB-17) | `DeliveredProbe` (qué llegó a un handler por cola local durable) y `HandlerFailureSwitch`/`FailAfterHandlerMiddleware` (el handler real falla después de publicar y antes de confirmar; pasa por 3 reintentos en línea, ~21 s) | Billing, Customer, Notification, Signature, Tasks, Tenant |
| `Messaging/DomainEventEnvelopeRoundTrip.cs:9-36` | Un `IDomainEvent` vuelve igual tras serializar el sobre (sin constructor público o sin setter falla al releer de `wolverine_incoming_envelopes`) | BuildingBlocks |

Contratos globales en `deploy/tests/TaxVision.BuildingBlocks.Tests/Messaging/`: `MessageIdentityTests` (BB-4: cada contrato declara `[MessageIdentity]`; `MessageWireNames.txt` guarda los nombres que ya viajaban), `NodeInteropContractTests` (H-13/H-14: tipos `communication.*.v1` de Node ↔ CLR), `FailurePolicyContractTests` (H-15: política de fallo única `WolverineFailurePolicies`), `HandlerTransactionContractTests` (un `[NonTransactional]` le quita la transacción a todos los `Handle` del mismo mensaje), `HandlerNamingContractTests`, `DomainEventCorrelationTests`, `WolverineConflictPolicyTests`, `WolverineFailurePoliciesTests` (aislada), `RabbitMqTransportExtensionsTests`, contratos por evento (`CallMissedContractTests`, `FileAvailableContractTests`, `MeetingLifecycleContractTests`...). Y `Architecture/CorrelationIdFitnessTests` (todo evento sale con `CorrelationId`).

### 2.4 Atomicidad en SQL (T-INT) **[Código verificado]**

19 proyectos consultan `wolverine_outgoing_envelopes` en sus pruebas (Auth, Billing, BuildingBlocks, Calendar, Campaigns, CloudStorage, Connectors, Correspondence, Customer, Growth, Notification, PaymentApp, PaymentClient, Postmaster, Signature, Subscription, Tasks, Tenant, Wallet). Ejemplo: `deploy/tests/TaxVision.Wallet.Tests/Integration/WalletDomainEventsIntegrationTests.cs:31-108` (sobre guardado junto a la fila; fallo después de guardar → ni fila ni sobre). Están en `.Integration.`: no corren en el gate.

Pruebas de caída del broker (excepción a "sin RabbitMQ real"): `deploy/tests/TaxVision.BuildingBlocks.Tests/Integration/BrokerOutageIntegrationTests.cs:14-18`, `RedisOutageIntegrationTests`, `DurableLocalQueueIntegrationTests`, `TransactionalJobUnitIntegrationTests`, con `TcpChaosProxy` y `LocalBrokers` (variables de entorno o user-secrets de Tasks.Api, nunca impresos). **[Código verificado]**

### 2.5 Consumers (T-APP) **[Código verificado]**

Los consumers se prueban como handlers con dobles: `deploy/tests/TaxVision.Notes.Tests/Application/CustomerDirectoryConsumerTests.cs`, `NotesFileScanResultConsumerTests.cs`, `NotesUserOffboardedConsumerTests.cs`; `Permissions/PermissionsProjectionConsumersTests.cs` y `UserLifecyclePermissionsProjectionConsumerTests.cs` (proyección de permisos desde Auth); `RateLimiting/TenantPlanCodeProjectionTests.cs`. Mismo patrón en el resto de servicios (`Consumers/`, `Projections/`, `Messaging/`).

## 3. Problemas

1. **La prueba de rutas en runtime solo existe en Calendar**; el resto depende de la lectura de fuente de `EventRoutingContractTests`, que tiene límites (lee texto). **[Código verificado]**
2. **Atomicidad del outbox solo en `.Integration.`**: el gate no la vigila; depende de correrlas a mano. **[Código verificado]**
3. **`SaveBeforePublish` adoptado por servicio solo en 4** (más la regla global de BuildingBlocks, que recorre todos). **[Código verificado]** **[Decisión pendiente]** confirmar si la global basta.
4. **Wolverine 6.14 no valida al arrancar** que exista el código pregenerado (la protección es solo la prueba). **[Doc. histórica]** `RAM-Tasks-3GB-Produccion-2026-11-13.md:60-63` ("subir a 6.30 o superior")
5. **Communication (Node)** publica y consume del mismo bus; su lado se prueba con Vitest fuera de la solución y con `NodeInteropContractTests` del lado .NET. **[Doc. histórica]** + **[Código verificado]**

## 4. Buenas prácticas ya vigentes

- Reglas heredables: un servicio nuevo hereda durabilidad, capas y codegen con pocas líneas.
- Medición sobre el runtime real cuando se puede (rutas, colas) y sobre el fuente cuando el runtime necesita infraestructura.
- Nombres de cable congelados (`MessageWireNames.txt`).
- Cada regla documenta el incidente que la originó.

## 5. Recomendación

Mínimo EDA para un servicio nuevo:
1. `PreGeneratedHandlerCodeTests` + `<Svc>RoutingHostFactory` y regenerar el código antes del gate.
2. `LocalQueueDurabilityTests` si rutea a colas locales.
3. Registrar cada evento publicado con `PublishMessage<T>()` (lo vigila `EventRoutingContractTests`); **[Recomendación]** copiar `IntegrationEventRoutingTests` de Calendar para medirlo en runtime.
4. Contratos en `BuildingBlocks.Messaging` con `[MessageIdentity]`.
5. T-APP por consumer y una T-INT de atomicidad por caso de uso que guarde y publique (patrón Wallet).
6. Guardar antes de publicar; en jobs, una unidad por transacción con outbox. **[Doc. histórica]** `AGENTS.md`

## 6. Riesgos

- Handler nuevo sin regenerar: en Static falla el primer mensaje de ese tipo en producción.
- Dependencia nueva en un handler: los hosts de `.Integration.` se arman a mano y el gate no lo ve; correr las `.Integration.` del servicio. **[Doc. histórica]**
- Prueba de codegen en paralelo con otro host: transportes apagados en el proceso (por eso la colección aislada).
- Host de pruebas con Rabbit encendido: roba mensajes de la flota local.

## 7. Pruebas

Ver 2.1-2.5.

## 8. Dependencias

- `BuildingBlocks.Messaging` (contratos), `BuildingBlocks.Web/Hosting/WolverineCodeGenerationExtensions`, `WolverineFailurePolicies`, `TaxVision.BuildingBlocks.Testing`.
- `scripts/codegen/regenerate-wolverine-code.ps1`.

## 9. Material para la guía

### 9.1 Prueba de código pregenerado (real, Notes)

```csharp
extern alias BbTesting;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;

[CollectionDefinition(nameof(PreGeneratedHandlerCodeTests), DisableParallelization = true)]
public sealed class PreGeneratedHandlerCodeCollection;

[Collection(nameof(PreGeneratedHandlerCodeTests))]
public sealed class PreGeneratedHandlerCodeTests
{
    private const string ApiProject = "src/Services/<Svc>/TaxVision.<Svc>.Api";

    [Fact]
    public void Every_handler_loads_from_pre_generated_code_in_production()
    {
        using var factory = ProductionHost(_ => { });
        PreGeneratedCodeRules.AssertAllHandlersArePreGenerated(factory.Services, typeof(Program).Assembly);
    }

    [Fact]
    public void Pre_generated_code_matches_the_current_handlers()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "<svc>-codegen-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var factory = ProductionHost(options => options.CodeGeneration.GeneratedCodeOutputPath = scratch);
            PreGeneratedCodeRules.AssertGeneratedCodeIsCurrent(factory.Services, PreGeneratedCodeRules.GeneratedDirectory(ApiProject), scratch);
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    private static WebApplicationFactory<Program> ProductionHost(Action<WolverineOptions> configure) =>
        new <Svc>RoutingHostFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Encryption:MasterKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            builder.ConfigureTestServices(services => services.ConfigureWolverine(configure));
        });
}
```
Fuente: `deploy/tests/TaxVision.Notes.Tests/Architecture/PreGeneratedHandlerCodeTests.cs:1-65`.

### 9.2 Durabilidad de colas locales (heredada)

```csharp
public sealed class LocalQueueDurabilityTests(<Svc>ApiFactory factory)
    : LocalQueueDurabilityRules, IClassFixture<<Svc>ApiFactory>
{
    protected override IServiceProvider Services => factory.Services;
}
```
Fuente: `<summary>` de `BuildingBlocks.Testing/Messaging/LocalQueueDurabilityRules.cs:14-19`. Para que corra en el gate, usar la fábrica de rutas (sin infraestructura), no la de integración (lección Linux, `memory/feedback_ci_linux_difiere_de_windows.md`).

### 9.3 Flujo al agregar o cambiar un handler

1. Escribir la T-APP (rojo → verde).
2. `powershell -ExecutionPolicy Bypass -File scripts\codegen\regenerate-wolverine-code.ps1 -Service <Svc>`.
3. Si publica un evento nuevo: `PublishMessage<T>()` en `Program.cs` y `[MessageIdentity]` en el contrato.
4. `dotnet csharpier format` sobre lo tocado (CSharpier ignora `**/Internal/Generated/`).
5. Gate exacto; después `.Integration.` del servicio si cambió persistencia o dependencias del handler.
