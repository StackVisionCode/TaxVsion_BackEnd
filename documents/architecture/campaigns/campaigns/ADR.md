# Campaigns — ADRs (service-level)

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-09-16 (revisión ADR-CAMP-001: **Campaign solo campaña, sin dinero**)
- **Estado:** DISEÑO — no implementado

Decisiones **internas** de Campaigns. La decisión raíz (descomposición de la capacidad, separación creador/ejecutor, contrato dispatch/result, Wallet **diferido**) está en `../05_Master_ADR.md` (ADR-CAMP-000 + revisión ADR-CAMP-001, APPROVED). Estos ADRs la refinan para este servicio. IDs `CAMP-C-###`.

> **Regla dura (ADR-CAMP-001 D1):** Campaign **no maneja balance, monto, costo ni dinero**. Los ADRs de dinero de la versión previa (precio congelado, saga reserve/consume/refund, `Money` local, gate de balance) quedan **retirados**. Si a futuro se cobra, lo hace **otro servicio** (Wallet) suscribiéndose a los eventos de Campaign, sin cambiar Campaign.

---

## ADR-CAMP-C-001 — Campaign y CampaignRun son aggregates separados

**Estado:** ACCEPTED
**Contexto:** El legado tiene una sola entidad `Campaign` que sirve a la vez de definición y de ejecución; las campañas recurrentes **mutan esa misma fila** (`CampaignSchedulerBackgroundService.cs:124-135` resetea `ScheduledAt`/`SentAt`/`Status`), destruyendo el historial y sin auditoría por ejecución.
**Decisión:** Dos aggregate roots: `Campaign` (definición estable, editable) y `CampaignRun` (ejecución **inmutable**: snapshot congelado de canal/audiencia/plantilla/remitente + contadores de entrega). Cada disparo crea un run nuevo. Sin FK física entre ellos (id opaco). **Sin campos de dinero en el run.**
**Consecuencias:** Auditoría reproducible; sin contención entre editar y ejecutar; más tablas y una saga de dispatch por run. Corrige anti-patrón #8 del Master ADR.

---

## ADR-CAMP-C-002 — Campaign no conoce dinero (retira el modelo previo de precio/costo)

**Estado:** ACCEPTED (deriva de ADR-CAMP-001 D1, decisión del usuario 2026-09-16)
**Contexto:** La versión previa congelaba `unit_price_minor` en el run y calculaba costo server-side. El usuario decidió que **Campaign solo orquesta campaña**: ningún concepto de precio/costo/saldo vive aquí.
**Decisión:** Se eliminan de Campaign todas las columnas/VOs/eventos monetarios (`unit_price`, `cost_estimate/actual`, `currency`, `Money`, `wallet_*`). El run se define por su **resultado de entrega** (contadores), no por dinero.
**Consecuencias:** Modelo más simple y de responsabilidad única. La medición/cobro, si se agrega, es responsabilidad de otro servicio que **observa** los eventos de Campaign (ADR-CAMP-C-004).

---

## ADR-CAMP-C-003 — Idempotencia por destinatario con `dispatch_id`

**Estado:** ACCEPTED
**Contexto:** El legado marca `Sent` a todos los no-fallidos en un `SaveChanges` sin clave por destinatario (`CampaignSendService.cs:63-71`).
**Decisión:** Clave `dispatch_id = f(runId, contactRef, channel, attemptNo)` con `UNIQUE(run_id, dispatch_id)`; el estado del recipient avanza por guard idempotente sobre el result del ejecutor. Un reintento legítimo usa `attemptNo+1` → key nueva.
**Consecuencias:** At-least-once seguro; sin doble-envío ni doble-conteo. Ver `Idempotency_Spec.md`. *(Tracking de engagement — open/click, set-once + dedupe — queda **diferido** post-fase, `State_Machines.md`.)*

---

## ADR-CAMP-C-004 — Orquestación como saga de dispatch (sin dinero); el Wallet futuro observa desde fuera

