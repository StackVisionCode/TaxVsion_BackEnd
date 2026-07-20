# Growth — Auditoría de código (bug-hunt del código implementado)

Fecha: 2026-07-19
Alcance: revisión adversarial a nivel de código de los ~206 archivos ya implementados de
`src/Services/Growth/` (Codes + Referrals), enfocada en **correctitud de dominio, seguridad,
concurrencia, y cumplimiento de guardrails §48–§49**. Complementa —no repite— las auditorías
previas (`Referrals_Codes_Code_Verification_Report.md`, `Referrals_Codes_Readiness_Checklist.md`),
que cubren el eje "qué falta para integración/producción".

Método: cuatro revisiones adversariales en paralelo (dominio Codes, dominio Referrals,
Infrastructure/concurrencia, Api/Application), con **cada hallazgo severo verificado por lectura
directa del código** antes de incluirlo. Los falsos positivos descartados se listan al final.

Estado del servicio al momento de la auditoría: **compila limpio, 49/49 tests pasan**.

---

## Resumen de severidad

> **Remediación 2026-07-20:** **B-01, B-02 y B-16 ARREGLADOS** (compila + 51/51 tests). B-02 y B-16
> además **verificados en vivo** contra `growth-api` real en contenedor (SQL Server + RabbitMQ).
> B-02: burst de 35 requests → primeras 30 en `400`, request 31+ en `429`, resetea tras la ventana
> de 1 min, sin afectar `/health/live`. B-16: flujo real Create → Activate → Read de un
> `CodeDefinition` vía HTTP con JWT real, persistido en SQL. Corrección de nota previa: **la
> migración EF inicial de Growth ya existía** (`InitialGrowth`, CVR-035 estaba resuelto); se
> detectó y corrigió un drift real de modelo (`GrowthModelSync`, columna `MinimumPaymentCurrency`
> nullable) y se completó el wiring de deploy (`.env`: `GROWTH_DB_CONNECTION` + 2 peppers). Ver
> nota al pie de cada hallazgo para el detalle.

| # | Sev | Área | Hallazgo | Estado |
|---|---|---|---|---|
| B-01 | HIGH | Infra/eventing | Domain events publicados NO transaccionalmente (outbox configurado pero no usado) | **Arreglado, verificado en vivo** |
| B-02 | HIGH | Api/seguridad | Sin rate limiting en endpoints públicos → enumeración/brute-force de códigos | **Arreglado, verificado en vivo** |
| B-16 | HIGH | Api/Application | Tenant-context no llega al scope de Wolverine en comandos locales → las 18 mutaciones de Growth fallan vía HTTP | **Arreglado, verificado en vivo** |
| B-03 | HIGH (latente) | Codes/dinero | `RevokeBenefit` cierra la reserva como terminal sin exigir reversión del descuento | Verificado |
| B-04 | MEDIUM | Codes/FSM | `CodeDefinition.Expire` permitido desde `Draft`/`Suspended` (viola el state machine) | Verificado |
| B-05 | MEDIUM | Referrals/dinero | `MeetsMinimum` compara importes entre monedas distintas cuando `MinimumPaymentCurrency` es null | Verificado |
| B-06 | MEDIUM | Referrals/FSM | Atribución `Pending` queda atascada para siempre al vencer la ventana | Verificado |
| B-07 | MEDIUM | Api/seguridad | Endpoint público de atribución sin `[HasPermission]` explícito | Verificado |
| B-08 | MEDIUM (latente) | Infra+Api/seguridad | Lookup de reward case para compensación/clawback sin scoping de tenant (IDOR) | Verificado |
| B-09 | MEDIUM | Guardrail §48.6 | LINQ dentro de aggregates (`CodeDefinition`, `ReferralProgram`) | Verificado |
| B-10 | LOW | Referrals/idempotencia | Idempotencia de finalización compara valor almacenado (trim) vs input crudo | Verificado |
| B-11 | LOW | Referrals/FSM | `FraudReview.Escalated` es terminal: no se puede registrar el desenlace de la escalación | Verificado |
| B-12 | LOW | Infra/concurrencia | UPDATE de quota exige `Maximum` idéntico → denegación silenciosa si cambia el tope | Verificado |
| B-13 | LOW | Infra/idempotencia | Operaciones fallidas no dejan registro de idempotencia (`Failed` es código muerto) → re-ejecución total | Verificado |
| B-14 | LOW | Codes+Referrals/dominio | Topes acumulados (compensación) y cuota anual dependen de que el caller pase el estado correcto — el dominio no auto-enforza | Verificado |
| B-15 | LOW | Referrals/defense-in-depth | `GrantId` no determinístico; el dominio no auto-enforza idempotencia (mitigado por índice único en BD) | Verificado |
| DEUDA | — | Guardrail §49 | Ninguna config usa `ValueGeneratedNever()`; todas las PK Guid son `ValueGeneratedOnAdd` | Verificado (no rompe insert) |

