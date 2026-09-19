# WhatsApp — Commands & Events

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — WhatsApp SÍ es un servicio nuevo (`TaxVision.WhatsApp`, Meta/WABA), pero de FASE POSTERIOR y solo como CONSUMER del contrato de dispatch — sin dinero.** Consume `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un Wallet o cobro por este canal queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- Mensajería: **Wolverine outbox/inbox durable, at-least-once** (nunca exactly-once). Todo handler idempotente + `ProcessedBusinessMessage` para dedupe de efecto de negocio.

Convención de nombres/versión: `MessageIdentity("whatsapp.<evento>.v1")` (espeja `PostmasterEmailEvents.cs:24`). Contratos **copiados por contexto** (no se comparten tipos con Campaigns).

> **Reconciliación #13:** el **tipo** de evento es el canónico `campaign.dispatch.requested.v1` / `campaign.dispatch.result.v1` (con campo `Channel=WhatsApp`). El string `whatsapp.dispatch_requested.v1` de abajo es la **routing key/alias por canal** (cola `campaign.dispatch.whatsapp`), no un contrato distinto. El `Outcome` del result usa el enum canónico `Accepted | Delivered | Failed | Skipped | Unknown` (accept de Meta = `Accepted`; timeout = `Unknown`). Ver `../campaigns/Commands_And_Events.md §2` y `../campaigns/State_Machines.md`.

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
  // ReservationRef — (removido: sin dinero en el canal; ver banner)
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
  // BilledAmount — (removido: sin dinero en el canal; ver banner)
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
| `ApplyDeliveryStatus` | `WhatsAppDeliveryStatusReceived` (webhook) | avanza estado (importe/pricing removido: sin dinero, ver banner) | guard monotónico + `ProcessedBusinessMessage(op="wa.status", scope=wamid, key=status)` |
| `SyncTemplates` | `POST /templates/sync` o cron | pull catálogo Meta | por `(Tenant, LastSyncedAt)` |

> Comandos `RequestConsume` / `RequestRefund` — (removidos: sin dinero en el canal; la autorización por balance es un interceptor/PEP externo y diferido, ver banner).

## 3. Eventos emitidos

| Evento | Cuándo | Consumidores |
|---|---|---|
| `whatsapp.dispatch_result.v1` | cada avance de estado | Campaigns (stats agregadas) |
| `whatsapp.template_status_changed.v1` | sync/webhook de plantilla | Campaigns (para bloquear campañas con plantilla no aprobada) |
| `whatsapp.session_opened.v1` | inbound del usuario | (opcional) Communication / analítica |

> Eventos `whatsapp.message_billed.v1` / `whatsapp.message_refunded.v1` — (removidos: sin dinero en el canal; ver banner).

## 4. Interacción con Wallet — (removida)

— (removido: sin dinero en el canal; este servicio no reserva/consume/refunda saldo ni emite `wallet.*`. La autorización por balance es un interceptor/PEP externo y DIFERIDO, ver banner y `../05_Master_ADR.md` D1/D3/D7.)

## 5. Diferencias con el legado

| Legado | Nuevo |
|---|---|
| `SendResult { Success, MessageId=Guid, Cost }` síncrono (`WhatsAppCampaignSender.cs:96-101`) | eventos multi-etapa con `wamid` real (importe/costo removido: sin dinero, ver banner) |
| Sin evento de estado (no webhook) | `whatsapp.dispatch_result.v1` por avance |
| Sin dedupe de reintento (doble-cuenta tracking, anti-patrón §3 ADR-CAMP-000) | `ProcessedBusinessMessage` por operación |

## 6. Evidencia

| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Patrón MessageIdentity/versión | `PostmasterEmailEvents.cs:24` | VERIFIED | 95% |
| Business-inbox dedupe | `ProcessedBusinessMessage.cs:27-105` | VERIFIED | 97% |
| Result síncrono legado | `WhatsAppCampaignSender.cs:96-101` | VERIFIED | 96% |