**Estado:** ACCEPTED (reemplaza el ADR previo de saga reserve→consume/refund)
**Contexto:** El legado hacía fan-out fire-and-forget volátil (`CampaignSchedulerBackgroundService.cs:38,78-95`). El usuario exige que Campaign **no** orqueste dinero.
**Decisión:** Campaigns orquesta una saga de **solo dispatch**: `resolve audiencia → fan-out CampaignDispatchRequested por destinatario (outbox) → aplicar DispatchResult idempotente → cerrar run por conteo`. No hay RESERVE/CONSUME/REFUND. Cuando exista el Wallet, será un **servicio separado** que se suscribe a los eventos públicos de Campaign (`campaign.run.started/dispatch.result/run.completed.v1`) y hace su propia medición/cobro con movimientos inmutables — sin que Campaign lo llame ni gane estados/columnas de dinero.
**Consecuencias:** Responsabilidad única; añadir Wallet no cambia Campaign (solo consume eventos que ya se publican). Coordinación distribuida at-least-once. Ver `Transactional_Protocol.md`, `../wallet-ledger/` (DIFERIDO).

---

## ADR-CAMP-C-005 — Contrato dispatch/result común, seam `dispatch_id`/`CampaignId` generalizado

**Estado:** ACCEPTED
**Contexto:** El sistema nuevo ya propaga una correlación opaca `CampaignId` end-to-end Notification↔Postmaster sin que el transporte la interprete (`PostmasterEmailEvents.cs:37,104`). El legado tenía un contrato por canal ad-hoc con `ChannelConfiguration: Dictionary<string,string>` sin esquema (`Campaign.cs:39`).
**Decisión:** Un contrato `CampaignDispatchRequested` / `CampaignDispatchResult` **común a todos los canales**, emitido **por destinatario**, con `dispatch_id` como correlación opaca que el ejecutor devuelve intacta. Config por canal tipada y **versionada** (`schema_version`), no diccionario suelto.
**Consecuencias:** Añadir un canal = un ejecutor (consumer) que honra el contrato; Campaigns no cambia. Generaliza un patrón ya probado en producción. Corrige anti-patrón #7.

---

## ADR-CAMP-C-006 — Audiencia resuelta por referencia, no snapshot stale en la definición

**Estado:** ACCEPTED
**Contexto:** El legado copia contactos/listas dentro de la propia `Campaign` (`Campaign.cs:25-27`), quedando stale.
**Decisión:** `Campaign.AudienceSpec` guarda solo la **referencia** a las 3 fuentes (Clients=Customer, ContactList propia, contactos manuales). La **materialización** ocurre al crear el `CampaignRun` (resolución contra Customer y/o las listas propias), congelando esa audiencia en ese run.
**Consecuencias:** Cada run refleja la audiencia al momento del disparo; sin drift; dependencia de runtime a Customer/listas para ejecutar.

---

## ADR-CAMP-C-007 — Dos verificaciones ortogonales (RBAC + entitlement), ninguna de dinero

**Estado:** ACCEPTED (deriva de ADR-CAMP-001 D1/D4)
**Decisión:** (a) RBAC `campaigns.manage` = ¿este usuario puede? (ya en `PermissionCatalog.cs:39`); (b) `module.campaigns` (Subscription entitlement, ya sembrado en Pro/Enterprise) = ¿el tenant tiene la feature? Se evalúan por separado, con errores distinguibles (`403 forbidden` vs `403 feature_not_enabled`). **No hay una tercera verificación de balance.**
**Consecuencias:** UX honesta; sin mezclar "no podés" con dinero. El acceso ya está cableado centralmente (no se re-agrega).

---

## ADR-CAMP-C-009 — Modelo v2 de ejecución (unidad, semántica de entrega, cierre, durabilidad)

