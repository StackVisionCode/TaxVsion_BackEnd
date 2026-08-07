# Scheduler — Commands & Events

Servicio: **TaxVision.Campaigns.Scheduler**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**

Mensajería = **Wolverine outbox/inbox durable** (at-least-once; nunca exactly-once). Todo handler idempotente + guardas de estado. Ningún efecto de negocio se emite fuera de la transacción que lo produce (outbox transaccional).

## 1. Comando saliente principal: `StartCampaignRun`

Es el **único** efecto de negocio del Scheduler. Va de Scheduler → Campaigns.

```csharp
[MessageIdentity("scheduler.start_campaign_run.v1")]
public sealed record StartCampaignRunCommand : IntegrationCommand
{
    public required Guid TenantId { get; init; }
    public required Guid CampaignId { get; init; }
    // Clave estable e inmutable del disparo. Campaigns la usa como idempotency key
    // para crear el CampaignRun (un run por ocurrencia). Ver Idempotency_Spec.
    public required Guid OccurrenceId { get; init; }
    public required int  SequenceNo { get; init; }   // 1..N dentro de la serie (1 = primera/única)
    public required DateTime DueAtUtc { get; init; }  // instante teórico (no el de disparo real)
    public required DateTime FiredAtUtc { get; init; }
    public required string TriggerKind { get; init; } // "OneShot" | "Recurring"
    public string? IdempotencyKey { get; init; }      // "run:{OccurrenceId:N}"
}
```

**Contrato con Campaigns:** al recibirlo, Campaigns crea el `CampaignRun` **inmutable** (idempotente por `OccurrenceId`), resuelve audiencia vía Customer, estima costo y pide `RESERVE` a Wallet — es decir, el Scheduler entrega el *"cuándo"* y Campaigns arranca el *"qué"*. El Scheduler **no** conoce audiencia, costo ni canales. La correlación `OccurrenceId`/`CampaignId` es opaca de punta a punta, mismo patrón que `CampaignId` en `PostmasterEmailEvents.cs:37,103` (el transporte la lleva y la devuelve sin interpretarla).

Corrige del legado: el disparo era un `Task.Run`/cola in-proc que llamaba `ICampaignExecutorService.ExecuteCampaignAsync` directo (`CampaignSchedulerService.cs:97`), síncrono y perdido al reiniciar. Ahora es un mensaje durable en outbox.

## 2. Evento de vuelta (opcional, para cerrar el lease): `CampaignRunStarted`

Publicado por Campaigns cuando el run quedó creado y aceptado.

```csharp
[MessageIdentity("campaigns.run_started.v1")]
public sealed record CampaignRunStartedIntegrationEvent : IntegrationEvent
{
    public required Guid TenantId { get; init; }
    public required Guid CampaignId { get; init; }
    public required Guid OccurrenceId { get; init; }
    public required Guid CampaignRunId { get; init; }
    public required DateTime StartedAtUtc { get; init; }
}
```

El Scheduler lo consume para: (a) confirmar `TriggerOccurrence → Fired` con la referencia real del run (cierre limpio del lease), y (b) disparar `MaterializeNext` para la próxima ocurrencia recurrente. **Nota de diseño (at-least-once):** el `Fired` NO depende de recibir este evento — el Scheduler marca `Fired` al comitear el `StartCampaignRun` en su outbox. `CampaignRunStarted` solo **enriquece** con `CampaignRunId` y **acelera** la materialización; si nunca llega, la reconciliación materializa igual desde la última ocurrencia `Fired`. Así no hay acoplamiento de disponibilidad.

## 3. Comandos internos (dentro del bounded context del Scheduler)

| Comando | Origen | Efecto | Idempotencia |
|---|---|---|---|
| `ScheduleCampaignCommand` | API/puerto interno (Campaigns) | crea `ScheduleEntry` (+ 1ª `TriggerOccurrence`) | `ProcessedBusinessMessage(op=schedule, scope=CampaignId, key)` |
| `PauseScheduleCommand` / `ResumeScheduleCommand` / `CancelScheduleCommand` | puerto interno | transición de `ScheduleEntry` | guarda de estado (idempotente por naturaleza) |
| `LeaseDueOccurrencesCommand` | timer interno (tick) | claim atómico de ocurrencias debidas | claim condicional por `RowVersion`/`LeaseUntilUtc` |
| `FireOccurrenceCommand` | tras lease exitoso | publica `StartCampaignRun` + marca `Fired` (misma tx) | por `OccurrenceId` (terminal absorbente) |
| `ReconcileStuckLeasesCommand` | timer interno (barrido) | leases vencidos → `Pending` (`Attempt++`) o `Failed` | idempotente (opera sobre `LeaseUntilUtc < now`) |
| `MaterializeNextOccurrenceCommand` | tras `Fired`/`CampaignRunStarted` | crea próxima `TriggerOccurrence` | por `(ScheduleEntryId, SequenceNo)` único |

## 4. Eventos de dominio emitidos (auditoría/observabilidad)

`ScheduleCreated`, `SchedulePaused`, `ScheduleResumed`, `ScheduleCancelled`, `ScheduleCompleted`, `OccurrenceLeased`, `OccurrenceFired`, `OccurrenceFailed`, `OccurrenceLeaseReclaimed`. Alimentan métricas (ver `Observability.md`). No cruzan el bus salvo `OccurrenceFired`→`StartCampaignRun` (§1).

## 5. Ordenamiento y entrega

- **At-least-once:** `StartCampaignRun` puede llegar duplicado a Campaigns (redelivery de Wolverine). Campaigns deduplica por `OccurrenceId` al crear el `CampaignRun`. Nunca se asume exactly-once.
- **Sin ordenamiento requerido:** cada ocurrencia es independiente; `SequenceNo` da orden lógico sin depender del orden de entrega del bus.
- **Tenant en el scope Wolverine:** al procesar/publicar, el `TenantId` se fija explícitamente en el scope (`.IgnoreQueryFilters()` + tenant explícito, ver `Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`); nunca se infiere del hilo.

## 6. Evidencia

| Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Seam `CampaignId` opaco end-to-end ya existe | `BuildingBlocks/Messaging/EmailIntegrationEvents/PostmasterEmailEvents.cs:37,103,119` | VERIFIED | 96% |
| Legado disparaba llamando executor síncrono in-proc | `CampaignSchedulerService.cs:97`; `CampaignSchedulerBackgroundService.cs:78-95` | VERIFIED | 95% |
| Wolverine outbox durable es el estándar de la casa | `../00_Overview_And_Index.md:45`; `PostmasterEmailEvents.cs:1` (`using Wolverine.Attributes`) | VERIFIED | 92% |
| `StartCampaignRun` / eventos internos | este documento | NEW | — |
