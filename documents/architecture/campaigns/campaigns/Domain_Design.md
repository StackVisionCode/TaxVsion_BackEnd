# Campaigns — Domain Design

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado (greenfield)
- **Rol (fijado ADR-CAMP-000):** CREADOR/orquestador. Define, resuelve audiencia, estima costo, reserva Wallet, hace fan-out de dispatch por destinatario, agrega results, y liquida Wallet (consume/refund). **NO entrega, NO integra proveedores, NO tiene secretos de proveedor.**

Coherente con `../00_Overview_And_Index.md`, `../02_Context_Map.md`, `../05_Master_ADR.md`.

---

## 1. Bounded context y lenguaje

Campaigns es el bounded context "definición + orquestación" de la capacidad de campañas. Se refiere a otros contexts por **IDs opacos** (nunca FK cross-context): `AudienceRef` (Customer), `TemplateRef` (Scribe/asset), `WalletAccountId` (Wallet), `SubscriptionTenantId` (gate). Ver `../03_Ubiquitous_Language.md`.

Términos propios:

| Término | Significado |
|---|---|
| **Campaign** | Definición reutilizable/versionable: canal, audiencia (ref), plantilla (ref), objetivo, schedule. Aggregate root. Vive entre ejecuciones. |
| **CampaignRun** | Una ejecución concreta e **INMUTABLE** (salvo su estado y contadores): snapshot de precio, estimación de costo, id de reserva Wallet, ventana temporal, resultado agregado. Aggregate root propio. |
| **CampaignRecipient** | Un destinatario **dentro de un run**: identidad de contacto resuelta, estado de entrega, tracking. Entidad hija de CampaignRun. |
| **AudienceSpec** | Cómo se resuelve la audiencia (segmento/lista/manual), no los contactos materializados. |
| **CostEstimate / CostActual** | Estimado (audiencia × precio-por-mensaje) y real (entregados × precio). Minor units USD. |

---

## 2. Aggregates y límites transaccionales

Tres aggregates, cada uno su propia frontera de consistencia y su propio `RowVersion`:

```
Campaign (root)            CampaignRun (root)                (referencia)
├─ AudienceSpec (VO)       ├─ CampaignRecipient[] (entities) ──► Campaign.Id (opaco)
├─ ChannelSpec (VO)        ├─ CostEstimate (VO)              ──► WalletReservationId (opaco)
├─ TemplateRef (VO)        ├─ RunCounters (VO)
├─ ScheduleSpec (VO)       └─ RunStatus
└─ CampaignStatus
```

**Por qué Campaign y CampaignRun son aggregates separados:** un run es inmutable por ejecución y de alta cardinalidad de hijos (destinatarios). Meterlos en el mismo aggregate que Campaign forzaría a cargar N destinatarios para cualquier edición de la definición y crearía contención de lock entre "editar la campaña" y "ejecutarla". La regla del legado —campañas recurrentes **mutan una sola fila** (`CampaignSchedulerBackgroundService.cs:124-126` resetea `ScheduledAt`/`SentAt` sobre la misma `Campaign`)— es exactamente el anti-patrón que la separación elimina: cada disparo crea un **CampaignRun nuevo**.

**Regla de oro de tamaño:** una transacción muta **un** aggregate. Cambiar el estado de un CampaignRecipient (llega un result) **no** toca `Campaign`; toca el CampaignRun (o solo el recipient con lock optimista propio — ver `Concurrency_Spec.md`).

---

## 3. Aggregate: Campaign

Definición estable, editable en `Draft`. Campos (todos value objects tipados, **no** `Dictionary<string,string>` — corrige anti-patrón legado `Campaign.ChannelConfiguration` en `Campaign.cs:39`):