---

## HIGH

### B-01 — Domain events publicados NO transaccionalmente con el guardado (no es un outbox real)
`Persistence/GrowthDbContext.cs:65-72`, `:131-158`

`SaveChangesAsync` ejecuta `await DispatchDomainEventsAsync(...)` **antes** de `base.SaveChangesAsync`
(líneas 71-72), y `DispatchDomainEventsAsync` hace `ClearDomainEvents()` (línea 154) y luego
`await messageBus.PublishAsync(domainEvent)` (línea 157) sobre el `IMessageBus` inyectado — no
sobre un outbox EF-enrolado (`IDbContextOutbox`/interceptor). Los eventos se publican y se limpian
del agregado **antes** de que la fila se persista.

**Escenario de fallo:** dentro de `SqlBusinessIdempotencyExecutor`, la `SaveChangesAsync`
(línea 126) dispara los eventos y **luego** viene el `CommitAsync` (línea 128-131). Si el commit
falla (deadlock, `DbUpdateConcurrencyException`, unique-violation en un insert hermano) o la
transacción ambiental hace rollback, los consumidores ya recibieron el evento para un estado que
nunca existió; y como el agregado ya limpió sus eventos, el reintento idempotente **no** los
re-emite → efecto fantasma permanente.

**Blast radius actual: acotado** — como Growth hoy no declara ningún `PublishMessage<T>()`
(ver B-02/M1), estos domain events no salen del proceso; el riesgo se materializa en cuanto se
cablee la mensajería de integración. Aun así es un defecto foundational del eventing.

**Recomendación:** publicar vía el outbox EF de Wolverine (persistir los envelopes en la MISMA
`SaveChanges`/transacción, p.ej. `IDbContextOutbox`) y drenar los domain events **después** de que
la fila esté en el change set pero dentro de la misma transacción atómica.

> **ARREGLADO (2026-07-19) — VERIFICADO EN VIVO (2026-07-20):** `GrowthDbContext.SaveChangesAsync`
> ahora ejecuta `base.SaveChangesAsync` **primero** y `DispatchDomainEventsAsync` **después**,
> dentro de la transacción ambiental de Wolverine (que no se confirma hasta que el handler
> termina). Así los eventos se encolan en el outbox durable y se entregan atómicamente al commit;
> si el save falla, no se despacha ni se limpia nada. Nota: el mismo antipatrón existe en
> `AuthDbContext` (fuera de alcance de este fix). **Verificación en vivo:** una vez arreglado B-16
> (que bloqueaba todo write real), el flujo Create→Activate de un `CodeDefinition` ejecutó dos
> `SaveChangesAsync` reales contra SQL Server en contenedor sin errores ni excepciones — confirma
> el reorder en producción, no solo en los 51 tests unitarios.

### B-02 — Sin rate limiting en endpoints públicos que consumen códigos (enumeración/brute-force)
`Api/Program.cs` (pipeline completo, nunca llama `AddRateLimiter`/`UseRateLimiter`);
`Api/Controllers/ReferralsController.cs:28`; gateway `BuildingBlocks.Web/RateLimiting/RateLimitingRegistration.cs:19-42`

Growth no registra ningún rate limiter, y el limitador del Gateway solo cubre rutas `/auth/*`
puntuales y `/storage/files/*`; **`/growth/*` cae en `GetNoLimiter("unlimited")`**. Los endpoints
internos M2M están fuera del Gateway (sin límite tampoco).

