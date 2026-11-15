# Wolverine, RabbitMQ y EDA en TaxVision — investigación

Fecha: 2026-11-15 · Trabajo de solo lectura · Repo: `TaxVsion_BackEnd` (rutas relativas a esa raíz salvo que se indique).

Leyenda: **[Código verificado]** leído hoy en el repo · **[Documentación histórica]** memoria/planes/libro · **[Recomendación]** propuesta de esta investigación · **[Decisión pendiente]** lo decide el dueño.

El codegen Static y el incidente de RAM están en `Roslyn_Produccion.md`.

---

## 1. Estado actual

### 1.1 Topología real — [Código verificado]

- **Un exchange fanout compartido: `taxvision-events`.** 294 líneas `PublishMessage<T>().ToRabbitExchange("taxvision-events")` en los Program.cs.
- **Dos exchanges dedicados:**
  - `taxvision-catalog` — Subscription publica `CatalogChangedIntegrationEvent` y `ModuleCategoryChangedIntegrationEvent` (`src/Services/Subscription/TaxVision.Subscription.Api/Program.cs:200-201`); lo escucha Communication (Node), que lo declara (`src/Services/Communication/src/infrastructure/rabbit/rabbit-connection.ts:81`).
  - `taxvision-catalog-items` — Catalog publica `CatalogItemUpdatedIntegrationEvent` y `CatalogItemPriceChangedIntegrationEvent` (`src/Services/Catalog/TaxVision.Catalog.Api/Program.cs:134-135`).
- **Una cola durable por servicio enlazada al fanout** (`ListenToRabbitQueue("<svc>-events", q => q.BindExchange("taxvision-events", string.Empty)).UseDurableInbox()`):

| Servicio | Cola del fanout | Colas extra (sin fanout) |
|---|---|---|
| Auth | `auth-tenant-events` (`Auth/Api/Program.cs:602-609`) | — |
| Billing | `billing-events` | — |
| Calendar | `calendar-events` | — |
| Campaigns | `campaigns-events` | — |
| Catalog | `catalog-events` | — |
| CloudStorage | `cloudstorage-events` (`Program.cs:300-306`) | `cloudstorage-file-deletions`, `cloudstorage-external-uploads` (`DefaultIncomingMessage<SaveFileRequestedIntegrationEvent>`, productores Node por el exchange default, `Program.cs:317-320`) |
| Connectors | `connectors-events` | — |
| Correspondence | `correspondence-events` | — |
| Customer | `customer-events` | `customer-client-form-commands` |
| Documents | `documents-events` | `documents-client-form-summary` |
| FinancialInsights | `financial-insights-events` (`Program.cs:120-122`) | — |
| Growth | `growth-events` | — |
| Inventory | `inventory-events` | — |
| Notes | `notes-events` | — |
| Notification | `notification-events` | `notification-client-form-commands` |
| PaymentApp | `payment-app-events` | — |
| PaymentClient | `payment-client-events` | — |
| Postmaster | `postmaster-events` | — |
| Reminder | `reminder-events` | — |
| Scribe | `scribe-events` | — |
| Signature | `signature-events` | `signature-client-form-commands` |
| Sms | `sms-events` | — |
| Subscription | `subscription-events` (`Program.cs:235-242`) | — |
| Tasks | `tasks-events` | — |
| Tenant | `tenant-events` | — |
| Wallet | `wallet-events` | — |
| Communication (Node) | cola de `config.rabbitmq.queue` enlazada a `COMMUNICATION_RABBITMQ_EXCHANGE` (default `taxvision-events`) + DLQ propia (`rabbit-connection.ts:83-93`) | — |

- **Dead letters:** `wolverine-dead-letter-queue` (comentario medido en `src/BuildingBlocks/Messaging/WolverineFailurePolicies.cs`; alerta Grafana a los 5 min y runbook README §48, según ese mismo comentario).
- **Transporte:** `options.UseTaxVisionRabbitMq(rabbitUri)` en 27 Program.cs (`src/BuildingBlocks/BuildingBlocks.Web/Hosting/RabbitMqTransportExtensions.cs:32-49`): `UseRabbitMq(uri).AutoProvision()` + `PublisherConfirmationsEnabled = true` y `PublisherConfirmationTrackingEnabled = true` (líneas 40-44). Motivo medido (T-2 Client Forms, 2026-10-23): sin confirmaciones, un canal cerrado por 403 hacía que el outbox borrara la fila "como enviada" y el mensaje se perdía. También enciende el acuse de offboarding (`AcknowledgeOffboardingSteps`).

