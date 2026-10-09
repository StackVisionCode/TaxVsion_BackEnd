# Wallet/Ledger — ADRs

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-10-06
- **Estado:** DISEÑO — no implementado
- **Rol:** monedero de saldo en dinero por tenant que cobra el envío de campañas vía autorizador externo (PEP), manteniendo a `TaxVision.Campaigns` money-agnostic.
- **Coherente con:** `00_Plan_And_Architecture.md` (documento autoritativo), `05_Master_ADR.md` (ADR-CAMP-001 D7). Deriva de `05_Master_ADR.md §Decisión 3`. IDs locales `ADR-WAL-xxx`.

> **Reconciliado con `00_Plan_And_Architecture.md` (2026-10-06).** El modelo de dinero cambió por decisión del usuario: **ADR-WAL-004 (reserve→consume→refund) queda SUPERSEDIDO por ADR-WAL-005 (prepago SIN refund, un solo débito; sin `HeldCents`, sin holds ni reservas)**. Se añadieron la **tarifa por destinatario×canal** (ADR-WAL-006), el **autorizador externo/PEP** (ADR-WAL-007), el flujo concreto **`WalletTopUp`** sobre PaymentApp (ADR-WAL-010) y el **acceso** `wallet.*` (ADR-WAL-012). ADR-WAL-001/002/003 siguen vigentes (003 pierde `HeldCents`). Los ADR de idempotencia, concurrencia y M2M se conservan, renumerados para dar sitio a los nuevos.

---

## ADR-WAL-001 — Wallet como microservicio independiente (no módulo de Campaigns)

**Estado:** APPROVED (decisión de usuario, `05_Master_ADR.md:38` alternativa 3 rechazada; ADR-CAMP-001 D7).
**Contexto:** el saldo debe ser reutilizable por Campaigns, envíos SMS individuales y futuros consumidores, y **Campaigns no debe conocer dinero**.
**Decisión:** `TaxVision.Wallet` independiente con DB propia (`TaxVision_Wallet`); los consumidores interactúan por eventos de integración (no acoplamiento directo). Campaigns ni referencia ni conoce al Wallet.
**Consecuencias:** (+) reutilización, aislamiento, escalado propio, Campaigns money-agnostic. (−) coordinación distribuida event-driven (PEP). BLOCKER-WAL-1: debe existir antes de que Campaigns ejecute cobros.

## ADR-WAL-002 — Saldo real en USD minor units (long), nunca decimal/float ni TXC

**Estado:** APPROVED.
**Contexto:** el legado usaba `decimal` y una moneda ficticia TaxCoin (TXC) en ReferralService (`ReferralService/Domain/WalletTransaction.cs:12`).
**Decisión:** `long` cents USD (ISO-4217), copia por-contexto del VO `Money` (`PaymentApp.Domain/ValueObjects/Money.cs:6-53`). El precio por canal y la moneda los define el catálogo de plataforma, no el frontend.
**Consecuencias:** sin errores de redondeo en débitos masivos; integración directa con el charge de PaymentApp (que ya usa cents).

## ADR-WAL-003 — Ledger inmutable append-only como fuente de verdad; saldo cacheado (`PostedCents`)

**Estado:** APPROVED (deriva de "movimientos INMUTABLES", `05_Master_ADR.md:29`). **Modificado 2026-10-06: elimina `HeldCents`** (ya no hay holds).
**Contexto:** el legado mutaba `BalanceBefore/BalanceAfter` y marcaba `IsActive` (`WalletTransaction.cs:14,21`) — historia editable.
**Decisión:** `LedgerEntry` append-only (sin setters, UPDATE/DELETE revocados en BD); un **único** saldo cacheado `PostedCents` como caché derivada actualizada en la misma TX que inserta el asiento y verificada por reconciliación. **Ya no existe `HeldCents`** porque el modelo prepago no reserva (ADR-WAL-005).
**Alternativas:** event-sourcing puro (recálculo en cada lectura) — rechazado por costo; saldo mutable suelto — rechazado (anti-patrón legado).
**Consecuencias:** auditabilidad total; corrección solo por entries compensatorios (`Adjustment`).