**Escenario de fallo:** un actor con cualquier JWT de tenant válido llama `POST /growth/referrals/attributions`
en bucle probando valores de `ReferralCode`, sin ningún tope. El handler devuelve `ReferralCode.Invalid`
vs `ReferralProgram.NotFound` (`CreateReferralAttributionHandler`), dando además un **oráculo de
enumeración**. Diverge de `20_Growth_Security_Model.md:39,43`, que exige rate limit y lista
"code enumeration / rate limiting" como prueba negativa obligatoria.

**Recomendación:** registrar `AddRateLimiter`/`UseRateLimiter` y aplicar `[EnableRateLimiting]`
(particionado por IP + tenant) al menos en `attributions` y en la creación de quotes.

> **ARREGLADO (2026-07-19) — VERIFICADO EN VIVO (2026-07-20):** `Program.cs` ahora registra
> `AddRateLimiter` + `UseRateLimiter` (después de auth) con dos políticas en
> `RateLimiting/GrowthRateLimitPolicies.cs`: `growth-referral-attribution` (30/min particionado por
> tenant+IP, aplicada a `POST /growth/referrals/attributions`) y `growth-code-quote` (1000/min por
> IP, aplicada a `POST /internal/codes/quotes`). Réplica del patrón ya probado en vivo en PaymentApp.
> `429` en exceso. **Verificación en vivo:** contenedor `growth-api` real contra SQL Server +
> RabbitMQ en Docker, con un JWT HS256 válido firmado con el `Jwt:Secret` de dev. Burst de 35
> requests a `POST /growth/referrals/attributions`: las primeras 30 devolvieron `400` (validación de
> negocio, pasaron el rate limiter), la request 31 en adelante devolvió `429` de forma consistente
> (6/6). `GET /health/live` (endpoint sin la política) siguió en `200` durante el burst — confirma
> que no afecta tráfico no relacionado. Tras esperar 61s (fin de la ventana fixed-window de 1 min),
> una nueva request volvió a `400` — confirma que es una ventana deslizante, no un ban permanente.

### B-16 — Tenant-context no llega al scope de Wolverine en comandos locales → las 18 mutaciones de Growth fallan vía HTTP
`Api/Program.cs:61-63` (política de middleware solo para `IIntegrationEvent`);
`Api/Common/GrowthTenantMessageMiddleware.cs`; `Infrastructure/Idempotency/SqlBusinessIdempotencyExecutor.cs:32-36`

**Encontrado durante la verificación en vivo de B-02** (no por revisión estática — por eso ninguno
de los 4 agentes de auditoría ni los 49 tests lo detectaron). `JwtTenantContextMiddleware` sí setea
`ITenantContext` correctamente en el scope HTTP a partir del claim `tenant_id` del JWT. Pero
`bus.InvokeAsync(command, ct)` en los controllers (p.ej. `ReferralsController.cs:42`) hace que
Wolverine ejecute el handler del comando en un **scope de DI nuevo**, donde `ITenantContext` vuelve
a nacer vacío. `GrowthTenantMessageMiddleware.Before` (el único código que re-establece el tenant
dentro del scope del mensaje) está cableado en `Program.cs:61-63` **solo** para
`Policies.ForMessagesOfType<IIntegrationEvent>()` — y **ninguno de los 18 comandos mutantes** de
Growth (`CreateReferralAttributionCommand`, `CreateQuoteCommand`, `ReserveCodeCommand`,
`CommitReservationCommand`, etc. — ninguno implementa `IIntegrationEvent`) recibe ese middleware.

**Consecuencia:** dentro del handler, cualquier código que dependa de `ITenantContext` ve
`HasTenant = false`. La primera capa que lo chequea, `SqlBusinessIdempotencyExecutor.ExecuteAsync`
(líneas 32-36), corta inmediatamente con `Growth.Idempotency.TenantRequired` **antes** de tocar el
dominio o `GrowthDbContext.SaveChangesAsync`. Efecto práctico: falla cerrado (no hay fuga de datos
cross-tenant — el guard de idempotencia lo previene), pero **ninguna mutación de Growth funciona
sobre HTTP real**, aunque el JWT sea válido y tenga el tenant correcto.

**Verificado en vivo:** con un JWT válido (`tenant_id` presente y parseable), `POST
/growth/referrals/attributions` devuelve consistentemente `{"code":"Growth.Idempotency.TenantRequired", ...}`
con `400`.

