# TaxVision.Wallet — Plan & Arquitectura (cobro de comunicaciones: campañas + envíos individuales)

- **Servicio:** `TaxVision.Wallet` (NUEVO, greenfield).
- **Fecha:** 2026-10-06.
- **Estado:** DISEÑO — no implementado. **Documento autoritativo** del folder `wallet-ledger/`; manda sobre los demás docs mientras se re-alinean.
- **Rol (binding):** monedero **prepago por tenant** que **reserva → consume → libera** saldo para pagar el envío de **campañas y de mensajes individuales (SMS/Email)**, con Campaigns y los canales **money-agnostic**. Para el MVP, Wallet también aloja el **catálogo de precios**, el **autorizador (PEP)** y la **coordinación de medición** (no se crea un servicio `CommunicationBilling` aparte; se puede extraer luego sin cambiar el contrato).
- **Coherente con:** `../05_Master_ADR.md` (ADR-CAMP-001 D7 — Campaign = orquestador sin dinero), `../../billing/*` (patrón de cobro SaaS), y la **propuesta complementaria 1.0 (2026-10-06)** cuyo rigor se adopta (micros, reserve/consume/release con dos deltas, precios versionados, identidad económica, expiración segura).

> **Historial de decisión:** el modelo pasó por: (v1) `wallet-ledger` original = reserve→consume→**refund**; (v2 efímera) prepago **sin** refund (ADR-WAL-005 previo); (v3 ACTUAL, aprobada por el usuario 2026-10-06) **reserve→consume→release** con dos deltas + **micros**, dentro de Wallet, cobro unificado. Esta v3 es la vigente.

---

## 1. Contexto y por qué

Campaigns orquesta el envío multicanal pero **no maneja dinero** (ADR-CAMP-001). Para monetizar el envío (campañas **y** mensajes sueltos) se cobra por uso contra un **saldo prepago por tenant**, sin acoplar Campaigns ni los canales a la facturación.

Flujo esencial: el tenant **recarga** (dinero entra, cobro real por Stripe vía PaymentApp). Antes de cada envío, un **autorizador (PEP)** cotiza el costo y **reserva** (hold) ese importe; al enviarse, se **consume** lo efectivamente facturable y se **libera** el remanente reservado-no-usado. Si no hay saldo, el envío queda **AwaitingFunds** y se ofrece recargar. **Nunca** se cobra desde el navegador ni se confía en un `GET /balance` seguido de un envío: la reserva atómica es la única garantía de que dos envíos simultáneos no gastan el mismo saldo.

---

## 2. Decisiones binding (ADRs vigentes)