### 1.2 Pila estándar de `UseWolverine` — [Código verificado]

Ejemplo completo y reciente: `src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/Program.cs:83-123`. Adopción contada en los Program.cs de `src/Services`:

| Pieza | Qué hace | Servicios |
|---|---|---|
| `UseTaxVisionRabbitMq` | transporte con confirmaciones | 27 (incluye MailboxListener) |
| `PersistMessagesWithSqlServer(sqlConn)` | tablas inbox/outbox en la base del servicio | todos con Wolverine |
| `Policies.UseDurableOutboxOnAllSendingEndpoints()` | todo lo que sale a Rabbit pasa por el outbox | 26 |
| `UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, XDbContext>()` + `Policies.AutoApplyTransactions()` | middleware transaccional de EF | todos |
| `UseTaxVisionConflictPolicy()` | `ConflictException` de RowVersion: 409 por HTTP; por mensaje, reintento programado y luego cola de errores | 26 |
| `ApplyStandardFailurePolicies()` | permanente → error queue; transitorio → 1 s, 5 s, 15 s | 26 |
| `UseTaxVisionDomainEvents()` (BB-1) | eventos de dominio desde EF a la cola local durable `domain-events` | 17 |
| `UseTaxVisionDurableLocalQueues()` (BB-2) | todas las colas locales durables | 19 |
| `IntegrationEventTenantMiddleware` (para `IIntegrationEvent`) y `LocalCommandTenantMiddleware` | tenant del mensaje en el scope del handler | FinancialInsights, Subscription y otros |

Sin `UseTaxVisionDomainEvents`: Catalog, Documents, Inventory, Notes, Notification, Reminder, Scribe, Signature, Sms. Sin `UseTaxVisionDurableLocalQueues`: Catalog, FinancialInsights, Growth, Inventory, Reminder, Scribe, Wallet (FinancialInsights usa la cola `domain-events`, que ya es durable por `UseDurableInbox`).

### 1.3 Eventos de dominio vs integración — [Código verificado]

- `IDomainEvent` (`src/BuildingBlocks/Domain/IDomainEvent.cs`): interfaz vacía; "se procesa in-process — nunca cruza la frontera del microservicio". Lo junta `AggregateRoot.AddDomainEvent` (también `TenantlessAggregateRoot`).
- `IIntegrationEvent` / `IntegrationEvent` (`src/BuildingBlocks/Messaging/IIntegrationEvent.cs`): `EventId` (Guid nuevo), `TenantId`, `OccurredOn` (UTC), `CorrelationId` (string, default vacío). Los contratos viven en `src/BuildingBlocks/Messaging/<Dominio>IntegrationEvents/`.
- Despacho BB-1 (`src/BuildingBlocks/BuildingBlocks.Web/Hosting/DomainEventDispatchExtensions.cs:35-59`):
  - `PublishDomainEventsFromEntityFrameworkCore<AggregateRoot, IDomainEvent>` y lo mismo para `TenantlessAggregateRoot`: al confirmar la transacción de un handler, Wolverine saca los eventos de los agregados rastreados y los guarda como mensajes **en la misma transacción**;
  - van a la cola local durable `domain-events` (`ToLocalQueue("domain-events")` + `UseDurableInbox()`); Auth, Postmaster y CloudStorage la ponen además en `.Sequential()` (orden de commit);
  - cada `XxxDomainEventHandler` **devuelve** el evento de integración, que sale en cascada por el outbox;
  - `DomainEventCorrelationMiddleware` lleva el `X-Correlation-Id` al `ICorrelationContext`.
  - Requisitos escritos en el propio archivo: `AddDbContext` normal (no `AddDbContextWithWolverineIntegration`, que en 6.14 no deja el evento en el inbox durable), `Ignore(x => x.DomainEvents)` en el mapeo, y **solo se despacha dentro de un handler de Wolverine o con `IDbContextOutbox.SaveChangesAndFlushMessagesAsync`**; un `SaveChangesAsync` suelto (controlador, job) pierde los eventos. Al adoptarlo hay que quitar la publicación manual o el evento sale dos veces.

