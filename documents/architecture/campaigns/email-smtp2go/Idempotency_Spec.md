# Email (SMTP2GO) — Idempotency Spec

- Servicio: **TaxVision.Campaigns.Email**
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

At-least-once en todo el bus (Wolverine). Toda entrega puede repetirse; **todo handler es idempotente**. Nunca exactly-once. Tres capas de defensa: unique constraint (transporte), `ProcessedBusinessMessage` (efecto de negocio), state guards (dominio).

## 1. Claves de idempotencia

| Efecto | Clave canónica | Mecanismo |
|---|---|---|
| Procesar un dispatch | `IdempotencyKey` = `(campaignRunId, recipientId, attempt)` | UNIQUE `(tenant,run,recipient,attempt)` en `email_dispatch` + `ProcessedBusinessMessage("ProcessEmailDispatch", key)` |
| Aplicar webhook de proveedor | `provider_event_id` | UNIQUE en `inbound_webhook_event` + `ProcessedBusinessMessage("ApplyProviderWebhook", provider_event_id)` |
| Emitir result event | (no requiere key nueva) | atómico con la mutación vía outbox; el consumidor (Campaigns) dedupe con su propia inbox |
| Open/click tracking | `(dispatchId, kind)` (open cuenta 1 vez) | UNIQUE en `email_tracking_event` |

`IdempotencyKey` es **copia local** del VO (`PaymentApp.Domain/ValueObjects/IdempotencyKey.cs`); no se comparte el tipo entre contexts.

## 2. Por qué dos mecanismos (unique + ProcessedBusinessMessage)
- El **unique constraint** atrapa duplicados a nivel fila, pero un handler puede tener **efectos colaterales** (POST al proveedor, emitir evento) que el constraint por sí solo no bloquea si la lógica corre igual.
- `ProcessedBusinessMessage` (business-inbox, `Growth/.../Idempotency/ProcessedBusinessMessage.cs`) marca que el **efecto de negocio** ya ocurrió, cortando el handler **antes** de re-ejecutar side-effects. Es la diferencia entre "no insertar fila dos veces" y "no **enviar el email** dos veces".

## 3. Idempotencia del envío al proveedor (el caso difícil)
SMTP2GO `email/send` no garantiza dedupe por client-key. Estrategia:
1. Persistir `Pending` + marcar `ProcessedBusinessMessage` **antes** del POST (dentro de una TX).
2. En reentrega: `ProcessedBusinessMessage` hit ⇒ el handler **no re-POSTea**; delega al reconciliador que verifica estado real antes de cualquier reintento.
3. Header `X-Campaign-Dispatch-Id` estable en cada POST para correlación/auditoría.
4. Ventana residual (crash entre POST y COMMIT) ⇒ posible 1 duplicado; acotada por reconciliación, nunca negada (at-least-once honesto). Ver `Transactional_Protocol.md §4-5`.

## 4. Idempotencia de webhooks (corrige double-count del legado)
El legado registraba cada evento de tracking sin dedupe y con contadores que **doble-contaban en reintento** (`TrackingController.cs:53,98`; anti-patrón #3). Diseño nuevo:
- Verificar firma HMAC ⇒ persistir `inbound_webhook_event` con UNIQUE `provider_event_id` (segundo POST del mismo evento = conflicto ⇒ no-op).
- Proyectar la transición vía state guard: `MarkDelivered` desde un dispatch ya `Delivered` = `Result.Failure` sin efecto (no incrementa nada dos veces).
- Contadores/stats viven en Campaigns y se derivan de result events **deduplicados**, no de un `++` por webhook.

## 5. State guards como tercera capa
Cada método del aggregate valida el estado origen (ver `State_Machines.md §1.2`). Una transición inválida es no-op idempotente, no una excepción de control de flujo. Esto hace que **el orden de llegada** de webhooks at-least-once no rompa invariantes (monotonicidad de severidad de bounce).

## 6. Tabla de escenarios
| Escenario | Resultado esperado |
|---|---|
| `dispatch_requested` entregado 2× | 1 fila, 1 POST, 1 `sent` event |
| webhook `delivered` entregado 3× | 1 transición a `Delivered`, 1 `delivered` event |
| webhook `bounce` llega antes que `delivered` (out-of-order) | severidad monótona: `Bounced` gana; `delivered` posterior = no-op |
| reintento HTTP a SMTP2GO (5xx) dentro del mismo attempt | mismo `dispatchId`, no fila nueva |
| Campaigns reintenta recipient (nuevo attempt) | fila nueva `(…,attempt+1)`, dispatch independiente |

## 7. Evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado sin dedupe de tracking (double-count) | `TrackingController.cs:53,98`; `../05_Master_ADR.md` #3 | VERIFIED | 92% |
| `ProcessedBusinessMessage` como business-inbox | `Growth/.../Idempotency/ProcessedBusinessMessage.cs` | VERIFIED | 95% |
| `IdempotencyKey` VO reutilizable (copia) | `PaymentApp.Domain/ValueObjects/IdempotencyKey.cs` | VERIFIED | 95% |
| Sin dedupe fuerte en SMTP2GO email/send | supuesto de diseño | DOCUMENTED_ONLY | 65% |
