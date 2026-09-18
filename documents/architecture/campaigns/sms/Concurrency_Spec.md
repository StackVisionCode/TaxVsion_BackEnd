# TaxVision.Sms — Concurrency Spec

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — SMS NO es un servicio nuevo; `TaxVision.Sms` YA EXISTE y ya es M2M** (`SendSmsBatchCommand`, `POST /sms/messages`, `ActorType.Service`). El canal SMS es un **CONSUMER dentro de `TaxVision.Sms`** que procesa `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un microservicio SMS nuevo y/o un Wallet queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- **Servicio:** SMS (`TaxVision.Sms`) — consumer del canal SMS **dentro de `TaxVision.Sms` (ya existente)**
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

## 1. Optimistic concurrency (RowVersion)
`SmsDispatch`, `SmsOptInRegistry` y `SmsProviderConfig` llevan `row_version` (bytea/`xmin`), como `ProcessedBusinessMessage.RowVersion` (`ProcessedBusinessMessage.cs:23`). Toda transición de estado hace `UPDATE … WHERE id=@id AND row_version=@expected`; si `0` filas ⇒ conflicto ⇒ recarga y re-evalúa el guard (o descarta si el estado ya avanzó). Esto impide que:
- un **webhook DLR** y un **job de reconciliación** pisen mutuamente el estado del mismo dispatch;
- dos DLR concurrentes (delivered + failed reordenados) produzcan doble efecto terminal — el segundo pierde el CAS y ve el estado terminal. — (removido: sin dinero en el canal; ver banner — antes doble movimiento de Wallet).

## 2. Carreras específicas y su resolución

| Carrera | Resolución |
|---|---|
| ~~`SmsWalletReserved` concurrente~~ | — (removido: sin dinero en el canal; ver banner). |
| DLR `delivered` y `undelivered` reordenados | primer terminal gana (CAS); el segundo ve estado terminal ⇒ descarta (log a observabilidad). |
| Envío duplicado por reintento de Wolverine | client-ref determinístico al proveedor + `UNIQUE` por destinatario; no se crean dos dispatch ni dos SMS. |
| STOP inbound / opt-out **después de congelar la audiencia**, con la unidad aún no enviada | el envío **revalida el opt-out en/adyacente a `Segmented→Dispatched`** (fuente única = `SmsOptInRegistry` de `TaxVision.Sms`, que alimenta la supresión de Campaign); si el registry ya está `StoppedByUser`, transición a `Suppressed` (`Outcome=Skipped`) ⇒ la unidad no-enviada se detiene aunque el opt-out llegara tras el freeze. Ventana mínima aceptada (at-least-once). — (removido: sin dinero en el canal; ver banner — antes refund de la reserva). |
| Dos webhooks del mismo `providerMessageId` | `ProcessedBusinessMessage (provider, providerMessageId, eventType)` ⇒ único. |

## 3. Sin doble-scheduler (delegado)
SMS **no agenda**: el disparo temporal y su **lease atómico** viven en el Scheduler (`scheduler/`), que corrige el doble-scheduler + `Status=Sending` no-atómico del legado (ADR-CAMP-000 §Anti-patrón 6). SMS sólo reacciona a `SmsDispatchRequested` ya materializado; su idempotencia por destinatario absorbe cualquier fan-out duplicado del upstream.

## 4. Backpressure y rate limits del proveedor
- El fan-out **no** es fire-and-forget con `Task.Run`/`Task.Delay` (anti-patrón legado, `SmsCampaignSender.cs:331`, que se pierde al reiniciar). El envío al proveedor lo hacen **handlers Wolverine durables** con concurrencia acotada por endpoint (`MaxDegreeOfParallelism`) y throttling configurable por tenant/sender.
- Los límites del proveedor (TPS por número/short code) se respetan con un limitador por `sender_id` (token bucket); el exceso re-encola con backoff (el mensaje sigue en el inbox durable, no se pierde en un reinicio).
- 429/5xx del proveedor ⇒ retry con backoff exponencial + jitter, hasta N intentos, luego `Failed`. — (removido: sin dinero en el canal; ver banner — antes refund).

## 5. Aislamiento transaccional
- Creación de dispatch: una sola tx local (outbox). Read-committed suficiente porque la unicidad la garantizan los constraints, no un read previo. — (removido: sin dinero en el canal; ver banner — antes reserva Wallet en la misma tx).
- Efectos terminales (Delivered/Failed): disparados por evento, cada uno su propia tx idempotente. — (removido: sin dinero en el canal; ver banner — antes consume/refund).
- Sin locks pesimistas salvo el limitador de TPS (que es in-memory/distribuido, no de BD).

## 6. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| RowVersion como patrón de concurrencia | `ProcessedBusinessMessage.cs:23` | VERIFIED | 96% |
| Legado fan-out con `Task.Delay` (se pierde al reiniciar) | `SmsCampaignSender.cs:331`, ADR-CAMP-000 §Anti-patrón 2 | VERIFIED | 96% |
| Doble-scheduler es responsabilidad del Scheduler | `05_Master_ADR.md` Dec.4 | VERIFIED (política) | 95% |
| Estrategia de concurrencia/backpressure SMS | este documento | NEW | — |
