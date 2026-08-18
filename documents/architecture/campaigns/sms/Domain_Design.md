# TaxVision.Sms — Domain Design

- **Servicio:** SMS (`TaxVision.Sms`) — ejecutor de canal SMS (NUEVO, greenfield)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- **Anchors:** `../00_Overview_And_Index.md`, `../02_Context_Map.md`, `../05_Master_ADR.md`

## 1. Rol y fronteras

`TaxVision.Sms` es un **ejecutor de canal**: recibe un contrato **dispatch por destinatario** (desde Campaigns, o desde un caller directo para envíos individuales), **renderiza** (vía Scribe si el cuerpo no viaja resuelto), **segmenta** (GSM-7/Unicode), **consume Wallet** por el costo real, **entrega vía un proveedor SMS externo** (proveedor a decidir — ver `ADR.md` SMS-ADR-001) y **reporta el resultado** (contrato result común). No define audiencias, no agenda, no posee el precio de plan.

Doble modo de uso (requisito del Overview §Servicios #5):
1. **Campaña:** un `SmsDispatchRequested` por destinatario, originado por Campaigns; `CampaignId`/`CampaignRunId` presentes.
2. **Individual:** un envío suelto (ej. recordatorio transaccional de otro servicio) — `CampaignId` null; consume Wallet directamente. Mismo aggregate, misma máquina de estados.

### Lo que NO hace (fronteras duras, ver `02_Context_Map.md` §Fronteras)
- **No muta saldo.** Pide `Reserve`/`Consume`/`Refund` a `TaxVision.Wallet`; jamás edita un balance.
- **No resuelve audiencia** ni copia contactos (anti-patrón snapshot stale del legado).
- **No es Postmaster** ni comparte su proveedor: SMS tiene su propio proveedor y sus propios secretos cifrados.
- **No agenda** (eso es del Scheduler).

## 2. Ubiquitous language (delta específico SMS)

| Término | Definición |
|---|---|
| **SmsDispatch** | Aggregate root: una intención de entregar un SMS a **un** destinatario, idempotente por `(TenantId, CampaignId?, RecipientKey, Attempt)`. Reemplaza al `SmsSendLog` mutable del legado. |
| **Segment** | Unidad de facturación del proveedor. GSM-7: 160 (1 seg) / 153 (concatenado). Unicode (UCS-2): 70 / 67. Determinístico sobre el cuerpo final renderizado. |
| **Encoding** | `Gsm7` \| `Ucs2`. Detectado del cuerpo; determina el tamaño de segmento y a menudo el precio del proveedor. |
| **SenderId** | Origen visible: DID (long code), toll-free, short code o alfanumérico. Propiedad de la config de proveedor del tenant, no del frontend. |
| **OptInState** | Estado de consentimiento del número por tenant (`Pending`/`Subscribed`/`StoppedByUser`/`Unsubscribed`/`Blocked`). Gate previo a todo envío marketing. |
| **MessageClass** | `Transactional` (account) \| `Marketing`. Marketing exige opt-in explícito y respeta STOP; transactional respeta STOP duro pero no requiere opt-in de marketing. |
| **CostQuote** | Costo estimado en USD minor units = `segments × pricePerSegment(encoding, destino)`. Determinístico ⇒ estimate == actual salvo que el proveedor reporte MCC/MNC distinto. |
| **ProviderMessageId** | Id opaco del proveedor, clave para conciliar webhooks de estado. |

## 3. Aggregates

### 3.1 `SmsDispatch` (aggregate root)
Estado de un envío individual. Inmutable en su identidad de idempotencia; muta estado sólo por métodos que devuelven `Result` (convención de la casa).

Campos: `Id`, `TenantId`, `CampaignId?`, `CampaignRunId?`, `RecipientKey` (hash estable del destinatario+campaign+attempt), `Attempt`, `ToPhoneE164`, `SenderId`, `MessageClass`, `RenderedBody` (o `TemplateRef`+vars), `Encoding`, `Segments`, `CostQuote` (`Money`), `ReservationId` (Wallet), `Status`, `ProviderMessageId?`, `ProviderScopeRef` (config cifrada, por ID), `FailureCode?`, timestamps (`CreatedAtUtc`, `AcceptedAtUtc?`, `DeliveredAtUtc?`, `FailedAtUtc?`), `RowVersion`.

Métodos (todos `Result`): `Quote()` (calcula encoding+segments+cost), `MarkAccepted(providerMessageId)`, `MarkDelivered(providerMessageId, at)`, `MarkFailed(code, at)`, `MarkSuppressed(reason)`. Guards de transición en `State_Machines.md`.

**Diferencia con legado:** el legado usa `SmsSendLog` como fila mutable con `RetryCount`/`LastRetryAt` (`SmsSendLog.cs:59-61`) y sin idempotencia; aquí cada intento es un `SmsDispatch` distinto (`Attempt` incremental), auditable, y el costo es `Money` (long) no `decimal EstimatedCost` (`SmsSendLog.cs:54`).

### 3.2 `SmsOptInRegistry` (aggregate por número)
Estado de consentimiento por `(TenantId, PhoneE164)`: `OptInState`, `AcceptsMarketing`, `AcceptsTransactional`, `OptInAtUtc?`, `OptOutAtUtc?`, `Source` (webhook STOP / API / import con prior-consent), `Language`. Único punto que decide si un envío está permitido. Consolida `SmsCell` (`SmsCell.cs:108-115`) + la lógica STOP dispersa del legado.

### 3.3 `SmsProviderConfig` (aggregate por tenant)
Config de proveedor SMS del tenant: `Provider` (enum), `SenderIds[]` (con tipo y default), `EncryptedCredentials` (blob cifrado, nunca plaintext — corrige `SmsProviderCredential.ClientApiKey`/`UserApiToken` en claro, `SmsProviderCredential.cs:20,25`), `WebhookSecret` (cifrado), `IsActive`, `DefaultMessageClass`. Ver `Security.md`.

## 4. Value Objects (copia-por-contexto, no compartidos)
- `Money(long AmountCents, string Currency)` — copia local, misma forma que `PaymentApp.Domain/ValueObjects/Money.cs:6-30`. Costos SMS SIEMPRE en USD minor units.
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
El costo depende de `segments` y del destino; el proveedor puede diferir por MCC/MNC, por lo que el `CostQuote` es un estimate honesto y `Consume` usa el **actual reportado** cuando el proveedor lo da (webhook), con conciliación en Wallet (`Transactional_Protocol.md`).

## 6. Invariantes
1. Ningún `SmsDispatch` sale a proveedor sin una **reserva Wallet confirmada** (`ReservationId != null`).
2. Ningún envío `Marketing` procede si el `SmsOptInRegistry` no está `Subscribed` con `AcceptsMarketing`.
3. Ningún envío (ni transactional) procede si el número está `StoppedByUser`/`Blocked` (STOP es duro).
4. `Segments >= 1` y `CostQuote.Currency == "USD"`.
5. Secretos de proveedor **nunca** en claro en la BD (fail-closed: sin config cifrada válida ⇒ dispatch rechazado).
6. `(TenantId, CampaignId?, RecipientKey, Attempt)` es único ⇒ un intento = un dispatch (idempotencia por destinatario).

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Servicio SMS nuevo no existe en backend nuevo | Notification SMS = `LoggingSmsSender` stub (ADR-CAMP-000) | VERIFIED | 96% |
| Legado calcula segmentos GSM-7/Unicode de forma simplista | `SmsCampaignSender.cs:402-427` | VERIFIED | 97% |
| Legado guarda secretos de proveedor en claro | `SmsProviderCredential.cs:20,25` | VERIFIED | 98% |
| Legado usa `decimal` para costo (anti-patrón money) | `SmsSendLog.cs:54` | VERIFIED | 98% |
| Legado gestiona opt-in por `SmsCell` con STOP disperso | `SmsCell.cs:108-115`, `SmsIncomingMessage.cs:79-89` | VERIFIED | 95% |
| `Money(long)` reusable como copia-por-contexto | `PaymentApp.Domain/ValueObjects/Money.cs:6-30` | VERIFIED | 97% |
| Diseño aggregate `SmsDispatch`/registry/config | este documento | NEW | — |
| Proveedor concreto (Twilio/SNS/otro) | decisión abierta | NEW (DOCUMENTED_ONLY) | — |