### 1.4 Identidad y versionado de eventos — [Código verificado]

- `[MessageIdentity(...)]` en 341 contratos de `BuildingBlocks/Messaging`:
  - 285 usan el **nombre CLR completo** (`"BuildingBlocks.Messaging.SubscriptionIntegrationEvents.TenantEntitlementsChangedIntegrationEvent"`);
  - 54 usan **`<dominio>.<hecho>.v1`** (p. ej. `financial.import_batch.completed.v1`, `calendar.booking_requested.v1`, `billing.invoice.payment_rejected.v1`, `auth.onboarding.referral_declared.v1`).
- El puente con Node depende de esos strings: Node publica con `type: 'communication.*.v1'` sin cabecera `dotnet-type-name`, y traduce lo que recibe con el diccionario `CLR_TYPE_TO_EVENT_TYPE` de `consumer-runtime.ts` (vigilado por `NodeInteropContractTests`).
- Evolución aditiva: campos nuevos opcionales para que "un mensaje viejo llegue null" (p. ej. `MaxStaffUsers`, `CurrentPeriodStartUtc` en `TenantEntitlementsChangedIntegrationEvent`).

### 1.5 Jobs: una unidad por transacción — [Código verificado]

`src/BuildingBlocks/Jobs/TransactionalJobUnit.cs` (BB-3): `RunAsync<TDbContext>` crea scope, abre transacción, ejecuta la acción y confirma con `IDbContextOutbox<TDbContext>.SaveChangesAndFlushMessagesAsync`; devuelve `Committed`, `Skipped`, `Conflict` (concurrencia: se registra en Debug y el siguiente barrido la retoma) o `Failed` (se registra y las demás unidades siguen). `RunEachAsync` hace una unidad por ítem en serie y devuelve `JobBatchSummary`. Usado en 26 archivos fuera de BuildingBlocks (p. ej. `Auth/Api/Bootstrap/PermissionsBackfillService.cs:74`). Modelo citado: `OverdueTaskSweepJob` de Tasks.

### 1.6 Reintentos y errores — [Código verificado]

`WolverineFailurePolicies.ApplyStandardFailurePolicies` (`src/BuildingBlocks/Messaging/WolverineFailurePolicies.cs`):
1. `DependencyNotReadyException` → `ScheduleRetry(delays)` y luego error queue (BB-7, va primero porque gana la primera regla).
2. Permanentes (línea 63): `JsonException`, `ArgumentException`, `FormatException`, `NotSupportedException` → `MoveToErrorQueue()` sin reintento.
3. Todo lo demás → `RetryWithCooldown(1 s, 5 s, 15 s)` (línea 80).
`InvalidOperationException`, `NullReferenceException` y `KeyNotFoundException` quedan como transitorias a propósito (consistencia eventual: la proyección puede no haber llegado).
`UseTaxVisionConflictPolicy` se registra **antes** de las estándar.

### 1.7 Idempotencia y correlación — [Código verificado]

- Inbox durable: deduplica por `MessageId` (comentario en `Notification.Api/Program.cs:252-262`). El libro lo prueba: "Mensaje entregado dos veces → el inbox lo descarta".
- Proyecciones con guard de revisión: `ApplyIfNewer(...)` con `RevisionNumber`/`PermissionsVersion` (p. ej. `FinancialInsights.Application/Projections/Consumers/ProjectionConsumer.cs:76-110`).
- Correlación: `CorrelationIdMiddleware` (header `X-Correlation-Id`, `src/BuildingBlocks/BuildingBlocks.Web/Middleware/CorrelationIdMiddleware.cs:12`); el handler de dominio copia `envelope.CorrelationId` al evento (`ImportBatchCompletedDomainEventHandler.cs:22`); los consumers hacen `correlation.Push(CorrelationOf(evt))`.

## 2. Evidencia de fallos reales (por qué existen las reglas)

