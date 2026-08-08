# Push + In-app — ADRs

Servicio: **Push (reusa `Notification`) + In-app (reusa `Communication`)**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**
Ancla: `../05_Master_ADR.md` (ADR-CAMP-000, vinculante), `Integration_Design.md`, `Commands_And_Events.md`.

Convención: IDs `ADR-PUSH-00x`. Deriva de las decisiones aprobadas del usuario en ADR-CAMP-000 (2026-07-28).

---

## ADR-PUSH-001 — Reusar Notification + Communication como ejecutores (no construir servicios nuevos)

**Estado:** APPROVED (deriva de ADR-CAMP-000 decisión 2).

**Contexto.** Email/SMS/WhatsApp son ejecutores nuevos porque no existían (o eran stubs). Push e In-app **ya funcionan** en producción para envíos por-evento single-recipient: `FcmPushSender` (FCM HTTP v1, `FcmPushSender.cs:20`) y el use-case `pushNotification` de Communication (`push-notification.ts:30`).

**Decisión.** Push = **reusar `Notification`**; In-app = **reusar `Communication`**. Se agrega solo un **contrato bulk/campaña + consumer** encima de los senders existentes; no se crea `TaxVision.Push` ni `TaxVision.InApp`.

**Alternativas.** (a) Ejecutores push/in-app nuevos dedicados — rechazada por ADR-CAMP-000 (duplica lo que ya funciona). (b) Que Campaigns llame HTTP a Notification — rechazada (acopla, no idempotente, no durable).

**Consecuencias.** Cero servicios nuevos; se hereda la postura de seguridad y el manejo de devices/tokens ya probados. A cambio, los cambios aterrizan **dentro** de dos deploys existentes (`Deployment.md`) y hay que respetar sus invariantes.

---

## ADR-PUSH-002 — Campaigns hace el fan-out; el ejecutor es por-destinatario (1 evento = 1 destinatario)

**Estado:** APPROVED.

**Contexto.** El legado (`PushNotificationCampaignSender.SendBatchAsync`, `:126-196`) mete el loop de destinatarios **dentro del sender**, síncrono y sin idempotencia → se pierde al reiniciar y doble-cuenta en reintento (anti-patrón ADR-CAMP-000 §2,§3).

**Decisión.** Campaigns emite **un evento dispatch por destinatario** desde su outbox durable. Notification/Communication consumen **un evento = un destinatario** y reusan su lógica single-recipient (`SendPushAsync` / `pushNotification`) envuelta por un consumer. El fan-out masivo y la backpressure viven en Campaigns, no en el ejecutor.

**Consecuencias.** Reintento resiliente (outbox), idempotencia por destinatario posible, y el ejecutor casi no cambia. El costo: más mensajes en el bus (uno por destinatario) — aceptable dado el backpressure y el volumen esperado.

---

## ADR-PUSH-003 — Idempotencia por `(campaign, run, recipient, attempt)` con business-inbox

**Estado:** APPROVED.

**Contexto.** At-least-once (Wolverine/RabbitMQ) garantiza re-entregas; el legado no dedupe por destinatario y doble-cuenta.

**Decisión.**
- **Push (Notification):** copiar `ProcessedBusinessMessage` (business-inbox, `Growth/.../Idempotency/ProcessedBusinessMessage.cs`) — Notification hoy **no** lo tiene → migración nueva. Clave `(TenantId,"campaigns.push",IdempotencyKey)`, marcada en la misma transacción que el `NotificationLog`.
- **In-app (Communication):** reusar la **doble guarda existente** — `createIfMissing` unique `(TenantId,SourceEventId,UserId)` (`prisma-notification-repository.ts:30-33`) + `ProcessedEventStore.tryMarkProcessed` (`consumer-runtime.ts:232`). `SourceEventId=IdempotencyKey` → cero schema nuevo.
- `Attempt` es parte de la clave: un reintento **deliberado** de Campaigns (`Attempt+1`) es un envío nuevo; una re-entrega del bus (mismo `Attempt`) es un duplicado → `AlreadyDelivered`.

**Consecuencias.** No hay doble push ni doble notificación in-app ante re-entrega. Notification carga una tabla nueva; Communication no.

---

## ADR-PUSH-004 — Semántica de "entregado" difiere por canal (define billing)