| # | Decisión | Nota |
|---|----------|------|
| **ADR-WAL-005** | **Reserve → Consume → Release** (dos deltas: Posted y Held). Se aparta el estimado (Held), se cobra lo facturable (Consume baja Posted y Held), y se **libera** el Held no usado (Release NO es un refund). | Supersede tanto el reserve→consume→**refund** original (ADR-WAL-004) como el prepago-sin-refund efímero. |
| **ADR-WAL-006** | **Precisión en micros** (enteros de 64 bits, millonésimas de USD): `PostedMicros`, `HeldMicros`, `AmountMicros`. USD 1.00 = 1 000 000 micros; USD 0.002 = 2 000 micros. | **Revisa ADR-WAL-002** (centavos): centavos no representan USD 0.002/email. En JSON los `*Micros` viajan como **string** (preserva `long` en JS). |
| **ADR-WAL-007** | **Catálogo de precios versionado e inmutable** (`PriceBook`/`PriceBookVersion`/`PriceRule`), ámbito tenant/plan/global, vigencias UTC, editable por **PlatformAdmin**. Una tarifa ausente **nunca** es precio cero. | Reemplaza el `ChannelPrice` plano. |
| **ADR-WAL-008** | **Una sola autoridad de cobro (PEP), dentro de Wallet**, para **campañas y envíos individuales**. Campaigns/SMS/Email siguen money-agnostic: publican ejecución/uso, no debitan. | El PEP cubre TODA ruta de ejecución (manual, programada, reintento, individual), no solo el botón "Enviar". |
| **ADR-WAL-009** | **Identidad económica estable**: `ExecutionRequestId` por ejecución, reserva única por `(Tenant, Currency, ConsumerContext, ExecutionRequestId, BudgetSliceId)`, consumo por `SettlementId`, crédito por `(SourceService, SaaSPaymentId)`. Dedupe técnico por evento **+** identidad económica permanente. | Reprocesar un evento histórico no crea cargo/crédito nuevo. |
| **ADR-WAL-010** | **MVP = medición estimada** (`reserva = consumo`): se reserva el estimado por conteo de unidades ya depurado (sin opt-outs/duplicados) y se consume ese importe; se libera solo el remanente real (p.ej. audiencia que encogió entre cotización y despacho). **Medición real por segmento SMS / aceptado por proveedor queda para una fase posterior.** | Permite entregar sin tocar aún los adaptadores de canal. |
| **ADR-WAL-011** | Acceso nuevo: `wallet.view`/`wallet.manage` + módulo `wallet`. Editar precios = solo PlatformAdmin. | Patrón de `campaigns`. |
| **ADR-WAL-001/003** | Se conservan: Wallet = microservicio independiente; **ledger append-only** + saldo cacheado (`PostedMicros`/`HeldMicros`), con reconciliación ledger↔caché. | |

---

## 3. Modelo de dinero (micros, dos deltas)

**Fórmulas (invariantes):**
```
AvailableMicros   = PostedMicros - HeldMicros
OutstandingMicros = AuthorizedMicros - ConsumedMicros - ReleasedMicros   (por reserva)
PostedMicros >= HeldMicros >= 0
OutstandingMicros >= 0      (al cerrar una reserva debe ser 0)
PostedMicros = Σ DeltaPostedMicros        HeldMicros = Σ DeltaHeldMicros = Σ OutstandingMicros de reservas abiertas
```

**Cada movimiento lleva DOS deltas explícitos** (corrección clave del review — `Release` y `UsageRefund` son distintos):

| Movimiento | ΔPosted | ΔHeld | Efecto |
|---|---|---|---|
| `TopUp(a)` | +a | 0 | Acredita un pago confirmado. |
| `Reserve(a)` | 0 | +a | Aparta saldo disponible (requiere `Available >= a`, atómico). |
| `Consume(a)` | −a | −a | Cobra parte de una reserva. |
| `Release(a)` | 0 | −a | Libera reserva no usada (NO crea crédito). |
| `UsageRefund(a)` | +a | 0 | Devuelve un consumo ya cobrado (vinculado al consumo original; ≤ lo cobrado). |
| `Adjustment(±a)` | ±a | 0 | Corrección económica autorizada, con motivo. |

**Aggregates:**
- `Wallet` (uno por `(TenantId, Currency)`): `PostedMicros` (long), `HeldMicros` (long), `Currency` ("USD"), `Status` (Active|Frozen), RowVersion (concurrencia optimista), `UpdatedAtUtc`. Métodos atómicos `Credit/Reserve/Consume/Release/Refund/Adjust`, cada uno agrega `LedgerEntry` y recalcula la caché en la MISMA transacción local.
- `Reservation`: `ExecutionRequestId`, `BudgetSliceId`, `AuthorizedMicros`, `ConsumedMicros`, `ReleasedMicros`, `Status` (Open|Closed), `QuoteId`, `ConsumerContext` (Campaign|IndividualSms|IndividualEmail). Único por `(TenantId,Currency,ConsumerContext,ExecutionRequestId,BudgetSliceId)`.
- `LedgerEntry` (append-only): `DeltaPostedMicros`, `DeltaHeldMicros`, `MovementType`, `OperationKey` (único por tenant), `ReferenceId`, `Actor`, `SequenceNo`, `CreatedAtUtc`.
- `FundingCredit`: único `(SourceService, SaaSPaymentId)` — dedupe de recargas.
- `Settlement`: único `(SourceService, SettlementId)` — dedupe de consumos, con huella del contenido.

