# Email (SMTP2GO) — Concurrency Spec

- Servicio: **TaxVision.Campaigns.Email**
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

## 1. Modelo de concurrencia
El servicio escala horizontalmente (N réplicas) consumiendo la **misma** cola Wolverine. Las garantías dependen de que ninguna operación asuma single-instance (el legado sí lo asumía, con un `HttpClient` mutable compartido y un scheduler in-proc).

## 2. Fan-out concurrente por destinatario
- Cada `dispatch_requested` es un **mensaje independiente**; el fan-out lo hace Campaigns emitiendo un mensaje por recipient. El ejecutor procesa mensajes en paralelo con el paralelismo de Wolverine (listener parallelism configurable).
- **No** hay loop en memoria con `Task.Delay` (anti-patrón legado `Smtp2GoService.cs:405`): el "spacing" entre envíos se logra con **rate limiting por proveedor** (§4), no con sleeps en un proceso que se pierde al reiniciar.
- Backpressure: si el rate del proveedor se satura, los mensajes se difieren/re-encolan (Wolverine retry con delay), no se bloquea el thread.

## 3. Concurrencia sobre un mismo dispatch
- UNIQUE `(tenant, run, recipient, attempt)`: dos réplicas procesando el mismo `dispatch_requested` (redelivery) ⇒ una gana el INSERT, la otra recibe conflicto ⇒ trata como dedupe hit (no-op).
- Mutaciones de estado con **optimistic concurrency** (`xmin`/`RowVersion`): un webhook `delivered` y un reconciliador tocando el mismo dispatch a la vez ⇒ el segundo `SaveChanges` falla con `DbUpdateConcurrencyException` ⇒ recarga + reevalúa el state guard (idempotente) ⇒ típicamente no-op.

## 4. Rate limiting del proveedor (SMTP2GO)
- El plan SMTP2GO impone un techo de envíos/segundo. Con N réplicas concurrentes, hay que **coordinar** para no exceder el límite del proveedor por tenant/credencial.
- Diseño: **rate limiter distribuido por `provider_credential`** (token bucket en Redis, o una cola dedicada por credencial con concurrencia limitada). `provider_rate_per_second` vive en `provider_credential`.
- Alternativa MVP: una **cola/endpoint por credencial** con `MaxDegreeOfParallelism` acotado, aceptando menor throughput a cambio de simplicidad. Ver `Deployment.md`.
- El legado seteaba headers en un `HttpClient` compartido (`Smtp2GoService.cs:75-79`) — **race condition** al servir múltiples credenciales concurrentes (una request podía usar la API key de otra). Diseño nuevo: **cliente por request/credencial** (typed client con handler que inyecta la key descifrada por-scope), nunca headers mutables compartidos.

## 5. Reconciliador y jobs periódicos — LEASE atómico
- El barrido de `Pending` huérfanos (`Transactional_Protocol.md §5`) y cualquier job periódico corren con **lease atómico** (misma disciplina que el Scheduler de la suite): `UPDATE ... SET leased_by=@me, leased_until=@t WHERE leased_until < now()` con optimistic check, para que **una sola réplica** procese cada lote (corrige el doble-scheduler del legado, anti-patrón #6).
- El lease es corto y renovable; si la réplica muere, otra lo toma tras expirar.

## 6. Webhooks concurrentes
- Múltiples webhooks del mismo evento (reintento del proveedor) o de eventos distintos del mismo dispatch pueden llegar simultáneamente a réplicas distintas.
- Defensa: UNIQUE `provider_event_id` (persistencia cruda) + optimistic concurrency en la proyección + state guards monótonos. Orden de llegada no importa (ver `Idempotency_Spec.md §6`).

## 7. Aislamiento de tenant bajo concurrencia
- Cada handler fija el tenant explícito en su scope (Wolverine) antes de tocar repos; el query filter global es fail-closed. No hay estado ambient compartido entre mensajes concurrentes de tenants distintos.
- El `provider_credential` se resuelve por `(tenant, scope)` **dentro** del handler, no cacheado en un singleton mutable (a diferencia de `_settings` cacheado en `Smtp2GoService.cs:53`, que era per-instance y no per-tenant).

## 8. Tabla de riesgos de concurrencia
| Riesgo | Mitigación |
|---|---|
| Doble envío por redelivery concurrente | UNIQUE dispatch + ProcessedBusinessMessage + reconciliador |
| Exceder rate del proveedor con N réplicas | rate limiter distribuido por credencial |
| HttpClient/headers compartidos entre credenciales (legado) | typed client por credencial, key inyectada por request |
| Doble reconciliador/scheduler | lease atómico |
| Webhooks out-of-order | state guards monótonos + optimistic concurrency |
| Cache de settings per-instance no per-tenant (legado) | resolución por-tenant dentro del handler |

## 9. Evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado `HttpClient` con headers mutables compartidos | `Smtp2GoService.cs:75-79` | VERIFIED | 90% |
| Legado cache de settings per-instance | `Smtp2GoService.cs:51-56` | VERIFIED | 90% |
| Legado spacing con `Task.Delay` en proceso | `Smtp2GoService.cs:405` | VERIFIED | 95% |
| Legado doble scheduler no-atómico (suite) | `../05_Master_ADR.md` #6 | VERIFIED | 85% |
| Lease atómico / rate limiter distribuido | este diseño | NEW | n/a |