**Por qué los tests no lo agarraron:** los 49 tests llaman los handlers de aplicación directo con un
`ITenantContext` de prueba ya poblado, sin pasar por Wolverine ni por el scope real de mensaje — el
gap solo existe en el camino API → `bus.InvokeAsync` → handler.

**Recomendación (no aplicada):** introducir un marcador común para los comandos mutantes de Growth
(p.ej. `ITenantScopedCommand { Guid TenantId }` — todos los 18 comandos ya reciben `tenantId` como
primer parámetro posicional) y cablear una política Wolverine genérica sobre ese marcador, análoga a
`GrowthTenantMessageMiddleware` pero para comandos locales en vez de `IIntegrationEvent`.

**Estado:** **ARREGLADO Y VERIFICADO EN VIVO (2026-07-20)** — el usuario pidió explícitamente
arreglarlo pese a que Growth no es un servicio que trabaje directamente.

> **Fix aplicado, sin tocar los 18 records de comando:** Wolverine expone soporte nativo de tenant
> por mensaje (`Envelope.TenantId` / `IMessageBus.TenantId`, confirmado por reflection sobre
> WolverineFx 6.14.0). `JwtTenantContextMiddleware.cs` ahora también estampa `bus.TenantId =
> tenantId.ToString()` junto con `TenantContext.SetTenant(...)`. Un middleware nuevo,
> `GrowthLocalCommandTenantMiddleware.cs` (`Before(Envelope, TenantContext)`), registrado
> **globalmente** en `Program.cs` vía `options.Policies.AddMiddleware(typeof(...))` (sin filtro de
> tipo de mensaje, a diferencia de `GrowthTenantMessageMiddleware` que solo cubre
> `IIntegrationEvent`), lee `Envelope.TenantId` de vuelta al `TenantContext` del scope que
> Wolverine crea para el handler — cubriendo los 18 comandos locales sin que ninguno necesite
> declarar tenant explícitamente.
>
> **Verificado en vivo:** contenedor real (SQL Server + RabbitMQ), flujo completo
> `POST /growth/codes` (crear) → `POST /growth/codes/{id}/activate` → `GET /growth/codes/{id}`,
> con JWT real con claims `perm`. Los 3 pasos devolvieron `200` y la fila quedó persistida en SQL
> (`codes.CodeDefinitions`, `Status=Active`, `RowVersion` incrementado por dos `SaveChangesAsync`
> reales sin errores — confirma también B-01 con un write real, no solo por los tests unitarios).
> Repetido contra Referrals: `POST /growth/referrals/attributions` pasó de `400
> Growth.Idempotency.TenantRequired` a `404 ReferralProgram.NotFound` (error de negocio legítimo,
> no hay programa creado) — confirma que el fix cubre ambos bounded contexts.
>
> **Tests:** 51/51 (se agregaron 2 tests nuevos — `JwtTenantContextMiddlewareTests` — para el stamp
> de `bus.TenantId` y para `GrowthLocalCommandTenantMiddleware.Before`).

### B-03 — `RevokeBenefit` cierra la reserva como terminal sin exigir reversión del descuento (latente)
`Compensations/CodeCompensation.cs:99-108` vs `:123-126`

`RestoreAvailability` está obligado a ajustar el descuento completo
(`cumulativeAdjustmentAmountCents != redemption.DiscountAmount.AmountCents` → failure, líneas 99-108).
`RevokeBenefit` **no** tiene ese guard, pero `isFinal` se fija en `true` para él (línea 124). Así,
un `RevokeBenefit` con `adjustmentAmount = 0` / `cumulative = 0` pasa todas las validaciones, marca
la reserva `Compensated` (terminal) y libera/conserva disponibilidad mientras el descuento nunca se
revirtió — y no se puede registrar ninguna compensación posterior.

**Escenario:** redemption con `DiscountAmount = 5000 USD`. `Create(RevokeBenefit, Money.Zero(USD), prior:0, …)`
→ todas las validaciones pasan, `IsFinal = true`, reserva `Compensated`. El beneficio se "revocó"
pero 5000 centavos de descuento nunca se contabilizaron.

**Latente:** la decisión de tipo de compensación depende de la matriz refund/chargeback que **aún
no está implementada** (bloqueador de integración conocido), así que hoy no es alcanzable por un
flujo real. Es un hueco de invariante del aggregate que se debe cerrar antes de cablear compensación.

