# Wallet/Ledger — State Machines

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-10-06
- **Estado:** DISEÑO — no implementado
- **Coherente con:** `00_Plan_And_Architecture.md §5/§6`, `Domain_Design.md`, `Transactional_Protocol.md`, `Commands_And_Events.md`, `../06_Cross_Service_Transactional_Protocol.md`.

> **Modelo prepago (ADR-WAL-005).** NO hay máquina de estados de Reserva (`Held→Consumed/Released/Expired` queda obsoleta junto con ADR-WAL-004). El cobro de un run es un **único débito** gobernado por la autorización del PEP. Las máquinas vigentes son: (1) la **autorización de cobro del run** (vista Wallet), (2) el **top-up** (recarga), (3) el **Wallet** (opcional Freeze).

---

## 1. Autorización de cobro de un run (el PEP)

Corazón del modelo prepago: un único débito antes del fan-out. Campaigns publica conteos; Wallet decide.

```
   CampaignRunPendingAuthorization { RunId, PerChannelUnits }
 ─────────────────────────────────────────────────────────────►  ┌──────────────┐
                                                                  │  EVALUANDO   │
                                                                  └──────┬───────┘
                                     costo = Σ(units×ChannelPrice)        │
                        ┌──────────────────────────────────────────────┬─┘
          PostedCents >= costo │                      PostedCents < costo │
                    Debit(costo)▼                               (no debita)▼
             ┌──────────────────────┐                     ┌────────────────────────┐
             │ AUTHORIZED (debitado)│                     │ DENIED (InsufficientFunds)│
             └──────────┬───────────┘                     └───────────┬────────────┘
                        │                                             │
      CampaignRunAuthorized{RunId}                    CampaignRunAuthorizationDenied
                        ▼                                   {RunId, Reason}▼
          Campaigns ejecuta fan-out                   Campaigns: CampaignRun.Reject
          + MarkDispatched                            ("InsufficientFunds") → Rejected
```

### Resultados (terminales desde la vista Wallet)

| Resultado | Semántica | `PostedCents` | Evento publicado |
|---|---|---|---|
| **Authorized** | Costo debitado (un `LedgerEntry Debit/CampaignCharge`). | `−= costo` | `CampaignRunAuthorizedIntegrationEvent { RunId }` |
| **Denied** | Saldo insuficiente; **no** se debita. | sin cambio | `CampaignRunAuthorizationDeniedIntegrationEvent { RunId, Reason="InsufficientFunds" }` |

**Idempotencia:** `opKey="run:"+RunId`. Reentrega del `PendingAuthorization` de un run **ya autorizado** → replay de `Authorized` **sin re-debitar** (candado `UNIQUE(TenantId, OperationKey)`). Un run `Denied` que luego recibe otro `PendingAuthorization` (p.ej. tras recarga y reintento) se evalúa de nuevo: si ahora alcanza, debita y `Authorized`.

**Sin reembolso (ADR-WAL-005):** no hay transición de reconciliación al cerrar el run. `CampaignRunCompletedIntegrationEvent` (Delivered/Failed/Skipped) **se ignora para dinero**. Lo que falle/rebote en el proveedor no se devuelve.

**Scheduler:** cada fire programado/recurrente (`CampaignSchedulerService`) es un run nuevo ⇒ su propio `PendingAuthorization` ⇒ su propio débito. Saldo insuficiente ⇒ ese fire queda `Rejected`; los siguientes lo reintentan.

### Lado Campaigns (estados ya existentes)

`CampaignRun.Rejected` ya existe (`RunEnums.cs:8-17`) y `RejectionReason` (`CampaignRun.cs:36`, `nvarchar(200)`). Se introduce la constante de razón `InsufficientFunds` (no existe hoy; grep = 0). El gate se inserta entre `CampaignRun.Start` (`StartCampaignRunCommand.cs:83`) y el loop de fan-out (`106-133`): el run queda pendiente de autorización y el fan-out NO ocurre hasta `Authorized`.

## 2. Máquina de estados del **WalletTopUp** (recarga, money-IN)

```
   POST /wallet/top-up { amountCents }
 ──────────────────────────────────────►  ┌──────────┐
   crea WalletTopUp + publica             │ PENDING  │
   WalletTopUpDueIntegrationEvent         └────┬─────┘
                                               │  PaymentApp cobra Stripe off-session
                         ┌─────────────────────┴─────────────────────┐
         WalletTopUpPaymentSucceeded │              WalletTopUpPaymentFailed │
                   Credit(opKey=topup:Id)▼                         (no acredita)▼
                    ┌──────────────────┐                        ┌──────────────┐
                    │    SUCCEEDED     │                        │   FAILED     │
                    │ (saldo +amount)  │                        │              │
                    └──────────────────┘                        └──────────────┘
```

| Estado | Semántica | `PostedCents` | Terminal |
|---|---|---|---|
| **Pending** | Recarga creada; PaymentApp aún cobrando. | sin cambio | no |
| **Succeeded** | Pago cobrado; `Credit` aplicado (idempotente por `topup:{Id}`). | `+= amount` | **sí** |
| **Failed** | Pago rechazado; no se acredita. | sin cambio | **sí** |

**Idempotencia:** `opKey="topup:"+topUpId`. Reentrega de `WalletTopUpPaymentSucceeded` → una sola `Credit`.

## 3. Máquina de estados del **Wallet** (opcional Freeze)

```
        crear (primer Credit)
   ───────────────────────────►  ┌──────────┐
                                  │  ACTIVE  │◄────┐ Unfreeze (admin)
                                  └────┬─────┘     │
                        Freeze (admin) │           │
                                       ▼           │
                                  ┌──────────┐─────┘
                                  │  FROZEN  │
                                  └──────────┘
```

| Estado | Credit (TopUp) | Debit (CampaignCharge) | Adjustment |
|---|---|---|---|
| **Active** | ✔ | ✔ | ✔ |
| **Frozen** | ✖ (rechaza) | ✖ (rechaza → Denied) | ✔ (admin) |

`Frozen` es una salvaguarda operativa (fraude/dispute): bloquea recargas y cobros sin destruir saldo ni historia. Opcional en MVP (puede diferirse si no se requiere).

## 4. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| `CampaignRun.Rejected` + `RejectionReason` ya existen | `RunEnums.cs:8-17`; `CampaignRun.cs:36` | VERIFIED | 99% |
| `InsufficientFunds` NO existe (crear constante) | grep en Campaigns = 0 | VERIFIED | 95% |
| Gate entre Start (`:83`) y fan-out (`106-133`) | `StartCampaignRunCommand.cs` | VERIFIED | 95% |
| Patrón top-up money-IN (Due→charge→succeeded/failed) | `SubscriptionPlanChangeDueConsumer.cs:21-61`; `SaaSPaymentResultPublisher.cs:76-119` | VERIFIED | 90% |
| Máquina de autorización single-debit (prepago) | `00_Plan §5` (diseño) | NEW | n/a |
| Máquina WalletTopUp Pending→Succeeded/Failed | `00_Plan §6` (diseño) | NEW | n/a |