- `Id`, `TenantId` (multi-tenant, ver `Security.md`)
- `Name`, `CreatedByUserId`
- `ChannelSpec`: `Channel` (Email | Sms | WhatsApp | Push | InApp) + config tipada por canal, **versionada** (`SchemaVersion`).
- `AudienceSpec`: `AudienceKind` (Segment | ContactList | Manual) + `AudienceRef` (id opaco Customer) o lista de contactos manuales. **No** materializa contactos (la resolución stale es anti-patrón legado — el legado copiaba `ManualRecipients`/`RecipientLists` a la propia Campaign, `Campaign.cs:25-27`).
- `TemplateRef`: `ScribeTemplateKey` + `Subject` (email) — el cuerpo se renderiza en el ejecutor vía Scribe, no aquí.
- `ScheduleSpec`: `Mode` (Immediate | Scheduled | Recurring) + `RecurrenceRule?` (owned por Scheduler, ver `../scheduler/`).
- `Objective`: Engagement | Conversion | Transactional | Retention (metadata analítica).
- `Status`: máquina de estados (ver `State_Machines.md`).

**Invariantes (métodos del aggregate devuelven `Result`, nunca setters públicos que rompan estado):**

- No se puede pasar a `Ready`/agendar sin `ChannelSpec` válido + `AudienceSpec` resoluble + `TemplateRef`.
- Editar contenido/audiencia solo en `Draft`.
- El precio-por-mensaje **no** vive en Campaign ni viaja desde el frontend; se resuelve al crear el run (Wallet/catálogo). Corrige el legado, que confía `estimatedCost` calculado localmente (`CreateCampaignCommandHandler.cs:219`).

Ejemplo de API de dominio:

```csharp
public Result Schedule(ScheduleSpec spec, IClock clock);      // Draft -> Scheduled
public Result MarkReady();                                    // Draft -> Ready (validación completa)
public Result Archive();                                      // *-> Archived (soft)
```

---

## 4. Aggregate: CampaignRun (inmutable por ejecución)

Creado por el disparo (Scheduler lease → comando `StartCampaignRun`). Snapshot **congelado** de la definición al momento del disparo — si la Campaign se edita después, los runs pasados no cambian (auditoría).

Campos:

- `Id`, `TenantId`, `CampaignId` (opaco), `TriggeredAtUtc`, `TriggerKind` (Manual | Scheduled | Recurring).
- **Snapshot inmutable:** `ChannelSnapshot`, `AudienceSnapshotRef`, `TemplateSnapshot`, `UnitPriceMinor` (USD cents por mensaje, congelado del catálogo).
- `CostEstimate`: `RecipientCount × UnitPriceMinor`.
- `WalletReservationId` (opaco, tras RESERVE) + `WalletReservedMinor`.
- `RunCounters` (VO agregado, ver §6): `Total`, `Dispatched`, `Delivered`, `Failed`, `Suppressed`, `Bounced`, `Opened`, `Clicked`.
- `RunStatus`: máquina propia (ver `State_Machines.md`).
- `CostActual` (al liquidar): `Delivered × UnitPriceMinor`.
- `RowVersion`.

Los hijos `CampaignRecipient` se crean **una vez** al materializar la audiencia (dentro del run) y no se re-crean en reintentos.

**Inmutabilidad:** ningún campo de snapshot ni `UnitPriceMinor` cambia tras `Created`. Solo mutan `RunStatus`, `RunCounters`, `WalletReservationId` (una vez), `CostActual` (una vez). El precio congelado hace la facturación auditable y reproducible.

---

## 5. Entity: CampaignRecipient (hijo de CampaignRun)

Un destinatario resuelto para **este** run. Corrige `CampaignRecipient` legado (`CampaignRecipient.cs`), que colgaba de `Campaign` (no de un run) y mezclaba PII con tracking sin idempotencia.