**Recomendación:** aplicar a `RevokeBenefit` el mismo requisito de descuento completo que
`RestoreAvailability`, o restringirlo explícitamente a grants de descuento cero / no monetarios.

---

## MEDIUM

### B-04 — `CodeDefinition.Expire` permitido desde `Draft`/`Suspended` (viola el FSM documentado)
`Definitions/CodeDefinition.cs:286-299`

`Expire` solo bloquea `Revoked`/`Expired` (línea 288). Un código `Draft` (nunca activado) o
`Suspended` cuya `ExpiresAtUtc` pasó transiciona directo a `Expired`. El FSM documentado
(`10_Codes_State_Machines.md:8`) solo permite `Active ──expiry job──► Expired`.

**Recomendación:** restringir `Expire` a `Status == Active` (y `Suspended` si se decide), acorde al FSM.

### B-05 — `MeetsMinimum` compara importes entre monedas distintas cuando la moneda es null
`Programs/ReferralProgramPolicy.cs:124-131`, `:160-171`

`Validate` solo valida el formato de moneda cuando `minimumPaymentCurrency` **no** es null
(líneas 160-171), así que se puede crear una policy con `minimumPaymentAmountCents = 5000,
minimumPaymentCurrency = null`. Entonces `MeetsMinimum` compara `amountCents` numéricamente
**ignorando la moneda** (línea 129: `MinimumPaymentCurrency is null || match`).

**Escenario:** umbral 5000 (pensado en USD, currency=null). Un pago de `5000 JPY` (~USD 33) →
`MeetsMinimum(5000, "JPY")` → `true` → califica indebidamente.
**Mitigación parcial:** el default MVP usa umbral = 1 minor unit, así que en la config por defecto
cualquier pago lo satisface. El bug muerde solo con un mínimo configurado y moneda null.

**Recomendación:** si `MinimumPaymentAmountCents > 0`, exigir `MinimumPaymentCurrency` no-null en `Validate`.

### B-06 — Atribución `Pending` queda atascada para siempre al vencer la ventana
`Attributions/ReferralAttribution.cs:121-133` (`Activate`), `:225-247` (`Expire`)

Una atribución `Pending` cuya ventana expiró no puede transicionar: `Activate` devuelve el error
`ReferralAttribution.Expired` **sin cambiar el estado** (línea 128-129, sigue `Pending`), y `Expire`
exige `Status == Active` (línea 234) → da `InvalidTransition`. Queda `Pending` para siempre. Si el
unique de re-atribución cuenta filas no terminales, **bloquea la re-atribución futura del referee**.
El FSM (`11_Referrals_State_Machines.md:6`) tampoco define `Pending→Expired`.

**Recomendación:** permitir `Expire` desde `Pending` cuando `nowUtc >= ExpiresAtUtc`.

### B-07 — Endpoint público de atribución sin permiso explícito
`Api/Controllers/ReferralsController.cs:19,28`

`CreateAttribution` solo tiene el `[Authorize]` de clase (línea 19), sin `[HasPermission(...)]` — es
la única mutación pública sin permiso explícito (comparar con `CodesController`, que exige
`CodesManage`/`CodesActivate`/etc). No hay IDOR (el referee sale del JWT, líneas 36-45; el
`ToString` del request redacta el código), pero se salta el modelo "JWT + permiso explícito" de
`20_Growth_Security_Model.md:5`. Sumado a B-02 (sin rate limit), amplía la superficie de enumeración.

**Recomendación:** agregar un permiso explícito (p.ej. `referrals.attribution.create`) o documentar
formalmente que es self-service sin permiso.

### B-08 — Lookup de reward case para compensación/clawback sin scoping de tenant (IDOR latente)
`Persistence/Repositories/Referrals/ReferralRewardCaseRepository.cs:39-57`;
`Referrals.Application/Rewards/RequestReferralRewardClawback/RequestReferralRewardClawbackHandler.cs:39`

