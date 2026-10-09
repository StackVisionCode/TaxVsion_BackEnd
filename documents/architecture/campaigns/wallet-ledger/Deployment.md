# Wallet/Ledger — Deployment

- **Servicio:** `TaxVision.Wallet` (microservicio INDEPENDIENTE)
- **Fecha:** 2026-10-06
- **Estado:** DISEÑO — no implementado
- **Coherente con:** `00_Plan_And_Architecture.md §7/§9`, `Data_Model.md`, `Commands_And_Events.md`, `ADR.md` (ADR-WAL-010/012).

---

## 1. Topología

Microservicio .NET independiente con **su propia base de datos** `TaxVision_Wallet` (sin FK cross-context). **4 proyectos** clonando `src/Services/Notes/` (`Domain/Application/Infrastructure/Api`; `AssemblyMarker` en Application como ancla Wolverine; Api = `Microsoft.NET.Sdk.Web`, Wolverine 6.14 + EF/RabbitMQ/SqlServer):

- `TaxVision.Wallet.Api` — endpoints de tenant (`GET /wallet`, `/wallet/ledger`, `POST /wallet/top-up`, `/wallet/estimate`, `GET`/`PUT /wallet/pricing`).
- `TaxVision.Wallet.Application` — commands/handlers (`Credit`/`Debit`/`Adjustment`/`StartWalletTopUp`) + consumers (PEP, top-up).
- `TaxVision.Wallet.Domain` — `Wallet`, `LedgerEntry`, `ChannelPrice`, `WalletTopUp`, VO `Money` (copia por-contexto).
- `TaxVision.Wallet.Infrastructure` — EF Core DbContext (query filter global por `TenantId`), Wolverine outbox/inbox, `IServiceTokenAcquirer` para M2M, proyecciones RBAC/plan copiadas de Notes.

## 2. Proyecto compartido: BuildingBlocks

Reusa `BuildingBlocks` para `IntegrationEvent`, `Result`, tenancy (`ITenantContext`), Wolverine setup, `ITenantOwned` fail-closed. Contratos de eventos:
- Los eventos del **PEP** (`CampaignRunPendingAuthorization`, `CampaignRunAuthorized`, `CampaignRunAuthorizationDenied`) viven en `BuildingBlocks/Messaging/CampaignsIntegrationEvents/` (los publica/consume Campaigns↔Wallet).
- Los eventos de **Wallet** (`WalletTopUpDue`, `WalletCredited`, `BalanceLowWarning`) en `BuildingBlocks/Messaging/WalletIntegrationEvents/` (nuevo folder).
- Los eventos **de PaymentApp** para el top-up (`WalletTopUpPaymentSucceeded/Failed`) en `BuildingBlocks/Messaging/PaymentAppIntegrationEvents/` (junto a los de plan-change), porque los publica PaymentApp.

## 3. Orden de despliegue (dependencias)

1. **PaymentApp** primero (BLOCKER-WAL-2): añadir `SaaSPaymentType.WalletTopUp` (`SaaSPaymentType.cs:7-49`), `WalletTopUpDueConsumer` (espejo de `SubscriptionPlanChangeDueConsumer.cs:21-61`) que cobre Stripe off-session, y el brazo `WalletTopUp` en `SaaSPaymentResultPublisher.PublishByTypeAsync` (`SaaSPaymentResultPublisher.cs:76-119`) que emita succeeded/failed.
2. **Wallet** desplegado y migrado (BLOCKER-WAL-1): debe existir antes de que Campaigns cobre.
3. **Campaigns**: insertar el gate (evento `CampaignRunPendingAuthorization` entre Start y fan-out) + consumers de `Authorized/Denied` + constante `InsufficientFunds`.

Wallet **no** depende de Campaigns para existir (es reutilizable); sí para el flujo de cobro end-to-end.

## 4. Seis sitios de despliegue (espejo de campaigns)

1. `TaxVision.slnx` — agregar los 4 proyectos.
2. `deploy/docker/docker-compose.yml` — bloque `wallet-api` (espejo de `campaigns-api:1272-1320`): `ConnectionStrings__Default: ${WALLET_DB_CONNECTION:?...}`, Redis/RabbitMq/JWT/RBAC, `Authorization__ModuleGate__EnforcedModules__0: wallet`. + cliente M2M en auth-api `ServiceAuth__Clients__N__ClientId: ${WALLET_SERVICE_CLIENT_ID:-wallet-worker}`.
3. Gateway cluster (compose): `ReverseProxy__Clusters__wallet__Destinations__wallet1__Address: http://wallet-api:8080/` + `depends_on`.
4. `src/Gateway/TaxVision.Gateway/appsettings.json` — route `wallet` (`Path: /wallet/{**catch-all}`), cluster `wallet`, y `"wallet": "Standard"` en el mapa de categorías. **(Olvidar la ruta del gateway da 404 silencioso — pasó con `/campaign-templates`.)**
5. `.env` — `WALLET_DB_CONNECTION=Server=sqlserver,1433;Database=TaxVision_Wallet;...` (+ referenciado en el bloque `migrations` del compose).
6. `deploy/docker/migrations/apply-migrations.sh` — entrada `"Wallet"` con sus 2 csproj + `$WALLET_DB_CONNECTION`.