## ADR-WAL-004 — ~~Protocolo reserve → consume/refund~~ (SUPERSEDIDO)

**Estado:** **SUPERSEDED por ADR-WAL-005 (2026-10-06).**
**Resumen histórico:** proponía una máquina de estados de Reserva `Held→Consumed/Released/Expired` para cobrar por entrega real (reservar estimado → consumir entregado → devolver resto).
**Por qué se descartó:** decisión del usuario de ir a **prepago sin reembolso** (ADR-WAL-005): más simple, sin saga de compensación, sin sweep de holds, sin `HeldCents`. El tenant paga por el envío autorizado, no por el resultado de entrega del proveedor. Toda mención a holds/reservas/consume/refund/settle queda obsoleta.

## ADR-WAL-005 — Cobro PREPAGO sin reembolso, un solo débito al autorizar

**Estado:** APPROVED (usuario, 2026-10-06). **Supersede ADR-WAL-004.**
**Contexto:** se necesita monetizar el envío masivo de campañas sin acoplar Campaigns a la facturación y sin la complejidad del reserve/consume/refund.
**Decisión:** el costo de un run se **debita UNA sola vez, antes del fan-out de dispatch**, calculado sobre la audiencia ya depurada (sin opt-outs ni duplicados). **No hay reembolso** por entregas que fallen, reboten o se omitan en el proveedor. Un solo `LedgerEntry` de `Debit (CampaignCharge)` por run. Invariante: `PostedCents >= 0` siempre; un débito que lo dejaría negativo se **rechaza** (gate `InsufficientFunds`).
**Consecuencias:** (+) sin saga, sin holds, sin sweep de expiración, sin `HeldCents`; un débito atómico idempotente por `run:{RunId}`. (−) el tenant asume el costo de lo no-entregado (aceptado). Si a futuro se quisiera refund de lo no-enviado, sería un ADR nuevo que reabra ADR-WAL-004.

## ADR-WAL-006 — Tarifa por destinatario × canal (catálogo `ChannelPrice` editable por PlatformAdmin)

**Estado:** APPROVED (usuario, 2026-10-06).
**Contexto:** el costo del envío depende del canal (Email/SMS/Push/WhatsApp); la plataforma debe poder ajustar precios sin desplegar código.
**Decisión:** catálogo `ChannelPrice` (aggregate de plataforma, no por tenant salvo override futuro): `{ Channel, UnitPriceCents, Currency, Active }`, editable **solo por PlatformAdmin**. Una **unidad** = `(destinatario, canal)`. Costo de un run = `Σ_canal (unidades_del_canal × UnitPriceCents(canal))`. Molde conceptual `SeatPricing`.
**Consecuencias:** precios versionables y auditables; el diseño no hardcodea números (los fija el usuario en el catálogo); el cálculo de costo vive en Wallet (que conoce precios), no en Campaigns (que solo cuenta unidades).

## ADR-WAL-007 — Autorizador externo (PEP): Campaigns money-agnostic, débito entre Start y fan-out

**Estado:** APPROVED (usuario, 2026-10-06; ADR-CAMP-001 D7).
**Contexto:** Campaigns orquesta el envío pero **no maneja dinero**. Hace falta un punto que cobre sin que Campaigns conozca precios ni saldo.
**Decisión:** un **Policy Enforcement Point** event-driven se interpone entre `CampaignRun.Start` y el fan-out de dispatch. Campaigns publica `CampaignRunPendingAuthorizationIntegrationEvent { TenantId, CampaignId, RunId, PerChannelUnits, TriggeredBy }` (solo conteos — contar no es "saber de dinero"); Wallet calcula el costo, debita y responde `CampaignRunAuthorizedIntegrationEvent { RunId }` o `CampaignRunAuthorizationDeniedIntegrationEvent { RunId, Reason="InsufficientFunds" }`. El fan-out NO ocurre hasta recibir `Authorized`; `Denied` → `CampaignRun.Reject("InsufficientFunds")`.
**Consecuencias:** Campaigns no cambia su modelo de dominio ni conoce al Wallet; el gate aplica también a disparos programados/recurrentes (cada fire = run nuevo = su propio débito). At-least-once + idempotente por `run:{RunId}`.