- `Id`, `RunId`, `TenantId`
- `ContactRef` (id opaco Customer) + destino resuelto según canal: `Email` | `PhoneE164` | `PushTokenRef`. PII **minimizada** (ver `Security.md`): solo lo que el canal necesita.
- `DispatchState`: máquina por destinatario (ver `State_Machines.md`): `Pending → Dispatched → Delivered | Failed | Suppressed | Bounced`.
- `AttemptNo` (int) — clave de idempotencia por `(RunId, RecipientId, AttemptNo)`.
- `DispatchIdempotencyKey` (string) — enviada al ejecutor, devuelta intacta.
- `ProviderMessageId?` (opaco, del ejecutor), `FailureCode?`.
- **Tracking idempotente:** `DeliveredAtUtc?`, `FirstOpenAtUtc?`, `FirstClickAtUtc?`, `OpenCount`, `ClickCount`. Los timestamps `First*` son *set-once* (idempotencia de tracking, ver `Idempotency_Spec.md`) — corrige el doble-conteo del legado.

**Anti-patrón legado corregido:** `CampaignSendService.cs:63-68` marca `Sent` a **todos** los destinatarios no-fallidos aunque el envío sea asíncrono y aún no confirmado. Aquí `Delivered` solo lo pone un **result event** del ejecutor sobre el `DispatchIdempotencyKey` correspondiente.

---

## 6. Contadores agregados (RunCounters)

Estadísticas por run, no globales por campaña (el legado tenía `CampaignStatistics` 1:1 con Campaign, `Campaign.cs:34`). Se derivan de transiciones idempotentes de los recipients: cada transición de estado de un recipient emite el incremento **una sola vez** (guard de estado + `ProcessedBusinessMessage`), de modo que un reintento de webhook no doble-cuenta (anti-patrón legado, ADR-CAMP-000 §Anti-patrones #3). Ver `Idempotency_Spec.md §Tracking`.

Vista `CampaignStatsRollup` (proyección read-model) agrega runs → campaña para dashboards, fuera de la ruta transaccional.

---

## 7. Qué NO pertenece a este dominio

| Fuera de Campaigns | Dónde vive |
|---|---|
| Render del cuerpo (Fluid/Liquid) | Scribe (REUSE) — ejecutor lo invoca |
| Entrega + secretos de proveedor | Ejecutor de canal (Email SMTP2GO / SMS / WhatsApp / Push) |
| Mutación de saldo | Wallet/Ledger (movimientos inmutables) |
| Precio de plan / entitlement | Subscription (`module.campaigns`) |
| Cobro del top-up | PaymentApp (`SaaSPaymentType`) |
| Reloj / lease de disparo | Scheduler |

Campaigns **orquesta** estas piezas; no las implementa.

---

## 8. Tabla de evidencia

| Afirmación de diseño | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado usa `Dictionary<string,string>` sin esquema para config de canal | `Campaign.cs:39` | VERIFIED | 98% |
| Legado cuelga recipients de Campaign, no de un run | `CampaignRecipient.cs:8-9` | VERIFIED | 98% |
| Legado no tiene entidad de run; recurrentes mutan una fila | `CampaignSchedulerBackgroundService.cs:124-126` | VERIFIED | 96% |
| Legado marca `Sent` a todos los no-fallidos sin confirmación real | `CampaignSendService.cs:63-68` | VERIFIED | 97% |
| Legado calcula/confía el costo localmente | `CreateCampaignCommandHandler.cs:219` | VERIFIED | 95% |
| Legado materializa audiencia dentro de Campaign (snapshot stale) | `Campaign.cs:25-27` | VERIFIED | 92% |
| Seam `CampaignId` opaco ya fluye Notification↔Postmaster | `PostmasterEmailEvents.cs:37,104` | VERIFIED | 97% |
| `ProcessedBusinessMessage` reutilizable para dedupe de efecto | `Growth/.../Idempotency/ProcessedBusinessMessage.cs:9-15` | VERIFIED | 97% |
| Separación aggregate Campaign vs CampaignRun | decisión ADR-CAMP-000 §Decisiones/#8 | DESIGN | 90% |
| Tracking set-once idempotente | diseño (este doc §5-6) | NEW | 88% |
