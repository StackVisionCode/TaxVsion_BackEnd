# Campaigns — ADRs (service-level)

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Decisiones **internas** de Campaigns. La decisión raíz (descomposición de la capacidad, separación creador/ejecutor, Wallet independiente, Scheduler con lease) está en `../05_Master_ADR.md` (ADR-CAMP-000, APPROVED). Estos ADRs la refinan para este servicio. IDs `CAMP-C-###`.

---

## ADR-CAMP-C-001 — Campaign y CampaignRun son aggregates separados

**Estado:** ACCEPTED
**Contexto:** El legado tiene una sola entidad `Campaign` que sirve a la vez de definición y de ejecución; las campañas recurrentes **mutan esa misma fila** (`CampaignSchedulerBackgroundService.cs:124-135` resetea `ScheduledAt`/`SentAt`/`Status`), destruyendo el historial y sin auditoría por ejecución.
**Decisión:** Dos aggregate roots: `Campaign` (definición estable, editable) y `CampaignRun` (ejecución **inmutable**: snapshot congelado + precio + reserva + contadores). Cada disparo crea un run nuevo. Sin FK física entre ellos (id opaco).
**Consecuencias:** Auditoría y facturación reproducibles; sin contención entre editar y ejecutar; más tablas y una saga por run. Corrige anti-patrón #8 del Master ADR.

---

## ADR-CAMP-C-002 — Precio congelado en el run, calculado server-side

**Estado:** ACCEPTED
**Contexto:** El legado calcula `estimatedCost` localmente y lo usa para cobrar al crear (`CreateCampaignCommandHandler.cs:219`), confiando el costo y cobrando el estimado por adelantado.
**Decisión:** El `unit_price_minor` (USD cents por mensaje) se resuelve server-side desde el catálogo y se **congela** en el `CampaignRun` al disparar. La estimación es `recipientCount × unitPrice`. El cobro real es por **entregado** (consume), no por estimado. El frontend nunca envía precio ni costo.
**Consecuencias:** Facturación justa (se paga lo entregado), auditable y reproducible. Requiere saga reserve→consume/refund (ADR-CAMP-C-004).

---

## ADR-CAMP-C-003 — Idempotencia por destinatario con `dispatch_idempotency_key` + set-once de tracking