`GetForCompensationAsync` hace `ReferralRewardCases.IgnoreQueryFilters().SingleOrDefaultAsync(rc => rc.Id == rewardCaseId)`
**sin ningún predicado de tenant/scope**, y devuelve la entidad *tracked* que el caller compensa y
guarda. A diferencia de las otras elevaciones (`ReferralProgramRepository.GetForEvaluationAsync`,
`ReferralCodeRepository.ResolveByHashAsync`), que sí restringen a platform/current-tenant + scope.
El handler de clawback (y el de begin-grant) tampoco validan `reward.TenantId == command.TenantId`
(a diferencia de `ConfirmReferralRewardGrantHandler:36-46`, que sí lo hace).

**Escenario:** un mensaje de compensación originado en el tenant A con un `rewardCaseId` del tenant B
lee y muta el reward case de B — IDOR cross-tenant de lectura+mutación.
**Latente:** `RequestReferralRewardClawbackCommand`/`BeginReferralRewardGrantCommand` no se despachan
hoy desde ningún controller ni consumer → no alcanzable aún.

**Recomendación:** al exponer estos comandos, cargar/validar por `(RewardCaseId, TenantId)` como
hace el confirm.

### B-09 — LINQ dentro de aggregates (guardrail §48.6)
`Definitions/CodeDefinition.cs:204,332,491,496-497`; `Programs/ReferralProgram.cs:245`

Guardrail §48.6 exige cero LINQ dentro de aggregates (bucles privados con nombre). Violaciones en
raíces de agregado:
- `CodeDefinition`: `_scopes.Any(...)` (:204), `_ruleVersions.OrderByDescending(...).First()` (:332),
  `_scopes.Where(...).Any(... targets.Any(...))` (:491, :496-497).
- `ReferralProgram`: `programCode.Trim().Any(character => ...)` (:245) en `ValidateCreation`.

**Recomendación:** reemplazar por métodos privados con nombre (`HasDuplicateScope`, `SelectLatestRule`,
`IsExcluded`, `IsIncluded`, `HasInvalidChar`). (Los `.All(...)`/`.Any(...)` en value objects y en
`DomainGuards`/repos quedan fuera del literal §48.6 "dentro de aggregates"; reescribir solo si el
guardrail se interpreta repo-wide.)

---

## LOW

### B-10 — Idempotencia de finalización compara valor almacenado (trim) contra input crudo
`Rewards/ReferralRewardCase.cs:137`; `Rewards/ReferralRewardAttempt.cs:106,155`

El replay idempotente compara el campo ya normalizado (`.Trim()`/truncado) contra el argumento crudo.
Un replay idéntico con espacios se clasifica mal: `ConfirmGranted` con referencia con espacios cae a
`InvalidTransition` (línea 140) en vez de éxito idempotente; `ReferralRewardAttempt` da falso
`IdempotencyConflict`. **Recomendación:** comparar contra `input.Trim()` (como sí se hace con la
`CompletionIdempotencyKey`).

### B-11 — `FraudReview.Escalated` es terminal: no se puede registrar el desenlace de la escalación
`Fraud/ReferralFraudReview.cs:133-155`

`Resolve` solo transiciona desde `Open`/`Investigating`. Una vez `Escalated`, cualquier
`Approve`/`Reject` posterior da `InvalidTransition` — la escalación nunca puede cerrarse.
**Recomendación:** permitir `Escalated → Approved/Rejected` (el doc `11_...:26` es ambiguo sobre si
`Escalated` es terminal; si la intención es que reciba desenlace, es un bug).

### B-12 — UPDATE de quota exige `Maximum` idéntico → denegación silenciosa si cambia el tope
`Persistence/Repositories/Referrals/SqlReferralRewardQuota.cs:102-115`

El `UPDATE ... WHERE ReservedCount < [Maximum] AND [Maximum] = {maximum}` (línea 111) incluye
`Maximum` en el `WHERE`. Si el máximo anual configurado cambia después de crear el counter, toda
reserva posterior pasa un `maximum` distinto → matchea 0 filas → `return false` habiendo capacidad.
Falla cerrado (no oversubscribe) pero **rechaza reservas legítimas en silencio**.
**Recomendación:** actualizar `Maximum` en el counter o no incluirlo en el `WHERE` del incremento.

### B-13 — Operaciones fallidas no dejan registro de idempotencia (re-ejecución total)
`Idempotency/SqlBusinessIdempotencyExecutor.cs:105-118`; `ProcessedBusinessMessage.cs:93-105`