**Estado:** APPROVED.

**Contexto.** Campaigns consume balance solo por entrega efectiva (ver `../06_Cross_Service_Transactional_Protocol.md`). "Entregado" no significa lo mismo en push que en in-app.

**Decisión.**
- **Push `Delivered`** = ≥1 dispositivo activo **aceptado por FCM** (best-effort multi-device, mismo criterio que `NotificationDispatcher.SendPushAsync` `:155-160`: un device basta).
- **In-app `Delivered`** = notificación **persistida** (`createIfMissing=true`); el emit por socket es best-effort (offline → la ve al reconectar). `SocketEmitted` es solo telemetría.
- No-entrega no-billable con outcome explícito: `NoDevices`, `NoRecipientUser`, `SuppressedByPreference`, `FailedPermanent` (refund); `FailedTransient` (retry, no refund); `AlreadyDelivered` (idempotente, no cobra de nuevo).

**Consecuencias.** El contrato result lleva un `Outcome` enumerado común (`Commands_And_Events.md §3.3`) que Campaigns traduce a consume/refund. Evita el `Sent` silencioso del legado.

---

## ADR-PUSH-005 — Sin secretos de proveedor nuevos; reusar credenciales FCM montadas

**Estado:** APPROVED (deriva de ADR-CAMP-000 anti-patrón 5).

**Contexto.** El legado guardaba secretos y JWT de usuario en la BD en texto plano (`SmtpProviderConfig.ApiKey`, `Campaign.BackgroundAuthToken`).

**Decisión.** Push reusa la cuenta de servicio Firebase ya montada como **secreto de archivo** (`FcmOptions`, sección `Notification:Push:Fcm`, `FcmOptions.cs:11-13`). In-app no tiene proveedor externo → sin secreto. **Ningún** token de usuario se persiste; cross-service = M2M por bus durable con tenant en el scope del mensaje.

**Consecuencias.** Push/In-app son los únicos ejecutores de la suite sin secreto cifrado nuevo. Superficie de secretos = 0.

---

## ADR-PUSH-006 — Destinatario = UserId interno; no-direccionable es un resultado explícito

**Estado:** APPROVED.

**Contexto.** Push/In-app entregan a un **usuario con dispositivos/sesión**, no a un email/teléfono. El legado marcaba fail solo si `RecipientId==Empty` (`:71-77`) y luego el monolito marcaba `Sent` al resto sin verificar direccionabilidad.

**Decisión.** La audiencia de Campaigns debe resolver a `TargetUserId` para estos dos canales (vía `Customer`, `02_Context_Map`). Un contacto sin userId → `NoRecipientUser`; un user sin tokens activos → `NoDevices` (`ListActiveForUserAsync`→0). Ambos son resultados **no-billable explícitos**, nunca un `Sent` silencioso. `RecipientId` es opaco para el ejecutor (mismo criterio que `CampaignId`).

**Consecuencias.** Métricas honestas (no infla entregas), refund correcto, y una dependencia dura sobre la resolución contacto→userId en Campaigns (**BLOCKER-PUSH-1**).

---

## Decisión abierta (no resuelta aquí)

**OQ-PUSH-A — Precio de push/in-app.** Legado = 0 (gratis; `01_Executive_Summary` cita Push 0). Si el catálogo de Wallet deja push/in-app en 0, la reserva/consumo es un movimiento de **monto 0** pero el contrato saga (reserve→consume/refund) es **idéntico** — no se bifurca el flujo. Si se les pone precio, nada del contrato dispatch/result cambia. Se difiere la fijación de precio a `wallet-ledger/` + `../09_Open_Questions.md`. Esta ADR-set es **agnóstica al precio**.

## Blockers (resumen, ver docs de detalle)

| ID | Blocker | Doc |
|---|---|---|
| BLOCKER-PUSH-1 | Audiencia debe resolver a `UserId` para push/in-app | `Integration_Design.md §4,§9` |
| BLOCKER-PUSH-2 | Business-inbox nuevo en Notification (migración) | `Deployment.md §2` |
| BLOCKER-PUSH-3 | Compatibilidad de envelope/`MessageIdentity` .NET(Wolverine)↔Node(ConsumerRuntime) | `Deployment.md §4` |