**Estado:** ACCEPTED
**Contexto:** El legado marca `Sent` a todos los no-fallidos en un `SaveChanges` sin clave por destinatario (`CampaignSendService.cs:63-71`) y doble-cuenta tracking en reintentos de webhook (anti-patrón #3).
**Decisión:** Clave `f(runId, recipientId, attemptNo)` con `UNIQUE(run_id, dispatch_idempotency_key)`; el estado del recipient avanza por guard idempotente sobre el result del ejecutor; tracking (open/click) es set-once + dedupe por `providerEventId` vía `ProcessedBusinessMessage` (copia local del de Growth, `ProcessedBusinessMessage.cs:9-23`).
**Consecuencias:** At-least-once seguro; sin doble-envío ni doble-conteo; reintentos legítimos usan `attemptNo+1`. Ver `Idempotency_Spec.md`.

---

## ADR-CAMP-C-004 — Orquestación como saga reserve→consume/refund (Campaigns nunca muta saldo)

**Estado:** ACCEPTED
**Contexto:** El legado hace TOCTOU no-atómico (check+debit en 2 HTTP calls, debit antes de `SaveChanges`, `CreateCampaignCommandHandler.cs:250,264,278,320`) y refund frágil dependiente de un JWT persistido (`CampaignSendService.cs:112-127`).
**Decisión:** Campaigns es orquestador de una saga: RESERVE (fondos por estimado) → fan-out dispatch → al cerrar el run, CONSUME (entregados) + REFUND (resto). **Solo Wallet muta saldo**, por movimientos inmutables; Campaigns solo emite solicitudes idempotentes por `runId`. Cierre por CAS de estado + conteo. Compensaciones explícitas + sweeper de timeout.
**Consecuencias:** Sin carreras de saldo; dinero fail-safe (consume ≤ reserved, refund cierra la diferencia); coordinación distribuida (at-least-once). Ver `Transactional_Protocol.md`. Alinea con ADR-CAMP-000 §Decisiones/#3.

---

## ADR-CAMP-C-005 — Contrato dispatch/result común, seam `CampaignId` generalizado

**Estado:** ACCEPTED
**Contexto:** El sistema nuevo ya propaga una correlación opaca `CampaignId` end-to-end Notification↔Postmaster sin que el transporte la interprete (`PostmasterEmailEvents.cs:37,104,120,137,151,169`). El legado tenía un contrato por canal ad-hoc con `ChannelConfiguration: Dictionary<string,string>` sin esquema (`Campaign.cs:39`).
**Decisión:** Un contrato `CampaignRecipientDispatchRequested` / `ChannelDispatchResult` **común a los 5 canales**, emitido **por destinatario**, con `dispatch_idempotency_key` como correlación opaca que el ejecutor devuelve intacta. Config por canal tipada y **versionada** (`schema_version`), no diccionario suelto.
**Consecuencias:** Añadir un canal = un ejecutor que honra el contrato; Campaigns no cambia. Generaliza un patrón ya probado en producción. Corrige anti-patrón #7.

---

## ADR-CAMP-C-006 — Audiencia resuelta contra Customer por referencia, no snapshot en Campaign

**Estado:** ACCEPTED
**Contexto:** El legado copia contactos/listas dentro de la propia `Campaign` (`Campaign.cs:25-27`), quedando stale.
**Decisión:** `Campaign.AudienceSpec` guarda solo la **referencia** (segment/list id opaco Customer, o contactos manuales explícitos). La **materialización** ocurre al crear el `CampaignRun` (resolución contra Customer), congelando esa audiencia en ese run. La definición no arrastra un snapshot stale.
**Consecuencias:** Cada run refleja la audiencia al momento del disparo; sin drift entre definición y realidad; dependencia de runtime a Customer para ejecutar.

---

## ADR-CAMP-C-007 — Gate, entitlement y balance son tres verificaciones ortogonales

**Estado:** ACCEPTED (deriva de ADR-CAMP-000 §Decisiones/#5)
**Decisión:** (a) RBAC `campaigns:send` = ¿este usuario puede?; (b) `module.campaigns` (Subscription entitlement, ya sembrado en tiers medio/alto) = ¿el tenant tiene la feature?; (c) **balance** Wallet = ¿cuánto puede enviar? Se evalúan por separado, con errores distinguibles (`403 forbidden` vs `403 feature_not_enabled` vs `402 insufficient`).
**Consecuencias:** UX honesta (el legado mezclaba "no podés" con "no tenés saldo"); un tenant con feature pero sin saldo recibe un `Rejected(insufficient)` claro que invita a top-up.

---

## ADR-CAMP-C-008 — `Money` como copia local por bounded context

**Estado:** ACCEPTED
**Contexto:** Existen múltiples copias de `Money`/`IdempotencyKey` por servicio (`Subscription/.../Money.cs`, `PaymentApp/.../Money.cs`, etc.), no un tipo compartido (decisión de arquitectura del monorepo).
**Decisión:** Campaigns define su propia `Money(long AmountMinor, string Currency)` (USD) en su Domain, y su propia `IdempotencyKey`. No se comparte el tipo con Wallet; se comparte el **contrato de wire** (long minor + currency string) en los eventos de BuildingBlocks.
**Consecuencias:** Sin acoplamiento de tipos entre contexts; consistente con el resto del repo.

---

## Decisiones abiertas (ver `../09_Open_Questions.md`)

| ID | Pregunta | Estado |
|---|---|---|
| CAMP-C-Q1 | ¿El precio-por-canal lo owns Wallet o un Catalog propio de Campaigns? | abierto (afecta ADR-C-002) |
| CAMP-C-Q2 | ¿Rollup de contadores incremental vs recompute-batch por defecto? | abierto (`Concurrency_Spec.md §3`) |
| CAMP-C-Q3 | ¿`dispatch_deadline` por canal o global? | abierto (sweeper) |
| CAMP-C-Q4 | ¿Retención PII configurable por tenant o política global? | abierto |

---

## Tabla de evidencia (resumen)

| ADR | Evidencia central | Clasificación | Confianza |
|---|---|---|---|
| C-001 | `CampaignSchedulerBackgroundService.cs:124-135` (muta una fila) | VERIFIED | 96% |
| C-002 | `CreateCampaignCommandHandler.cs:219` (costo local, prepay) | VERIFIED | 95% |
| C-003 | `CampaignSendService.cs:63-71`; `ProcessedBusinessMessage.cs:9-23` | VERIFIED | 97% |
| C-004 | `CreateCampaignCommandHandler.cs:250-320`; `CampaignSendService.cs:112-127` | VERIFIED | 96% |
| C-005 | `PostmasterEmailEvents.cs:37,104`; `Campaign.cs:39` | VERIFIED | 97% |
| C-006 | `Campaign.cs:25-27` (snapshot stale) | VERIFIED | 94% |
| C-007 | ADR-CAMP-000 §Decisiones/#5; seeder `module.campaigns` | VERIFIED | 92% |
| C-008 | copias `Money.cs` en Subscription/PaymentApp/Billing/Codes | VERIFIED | 96% |