## ADR-WAL-008 — Idempotencia por `OperationKey` único en el ledger (copia por-contexto)

**Estado:** APPROVED. *(Antes ADR-WAL-005.)*
**Contexto:** at-least-once (Wolverine), reintentos de eventos; el legado no tenía idempotencia (`WalletServiceClient.cs:101,198`).
**Decisión:** cada movimiento lleva una `OperationKey` de negocio (`"run:"+RunId`, `"topup:"+topUpId`, `"adjust:"+ticket`); índice **UNIQUE (`TenantId`, `OperationKey`)** sobre `wallet_ledger_entries`. Un segundo intento de insertar el mismo asiento colisiona ⇒ no se duplica el débito/crédito; se replica el resultado previo. (Patrón conceptual `SqlBusinessIdempotencyExecutor.cs`; aquí basta el candado en el propio ledger.)
**Consecuencias:** "un pago = una recarga", "un run = un débito"; sin doble-cobro en reentrega. La capa de transporte (Wolverine inbox durable) deduplica envelopes; el candado del ledger garantiza el efecto de negocio.

## ADR-WAL-009 — Concurrencia por conditional update + RowVersion (optimistic, un ganador)

**Estado:** APPROVED. *(Antes ADR-WAL-006.)*
**Contexto:** operaciones concurrentes sobre el mismo `Wallet` (dos débitos, o un top-up mientras se debita); sin saldo negativo jamás.
**Decisión:** `UPDATE ... WHERE RowVersion=@expected`; la perdedora recarga y **reevalúa** la guarda `PostedCents - debit >= 0` antes de reintentar; `CHECK(PostedCents >= 0)` en BD como red.
**Consecuencias:** sin lost update ni sobre-giro; sin locks pesimistas/deadlocks; reintento acotado con backoff.

## ADR-WAL-010 — Recarga (top-up) acreditada solo por el flujo `WalletTopUp` de PaymentApp

**Estado:** APPROVED (deriva de `05_Master_ADR.md:32`, decisión 6). *(Antes ADR-WAL-007; ahora concreto.)*
**Contexto:** no crear saldo sin cobro real; reusar el patrón de cobro SaaS ya probado.
**Decisión:** la recarga clona el flujo plan-change: nuevo `SaaSPaymentType.WalletTopUp`; `POST /wallet/top-up { amountCents }` crea un `WalletTopUp` (Pending) + publica `WalletTopUpDueIntegrationEvent`; PaymentApp lo cobra en **Stripe off-session** contra la tarjeta guardada y emite `WalletTopUpPaymentSucceeded/FailedIntegrationEvent`; Wallet acredita (`Credit`, `opKey="topup:"+topUpId`) al recibir "succeeded". El débito de campaña es interno (no toca Stripe).
**Consecuencias:** BLOCKER-WAL-2 (falta `SaaSPaymentType.WalletTopUp` + los eventos upstream en PaymentApp); `Adjustment` admin queda como única otra vía (auditada).

## ADR-WAL-011 — Nunca persistir JWT de usuario; M2M client-credentials para lo interno

