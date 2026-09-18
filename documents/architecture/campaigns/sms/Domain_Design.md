# TaxVision.Sms — Domain Design

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — SMS NO es un servicio nuevo; `TaxVision.Sms` YA EXISTE y ya es M2M** (`SendSmsBatchCommand`, `POST /sms/messages`, `ActorType.Service`). El canal SMS es un **CONSUMER dentro de `TaxVision.Sms`** que procesa `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un microservicio SMS nuevo y/o un Wallet queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- **Servicio:** SMS (`TaxVision.Sms`) — consumer del canal SMS **dentro de `TaxVision.Sms` (ya existente)**
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- **Anchors:** `../00_Overview_And_Index.md`, `../02_Context_Map.md`, `../05_Master_ADR.md`

## 1. Rol y fronteras

El canal SMS es un **CONSUMER dentro de `TaxVision.Sms` (ya existente)**: consume `campaign.dispatch.requested.v1` (Campaigns como orquestador agnóstico), **reconstruye el texto congelado** desde el `ContentRef` (inmutable) + `SmsPayload{ TemplateRef, Variables }` que viajan en el contrato (no adivina campos; render vía Scribe **sólo** si el `SmsPayload` trae `TemplateRef` sin texto materializado, resolviendo al MISMO texto que la API mostró al crear), **segmenta** (GSM-7/Unicode), **entrega vía un proveedor SMS externo** (proveedor a decidir — ver `ADR.md` SMS-ADR-001) y **responde `campaign.dispatch.result.v1`** con `Outcome = Accepted | Delivered | Failed | Skipped | Unknown` (acepta proveedor ⇒ `Accepted`; DLR de entrega ⇒ `Delivered`; timeout ⇒ `Unknown`, nunca `Failed`). No define audiencias, no agenda, no posee precio de plan. La correlación es un `dispatch_id` opaco que se devuelve intacto (patrón `PostmasterEmailEvents.cs:37,104`). **Sin dinero:** — (removido: sin dinero en el canal; ver banner). `TaxVision.Sms` ya es M2M-ready (`SendSmsBatchCommand`, `POST /sms/messages`, `ActorType.Service` per `MessagesController.cs:21`).

Doble modo de uso (requisito del Overview §Servicios #5):
1. **Campaña:** un `campaign.dispatch.requested.v1` por destinatario, originado por Campaigns; `CampaignId`/`CampaignRunId` presentes en el `dispatch_id`/contexto.
2. **Individual:** un envío suelto (ej. recordatorio transaccional de otro servicio) — `CampaignId` null. Mismo aggregate, misma máquina de estados. — (removido: sin dinero en el canal; ver banner).

### Lo que NO hace (fronteras duras, ver `02_Context_Map.md` §Fronteras)
- **No muta saldo ni autoriza balance.** — (removido: sin dinero en el canal; ver banner). La autorización por balance es un interceptor/PEP externo y DIFERIDO, fuera de este canal.
- **No resuelve audiencia** ni copia contactos (anti-patrón snapshot stale del legado).
- **No es Postmaster** ni comparte su proveedor: SMS tiene su propio proveedor y sus propios secretos cifrados.
- **No agenda** (eso es del Scheduler).

## 2. Ubiquitous language (delta específico SMS)

| Término | Definición |
|---|---|
| **SmsDispatch** | Aggregate root: una intención de entregar un SMS a **un** destinatario en **un** intento — es la materialización SMS del `CampaignDispatchAttempt` canónico (unidad de trabajo = destinatario/canal). Idempotente por `(TenantId, CampaignRunId?, RecipientId, Attempt)`, único por `(run_id, recipient_id, attempt_no)` (equivalente al `dispatch_id` opaco, que es **por intento**). Reemplaza al `SmsSendLog` mutable del legado. |
| **RecipientId / ContactRef** | Id **estable por unidad**, el mismo entre reintentos del mismo destinatario (nunca incluye el número de intento). Los contactos manuales reciben un id estable **generado** (nunca el literal `"manual"`). Es el `recipient_id` del `UNIQUE(run_id, recipient_id, attempt_no)` canónico. |
| **ContentRef** | Referencia **inmutable** al contenido congelado en el momento de crear la campaña; ancla el texto exacto que la API mostró. El consumer reconstruye el cuerpo desde aquí, no lo adivina. |
| **SmsPayload** | Payload de canal tipado `{ TemplateRef, Variables }` que, junto al `ContentRef`, resuelve al **texto exacto congelado**. Reemplaza el par ad-hoc `RenderedBody`/`TemplateRef`+vars sueltos. |
| **Segment** | Unidad de segmentación del proveedor. GSM-7: 160 (1 seg) / 153 (concatenado). Unicode (UCS-2): 70 / 67. Determinístico sobre el cuerpo final renderizado. |
| **Encoding** | `Gsm7` \| `Ucs2`. Detectado del cuerpo; determina el tamaño de segmento. |
| **SenderId** | Origen visible: DID (long code), toll-free, short code o alfanumérico. Propiedad de la config de proveedor del tenant, no del frontend. |
| **OptInState** | Estado de consentimiento del número por tenant (`Pending`/`Subscribed`/`StoppedByUser`/`Unsubscribed`/`Blocked`). Gate previo a todo envío marketing. |
| **MessageClass** | `Transactional` (account) \| `Marketing`. Marketing exige opt-in explícito y respeta STOP; transactional respeta STOP duro pero no requiere opt-in de marketing. |
| **~~CostQuote~~** | — (removido: sin dinero en el canal; ver banner). |
| **ProviderMessageId** | Id opaco del proveedor, clave para conciliar webhooks de estado. |

## 3. Aggregates

### 3.1 `SmsDispatch` (aggregate root)
Estado de un envío individual. Inmutable en su identidad de idempotencia; muta estado sólo por métodos que devuelven `Result` (convención de la casa).

Campos: `Id`, `TenantId`, `CampaignId?`, `CampaignRunId?`, `RecipientId` (estable por unidad, **no** incluye `Attempt`; contactos manuales ⇒ id generado, nunca `"manual"`), `Attempt`, `ToPhoneE164`, `SenderId`, `MessageClass`, `ContentRef` (inmutable), `SmsPayload` (`{ TemplateRef, Variables }` ⇒ texto congelado exacto), `Encoding`, `Segments`, `Status`, `ProviderMessageId?`, `ProviderScopeRef` (config cifrada, por ID), `FailureCode?`, timestamps (`CreatedAtUtc`, `AcceptedAtUtc?`, `DeliveredAtUtc?`, `FailedAtUtc?`), `RowVersion`. El cuerpo se **reconstruye** desde `ContentRef`/`SmsPayload`; no se adivina desde campos sueltos. — (removido: sin dinero en el canal; ver banner — antes `CostQuote`/`ReservationId`).

Métodos (todos `Result`): `Segment()` (calcula encoding+segments), `MarkAccepted(providerMessageId)`, `MarkDelivered(providerMessageId, at)`, `MarkFailed(code, at)`, `MarkSuppressed(reason)`. Guards de transición en `State_Machines.md`.

**Diferencia con legado:** el legado usa `SmsSendLog` como fila mutable con `RetryCount`/`LastRetryAt` (`SmsSendLog.cs:59-61`) y sin idempotencia; aquí cada intento es un `SmsDispatch` distinto (`Attempt` incremental) y auditable. — (removido: sin dinero en el canal; ver banner).

### 3.2 `SmsOptInRegistry` (aggregate por número)
Estado de consentimiento por `(TenantId, PhoneE164)`: `OptInState`, `AcceptsMarketing`, `AcceptsTransactional`, `OptInAtUtc?`, `OptOutAtUtc?`, `Source` (webhook STOP / API / import con prior-consent), `Language`. Único punto que decide si un envío está permitido. Consolida `SmsCell` (`SmsCell.cs:108-115`) + la lógica STOP dispersa del legado.

### 3.3 `SmsProviderConfig` (aggregate por tenant)
Config de proveedor SMS del tenant: `Provider` (enum), `SenderIds[]` (con tipo y default), `EncryptedCredentials` (blob cifrado, nunca plaintext — corrige `SmsProviderCredential.ClientApiKey`/`UserApiToken` en claro, `SmsProviderCredential.cs:20,25`), `WebhookSecret` (cifrado), `IsActive`, `DefaultMessageClass`. Ver `Security.md`.

## 4. Value Objects (copia-por-contexto, no compartidos)
- ~~`Money(long AmountCents, string Currency)`~~ — (removido: sin dinero en el canal; ver banner).
- `IdempotencyKey` — copia local, forma de `PaymentApp.Domain/ValueObjects/IdempotencyKey.cs:10-30`.
- `PhoneE164` — VO nuevo: normaliza y valida E.164 (corrige el `FormatPhoneNumber` ad-hoc del legado que sólo asumía USA, `SmsCampaignSender.cs:387-400`).
- `SmsSegmentation` — VO/servicio de dominio puro: dado el cuerpo devuelve `(Encoding, Segments)`. Formaliza y corrige el cálculo del legado (`SmsCampaignSender.cs:402-427`), que sólo miraba `c > 127` sin considerar el conjunto de caracteres GSM-7 extendido (que ocupa 2 septetos) ni los saltos de línea.

## 5. Segmentación (regla de dominio central)

```
encoding = TodosLosCaracteresEnGsm7Base(body) ? Gsm7 : Ucs2
if Gsm7:
    weightedLen = Σ (esGsm7Extendido(c) ? 2 : 1)   // { } [ ] ~ ^ \ | € cuentan doble
    segments = weightedLen <= 160 ? 1 : ceil(weightedLen / 153)
