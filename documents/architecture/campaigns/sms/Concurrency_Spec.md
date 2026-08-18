# TaxVision.Sms — Concurrency Spec

- **Servicio:** SMS (`TaxVision.Sms`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

## 1. Optimistic concurrency (RowVersion)
`SmsDispatch`, `SmsOptInRegistry` y `SmsProviderConfig` llevan `row_version` (bytea/`xmin`), como `ProcessedBusinessMessage.RowVersion` (`ProcessedBusinessMessage.cs:23`). Toda transición de estado hace `UPDATE … WHERE id=@id AND row_version=@expected`; si `0` filas ⇒ conflicto ⇒ recarga y re-evalúa el guard (o descarta si el estado ya avanzó). Esto impide que:
- un **webhook DLR** y un **job de reconciliación** pisen mutuamente el estado del mismo dispatch;
- dos DLR concurrentes (delivered + failed reordenados) produzcan doble movimiento de Wallet — el segundo pierde el CAS y ve el estado terminal.

## 2. Carreras específicas y su resolución

| Carrera | Resolución |
|---|---|
| `SmsWalletReserved` llega mientras otro handler ya lo procesó | idempotencia por `DispatchId` + CAS en `Reserved`; el segundo es no-op. |
| DLR `delivered` y `undelivered` reordenados | primer terminal gana (CAS); el segundo ve estado terminal ⇒ descarta (log a observabilidad). |
| Envío duplicado por reintento de Wolverine | client-ref determinístico al proveedor + `UNIQUE` por destinatario; no se crean dos dispatch ni dos SMS. |
| STOP inbound concurrente con un envío marketing en curso | el envío revalida opt-in en el punto de `Reserved→Dispatched`; si el registry ya está `StoppedByUser`, transición a `Suppressed` + refund de la reserva. Ventana mínima aceptada (at-least-once). |
| Dos webhooks del mismo `providerMessageId` | `ProcessedBusinessMessage (provider, providerMessageId, eventType)` ⇒ único. |

## 3. Sin doble-scheduler (delegado)
SMS **no agenda**: el disparo temporal y su **lease atómico** viven en el Scheduler (`scheduler/`), que corrige el doble-scheduler + `Status=Sending` no-atómico del legado (ADR-CAMP-000 §Anti-patrón 6). SMS sólo reacciona a `SmsDispatchRequested` ya materializado; su idempotencia por destinatario absorbe cualquier fan-out duplicado del upstream.

## 4. Backpressure y rate limits del proveedor
- El fan-out **no** es fire-and-forget con `Task.Run`/`Task.Delay` (anti-patrón legado, `SmsCampaignSender.cs:331`, que se pierde al reiniciar). El envío al proveedor lo hacen **handlers Wolverine durables** con concurrencia acotada por endpoint (`MaxDegreeOfParallelism`) y throttling configurable por tenant/sender.
- Los límites del proveedor (TPS por número/short code) se respetan con un limitador por `sender_id` (token bucket); el exceso re-encola con backoff (el mensaje sigue en el inbox durable, no se pierde en un reinicio).
- 429/5xx del proveedor ⇒ retry con backoff exponencial + jitter, hasta N intentos, luego `Failed` + refund.

## 5. Aislamiento transaccional
- Reserva + creación de dispatch: una sola tx local (outbox). Read-committed suficiente porque la unicidad la garantizan los constraints, no un read previo.
- Consume/refund: disparados por evento, cada uno su propia tx idempotente.
- Sin locks pesimistas salvo el limitador de TPS (que es in-memory/distribuido, no de BD).

## 6. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| RowVersion como patrón de concurrencia | `ProcessedBusinessMessage.cs:23` | VERIFIED | 96% |
| Legado fan-out con `Task.Delay` (se pierde al reiniciar) | `SmsCampaignSender.cs:331`, ADR-CAMP-000 §Anti-patrón 2 | VERIFIED | 96% |
| Doble-scheduler es responsabilidad del Scheduler | `05_Master_ADR.md` Dec.4 | VERIFIED (política) | 95% |
| Estrategia de concurrencia/backpressure SMS | este documento | NEW | — |