| Regla | Incidente | Fuente |
|---|---|---|
| Todo evento de integración necesita `PublishMessage<T>()` | Sin ruta, Wolverine registra "No routes can be determined for Envelope…" y lo descarta. Demo pública 2026-11-13: el correo "ya tienes una demo" nunca salió; aparecieron 3 eventos de Calendar sin ruta. H-12: 4 veces (CloudStorage, Auth, PaymentClient…) | memoria `feedback_wolverine_evento_nuevo_necesita_ruta.md`; `EventRoutingContractTests.cs` |
| Guardar antes de publicar (comandos) | Con `InvokeAsync`, si `PublishAsync` va antes de un `SaveChangesAsync` que falla, el reintento en línea envía lo del intento revertido. T-8 Client Forms: 2 `CustomerCreationFromFormRequested` para un run | `feedback_wolverine_reintento_en_linea_filtra_mensajes.md`; `WolverineConflictPolicy.cs` |
| Guardar antes de publicar (jobs) | `PublishAsync` desde un job no está en la transacción de EF; publicar antes y fallar el save repite avisos | `feedback_guardar_antes_de_publicar_en_barridos.md`; hoy lo resuelve `TransactionalJobUnit` |
| Nombre singular `Handler`/`Consumer` | `CustomerEmailDirectoryConsumers`, `AuditChainAppenderConsumers` (0 filas de auditoría), `UserSlotPriorityDomainEventHandlers`: no se descubrían ("No known handler") | `feedback_wolverine_no_descubre_sufijo_plural.md`; `HandlerNamingContractTests` |
| try/catch dentro del handler no cubre la base caída | El middleware abre la transacción antes del cuerpo | `feedback_wolverine_transaccion_antes_del_handler.md` |
| Confirmaciones del publicador | Mensajes perdidos con canal cerrado por 403 | `RabbitMqTransportExtensions.cs` |
| `DefaultIncomingMessage` solo en cola dedicada | Forzar un tipo en el fanout rompería los demás eventos | `CloudStorage.Api/Program.cs` (comentario de `cloudstorage-external-uploads`) |
| Antes de re-publicar un evento, revisar todos sus consumidores | La reconciliación de suscripciones habría mandado "tu suscripción venció" cada día | `project_a6_module_gate_estado_medido.md` |
| Fanout sin consumidor = ruido "No known handler" | `CatalogChangedIntegrationEvent` llenaba logs de CloudStorage/Auth | `feedback_fanout_rabbit_sin_dueno_ruido_no_known_handler.md` |

## 3. Problemas y contradicciones