---

## 4. Catálogo de precios versionado

- `PriceBook` (Id, Name, Currency, Scope: Global|Plan|Tenant), `PriceBookVersion` (Version, Status: Draft|Published|Retired, EffectiveFromUtc, Creator, Publisher), `PriceRule` (ProductCode, Channel, DestinationCountry?, Route/ServiceClass?, Unit, UnitPriceMicros, ChargeCondition, Priority), `PriceBookAssignment` (Tenant|Plan → libro, vigencia).
- **Productos iniciales:** `SMS_OUTBOUND_SEGMENT` (unidad = segmento), `EMAIL_OUTBOUND_RECIPIENT` (unidad = destinatario aceptado). `CAMPAIGN_EXECUTION_FEE` opcional, OFF por defecto.
- **Reglas:** tarifas publicadas **inmutables** (un cambio = nueva versión); precedencia tenant → plan → global, y dentro de un ámbito la regla de destino/ruta más específica; empates/vigencias solapadas/ausencia de tarifa **se rechazan** al publicar o cotizar. Precisión máx. 6 decimales.
- **Cotización (`Quote`)**: líneas con `quantity`, `unitPriceMicros`, `totalMicros`, `maximumChargeMicros`, `expiresAtUtc`, huella de los datos cotizados y `priceBookVersion`. Una ejecución autorizada **conserva su versión**; si cambia precio o audiencia respecto al presupuesto, se recotiza y re-autoriza.

---

## 5. Protocolo de cobro (PEP) — campañas e individuales

**Identidad:** antes de ejecutar se genera un `ExecutionRequestId` estable. La reserva se hace con ese id y luego se correlaciona con el `CampaignRunId` (o el id del envío individual).

**Secuencia (event-driven, at-least-once + idempotente por identidad económica):**
1. **Cotizar:** el PEP (Wallet) recibe audiencia+contenido autorizados y calcula unidades × precio de la versión vigente → `Quote` (huella + caducidad + `maximumChargeMicros`).
2. **Reservar:** `Reserve(maximumChargeMicros)` atómico (`Available >= amount`). Insuficiente → `AwaitingFunds` (devuelve requerido/disponible/faltante); se conserva el borrador/programación.
3. **Autorizar:** el PEP registra la reserva y habilita los mensajes cubiertos (el canal exige una autorización válida para emitir cada mensaje facturable — *dispatch claim* durable).
4. **Enviar y medir:** los adaptadores publican evidencia por mensaje; el PEP normaliza la unidad facturable y aplica la tarifa congelada. *(MVP: medición estimada = unidades reservadas.)*
5. **Consumir:** `Consume(amount)` por lote de liquidación durable (`SettlementId` estable; reintentar el mismo lote no re-cobra).
6. **Cerrar:** tras detener nuevos envíos y resolver los en curso, `Release(remanente)`.

**Punto de inserción en Campaigns** (money-agnostic): entre `CampaignRun.Start` (`StartCampaignRunCommand.cs:83`) y el fan-out (`106-133`): Campaigns publica `CampaignRunPendingAuthorizationIntegrationEvent { TenantId, CampaignId, RunId, ExecutionRequestId, PerChannelUnits, TriggeredBy }` (solo cuenta unidades) y **retiene el fan-out** hasta `CampaignRunAuthorizedIntegrationEvent{RunId}`; si `...AuthorizationDeniedIntegrationEvent{RunId, Reason="InsufficientFunds"}` → `CampaignRun.Reject("InsufficientFunds")` (estado `Rejected` + `RejectionReason` ya existen; falta la constante). Mismo gate en el **scheduler** (cada fire = su propia reserva).

**Envíos individuales (SMS/Email):** la misma autoridad. El servicio SMS/Email publica `CommunicationExecutionPendingAuthorization { ExecutionRequestId, Channel, Units/recipients }` antes de emitir; Wallet reserva/autoriza igual. Un mensaje ya cubierto por una reserva **no** se debita dos veces.

