# Campaigns — Domain Design

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-09-16 (revisión de alcance)
- **Estado:** DISEÑO — no implementado (greenfield)
- **Rol (fijado ADR-CAMP-000, revisado):** CREADOR/orquestador. Define la campaña, gestiona **remitentes**, **contactos/listas** y **clientes** como audiencia, resuelve la audiencia de un run, hace **fan-out de dispatch** por destinatario/canal, agrega results y expone **detalles/reportes**. **NO entrega, NO integra proveedores, NO tiene secretos de proveedor.**
- **Alcance de esta fase (2026-09-16/17, decisión del usuario):** **Campaign SIN dinero (D1).** El dominio **no** tiene campos, estados ni ganchos monetarios — nada de `CostEstimate`/`ReservationId`/consume/refund en el `CampaignRun` (fix #21). Si a futuro se cobra, lo hace un **interceptor/PEP + Wallet externos** que interceptan el trigger; Campaign **no cambia** (ver §8.1, `../05_Master_ADR.md` D7). El acceso ya está cableado central (`campaigns.manage`), no se re-agrega. Los ejecutores de canal **reusan servicios existentes** (Email/Push → `Notification`; SMS → `TaxVision.Sms`), no servicios dedicados nuevos.

Coherente con `../00_Overview_And_Index.md`, `../05_Master_ADR.md`, `Commands_And_Events.md`, `API_Contracts.md`.

---

## 1. Bounded context y lenguaje

Campaigns es el bounded context "definición + orquestación" de la capacidad de campañas multicanal. Se refiere a otros contexts por **IDs opacos** (nunca FK cross-context): `AudienceRef` (Customer), `TemplateRef` (Scribe/asset), `SubscriptionTenantId` (gate `module.campaigns`). **Gestiona todas las aristas del envío pero no envía** — publica un evento de dispatch por destinatario/canal y consume el result.

Términos propios:

| Término | Significado |
|---|---|
| **Campaign** | Definición reutilizable/versionable: canal(es), **remitente por canal**, audiencia (refs), contenido (ref), modo de envío. Aggregate root. Vive entre ejecuciones. |
| **CampaignRun** | Una ejecución concreta e **INMUTABLE** (salvo estado y contadores): snapshot de la definición al disparo, ventana temporal, `TriggeredBy`, resultado agregado. Aggregate root propio. |
| **CampaignRecipient** | Un destinatario **dentro de un run**: destino resuelto por canal, estado de entrega, outcome/razón. Entidad hija de CampaignRun. |
| **AudienceSpec** | Cómo se resuelve la audiencia: combinación de **Clients** (Customer), **ContactLists** (propias) y **Manual**. No los contactos materializados. |
| **Contact / ContactList** | Sub-dominio propio de Campaigns: contactos (que pueden **no** ser clientes), agrupados en listas, importables (CSV) y con **opt-out/consentimiento**. |
| **SenderProfile** | "Quién envía": identidad de remitente por tenant/canal (from/dominio, sender ID/número, número WABA, app push). La campaña la referencia; el ejecutor la resuelve. |

Ver `../03_Ubiquitous_Language.md`.

---

## 2. Aggregates y límites transaccionales

Aggregates, cada uno con su propia frontera de consistencia y su `RowVersion`:

```
Campaign (root)              CampaignRun (root)                 Sub-dominio audiencia
├─ ChannelSpec[] (VO)        ├─ CampaignRecipient[] (entities)  ContactList (root)
├─ SenderSelection[] (VO)    ├─ RunCounters (VO)                └─ Contact[] (entities)
├─ AudienceSpec (VO)         ├─ DefinitionSnapshot (VO)         SenderProfile (root, por canal)
├─ ContentRef[] (VO)         ├─ TriggeredBy (VO)
├─ SendMode (VO)             └─ RunStatus
└─ CampaignStatus
```

**Por qué Campaign y CampaignRun son aggregates separados:** un run es inmutable por ejecución y de alta cardinalidad de hijos (destinatarios). Meterlos juntos forzaría a cargar N destinatarios para cualquier edición de la definición y crearía contención entre "editar" y "ejecutar". La regla del legado —campañas recurrentes **mutan una sola fila** (`CampaignSchedulerBackgroundService.cs:124-126` resetea `ScheduledAt`/`SentAt`)— es el anti-patrón que la separación elimina: **cada disparo crea un CampaignRun nuevo**.

**Por qué Contact/ContactList y SenderProfile son aggregates propios:** son entidades de larga vida, editables fuera de una campaña (gestión de contactos, import, opt-out; alta de remitentes verificados), y de cardinalidad alta. La campaña solo los **referencia**.

**Regla de oro de tamaño:** una transacción muta **un** aggregate. Aplicar un result a un `CampaignRecipient` **no** toca `Campaign`; toca el CampaignRun (o solo el recipient con lock optimista propio — ver `Concurrency_Spec.md`).

---

## 3. Aggregate: Campaign

Definición estable, editable en `Draft`. Campos tipados (VOs, **no** `Dictionary<string,string>` — corrige el legado `Campaign.ChannelConfiguration`, `Campaign.cs:39`):

- `Id`, `TenantId`, `Name`, `CreatedByUserId`
- **`ChannelSpec[]`** — uno o varios canales (`Email | Sms | WhatsApp | Push`), con config tipada por canal, versionada (`SchemaVersion`). Una campaña **multicanal** genera un dispatch por (destinatario, canal aplicable).
- **`SenderSelection[]`** — "quién envía": por cada canal, un `SenderRef` (opaco) a un `SenderProfile` del tenant. El orquestador **no** valida ni resuelve el proveedor; el ejecutor lo hace.
- **`AudienceSpec`** — combinación de fuentes: `Clients` (0..N `AudienceRef` a Customer, segmento/lista), `ContactLists` (0..N ids de `ContactList` propias), `Manual` (0..N direcciones/números sueltos). **No** materializa contactos (la resolución stale es anti-patrón legado — el legado copiaba `ManualRecipients`/`RecipientLists` a la Campaign, `Campaign.cs:25-27`).
- **`ContentRef[]`** — por canal: `ScribeTemplateKey`+`Subject` (email), texto (SMS), plantilla WABA (WhatsApp), título+cuerpo (push). El cuerpo se renderiza en el ejecutor, no aquí.
- **`SendMode`** — `Immediate | Scheduled(atUtc) | Recurring(rule)`. Immediate dispara el run ya; Scheduled/Recurring los dispara el Scheduler (ver `../scheduler/`).
- `Status`: máquina de estados (ver `State_Machines.md`).

**Invariantes (métodos del aggregate devuelven `Result`):**
- No se puede pasar a `Ready`/agendar sin, por cada canal declarado: `ChannelSpec` válido + `SenderSelection` presente + `ContentRef` + `AudienceSpec` resoluble a ≥1 destinatario.
- Editar contenido/audiencia/remitente/canales solo en `Draft`.
- El **remitente no viaja como secreto**; es una referencia; el ejecutor tiene los secretos (ver `Security.md`).

```csharp
public Result SelectSender(Channel channel, Guid senderProfileId);   // en Draft
public Result SetAudience(AudienceSpec spec);                        // en Draft
public Result MarkReady(IClock clock);                              // Draft -> Ready (validación completa)
public Result TriggerNow(IClock clock);                             // valida + MarkReady interno + crea CampaignRun (NO hay estado "Sending" en Campaign)
public Result Schedule(SendMode mode, IClock clock);                // Ready -> Scheduled
public Result Unschedule();                                         // Scheduled -> Ready
public Result Archive();                                            // *-> Archived (soft)
```

**Cancelación (fix #10):** cancelar la **agenda** de una Campaign (`Unschedule`) es distinto de cancelar una **ejecución** en curso (`CampaignRun.Cancel`). La Campaign no tiene estado `Sending`; una ejecución activa se cancela por `runId` (ver `API_Contracts.md`: `/campaigns/{id}/runs/{runId}/cancel`), no por un `/cancel` ambiguo sobre la Campaign.

---

## 4. Aggregate: CampaignRun (inmutable por ejecución)

Creado por el disparo (SendNow inmediato, o Scheduler lease → `StartCampaignRun`). Snapshot **congelado** de la definición al disparo — si la Campaign se edita después, los runs pasados no cambian (auditoría).

- `Id`, `TenantId`, `CampaignId` (opaco), `TriggeredAtUtc`, **`TriggeredBy`** (`Manual{userId}` | `Scheduled` | `Recurring`).
- **Snapshot inmutable (fix #18):** `ChannelsSnapshot`, `SenderSnapshot` (identidad de remitente **efectiva** por canal, no solo el ref), `AudienceSnapshotRef`, `ContentSnapshot` = **revisión/hash inmutable de plantilla** + revisiones de assets + (según canal) variables base. El ejecutor renderiza con Scribe usando esa **revisión congelada**; si Scribe no garantiza inmutabilidad de la key, se congela un hash del contenido resuelto. Rotar una credencial es compatible; cambiar silenciosamente contenido o identidad visible **no**.
- `RecipientCount` — **total congelado de UNIDADES** (destinatario/canal), fijado al terminar la materialización; `materialization_complete`/`emission_complete` (flags de progreso).
- `RunCounters` (VO, **caché**, ver §7): `Dispatched`, `Accepted`, `Delivered`, `Failed`, `Skipped`, `Unknown` (derivables por canal desde las unidades).
- `RunStatus`: máquina propia (ver `State_Machines.md`): `Created → Materializing → Dispatching → Completed | PartiallyFailed | Failed | Cancelled | Rejected`.
- `RowVersion`.

Las unidades `CampaignRecipient` se crean **una vez** al materializar la audiencia — **una por (contacto, canal)** — de forma **paginada con checkpoint durable** (fix #12); no se re-crean en reintentos. **Inmutabilidad:** ningún campo de snapshot cambia tras materializar; solo mutan `RunStatus`, flags y `RunCounters`.

---

## 5. Entity: CampaignRecipient (hijo de CampaignRun)

Una **unidad destinatario/canal** de **este** run. Corrige `CampaignRecipient` legado (`CampaignRecipient.cs:8-9`), que colgaba de `Campaign` (no de un run) y mezclaba PII con tracking sin idempotencia.

- `Id` (recipient_id, **estable por unidad**), `RunId`, `TenantId`, `Channel`
- `ContactRef` (opaco: Customer id, Contact id, o **id generado por entrada manual** — nunca el literal `"manual"`, que colisionaría, fix #09) + destino resuelto según canal: `Email` | `PhoneE164` | `PushTokenRef` | `WhatsAppE164`. PII **minimizada** (ver `Security.md`).
- `DispatchState`: `Pending → Dispatched → Accepted → Delivered | Failed | Unknown | Skipped` (ver `State_Machines.md`). `Accepted`≠`Delivered`; `Unknown`=timeout reconciliable.
- `CurrentAttemptNo`, `DispatchDeadlineUtc`, `ProviderRef?`, `Reason?`, `AcceptedAtUtc?`, `DeliveredAtUtc?`, `SettledAtUtc?`.
- Los **intentos** son entidades `CampaignDispatchAttempt` con su propio `DispatchId = f(RunId, RecipientId, Channel, AttemptNo)` (fix #09, ver `Data_Model.md §1.3b`). Un reintento legítimo es un intento nuevo, no una unidad nueva.
- **Unicidad de unidad:** `UNIQUE(RunId, ContactRef, Channel)`.

**Anti-patrón legado corregido:** `CampaignSendService.cs:63-68` marca `Sent` a **todos** los no-fallidos sin confirmación. Aquí `Accepted`/`Delivered`/`Failed` solo lo pone un **result event** del ejecutor sobre el `DispatchId` del intento; un timeout es `Unknown`, no `Failed`.

---

## 6. Sub-dominio: Contactos, Listas y Remitentes (aristas explícitas)

**Contact** — `Id`, `TenantId`, identidad de contacto (nombre, `Email?`, `PhoneE164?`, canales permitidos), `Source` (Import | Manual | FromCustomer), **`OptOut`** por canal (consentimiento; un contacto opt-out se **Skip** al materializar), `CustomerRef?` (si además es cliente). No es un cliente: es la libreta de contactos de campañas.

**ContactList** — `Id`, `TenantId`, `Name`, membresía de `Contact` (entidad hija o join), soporte de **import** (CSV → `ImportContactsCommand`, dedupe por email/teléfono). Una campaña referencia listas por id.

**SenderProfile** — `Id`, `TenantId`, `Channel`, identidad de remitente (`FromEmail`/`FromName`/dominio; `SmsSenderId`/número; número WABA; app push), `Status` (`Pending | Verified | Disabled`). **No guarda secretos de proveedor** — esos viven en el ejecutor.

**Ciclo de vida del remitente (fix #19):**
- El ejecutor reporta verificación/revocación por un **evento versionado** (`campaign.sender.status_changed.v1`: tenant, channel, senderRef, status, reason) que Campaigns aplica de forma **idempotente** (set por `(tenant, channel, senderRef)`; el catálogo común debe incluir este evento — hoy no estaba, por eso se agrega).
- **Antes de aceptar un run** se valida que el `SenderProfile` seleccionado esté `Verified`; **antes del envío** el ejecutor **revalida** la autorización efectiva.
- **Sin fallback silencioso:** si el remitente elegido fue revocado, la campaña **no** usa otro remitente por su cuenta — el run se rechaza/omite con razón explícita.
- Dos vistas coexisten: `SenderProfile` (cara de negocio en Campaigns) y la config real (secretos) en el ejecutor; la responsabilidad del secreto es del ejecutor.

Estas tres piezas cubren, respectivamente, **los contactos**, **la audiencia** y **quién envía** del pedido.

---

## 7. Detalles / reporting (RunCounters + drill-down)

Estadísticas **por run**, no globales por campaña (el legado tenía `CampaignStatistics` 1:1 con Campaign, `Campaign.cs:34`). La **fuente de verdad** son las unidades; `RunCounters` es **caché** recomputada por rollup, y el cierre consulta un `COUNT` autoritativo (fix #07, ver `Concurrency_Spec.md §3`). Ver `Idempotency_Spec.md`.

Superficies de "detalles":
- **Stats por run/campaña y por canal:** derivadas de las unidades (`GROUP BY channel, dispatch_state`): Dispatched/Accepted/Delivered/Failed/Skipped/Unknown.
- **Estado por destinatario/canal (drill-down):** cada `CampaignRecipient` con `DispatchState`, `Reason`, `ProviderRef`, y sus intentos.
- **Historial de runs:** cuándo, `TriggeredBy`, resultado agregado.
- Proyección read-model `CampaignStatsRollup` agrega runs → campaña para dashboards, fuera de la ruta transaccional.

---

## 7.5 Política de audiencia y envío (CONFIRMADA — usuario 2026-09-17)

Decisiones funcionales del review (#14/#29), ya **fijadas**:

| Tema | Comportamiento confirmado | Dónde se aplica |
|---|---|---|
| **Identidad/dedupe de la unión** (#14) | Una unidad por `(tenant, channel, destino_normalizado)`; la **supresión (opt-out) prevalece**; no se fusionan personas por coincidencias legítimas. | materialización (`Data_Model.md §3b`) |
| **Multicanal** | Envío a **todos los canales seleccionados** → una unidad por canal. | materialización |
| **Frecuencia máx por persona** | **Configurable por tenant** (`MaxSendsPerContactPerWindow` + `Window`); **sin límite global obligatorio** por defecto. Al exceder → `Skipped(frequency_cap)`. Cuenta **entre campañas** (el solape está permitido pero sujeto al cap). | check-and-increment **atómico al despachar** (§7.5.1) |
| **Preferencia de canal** | Se **respeta la preferencia del contacto**: si existe, solo se materializan los canales permitidos/preferidos; los demás → `Skipped(channel_pref)`. | materialización |
| **Quiet hours / ventanas** | **Postergar, no descartar:** fuera de ventana válida se calcula `nextEligibleAt` y la unidad se **difiere** (queda `Pending` con `eligible_at_utc`), se despacha al abrir la ventana. **No** es `Skipped`. | dispatch (respeta `eligible_at_utc`) |
| **Solape entre campañas distintas** | **Permitido:** dos campañas distintas pueden alcanzar al mismo contacto, **sujeto al frequency cap**. El anti-solape del Scheduler es solo **por schedule-entry** (`../scheduler/Concurrency_Spec.md §5.1`), no entre campañas. | — |
| **Repetición manual** (mismo `send-now` dos veces) | Mismo `Idempotency-Key` → mismo run (no reenvía); clave distinta = intención nueva = run nuevo (sujeto al cap). | API (`API_Contracts.md §1`) |

**Regla rectora:** un envío duplicado es un **defecto** solo si viola una de estas políticas; con clave distinta y dentro del cap, reenviar es decisión de negocio. Toda omisión emite `Skipped(<reason>)` auditable; todo diferido emite un evento de reprogramación.

### 7.5.1 Cómo se hacen correctos frequency cap y quiet hours (concurrencia)

- **Frequency cap (correcto entre campañas concurrentes):** un **contador por contacto/canal/ventana** (`contact_send_ledger`, `Data_Model.md`) se consulta e **incrementa atómicamente al emitir el dispatch** (no solo al materializar), porque dos campañas paralelas podrían pasar un chequeo hecho solo en materialización. Si al despachar el contador ya alcanzó `MaxSendsPerContactPerWindow` → la unidad va a `Skipped(frequency_cap)` sin emitir. El incremento se revierte si el dispatch no llega a `Accepted/Delivered` (o se cuenta solo al confirmar aceptación — decisión de afinación, ver `Idempotency_Spec.md`).
- **Quiet hours (diferir):** al materializar/emitir, si `now` está fuera de la ventana del contacto (su TZ), se fija `eligible_at_utc = nextEligibleAt` y la unidad **no** se despacha aún. El emisor de fan-out solo toma unidades `Pending` con `eligible_at_utc <= now`; un job de "wake" durable re-evalúa las diferidas. El run permanece `Dispatching` hasta que las diferidas se envían (o vencen), lo cual es correcto (no están "stuck": están programadas).

## 8. Qué NO pertenece a este dominio

| Fuera de Campaigns | Dónde vive |
|---|---|
| Render del cuerpo (Fluid/Liquid) | Scribe (REUSE) — el ejecutor lo invoca |
| Entrega + secretos de proveedor + verificación de remitente | Ejecutor de canal (`Notification` para Email/Push, `TaxVision.Sms` para SMS, WhatsApp nuevo) |
| Precio de plan / entitlement | Subscription (`module.campaigns`, ya cableado) |
| Reloj / lease de disparo | Scheduler (lease atómico) |
| **Dinero / saldo / costo / cobro** | **Fuera de Campaign, siempre.** Lo hace un **interceptor de autorización de ejecución (PEP)** + el Wallet, ambos externos y **DIFERIDOS**. Campaign no tiene columnas, estados ni llamadas de dinero, ni "ganchos" internos. Ver §8.1. |
| **Verificar "para cuántas personas / si hay saldo"** | El **interceptor/PEP externo** (no Campaign). Campaign solo resuelve **a dónde** va el mensaje (canal + destinatarios). |

Campaigns **orquesta** estas piezas; no las implementa.

### 8.1 Seam de autorización de ejecución (dinero, externo y diferido)

La regla de negocio "si no hay saldo, no se envía; para scheduled/recurrente se cobra **antes** de ejecutar, según cuántos destinatarios tenga ese disparo" es **real**, pero **no vive en Campaign**. Se implementa como un **punto de verificación externo (Policy Enforcement Point / interceptor / api-gateway)** delante de la ejecución:

- **Dónde se coloca:** en el borde de la **ejecución**, no de la definición — sobre el trigger manual **y** sobre cada entrega `RunDue` del Scheduler (por eso valida **por ocurrencia** en recurrentes, no una sola vez al crear).
- **Qué hace (fuera de Campaign):** calcula el `recipientCount` de ese disparo (+ si es recurrente), consulta el saldo al Wallet, **cobra/reserva antes** de dejar pasar, y **veta** si no alcanza.
- **Qué ve Campaign:** o bien recibe únicamente triggers **ya autorizados** (el PEP los frena antes de llegar), o bien un veredicto opaco `authorized: yes/no` de una autoridad externa. Campaign **no sabe** que la razón es el saldo; solo respeta un gate genérico "autorizado / rechazado".
- **Hoy (fase actual):** este seam está **abierto por defecto** (no hay Wallet ni PEP → todo trigger se considera autorizado). Añadir el PEP + Wallet más adelante **no cambia el modelo de Campaign**: solo intercepta el trigger por fuera. Ver `Transactional_Protocol.md §7`, `../wallet-ledger/`, y `../05_Master_ADR.md` D7.

**Analogía (Meta Ads):** el objeto campaña lleva un budget como dato, pero quién frena/deja correr la entrega por dinero es el sistema de delivery+billing, separado del objeto campaña. Aquí lo mismo: Campaign define y enruta; el PEP externo autoriza por dinero.

---

## 9. Tabla de evidencia

| Afirmación de diseño | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado usa `Dictionary<string,string>` sin esquema para config de canal | `Campaign.cs:39` | VERIFIED | 98% |
| Legado cuelga recipients de Campaign, no de un run | `CampaignRecipient.cs:8-9` | VERIFIED | 98% |
| Legado no tiene entidad de run; recurrentes mutan una fila | `CampaignSchedulerBackgroundService.cs:124-126` | VERIFIED | 96% |
| Legado marca `Sent` a todos los no-fallidos sin confirmación real | `CampaignSendService.cs:63-68` | VERIFIED | 97% |
| Legado materializa audiencia dentro de Campaign (snapshot stale) | `Campaign.cs:25-27` | VERIFIED | 92% |
| Seam `CampaignId` opaco ya fluye Notification↔Postmaster | `PostmasterEmailEvents.cs:37,104` | VERIFIED | 97% |
| SMS ya acepta `ActorType.Service` (M2M) para el consumer de dispatch | `Sms/.../MessagesController.cs:21`, `SendSmsBatch.cs:16-17` | VERIFIED | 96% |
| Email `SendEmailCommand` soporta recipients+adjuntos pero fija `campaignId:null` | `Notification/.../SendEmail.cs:16-24,58` | VERIFIED | 95% |
| Acceso ya cableado: permiso/módulo/entitlement `campaigns` existen | `PermissionCatalog.cs:39,474-482`, `PermissionModuleMap.cs:45`, `SubscriptionPlanCatalogSeeder.cs` | VERIFIED | 96% |
| `ProcessedBusinessMessage` reutilizable para dedupe de efecto | `Growth/.../Idempotency/ProcessedBusinessMessage.cs:9-15` | VERIFIED | 97% |
| Separación aggregate Campaign vs CampaignRun | ADR-CAMP-000 §Decisiones/#8 | DESIGN | 90% |
| Sub-dominio Contact/ContactList/SenderProfile (aristas) | diseño (este doc §6), decisión 2026-09-16 | NEW | 88% |
| Wallet DIFERIDO (va, pero no ahora); dominio Wallet-free extensible | decisión del usuario 2026-09-16 | DECISION | 99% |