1. **[Código verificado vs Documentación histórica] Exchanges dedicados.** La memoria `feedback_fanout_rabbit_sin_dueno_ruido_no_known_handler.md` registra una corrección del usuario (2026-10-21): "solo hay un taxvision-events; si inventas otra nada se conecta a nada — publicar siempre a `taxvision-events`". Pero el código tiene `taxvision-catalog` (Subscription, con su consumer Node) y `taxvision-catalog-items` (Catalog). **[Decisión pendiente]** qué regla escribe la guía: "siempre `taxvision-events`" o "dedicado solo si el consumidor único ya declara el binding".
2. **[Código verificado] Dos convenciones de `MessageIdentity`** (285 nombre CLR vs 54 `.v1`). No hay regla escrita de cuál usar. **[Decisión pendiente]** convención para eventos nuevos (los recientes, como FinancialInsights y Calendar, usan `.v1`). Cambiar la identidad de un evento existente rompe a los consumidores en vuelo y a Node.
3. **[Código verificado] El guardarraíl de rutas no ve los eventos en cascada.** `EventRoutingContractTests` (`deploy/tests/TaxVision.BuildingBlocks.Tests/Messaging/EventRoutingContractTests.cs:78-93`) detecta `PublishAsync/SendAsync(new XIntegrationEvent…)` o por variable, por texto. Un evento **devuelto** por un `XxxDomainEventHandler` (el patrón BB-1, p. ej. FinancialInsights) no lo detecta. La prueba de runtime `IntegrationEventRoutingTests` (`RoutingFor(type).RouteForPublish(...)` exige `rabbitmq://`) **solo existe en Calendar**. FinancialInsights tiene la ruta (`Program.cs:112`), pero nada la vigila.
4. **[Código verificado] Excepciones conocidas sin ruta:** `KnownUnroutedPendingOwner` en `EventRoutingContractTests` lista 8 eventos de PaymentClient sin ruta ni consumidor, "pendiente del dueño del servicio".
5. **[Código verificado] Adopción desigual** de BB-1/BB-2 (ver 1.2). No es un fallo en sí, pero la guía debe decir cuál es el estándar para un servicio nuevo.
6. **[Documentación histórica vs Código verificado] Libro vs TaxVision.** El libro (`Guia-DDD-EDA-NET10-Wolverine-RabbitMQ-Docker.pdf`, §5.4) usa `AddDbContextWithWolverineIntegration`, `UseRabbitMq(...).AutoProvision()` y `Policies.UseDurableLocalQueues()`. TaxVision usa `AddDbContext` normal (el comentario de BB-1 dice que con 6.14 la integración no deja el evento en el inbox durable), `UseTaxVisionRabbitMq` con confirmaciones, y las políticas propias. Conceptos del libro que sí aplican: doble escritura, outbox "al menos una vez" → consumidor idempotente (§7.2-7.3), "un fanout sin cola pierde el mensaje" (§7.4), vigilar `wolverine-dead-letter-queue` (§7.7).
7. **[Documentación histórica] "Guardar antes de publicar" tiene un límite estático:** `SaveBeforePublishRules` lee texto método por método; "no ve lo que un servicio publique por dentro ni el guardado que hace Wolverine al salir del handler" (comentario del propio archivo).
8. **[Código verificado] Las pruebas de aplicación usan `FakeMessageBus`**: pasan aunque falte la ruta (así se escapó E-16).

## 4. Buenas prácticas (las que sigue el código)

- **Dominio → integración:** el agregado registra `IDomainEvent`; un `XxxDomainEventHandler` estático en Application lo traduce y **devuelve** el `IntegrationEvent` (cascada por el outbox). No leer de nuevo el agregado en el handler: todo sale del evento.
- **Ruta en el mismo cambio:** cada `IntegrationEvent` publicado = una línea `options.PublishMessage<T>().ToRabbitExchange("taxvision-events");` en el Program.cs del emisor, con comentario "sin ruta, Wolverine descarta el evento".
- **Consumir:** una sola cola por servicio, `<svc>-events`, enlazada a `taxvision-events`, con `UseDurableInbox()`. Handlers en clases `public static class XxxConsumer`/`XxxHandler` (singular).
- **Consumidor idempotente:** proyecciones con `ApplyIfNewer` por revisión/versión; correos con clave de idempotencia que incluya destinatario.
- **Comandos (controladores con `InvokeAsync`):** construir el mensaje, `SaveChangesAsync` y después publicar, o dejar que BB-1 lo haga en el commit.
- **Jobs:** `TransactionalJobUnit.RunEachAsync` (una unidad por entidad) en vez de "guardar y luego `PublishAsync`".
- **Fallos:** `UseTaxVisionConflictPolicy()` y luego `ApplyStandardFailurePolicies()`; lanzar `DependencyNotReadyException` cuando falta una proyección que llegará.
- **Correlación:** copiar `envelope.CorrelationId` al evento; `correlation.Push(...)` en el consumer.
- **Evolución:** solo campos nuevos opcionales; nunca renombrar namespace/tipo de un evento con identidad CLR sin revisar Node (`CLR_TYPE_TO_EVENT_TYPE`).
- **Antes de re-publicar** un evento (reconciliación, backfill): revisar todos sus consumidores, sobre todo los que mandan correos.
- **Diagnóstico:** "No routes can be determined" en el emisor = falta `PublishMessage`; "No known handler" en el receptor = nadie lo maneja (normal si es de otro servicio; bug si el handler existe y tiene nombre plural o no está en `Discovery`).

## 5. Cuándo EDA y cuándo M2M — [Código verificado + Recomendación]