---

## 6. Recarga (top-up) vía Stripe

Clona el patrón plan-change (money-IN), en **micros**:
1. `POST /wallet/top-up { amountMicros | packageId }` → PaymentApp crea `TopUpOrder` (id estable, `amountToChargeMicros`, `amountToCreditMicros`; comisiones/promos aparte). `amountMicros = checked(amountCents * 10_000)`.
2. PaymentApp cobra Stripe **off-session** (tarjeta guardada) con clave idempotente de la orden (reintento = misma operación, no doble cobro). Nuevo `SaaSPaymentType.WalletTopUp` (verificar el enum real antes de asignar número; **no** asumir el 9).
3. **Confirmación server-side** (webhook firmado/consulta autenticada; coincidir pago/tenant/moneda/importe/estado). PaymentApp registra pago + evento de salida en la MISMA transacción (no publica desde una redirección del navegador).
4. Wallet consume el evento, verifica `FundingCredit` único `(SourceService,SaaSPaymentId)`, hace `TopUp(credit)` y registra saldo+dedupe+outbox en una transacción. Estados de recarga: `PendingPayment → PaidPendingCredit → Credited | Failed | Cancelled`.
5. Conciliación: detecta pagos confirmados aún sin acreditar. **Wallet Frozen:** bloquea nuevas reservas, pero un pago ya confirmado se acredita conservando `Frozen` (nunca se descarta el hecho financiero).

---

## 7. Acceso (permiso + módulo)

`wallet.view` (saldo/ledger/tarifas), `wallet.manage` (recargar). Editar tarifas = **PlatformAdmin** (actor-type, no permiso de tenant). Módulo `wallet` (ya en module-map + plan catalog Pro/Enterprise). M2M: solo Wallet tiene permisos de `Reserve/Consume`; los canales solo reportan evidencia; delegación por ámbito acotada (un token de servicio global no autoriza cualquier tenant).

---

## 8. API

**Tenant (vía Gateway/BFF, tenant de la sesión):**
- `GET /wallet` → `{ postedMicros, heldMicros, availableMicros, currency, status }` (`wallet.view`).
- `GET /wallet/transactions?cursor` → ledger paginado por cursor (`wallet.view`).
- `GET /wallet/rates` → tarifas de venta vigentes (`wallet.view`).
- `POST /wallet/top-ups { amountMicros | packageId }` → crea orden; `GET /wallet/top-ups/{id}` estado pago+acreditación (`wallet.manage`).
- `POST /wallet/quotes { source/content ref }` → cotización (costo por canal, `maximumChargeMicros`, `availableMicrosAtQuote`, `canAffordAtQuote`). *`canAffordAtQuote` NO es autorización.*
- `PUT /admin/price-books/... publish` → PlatformAdmin.

**Interno v2 (M2M, solo Wallet muta saldo):** `POST /internal/v2/wallet/reservations` (importe micros + identidad + QuoteId), `.../{id}/consumptions` (**delta** a consumir + `SettlementId`; respuesta = total acumulado), `.../{id}/release`, `/usage-refunds`, `/adjustments`. *(El contrato de consumo declara delta, no acumulado — evita el bug de "enviar 10 y luego 15 = consumir 25".)*

**Campaigns (money-agnostic):** `POST /campaigns/{id}/preview-audience` → `{ perChannelUnits, totalRecipients }` (para cotizar antes de enviar).

Recarga entra **solo** por evento confirmado de PaymentApp; `POST /top-ups` inicia un pago, no incrementa saldo.

---

## 9. Eventos e idempotencia