## 5. Acceso (ADR-WAL-012)

Tres toques, espejo de `campaigns`:
1. `WalletPermissions.cs` en `BuildingBlocks/Authorization/`: `View="wallet.view"`, `Manage="wallet.manage"`.
2. `PermissionModuleMap.cs` (`PrefixToModule`, `:36-56`): agregar `("wallet.", "wallet")`.
3. `PlanModuleCatalog.cs` (Subscription, `:28-58`): agregar `"wallet"` a los tiers con campañas. Opcional: `addon-wallet` en `ModuleAddOnCatalog.cs`.

## 6. Configuración

| Config | Propósito |
|---|---|
| `Wallet:DefaultCurrency` | `USD` (única moneda MVP). |
| `Wallet:LowBalanceThresholdCents` | umbral para `BalanceLowWarningIntegrationEvent`. |
| M2M auth (audience `taxvision-wallet`, issuer) | tokens de servicio para jobs/consumers. |
| Connection string + Wolverine transport | BD propia + bus durable (`taxvision-events`). |

Wallet **no** tiene secretos de proveedor (el charge lo hace PaymentApp). Solo credenciales M2M y connection string, por el mecanismo de secretos de la plataforma.

## 7. Migraciones y jobs

- **Migración inicial EF Core:** `wallet_wallets`, `wallet_ledger_entries`, `wallet_channel_prices`, `wallet_top_ups` + índices/uniques/CHECKs (`Data_Model.md`), incluido `UNIQUE(TenantId, OperationKey)`. Post-migración: **revocar UPDATE/DELETE** sobre `wallet_ledger_entries`. Semilla inicial de `wallet_channel_prices`.
- **Jobs de fondo:** `LedgerReconciliation` (verifica `PostedCents == Σ asientos`). **No hay sweep de expiración de reservas** (ya no existen). Corren como hosted services con tenant explícito en scope Wolverine.

## 8. Health / readiness / rollout

- Readiness: DB alcanzable + migraciones aplicadas + bus conectado.
- Servicio **stateless** salvo su DB; escalar horizontalmente es seguro (la corrección de doble-ejecución es la idempotencia `OperationKey` + RowVersion, no la instancia única).
- Rollback de código sin pérdida: el ledger es la fuente de verdad; una versión previa reconstruye `PostedCents` por reconciliación.

## 9. Plan por fases (resumen; detalle en `00_Plan §10`)

- **F1:** acceso + esqueleto (4 proyectos, DB + migración `Wallets`/`LedgerEntries`/`ChannelPrices`, 6 sitios). Sin lógica de cobro.
- **F2:** top-up (money-IN): `SaaSPaymentType.WalletTopUp` + eventos + consumers + `POST /wallet/top-up`.
- **F3:** PEP (money-OUT): evento `CampaignRunPendingAuthorization` + gate en `StartAndDispatchAsync` + consumer + `Authorized/Denied` + `InsufficientFunds`.
- **F4:** catálogo de precios + front (cobro visible): `preview-audience` + `estimate` + apartado Wallet + intercepción visual.

## 10. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Esqueleto clon de Notes (4 proyectos) | `src/Services/Notes/` | VERIFIED | 90% |
| 6 sitios de despliegue (campaigns como molde) | compose `campaigns-api:1272-1320`; gateway `appsettings.json` routes/cluster; `.env:218`; `apply-migrations.sh:158-161` | VERIFIED | 90% |
| `SaaSPaymentType` (agregar `WalletTopUp`) | `SaaSPaymentType.cs:7-49` | VERIFIED | 99% |
| Eventos PaymentApp en `BuildingBlocks/Messaging/PaymentAppIntegrationEvents/` | ls de ese folder | VERIFIED | 95% |
| Acceso (permiso/módulo/plan) a replicar | `CampaignsPermissions.cs:12-21`; `PermissionModuleMap.cs:36-56`; `PlanModuleCatalog.cs:28-58` | VERIFIED | 95% |
| Sin sweep de reservas (modelo prepago) | `00_Plan §2` (diseño) | NEW | n/a |