Cuando `operationBody` retorna `IsFailure`, se hace rollback del claim completo y **nunca** se
persiste un registro `Failed` (`ProcessedBusinessMessage.Fail(...)` es código muerto). Una operación
con side-effects previos al failure se re-ejecuta íntegra en el reintento. **Recomendación:**
confirmar si es intencional; si hay side-effects previos al failure, persistir el resultado `Failed`.

### B-14 — Topes acumulados dependen de que el caller pase el estado correcto (el dominio no auto-enforza)
`Compensations/CodeCompensation.cs:29,64-81`; `Qualifications/ReferralQualification.cs:44,173`; `Rewards/ReferralRewardCase.cs`

Dos invariantes de dinero se enforzan **fuera** del aggregate:
- La cap acumulada de compensación (`cumulative <= discount`, `CodeCompensation.cs:75`) es sólida
  solo si el caller pasa el `priorCumulativeAdjustmentAmountCents` real; con `prior = 0` cada refund
  parcial pasa independientemente (N × descuento posible).
- La cuota anual (`MaximumRewardsPerReferrerPerCalendarYear`) **no** se aplica en el dominio:
  `ReferralQualification.Evaluate` confía en el booleano `annualRewardSlotAvailable` y
  `ReferralRewardCase.Request` no re-verifica ningún contador. (El enforcement real y correcto está
  en `SqlReferralRewardQuota` — ver "verificado correcto".)

Es una separación dominio/persistencia deliberada, pero deja los invariantes de dinero dependiendo
de la orquestación. **Recomendación:** documentar el contrato (el caller DEBE originar `prior`/slot
del estado persistido) y cubrirlo con tests de aplicación; considerar mover la cap al aggregate.

### B-15 — `GrantId` no determinístico; el dominio no auto-enforza idempotencia de reward
`Rewards/ReferralRewardCase.cs:96`; `Configurations/Referrals/ReferralRewardCaseConfiguration.cs:60-74`

`GrantId = Guid.NewGuid()` se genera fresco en cada `Request` y el aggregate no verifica si ya existe
un caso para esa `QualificationId`. **Sin embargo, el doble reward está prevenido en BD** por el
índice único `UX_ReferralRewardCases_Qualification_Beneficiary_Reward` sobre
`(QualificationId, BeneficiaryType, BeneficiaryId, RewardType)` (líneas 62-70): un segundo `Request`
choca contra el índice → `ConflictException`. Residual: (a) el dominio no se auto-protege (frágil si
el índice cambia o si un mismo qualification pudiera emitir distinto `RewardType`); (b) un segundo
trigger legítimo con distinta idempotency key recibe un conflicto duro en vez de un replay limpio;
(c) `GrantId` aleatorio no sirve como ancla de idempotencia cross-service para el command de grant.
**Recomendación:** derivar `GrantId` determinísticamente (p.ej. hash de `QualificationId`) para
anclar la idempotencia del grant hacia Subscription.

---

## Deuda de convención — Guardrail §49 (NO rompe insert)

Ninguna configuración EF de Growth llama `ValueGeneratedNever()`; el model snapshot mapea **todas**
las PK `Guid` como `ValueGeneratedOnAdd()`, mientras `BuildingBlocks/Domain/BaseEntity.cs:5` genera
el `Id` en dominio (`= Guid.NewGuid()`). **Esto NO rompe el insert con SqlServer**: para PK `Guid`
con `ValueGeneratedOnAdd`, EF usa `SequentialGuidValueGenerator`, que solo genera cuando el valor es
`Guid.Empty`; como el dominio siempre asigna un Guid no-vacío, EF respeta el valor del dominio. Es
una violación universal de la convención §49 (fragilidad: se rompería si alguien agrega un
value-converter en la key o cambia el sentinel), pero no el fallo de inserción que el guardrail
advierte. Las tablas de quota se insertan por SQL crudo, así que §49 les es irrelevante.

**Recomendación:** agregar `ValueGeneratedNever()` a las configs de entidades cuyo `Id` se genera en
factory de dominio, por consistencia con el resto del ecosistema y para blindar contra el fallo que
el guardrail describe.

---

## Verificado como CORRECTO (descartado explícitamente)