`wallet.topup.payment-succeeded.v2` (PaymentApp); `wallet.balance.credited.v2`, `wallet.funds.reserved.v2`, `wallet.funds.consumed.v2`, `wallet.funds.released.v2`, `wallet.usage.refunded.v2` (Wallet); `communication.usage.reported.v1` (adaptadores); `campaign.run.pending-authorization` / `.authorized` / `.authorization-denied`, `communication.execution.awaiting-funds.v1` (PEP). Cada evento: `EventId, Version, TenantId, OccurredOn, CorrelationId, CausationId`; los financieros llevan moneda + precisión; los de uso llevan `MessageId/channel/component/qty/evidencia`. **Dos capas:** dedupe técnico por evento **+** identidad económica permanente (pago/reserva/liquidación/uso) — una clave HTTP distinta NO permite recobrar. Reprocesar histórico tras purgar respuestas no crea cargo.

---

## 10. UX de intercepción en el front (el cobro debe VERSE)

Principio: el costo y el saldo se muestran **antes**; el rechazo seco solo es red de seguridad.
1. **Pill de saldo** siempre visible: `Disponible $X` (= available), con `Reservado $Y` y alerta de saldo bajo. Click → apartado Wallet (saldo confirmado/reservado/disponible, recargar, historial).
2. **Pre-visualización en Send/Schedule:** `preview-audience` → `quote` muestra destinatarios por canal, **costo estimado vs disponible**. Alcanza → **Enviar** verde. No alcanza → Enviar deshabilitado + *"Te faltan $X"* + **Recargar**.
3. **Recargar desde la intercepción:** modal pre-llenado, por **dinero** (déficit/presets) o por **cantidad de mensajes** ("¿cuántos quieres enviar?" → calcula micros con las tarifas). Paga con tarjeta guardada; al acreditar (evento `Credited`) el saldo se refresca y **Enviar** se habilita solo. "Saldo acreditado" solo cuando Wallet confirma (no al volver del proveedor).
4. **Detalle de ejecución:** reservado / consumido / liberado / pendiente.
5. **Red de seguridad:** `AwaitingFunds`/`Rejected(InsufficientFunds)` con CTA **Recargar y reintentar** (revalida cotización, misma idempotencia).

---

## 11. Esqueleto (clonar Notes) + despliegue

4 proyectos clon de `src/Services/Notes` (`Domain/Application/Infrastructure/Api`), DB propia `TaxVision_Wallet`, Wolverine outbox/inbox sobre `taxvision-events`, filtro fail-closed `ITenantOwned`, proyecciones RBAC/plan copiadas, `IServiceTokenAcquirer` M2M. **6 sitios** (ya cableados por mí salvo los proyectos+migración): `TaxVision.slnx`; `docker-compose.yml` (bloque `wallet-api` + M2M `ServiceAuth__Clients__30` wallet-worker + migraciones + cluster gateway) ✅; gateway `appsettings.json` (ruta `/wallet` + cluster + categoría) ✅; `.env` (`WALLET_DB_CONNECTION` + secret) ✅; `apply-migrations.sh` (entrada Wallet) ✅. Falta: los 4 proyectos + migración inicial.

---

## 12. Plan de implementación por fases

- **F1 — Núcleo Wallet + identidades económicas:** aggregates (Wallet/Reservation/LedgerEntry/FundingCredit/Settlement) en **micros**, dos deltas; esqueleto clon-Notes; DB + migración; `GET /wallet` + ledger; acceso. *Verificable: saldo 0, invariantes.*
- **F2 — Recarga (money-IN):** `SaaSPaymentType.WalletTopUp` + eventos + consumers PaymentApp/Wallet + `POST /wallet/top-ups`; dedupe `FundingCredit`. *Verificable: recarga con `pm_card_visa` acredita 1 solo crédito aun con webhooks duplicados.*
- **F3 — Pricing versionado + cotización:** PriceBook/Version/Rule + `POST /wallet/quotes` + `preview-audience` en Campaigns. *Verificable: cotización reproducible; cambiar tarifa no altera una ejecución autorizada.*
- **F4 — PEP reserve/consume/release (money-OUT):** reservas atómicas + gate en Campaigns (y scheduler) + consumo/liberación + `AwaitingFunds`/`Rejected`. *Verificable: 2 reservas de $70 sobre $100 no pasan ambas; con $40 y cotización $60 no autoriza y reporta faltan $20; tabla de saldo §3 reproducible.*
- **F5 — Front (cobro visible):** pill de saldo, preview de costo, recargar pre-llenado, detalle de ejecución.
- **F6 (posterior) — Medición real + individuales + conciliación:** por segmento SMS/aceptado email (adaptadores reportan uso), cobro de SMS/email individuales por el mismo PEP, settlement batches, conciliación de 3 relaciones.