| Necesidad | Mecanismo en TaxVision | Ejemplo |
|---|---|---|
| Avisar un hecho a quien le interese, sin esperar respuesta | Evento de integración por `taxvision-events` | `FinancialImportBatchCompletedIntegrationEvent` → Notification |
| Datos de otro servicio que se leen en cada request (permisos, plan, tenant, clientes) | **Proyección local** alimentada por eventos, con `ApplyIfNewer` | FinancialInsights proyecta `TenantEntitlementsChanged`, roles, usuarios |
| Rellenar una proyección vacía o recuperar eventos perdidos | **M2M interno** (`internal/...`, token de servicio) en un job de reconciliación | Subscription `internal/tenants/{id}/entitlements` + `TenantEntitlementsReconciliationJob`; FinancialInsights `TenantDirectoryReconciler` (al arrancar y cada 12 h) [Documentación histórica, memoria FI] |
| Consulta síncrona puntual que no puede esperar consistencia eventual | M2M directo | Auth `PlanCatalogClient` → `GET internal/plans/{id}` [Documentación histórica] |
| Orden de trabajo a un único servicio | Cola dedicada sin fanout (`*-client-form-commands`, `cloudstorage-file-deletions`) | Client Forms |

**[Recomendación]** La guía debe decir: "en el camino de la request se lee la proyección local; M2M solo para reconciliar o para una consulta que no admite retraso". La revisión de FinancialInsights marcó como 🔴 las llamadas en vivo a Customer y Auth en cada request (`Revision_y_Plan_FinancialInsights.md`, tabla Microservicio).

## 6. Recomendación

- **[Recomendación]** Que cada servicio nuevo tenga la prueba de rutas de runtime al estilo `IntegrationEventRoutingTests` de Calendar, porque la de texto no ve la cascada.
- **[Recomendación]** Que la plantilla de servicio nuevo active BB-1 y BB-2 (`UseTaxVisionDomainEvents` y `UseTaxVisionDurableLocalQueues`) y herede `DomainEventHandlerConventionRules` y `LocalQueueDurabilityRules`.
- **[Decisión pendiente]** Regla de exchanges dedicados (punto 3.1) y convención de `MessageIdentity` (punto 3.2).
- **[Decisión pendiente]** Los 8 eventos de PaymentClient sin ruta.

## 7. Riesgos

| Riesgo | Efecto | Vigilancia |
|---|---|---|
| Evento sin `PublishMessage` | Se descarta en silencio | `EventRoutingContractTests` (texto; no ve cascadas) · `IntegrationEventRoutingTests` (solo Calendar) |
| Handler con nombre plural o fuera de `Discovery` | Evento ignorado ("No known handler") | `HandlerNamingContractTests` |
| Evento de dominio sin handler | Muerto en `domain-events` | `DomainEventHandlerConventionRules.EveryDomainEvent_HasAHandler` |
| Evento de dominio que no sobrevive al sobre | Falla tras reinicio | `EveryDomainEvent_SurvivesTheEnvelopeSerializer` |
| Cola local en memoria | Mensajes perdidos al reiniciar | `LocalQueueDurabilityRules.LocallyRoutedMessages_UseDurableQueues` |
| Publicar antes de guardar | Mensajes duplicados o fantasma | `SaveBeforePublishRules` (texto) + pruebas `.Integration.` con SQL real |
| `SaveChangesAsync` suelto con BB-1 | Eventos de dominio perdidos | Ninguna automática |
| Renombrar namespace/tipo con identidad CLR | Node deja de recibir | `NodeInteropContractTests` |
| Re-publicar un evento | Correos repetidos | Revisión manual de consumidores |
| Handler con DI roto | Falla al primer mensaje | Pruebas que resuelven el handler contra el contenedor real / fitness de pares (ej. `ModuleGateWiringFitnessTests`) |
| Gate de pruebas fuera del CI | Todo lo anterior solo se ve si se corre el gate local | Disciplina |

## 8. Pruebas