Estos ejes se auditaron a fondo y están bien implementados — se listan para dar confianza:

- **Oversubscription de quota:** `SqlReferralRewardQuota` usa `UPDATE ... SET ReservedCount = ReservedCount + 1 ... WHERE ReservedCount < [Maximum]` (atómico, condicional, `<` correcto no `<=`), con `UPDLOCK, HOLDLOCK` respaldados por índices únicos (key-range lock real), en transacción ambiental, revirtiendo la reserva si el counter está lleno. Sin TOCTOU.
- **Idempotencia insert-first:** `SqlBusinessIdempotencyExecutor` inserta el claim primero; el índice único `UX_ProcessedBusinessMessages_Tenant_Operation_Scope_Key` **excluye** el fingerprint → mismo key con distinto payload = `FingerprintConflict`; la unique-violation se traduce a `ConflictException` y se resuelve como replay. Sin ventana de doble-win. Savepoints correctos.
- **Result nunca ignorado:** todos los handlers chequean `.IsFailure`; `PaymentLifecycleConsumers.EnsureApplied` lanza ante fallo — el patrón del bug de webhooks de Payment **no** está presente. Cancel maneja bien el evento stale.
- **IDOR / ownership (excepto B-08):** `CodesController.ResolveOwnership` fuerza owner=caller para scope Tenant, exige PlatformTenant para scope Platform y `AdminCrossTenant` para cross-tenant; los lookups de reserva/quote/redemption pasan `command.TenantId`; el global query filter aplica a TODAS las `ITenantOwned` (sin tenant → `Guid.Empty` → 0 filas, fail-closed).
- **Redacción de secretos:** peppers HMAC obligatorios y validados (`>= 32 bytes`) en `ValidateOnStart` y constructor, separados por dominio; tokens nunca logueados/persistidos en claro (solo digest); `ToString` de requests y responses redactan.
- **FailClosedPaymentOutcomeVerifier:** todos los caminos retornan `Result.Failure`. Correcto.
- **Auth/M2M:** policies exigen claim `perm` exacto (sin bypass de rol PlatformAdmin); M2M exige `actor_type=Service` + audience `taxvision-growth` + scope exacto; rutas `/internal/*` fuera del match público del Gateway. JWT valida audience/issuer/lifetime/signing.
- **Constraints/índices:** counter no-negativo y `<= Maximum`, unique payment por reservation, unique attribution activa por referee, no-self-referral, unique redemption por reservation, unique reward case por `(Qualification, Beneficiary, RewardType)`. Presentes.
- **RowVersion:** en todas las entidades con contadores/estado mutable. `ReferralQualification` sin RowVersion pero es write-once (correcto).
- **Cálculo de descuento (Codes):** `PercentageBasisPoints.ApplyTo` con `MidpointRounding.AwayFromZero` y `Value <= 10_000` garantiza `discount <= gross`, `net >= 0`; `CodeQuote.Create` re-valida. `Money` bloquea negativos y usa `checked` con overflow → failure. Off-by-one de disponibilidad correcto. Boundaries de TTL/expiración consistentes. Sin `virtual` (§48.7). Transiciones inválidas → `Result.Failure` (§48.5).
- **Self-referral, ventana, waiting period (Referrals):** `referrer == referee` bloqueado; `Activate` rechaza `TaxpayerToTaxpayer`; policy versionada/inmutable; boundaries de ventana consistentes; waiting period desde `paymentSucceededAtUtc`; clawback monotónico. Sin `virtual`; transiciones inválidas → `Result.Failure`.

---

## Notas de dirección

1. **Nada de esto bloquea el estado actual** (`READY_FOR_DOMAIN_IMPLEMENTATION`). B-01 y B-02
   son los dos que conviene atacar antes de habilitar tráfico real; B-03 y B-08 son latentes pero
   deben cerrarse **antes** de cablear compensación/clawback (parte de la integración pendiente).
2. **Documentos superseded:** las auditorías previas ya recomiendan (DCM-002/003/005) rotular como
   históricos los docs `Implementaciones/Referrals_Service_Analysis_And_Design.md`,
   `Referrals_README.md` y `ProductsAndServices_Service_Analysis_And_Design.md` — el diseño vigente y
   más completo es esta serie `documents/architecture/growth/` + el código de `TaxVision.Growth`.