**Estado:** APPROVED (corrige anti-patrón §5 de `05_Master_ADR.md:48`). *(Antes ADR-WAL-008.)*
**Contexto:** el legado guardaba `BackgroundAuthToken` (JWT) para refunds asíncronos (`CreateCampaignCommandHandler.cs:67`; `WalletServiceClient.cs:179-180`).
**Decisión:** el cobro de campaña ocurre por evento (PEP), sin token de usuario; los jobs/consumers usan M2M client-credentials (audience `taxvision-wallet` + scopes) cuando necesiten llamar a otros servicios. Los endpoints de tenant (`/wallet`, `/wallet/top-up`, `/wallet/estimate`) usan el JWT del usuario con `wallet.view`/`wallet.manage`.
**Consecuencias:** superficie de credenciales mínima; sin tokens de larga vida en BD.

## ADR-WAL-012 — Acceso: permiso `wallet.manage`/`wallet.view` + módulo `wallet`

**Estado:** APPROVED (usuario, 2026-10-06; ADR-CAMP-001 D7).
**Contexto:** el apartado Wallet del front (saldo, recargar, ledger, estimador) necesita RBAC, espejo de `campaigns`.
**Decisión:** `WalletPermissions` (`View="wallet.view"`, `Manage="wallet.manage"`); `PermissionModuleMap` gana `("wallet.", "wallet")`; `PlanModuleCatalog` ofrece `"wallet"` en los tiers con campañas. Editar precios = `PlatformAdmin`, no un permiso de tenant.
**Consecuencias:** gate de módulo en el gateway; el front muestra/oculta el apartado según plan+permiso.

## Resumen de blockers

| ID | Blocker | Bloquea |
|---|---|---|
| BLOCKER-WAL-1 | Wallet debe desplegarse antes que el cobro de Campaigns | cobro de campañas |
| BLOCKER-WAL-2 | Falta `SaaSPaymentType.WalletTopUp` + eventos `WalletTopUpDue/Succeeded/Failed` en PaymentApp | recarga de saldo vía pago |

## Tabla de evidencia consolidada

| ADR | Evidencia clave | Clasificación | Confianza |
|---|---|---|---|
| WAL-001 | `05_Master_ADR.md:29,38,57`; ADR-CAMP-001 D7 | VERIFIED (decisión) | 95% |
| WAL-002 | `PaymentApp.Domain/ValueObjects/Money.cs:6-53`; `WalletTransaction.cs:12` | VERIFIED | 96% |
| WAL-003 | `WalletTransaction.cs:14,21` (legado mutable) | VERIFIED | 95% |
| WAL-004 | SUPERSEDED — histórico `ADR.md` (esta suite) | VERIFIED | 95% |
| WAL-005 | `00_Plan_And_Architecture.md §2 D1/D2`; `CreateCampaignCommandHandler.cs:278-320` (prepago legado) | VERIFIED (decisión) | 95% |
| WAL-006 | `00_Plan §4`; `StartCampaignRunCommand.cs:177-192` (unidad=destinatario×canal) | VERIFIED | 90% |
| WAL-007 | `StartCampaignRunCommand.cs:83` (Start) …`106-133` (fan-out); `RunEnums.cs:8-17` (`Rejected`); `CampaignRun.cs:36` (`RejectionReason`) | VERIFIED | 95% |
| WAL-008 | `ProcessedBusinessMessage.cs`; `SqlBusinessIdempotencyExecutor.cs` (patrón) | VERIFIED | 95% |
| WAL-009 | `ProcessedBusinessMessage.cs:23` (RowVersion); diseño | PARTIAL | 88% |
| WAL-010 | `SaaSPaymentType.cs:7-49`; `SubscriptionPlanChangeDueConsumer.cs:21-61`; `SaaSPaymentResultPublisher.cs:76-119` | VERIFIED | 92% |
| WAL-011 | `CreateCampaignCommandHandler.cs:67`; `WalletServiceClient.cs:179-180` | VERIFIED | 95% |
| WAL-012 | `CampaignsPermissions.cs:12-21`; `PermissionModuleMap.cs:36-56`; `PlanModuleCatalog.cs:28-58` | VERIFIED | 95% |