**Estado:** ACCEPTED (decisiones del usuario 2026-09-17, tras review externo)
**Contexto:** el review detectó defectos en el modelo de ejecución: predicado de cierre roto con `Skipped` (#01), confusión Accepted/Delivered (#03), timeout→Failed que descartaba evidencia (#04), falta de despertar durable T1→T2 (#08), identidad de intento ambigua (#09), estrategia de contadores contradictoria (#07).
**Decisión:**
- **Unidad de trabajo = destinatario/canal.** `recipient_count` = total **congelado** de unidades (una por (contacto, canal)); envío a **todos los canales seleccionados**.
- **Semántica de entrega explícita:** `Accepted` (proveedor aceptó) ≠ `Delivered` (webhook) ≠ `Unknown` (timeout, reconciliable). Un timeout **no** es `Failed`.
- **Cierre por total congelado:** `delivered+accepted+failed+skipped+unknown == recipient_count`, con `materialization_complete ∧ emission_complete`; nunca contra `Dispatched`.
- **Durabilidad:** T1 encola `DispatchRun` en la misma tx; materialización y fan-out **paginados con checkpoint**.
- **Intentos:** entidad `CampaignDispatchAttempt` con `UNIQUE(run_id, recipient_id, attempt_no)`; `recipient_id` estable por unidad; `contactRef` estable incluso para manual.
- **Contadores:** unidades = fuente de verdad; `counter_*` = caché por rollup; el cierre consulta un `COUNT` autoritativo.
**Consecuencias:** cierre correcto ante opt-out/cero-envíos/resultados repetidos/concurrencia; reconciliación tardía sin doble conteo; resiliencia a crash en cada frontera. Ver `State_Machines.md`, `Transactional_Protocol.md`, `Data_Model.md`, `Concurrency_Spec.md`, `Idempotency_Spec.md`.

## ADR-CAMP-C-010 — Autorización en el bus, no solo en HTTP

**Estado:** ACCEPTED (fix #16)
**Decisión:** el dispatch/result viaja por AMQP; la autorización se verifica en el **broker** (permisos de publish/consume por servicio) y al **aplicar** el result (tenant del envelope == tenant del run; correspondencia `DispatchId↔RunId↔CampaignId↔channel`). Reutilizar un consumer no abre su controller HTTP a `ActorType.Service` de forma global. La multi-tenancy se valida también **al escribir** (no solo query filters).
**Consecuencias:** un publicador AMQP no puede fabricar results ajenos ni elegir tenant arbitrario. Ver `Security.md §3`.

## Decisiones abiertas (ver `../09_Open_Questions.md`)

| ID | Pregunta | Estado |
|---|---|---|
| CAMP-C-Q2 | ¿Rollup de contadores incremental vs recompute-batch por defecto? | abierto (`Concurrency_Spec.md §3`) |
| CAMP-C-Q3 | ¿`dispatch_deadline` por canal o global? | abierto (sweeper) |
| CAMP-C-Q4 | ¿Retención PII configurable por tenant o política global? | abierto |
| CAMP-C-Q5 | ¿El Wallet futuro mide por evento `dispatch.result` o por `run.completed`? | abierto (afecta solo al Wallet, no a Campaign) |

---

## Tabla de evidencia (resumen)

| ADR | Evidencia central | Clasificación | Confianza |
|---|---|---|---|
| C-001 | `CampaignSchedulerBackgroundService.cs:124-135` (muta una fila) | VERIFIED | 96% |
| C-002 | ADR-CAMP-001 D1 (decisión del usuario: sin dinero) | DECISION | 99% |
| C-003 | `CampaignSendService.cs:63-71`; `ProcessedBusinessMessage.cs:9-23` | VERIFIED | 97% |
| C-004 | `CampaignSchedulerBackgroundService.cs:38,78-95`; ADR-CAMP-001 D1/D2 | VERIFIED/DECISION | 95% |
| C-005 | `PostmasterEmailEvents.cs:37,104`; `Campaign.cs:39` | VERIFIED | 97% |
| C-006 | `Campaign.cs:25-27` (snapshot stale) | VERIFIED | 94% |
| C-007 | `PermissionCatalog.cs:39`; `SubscriptionPlanCatalogSeeder.cs` | VERIFIED | 93% |