else: // Ucs2
    len = unidadesUtf16(body)                       // pares surrogate = 2
    segments = len <= 70 ? 1 : ceil(len / 67)
```
La segmentación es determinística sobre el cuerpo final reconstruido desde `ContentRef`/`SmsPayload`; el `Segments` alimenta el `result` y la observabilidad. **"Cuántos segmentos" (relevante para facturación) ≠ entrega:** el conteo de segmentos es un dato de facturación del proveedor, pero **Campaign NO lo tarifa** (sin dinero en el canal); es sólo un atributo del envío y de las métricas, no una decisión de entrega ni de precio. — (removido: sin dinero en el canal; ver banner — antes conciliación de costo/Consume en Wallet).

## 6. Invariantes
1. — (removido: sin dinero en el canal; ver banner — antes reserva Wallet previa al envío). La autorización por balance es un interceptor/PEP externo y DIFERIDO.
2. Ningún envío `Marketing` procede si el `SmsOptInRegistry` no está `Subscribed` con `AcceptsMarketing`.
3. Ningún envío (ni transactional) procede si el número está `StoppedByUser`/`Blocked` (STOP es duro). El opt-out se **revalida en el punto de envío** (adyacente a `Segmented→Dispatched`): un STOP recibido DESPUÉS de congelar la audiencia igual detiene una unidad aún no enviada (⇒ `Suppressed`/`Skipped`). La supresión de Campaign se alimenta del optout de `TaxVision.Sms` (fuente única de consentimiento).
4. `Segments >= 1`. — (removido: sin dinero en el canal; ver banner).
5. Secretos de proveedor **nunca** en claro en la BD (fail-closed: sin config cifrada válida ⇒ dispatch rechazado).
6. `(TenantId, CampaignRunId?, RecipientId, Attempt)` es único (≡ `UNIQUE(run_id, recipient_id, attempt_no)` canónico) ⇒ un intento = un dispatch (`CampaignDispatchAttempt`); idempotencia por destinatario, `RecipientId` estable entre intentos.

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| `TaxVision.Sms` YA EXISTE y ya es M2M (consumer, no servicio nuevo) | `MessagesController.cs:21` (`ActorType.Service`), `SendSmsBatch.cs` | VERIFIED | 96% |
| Legado calcula segmentos GSM-7/Unicode de forma simplista | `SmsCampaignSender.cs:402-427` | VERIFIED | 97% |
| Legado guarda secretos de proveedor en claro | `SmsProviderCredential.cs:20,25` | VERIFIED | 98% |
| Legado gestiona opt-in por `SmsCell` con STOP disperso | `SmsCell.cs:108-115`, `SmsIncomingMessage.cs:79-89` | VERIFIED | 95% |
| Diseño aggregate `SmsDispatch`/registry/config | este documento | NEW | — |
| Proveedor concreto (Twilio/SNS/otro) | decisión abierta | NEW (DOCUMENTED_ONLY) | — |
