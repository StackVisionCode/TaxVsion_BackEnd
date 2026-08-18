# Push + In-app — Deployment

Servicio: **Push (reusa `Notification`) + In-app (reusa `Communication`)**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**
Ancla: `Integration_Design.md`, `Commands_And_Events.md`, `../05_Master_ADR.md`.

## 1. Sin servicio desplegable nuevo

No hay un `TaxVision.Push` ni un `TaxVision.InApp`. Los cambios **aterrizan dentro de dos deployments existentes**:

| Deploy | Runtime | Rol en la suite | Cambio |
|---|---|---|---|
| `Notification` (`TaxVision.Notification.Api`) | .NET (net10.0), Wolverine/RabbitMQ | Ejecutor Push | Consumer + contratos + business-inbox (migración) |
| `Communication` | Node/TypeScript, Socket.IO, RabbitMQ, Prisma | Ejecutor In-app | Binding de consumer + publish de result (posible cero-migración) |

Ambos ya están desplegados y consumen/publican en el bus compartido. Esta integración **amplía** su superficie de consumo, no cambia su topología.

## 2. Cambios en `Notification` (Push)

1. **Contratos** en `BuildingBlocks.Messaging` (proyecto compartido): `CampaignPushDispatchRequested` / `CampaignPushDispatchResult` (ver `Commands_And_Events.md`). Al vivir en BuildingBlocks, Campaigns (publisher) y Notification (consumer) comparten el tipo — cero drift.
2. **Consumer** `CampaignPushDispatchConsumer` (`Notification.Application/Consumers/Campaigns/…`) registrado en Wolverine (mismo mecanismo que los consumers Postmaster existentes, `Consumers/Postmaster/*`).
3. **Business-inbox** (`ProcessedBusinessMessage`, copia-por-contexto desde `Growth/.../Idempotency/ProcessedBusinessMessage.cs`): **tabla + migración EF nueva** — Notification hoy **no** la tiene (a diferencia de Growth/Communication). Convención de migraciones ya presente (`Notification.Infrastructure/Persistence/Migrations/*`).
4. **Config / flags**:
   - `Notification:UseFcmPush` **debe estar true** (hoy gatea `FcmPushSender` real vs `LoggingPushSender`; ver `05_Master_ADR` VERIFIED). En un entorno con el flag en false, el consumer de campaña entrega vía el logging stub → inútil para producción.
   - Secreto FCM ya montado (`Notification:Push:Fcm` → `ServiceAccountJsonPath`, archivo). **Sin secreto nuevo.**
   - Flag nuevo sugerido `Campaigns:Push:Enabled` para poder activar/desactivar el consumer de campaña independientemente del push transaccional.
5. **Sin** nuevos endpoints → sin cambios de rate-limit ni de gateway.

## 3. Cambios en `Communication` (In-app)

1. **Binding de consumer** nuevo en `ConsumerRuntime.register(eventType, handler)` para `campaigns.inapp.dispatch_requested.v1`, mapeando a `pushNotification` con `sourceEventId = IdempotencyKey`. Patrón idéntico a `bindCloudStorageNotificationConsumers` (`application/event-handlers/cloudstorage-notification-consumers.ts`). Recordar: `ConsumerRuntime` admite **un** handler por `eventType` (lanza en duplicado) → el binding de campaña es un `eventType` nuevo, sin colisión.
2. **Publish del result** (`campaigns.inapp.dispatch_result.v1`) vía el **outbox existente** (`infrastructure/rabbit/outbox-drainer.ts`) — publicación transaccional con la creación de la notificación.
3. **Migración Prisma**: **probablemente cero**. La idempotencia se cubre con el unique existente `(TenantId, SourceEventId, UserId)` sobre `notificationEntry` (`prisma-notification-repository.ts:30-33`) + `ProcessedEventStore` (`consumer-runtime.ts:232`). Solo se agregaría schema si se decide un índice/columna nueva de correlación de campaña (opcional, observabilidad).
4. **Sin** endpoints HTTP nuevos; **sin** secretos.

