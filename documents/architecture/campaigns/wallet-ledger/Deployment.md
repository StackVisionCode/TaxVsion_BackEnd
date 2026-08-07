# Wallet/Ledger — Deployment

- **Servicio:** `TaxVision.Wallet` (microservicio INDEPENDIENTE)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

---

## 1. Topología

Microservicio .NET independiente con **su propia base de datos** (sin FK cross-context). Se despliega como un contenedor más del stack (patrón del monorepo, ver `project_local_dev_stack_and_login.md`). Componentes:

- `TaxVision.Wallet.Api` — endpoints M2M (reserve/consume/refund/adjust/get-balance/ledger).
- `TaxVision.Wallet.Application` — commands/handlers + ejecutor idempotente (copia del patrón `SqlBusinessIdempotencyExecutor`).
- `TaxVision.Wallet.Domain` — `TenantBalance`, `Reservation`, `LedgerEntry`, VOs `Money`/`IdempotencyKey` (copias por-contexto).
- `TaxVision.Wallet.Infrastructure` — EF Core DbContext (query filter global), Wolverine outbox/inbox, consumers (top-up), jobs (sweep, reconciliación).

## 2. Proyecto compartido: BuildingBlocks

Reusa `BuildingBlocks` para `IntegrationEvent`, `Result`, tenancy (`ITenantContext`), Wolverine setup. Los eventos de integración de Wallet viven en `src/BuildingBlocks/Messaging/WalletIntegrationEvents/` (nuevo folder), simétrico a `PaymentAppIntegrationEvents/`. El nuevo `WalletTopUpPaymentSucceededIntegrationEvent` lo publica **PaymentApp**, así que su contrato va en `PaymentAppIntegrationEvents/` (junto a `SubscriptionRenewalPaymentSucceededIntegrationEvent.cs`).

## 3. Orden de despliegue (dependencias)

1. **PaymentApp** primero: añadir `SaaSPaymentType.WalletTopUp = 9` (`SaaSPaymentType.cs:32` hoy termina en `OnboardingInitial=8`), flujo de charge del top-up, y publicación de `WalletTopUpPaymentSucceededIntegrationEvent` — **BLOCKER-WAL-2**.
2. **Wallet** desplegado y migrado (tablas de `Data_Model.md`) — **BLOCKER-WAL-1**: debe existir antes de que Campaigns pueda ejecutar (`05_Master_ADR.md:57`).
3. **Campaigns/SMS** (consumidores) apuntan a la API M2M de Wallet una vez arriba.

Wallet **no** depende de Campaigns: es reutilizable e independiente (puede servir a un envío SMS suelto sin que Campaigns exista).

## 4. Configuración

| Config | Propósito |
|---|---|
| `Wallet:DefaultCurrency` | `USD` (única moneda soportada en MVP). |
| `Wallet:BusinessIdempotency:RetentionDays` | ventana de retención de claims (patrón `SqlBusinessIdempotencyExecutor.cs:89`). |
| `Wallet:Reservation:DefaultHoldTtl` | TTL por defecto de holds (para el sweep). |
| `Wallet:LowBalanceThresholdCents` | umbral para `BalanceLowWarningIntegrationEvent`. |
| M2M auth (audience `taxvision-wallet`, issuer) | validación de tokens de servicio. |
| Connection string + Wolverine transport | BD propia + bus durable. |

Secretos por el mecanismo de plataforma; **Wallet no tiene secretos de proveedor** (`Security.md §6`).

## 5. Migraciones y jobs

- **Migraciones EF Core**: crear `wallet_tenant_balances`, `wallet_reservations`, `wallet_ledger_entries`, `wallet_processed_business_messages` + índices/uniques/CHECKs (`Data_Model.md`). Post-migración: **revocar UPDATE/DELETE** sobre `wallet_ledger_entries` al rol de app.
- **Jobs de fondo:** `ReservationExpirySweep` (holds vencidos), `IdempotencyRetentionPurge` (claims vencidos), `LedgerReconciliation` (verifica caché vs suma). Corren como hosted services con tenant explícito en scope Wolverine.

## 6. Health / readiness

- Liveness: proceso arriba.
- Readiness: DB alcanzable + migraciones aplicadas + bus conectado.
- Métrica de arranque: reconciliación inicial opcional (muestra de balances) para detectar corrupción antes de servir.

## 7. Rollout / rollback

- Servicio **stateless** salvo su DB; escalar horizontalmente es seguro (la corrección de doble-ejecución es la idempotencia + RowVersion, no la instancia única — a diferencia del doble-scheduler legado).
- Rollback de código sin pérdida: el ledger es la fuente de verdad; una versión previa reconstruye la caché por reconciliación si hiciera falta.

## 8. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Stack multi-contenedor del monorepo | `project_local_dev_stack_and_login.md` (memoria) | DOCUMENTED_ONLY | 85% |
| `SaaSPaymentType` termina en 8, falta top-up | `SaaSPaymentType.cs:8-32` | VERIFIED | 96% |
| Eventos PaymentApp en `BuildingBlocks/Messaging/PaymentAppIntegrationEvents/` | ls de ese folder | VERIFIED | 95% |
| Wallet independiente, sin depender de Campaigns | `05_Master_ADR.md:29,57`; `02_Context_Map:44` | VERIFIED | 92% |
| Jobs sweep/purge/reconciliación | diseño | NEW | n/a |