---

## 13. Criterios de aceptación (clave, de la propuesta)

1. Recarga de $100 → un único crédito de 100 000 000 micros aun con eventos duplicados/claves técnicas nuevas.
2. Pago fallido/pendiente o redirección manipulada → no acredita.
3. Dos reservas de $70 sobre $100 disponibles → no pasan ambas.
4. $40 disponibles y cotización $60 → no autoriza; informa faltan $20.
5. Tabla §3 reproducible: recarga $100 → reserva $60 → consumo $54 → liberación $6 ⇒ confirmado $46 (no $52).
6. 500 emails a $0.002 consumen $1.00 exacto, sin depender del tamaño de lote.
7. Cambiar la tarifa publicada no altera una ejecución ya autorizada.
8. Un envío de campaña se cobra **una** vez pese a reintentos/callbacks/eventos fuera de orden.
9. Consumo delta repetido con la misma liquidación no re-descuenta; contenido distinto con esa identidad → conflicto.
10. El cliente de un tenant no puede ver/recargar/gastar la wallet de otro.
11. Wallet congelada procesa de forma recuperable el pago confirmado en curso.

---

## 14. Reconciliación con la suite previa
- **Supersede:** el prepago-sin-refund efímero **y** el refund mal definido del original. Vuelve a **reserve/consume/release** pero con **dos deltas** (Release ≠ UsageRefund) y **micros**.
- **Conserva:** ADR-WAL-001 (servicio independiente), ADR-WAL-003 (ledger append-only + caché), el flujo top-up sobre PaymentApp.
- **Añade:** micros (revisa ADR-WAL-002), pricing versionado, cobro unificado campañas+individuales, identidad económica, expiración segura, wallet congelada.
- **Pendiente:** los 11 docs del folder (que B dejó en el modelo prepago) se re-alinean a esta v3; este doc manda mientras tanto.

---

## Evidencia real (VERIFIED contra código)
| Hecho | Evidencia | Clasificación |
|---|---|---|
| `CampaignRunStatus.Rejected` + `RejectionReason` existen; falta `InsufficientFunds` | `Campaigns/.../Runs/RunEnums.cs:8-17`, `CampaignRun.cs:36`; grep InsufficientFunds=0 | VERIFIED |
| Gate entre Start y fan-out | `StartCampaignRunCommand.cs:83` / `106-133` | VERIFIED |
| Unidad = (destinatario, canal) | `StartCampaignRunCommand.cs:177-192` | VERIFIED |
| `SaaSPaymentType` (agregar WalletTopUp; verificar número) | `PaymentApp/.../SaaSPaymentType.cs:7-49` | VERIFIED |
| Patrón money-IN a clonar | `Subscription ChangePlanHandler.cs:150-164` → `PaymentApp SubscriptionPlanChangeDueConsumer.cs:21-61` → `SaaSPaymentResultPublisher.cs:76-119` | VERIFIED |
| Acceso a replicar | `BuildingBlocks/Authorization/CampaignsPermissions.cs`, `PermissionModuleMap.cs:36-56`, `PlanModuleCatalog.cs` | VERIFIED |
| Despliegue (6 sitios) | compose `campaigns-api`, gateway `appsettings.json`, `.env`, `apply-migrations.sh` | VERIFIED |

**Fuentes externas del review** (comportamiento técnico, sin elegir proveedor): Twilio segmentación/facturación por segmento [S1][S2]; Stripe webhooks duplicados/sin orden [S3]; EF Core concurrencia/rowversion [S4].