## 4. Interoperabilidad de contratos .NET ↔ Node (BLOCKER-PUSH-3)

Punto de riesgo real de deployment: el **dispatch in-app** lo publica Campaigns (**.NET/Wolverine**) y lo consume Communication (**Node**, su propio `ConsumerRuntime`, no Wolverine). El result lo publica Communication (Node) y lo consume Campaigns (.NET). Requisitos para que el wire alinee:

- El **`eventType`/`MessageIdentity`** (`campaigns.inapp.dispatch_requested.v1`, `…result.v1`) debe ser el **mismo string** en ambos lados; el registro por `eventType` en Node debe matchear el `[MessageIdentity]` de Wolverine (precedente ya operativo: Communication consume `cloudstorage.*` / eventos de otros servicios .NET).
- El **envelope** (headers de correlación, tenant, message-id) debe ser compatible con el `IncomingEnvelope` de Communication (`application/ports/event-consumer.ts`). Validar contra el envelope que ya usan los consumers `cloudstorage.*` (que consumen eventos .NET hoy) — es el mismo puente.
- El **push .NET↔.NET** (Campaigns→Notification y de vuelta) es Wolverine puro → sin este riesgo.

Este puente **ya existe y funciona** para cloudstorage/otros; el trabajo es replicarlo, no inventarlo. Aun así se marca BLOCKER porque un mismatch de envelope entre dos runtimes es la falla de integración más cara de diagnosticar.

## 5. Orden de despliegue

1. Publicar los contratos en `BuildingBlocks.Messaging` (consumidos por 3 deploys: Campaigns, Notification, Communication).
2. Desplegar `Notification` con el consumer push + migración de business-inbox (idempotente ante eventos que aún nadie publica).
3. Desplegar `Communication` con el binding in-app (idem, inerte hasta que Campaigns publique).
4. Desplegar `Campaigns` (publisher). Los ejecutores ya están listos → sin ventana de eventos huérfanos.
5. Verificar `Notification:UseFcmPush=true` y `Campaigns:Push:Enabled=true` en el entorno objetivo antes de habilitar campañas push.

Orden = **ejecutores antes que publisher** (consumidor listo antes de que exista el mensaje) — evita dead-letter por `eventType` no registrado.

## 6. Observabilidad mínima

- Push: `NotificationLog` (Sent/Failed) con `CampaignId`/`RunId`; contadores `DevicesTried`/`DevicesDelivered` en el result. Reusa el logging existente de `FcmPushSender`/`NotificationDispatcher` (token enmascarado).
- In-app: `created` (persistida) + `SocketEmitted` (telemetría) en el result; `ProcessedEventStore` da la traza de dedupe.
- Ambos: `CorrelationId` de `IntegrationEvent` + tupla `(CampaignId,RunId,RecipientId,Attempt)` end-to-end (mismo patrón que el `CampaignId` opaco Notification→Postmaster).

## 7. Rollback

- Push: `Campaigns:Push:Enabled=false` desactiva el consumer de campaña **sin** tocar el push transaccional (que sigue por `NotificationDispatcher`). La migración de business-inbox es aditiva (no destructiva).
- In-app: quitar/deshabilitar el binding del `eventType` de campaña; el resto de Communication intacto.
- Ningún rollback requiere revertir schema (aditivo puro).

## 8. Dependencias de deployment

- **Dura**: Wallet/Ledger + Campaigns desplegados (el ejecutor no sirve sin quien dispare y reserve). Ver `../07_MVP_Scope.md`.
- **Dura**: business-inbox en Notification (migración) antes de habilitar el consumer.
- **Blanda**: audiencia de Campaigns resolviendo `UserId` (BLOCKER-PUSH-1) — sin ella el ejecutor solo produce `NoRecipientUser`.