Existentes (rutas reales):
- `deploy/tests/TaxVision.BuildingBlocks.Tests/Messaging/EventRoutingContractTests.cs` — `TodoEventoPublicadoTieneRutaAlExchange`, `MapaDeConsumoPorServicio`.
- `deploy/tests/TaxVision.Calendar.Tests/Architecture/IntegrationEventRoutingTests.cs` — `Every_calendar_integration_event_is_routed_to_the_broker` (runtime).
- `deploy/tests/TaxVision.Catalog.Tests/Architecture/CatalogRoutingTests.cs`, `deploy/tests/TaxVision.CloudStorage.Tests/Contracts/EventRoutingContractTests.cs`.
- `deploy/tests/TaxVision.BuildingBlocks.Tests/Messaging/NodeInteropContractTests.cs`.
- `deploy/tests/TaxVision.BuildingBlocks.Tests/Messaging/HandlerNamingContractTests.cs`, `HandlerTransactionContractTests.cs`, `WolverineFailurePoliciesTests.cs`.
- `deploy/tests/TaxVision.BuildingBlocks.Testing/Messaging/DomainEventHandlerConventionRules.cs` → heredada en `Architecture/DomainEventHandlerConventionTests.cs` de Auth, Billing, BuildingBlocks, Calendar, Campaigns, CloudStorage, Connectors, Correspondence, Customer, Growth, PaymentApp, PaymentClient, Postmaster, Reminder, Signature, Subscription, Tasks, Tenant, Wallet; FinancialInsights la usa en `Application/ImportBatchCompletedDomainEventHandlerTests.cs`.
- `deploy/tests/TaxVision.BuildingBlocks.Testing/Messaging/LocalQueueDurabilityRules.cs` (heredada en ~20 clases).
- `deploy/tests/TaxVision.BuildingBlocks.Testing/Architecture/SaveBeforePublishRules.cs` + `SaveBeforePublishTests` (BuildingBlocks), `CloudStorageSaveBeforePublishTests`, `NotificationSaveBeforePublishTests`, `TaskCommandSaveBeforePublishTests`.
- Integración (namespace `.Integration.`, base y Rabbit reales, fuera del gate): `BrokerOutageIntegrationTests`, `DomainEventDispatchIntegrationTests`, `TransactionalJobUnitIntegrationTests` (BuildingBlocks), `AuthSaveBeforePublishIntegrationTests`, `BillingDurableLocalQueueIntegrationTests`, `InvoicePaymentRejectionOutboxIntegrationTests`, `LongRetryPersistenceIntegrationTests`, `ScanConflictOutboxIntegrationTests`, entre otras.

Las que debe tener un servicio nuevo:
1. `DomainEventHandlerConventionTests : DomainEventHandlerConventionRules` (si tiene agregados con eventos).
2. `LocalQueueDurabilityTests : LocalQueueDurabilityRules` con su host.
3. Prueba de rutas de runtime (copiar `IntegrationEventRoutingTests` de Calendar) sobre todos sus eventos publicados.
4. Si publica desde comandos o jobs: prueba de "guardar antes de publicar" y, en `.Integration.`, la de concurrencia con `TrackActivity()` y SQL real.
5. Consumers de proyección: prueba de `ApplyIfNewer` (evento viejo no pisa uno nuevo).
6. Si hay contrato con Node: entrada en `NodeInteropContractTests`/`CLR_TYPE_TO_EVENT_TYPE`.
7. `PreGeneratedHandlerCodeTests` (ver `Roslyn_Produccion.md`).
8. Si agrega dependencias a un handler: correr las `.Integration.` del servicio (el gate no las corre; memoria `feedback_dependencia_nueva_correr_integration_del_servicio.md`).

## 9. Dependencias

- WolverineFx 6.14.0 (+ `.RabbitMQ`, `.SqlServer`, `.EntityFrameworkCore`), RabbitMQ con `AutoProvision`, SQL Server por servicio (tablas `wolverine_*`).
- `BuildingBlocks.Messaging` (contratos, políticas), `BuildingBlocks.Web/Hosting` (transporte, BB-1, BB-2, codegen), `BuildingBlocks.Jobs` (BB-3), `BuildingBlocks.Web/Tenancy` (middlewares de tenant).
- Communication (Node) como consumidor/productor del mismo fanout.

## 10. Material para la guía (fragmentos reales)

