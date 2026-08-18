# Wallet/Ledger — Idempotency Spec

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- Base: **business-inbox `ProcessedBusinessMessage`** (`src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Idempotency/ProcessedBusinessMessage.cs:9-124`) + ejecutor `SqlBusinessIdempotencyExecutor.cs`. **Copia por-contexto** (no compartir tipos).

---

## 1. Dos capas de dedupe (independientes)

1. **Transporte (Wolverine inbox durable):** deduplica *envelopes* del bus. No basta: at-least-once puede reentregar y el reintento HTTP del cliente no pasa por el bus.
2. **Efecto de negocio (`ProcessedBusinessMessage`):** protege la *operación* por `(TenantId, Operation, ScopeId, IdempotencyKey)`. Es la capa que garantiza "un pago = una recarga", "un reserve = un hold". Cita: comentario del propio tipo, `ProcessedBusinessMessage.cs:5-8`.

Regla de suite: **nunca "exactly-once"**; siempre at-least-once + handlers idempotentes + unique constraints + state guards (`00_Overview:45`).

## 2. Clave de idempotencia por operación

| Operación | `Operation` | `ScopeId` | `IdempotencyKey` (recomendada) |
|---|---|---|---|
| Reserve | `wallet.reserve` | consumidor's scope (RunId/SmsSendId) | `run-reserve-{RunId}` |
| Consume | `wallet.consume` | ReservationId | `run-consume-{RunId}` (o `-{batch}`) |
| Refund | `wallet.refund` | ReservationId | `run-refund-{RunId}` |
| Adjust | `wallet.adjust` | TenantId | `adjust-{ticket}` |
| Recharge (top-up) | `wallet.recharge` | **SaaSPaymentId** | `IdempotencyKey` del evento PaymentApp |

El **candado** es `UNIQUE(TenantId, Operation, ScopeId, IdempotencyKey)` en `wallet_processed_business_messages` (`Data_Model.md §1.4`). El `IdempotencyKey` VO (copia de `PaymentApp.Domain/ValueObjects/IdempotencyKey.cs:10-30`) solo valida no-vacío y ≤200 chars; no reinterpreta el formato.

## 3. RequestFingerprint (detección de conflicto semántico)

`RequestFingerprint` = SHA-256 (64-hex, validado en `ProcessedBusinessMessage.cs:52-56`) del cuerpo canónico de la petición (tenant+amount+currency+scope+op). Reglas (patrón `SqlBusinessIdempotencyExecutor.cs:175-216`):

- **Misma key + mismo fingerprint** → replay: se devuelve `ResponseJson` almacenado (mismo `reservationId`, mismo resultado). Idempotencia verdadera.
- **Misma key + fingerprint distinto** → `409 Wallet.IdempotencyConflict` ("la clave ya se usó con otra petición"). Evita que un cliente reuse una key para un monto diferente.
- **Estado `Processing`** (petición concurrente aún en curso) → `409/Retry Wallet.OperationInProgress` (`SqlBusinessIdempotencyExecutor.cs:195-199`).

## 4. Ciclo de vida de un claim

```
Begin(Processing) ──ok──► operationBody ──success──► Complete(Completed, responseJson)
        │                        │
        │                        └──Result.Failure──► ROLLBACK (claim NO persiste) ──► reintentable
        └──UNIQUE conflict──► ResolveExisting ──► replay o 409 fingerprint
```

**Clave de resiliencia:** en fallo del cuerpo, el claim NO se marca Completed y se hace rollback del savepoint (`SqlBusinessIdempotencyExecutor.cs:118-131`), de modo que un reintento posterior con la misma key puede **volver a intentar** limpio (no queda "envenenado"). Solo el éxito graba respuesta replayable.

## 5. Idempotencia por estado terminal de reserva

Complementa el claim: reintentar Consume/Refund sobre una reserva ya terminal (`Consumed`/`Released`/`Expired`) con la **misma key** → replay. Con **key nueva** → rechazo por state guard (`Wallet.ReservationNotConsumable`). Doble protección: candado de key + guarda de estado del aggregate (evita el legado que marcaba `Sent` a todos y doble-contaba en reintento, `05_Master_ADR.md:46`).

## 6. Retención

`ExpiresAtUtc` en el claim (`ProcessedBusinessMessage.cs:22`) = ventana de retención (p.ej. 30–90 días, config `BusinessIdempotencyOptions.RetentionDays`, patrón `SqlBusinessIdempotencyExecutor.cs:89`). Un job purga claims vencidos. Las reclamaciones de dinero (top-up) conviene retenerlas más que las de reserve/consume operativas.

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| `ProcessedBusinessMessage` es la capa de dedupe de negocio | `ProcessedBusinessMessage.cs:5-8` | VERIFIED | 97% |
| Conflict-insert → replay respuesta previa | `SqlBusinessIdempotencyExecutor.cs:93-116,166-216` | VERIFIED | 96% |
| Fingerprint sha256 64-hex validado | `ProcessedBusinessMessage.cs:52-56` | VERIFIED | 97% |
| Fallo del body no envenena el claim (rollback) | `SqlBusinessIdempotencyExecutor.cs:118-131` | VERIFIED | 95% |
| `IdempotencyKey` VO ≤200, no reinterpreta | `PaymentApp.Domain/ValueObjects/IdempotencyKey.cs:10-30` | VERIFIED | 96% |
| Claves por operación de Wallet | diseño | NEW | n/a |
