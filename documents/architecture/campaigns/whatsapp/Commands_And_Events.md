# WhatsApp — Commands & Events

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- Mensajería: **Wolverine outbox/inbox durable, at-least-once** (nunca exactly-once). Todo handler idempotente + `ProcessedBusinessMessage` para dedupe de efecto de negocio.

Convención de nombres/versión: `MessageIdentity("whatsapp.<evento>.v1")` (espeja `PostmasterEmailEvents.cs:24`). Contratos **copiados por contexto** (no se comparten tipos con Campaigns/Wallet).

## 1. Contrato dispatch/result (común a canales)

### Entrante (Campaigns → WhatsApp)
```
[MessageIdentity("whatsapp.dispatch_requested.v1")]
WhatsAppDispatchRequested {
  DispatchId: Guid            // clave de idempotencia por destinatario
  TenantId: Guid
  CampaignId: Guid            // opaco, eco de vuelta
  CampaignRunId: Guid         // run inmutable
  RecipientRef: string        // id opaco de contacto
  ToPhoneE164: string
  Attempt: int
  TemplateRef?: { Name, Language }   // requerido fuera de sesión
  Variables?: Dictionary<string,string>
  FreeText?: string                  // solo si sesión abierta
  Category?: enum
  ReservationRef: Guid        // reserva Wallet ya tomada por Campaigns
}
```

### Saliente (WhatsApp → Campaigns)
```
[MessageIdentity("whatsapp.dispatch_result.v1")]
WhatsAppDispatchResult {
  DispatchId: Guid
  TenantId: Guid
  CampaignId: Guid            // devuelto INTACTO, no interpretado
  Outcome: enum(Accepted|Sent|Delivered|Read|Failed|Rejected)
  ProviderMessageId?: string  // wamid
  BilledAmount?: Money        // costo real (minor units) desde webhook
  ConversationCategory?: enum
  FailureCode?: string
  OccurredAtUtc: DateTime
}
```
Se emite **más de una vez** por `DispatchId` a medida que avanza el estado (Accepted→Sent→Delivered→Read o →Failed). Campaigns agrega idempotentemente por `(DispatchId, Outcome)`.

## 2. Comandos internos (handlers Wolverine)

| Comando | Origen | Efecto | Idempotencia |
|---|---|---|---|
| `ValidateAndAcceptDispatch` | `WhatsAppDispatchRequested` | valida plantilla/sesión/número; crea `WhatsAppMessage(Accepted)` o `Rejected` | `ProcessedBusinessMessage(op="wa.accept", scope=DispatchId)` |
| `SendToMeta` | tras Accept | POST Cloud API; persiste `wamid`; `→Sent` | `wamid` único; reintento no crea 2 `Sent` (upsert por DispatchId) |
| `ApplyDeliveryStatus` | `WhatsAppDeliveryStatusReceived` (webhook) | avanza estado; captura `pricing` | guard monotónico + `ProcessedBusinessMessage(op="wa.status", scope=wamid, key=status)` |
| `RequestConsume` | al entrar `Delivered` (o `Sent`, ADR-WA-004) | pide `consume` a Wallet | por `DispatchId` (un solo consume) |
| `RequestRefund` | al entrar `Failed`/`Rejected` | pide `refund` de la reserva | por `DispatchId` (un solo refund) |
| `SyncTemplates` | `POST /templates/sync` o cron | pull catálogo Meta | por `(Tenant, LastSyncedAt)` |

## 3. Eventos emitidos

| Evento | Cuándo | Consumidores |
|---|---|---|
| `whatsapp.dispatch_result.v1` | cada avance de estado | Campaigns (stats agregadas) |
| `whatsapp.message_billed.v1` | al confirmar costo real (Delivered) | Wallet (consume), observabilidad |
| `whatsapp.message_refunded.v1` | Failed/Rejected | Wallet (refund) |
| `whatsapp.template_status_changed.v1` | sync/webhook de plantilla | Campaigns (para bloquear campañas con plantilla no aprobada) |
| `whatsapp.session_opened.v1` | inbound del usuario | (opcional) Communication / analítica |

## 4. Interacción con Wallet (movimientos, no edición de saldo)

WhatsApp **nunca** muta el saldo. Emite solicitudes; Wallet aplica el movimiento inmutable:
- `wallet.consume_requested.v1 { ReservationRef, DispatchId (idempotency), Amount, Reason="whatsapp.delivered" }`
- `wallet.refund_requested.v1 { ReservationRef, DispatchId, Reason="whatsapp.failed|rejected" }`

(En envío individual, WhatsApp también origina el `reserve` antes de aceptar; en campaña, la reserva la trae Campaigns en `ReservationRef`.)

## 5. Diferencias con el legado

| Legado | Nuevo |
|---|---|
| `SendResult { Success, MessageId=Guid, Cost }` síncrono (`WhatsAppCampaignSender.cs:96-101`) | eventos multi-etapa con `wamid` real y costo desde webhook |
| Sin evento de estado (no webhook) | `whatsapp.dispatch_result.v1` por avance |
| Cost calculado localmente al enviar (`CostService.cs`) | `BilledAmount` del webhook `pricing`; Wallet consume |
| Sin dedupe de reintento (doble-cuenta tracking, anti-patrón §3 ADR-CAMP-000) | `ProcessedBusinessMessage` por operación |

## 6. Evidencia

| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Patrón MessageIdentity/versión | `PostmasterEmailEvents.cs:24` | VERIFIED | 95% |
| Business-inbox dedupe | `ProcessedBusinessMessage.cs:27-105` | VERIFIED | 97% |
| Result síncrono legado | `WhatsAppCampaignSender.cs:96-101` | VERIFIED | 96% |
| Cost local legado | `CostService.cs:27-72` | VERIFIED | 96% |