**Program.cs de un servicio nuevo** (real, FinancialInsights `Program.cs:83-123`, recortado):
```csharp
builder.Host.UseWolverine(options =>
{
    options.UseTaxVisionCodeGeneration(builder.Environment, builder.Configuration, typeof(Program).Assembly);
    options.Discovery.IncludeAssembly(typeof(AssemblyMarker).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    options.UseTaxVisionRabbitMq(rabbitUri);
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, FinancialInsightsDbContext>();
    options.Policies.AutoApplyTransactions();

    options.UseTaxVisionConflictPolicy();
    options.ApplyStandardFailurePolicies();
    options.UseTaxVisionDomainEvents();

    // PUBLICADOS — sin ruta, Wolverine descarta el evento.
    options.PublishMessage<FinancialImportBatchCompletedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    options
        .Policies.ForMessagesOfType<IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));

    options
        .ListenToRabbitQueue("financial-insights-events", queue => queue.BindExchange("taxvision-events", string.Empty))
        .UseDurableInbox();
});
```

**Contrato de integración** (real, `BuildingBlocks/Messaging/FinancialInsightsIntegrationEvents/FinancialImportBatchCompletedIntegrationEvent.cs`):
```csharp
[MessageIdentity("financial.import_batch.completed.v1")]
public sealed record FinancialImportBatchCompletedIntegrationEvent : IntegrationEvent
{
    public Guid BatchId { get; init; }
    public Guid CustomerId { get; init; }
    public Guid CreatedByUserId { get; init; }
    // …
}
```

**Handler de dominio → integración** (real, `ImportBatchCompletedDomainEventHandler.cs`):
```csharp
public static class ImportBatchCompletedDomainEventHandler
{
    public static FinancialImportBatchCompletedIntegrationEvent Handle(
        ImportBatchCompletedDomainEvent completed,
        Envelope envelope
    ) =>
        new()
        {
            TenantId = completed.TenantId,
            OccurredOn = completed.CompletedAtUtc,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            BatchId = completed.BatchId,
            // …
        };
}
```

**Consumer de proyección idempotente** (real, `ProjectionConsumer.cs:76-110`, recortado):
```csharp
public static async Task Handle(TenantEntitlementsChangedIntegrationEvent evt, IFinancialProjectionRepository projections,
    IUnitOfWork unitOfWork, ITenantPlanCodeCacheInvalidator cache, ICorrelationContext correlation, CancellationToken ct)
{
    using (correlation.Push(CorrelationOf(evt)))
    {
        var row = await projections.FindTenantEntitlementsAsync(evt.TenantId, ct);
        if (row is null) projections.AddTenantEntitlements(TenantEntitlementsProjection.Create(/* … */));
        else row.ApplyIfNewer(evt.PlanCode, evt.RevisionNumber, /* … */);
        await unitOfWork.SaveChangesAsync(ct);
        await cache.InvalidateAsync(evt.TenantId, ct);
    }
}
```

**Job con una unidad por transacción** (real, `Auth/Api/Bootstrap/PermissionsBackfillService.cs:73-80`):
```csharp
// Una unidad por usuario: el aviso y su cursor (PermissionsBackfilledAt) se confirman juntos.
var summary = await TransactionalJobUnit.RunEachAsync<Guid, AuthDbContext>(
    scopeFactory, pending, RepublishAsync, logger, cancellationToken);
```

**Prueba de rutas de runtime** (real, Calendar `IntegrationEventRoutingTests.cs:37-53`):
```csharp
var destinations = runtime.RoutingFor(eventType).RouteForPublish(message, null)
    .Select(envelope => envelope.Destination?.ToString() ?? string.Empty).ToList();
Assert.True(destinations.Any(d => d.StartsWith("rabbitmq://", StringComparison.OrdinalIgnoreCase)), /* … */);
```

**Checklist de un evento nuevo:** (1) record en `BuildingBlocks/Messaging/<Dominio>IntegrationEvents/` heredando `IntegrationEvent`, con `[MessageIdentity]`; (2) `PublishMessage<T>()` en el Program.cs emisor; (3) handler/consumer en singular en el receptor (y su cola enlazada si es servicio nuevo); (4) si lo consume Node, mapa `CLR_TYPE_TO_EVENT_TYPE` o lista de ignorados (`ignored-event-types.ts`); (5) regenerar codegen de emisor y receptor; (6) prueba de rutas y del consumer; (7) en el log del emisor, ninguna línea "No routes can be determined".
