# Campaigns — API Contracts

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-09-17 (revisión v2 — cancel split #10, contenido #02, idempotencia por endpoint #23, gate enforce #17)
- **Estado:** DISEÑO — no implementado

REST para staff (JWT usuario) + contrato de eventos con Scheduler y ejecutores. Todo endpoint público lleva `[RateLimit(categoría)]` y `[HasPermission("campaigns.manage")]` (el permiso YA existe, `PermissionCatalog.cs:39`); actores `TenantEmployee/TenantAdmin/PlatformAdmin`; tenant del JWT (fail-closed). **Gate de plan (fix #17):** los endpoints de ejecución (`send-now`, `schedule`) validan el entitlement `module.campaigns`; en **producción** el gate es **enforce** (`403 feature_not_enabled`), no log-only. **Sin dinero:** no hay endpoints de estimación/costo/saldo.

---

## 1. Convenciones

- Base: `/campaigns` (ruta del gateway `/campaigns/{**catch-all}`), como el resto de la flota (`/notes`, `/sms`).
- Auth: JWT usuario staff, `[AllowActorTypes(TenantEmployee, TenantAdmin, PlatformAdmin)]` + `[HasPermission("campaigns.manage")]`.
- Idempotencia de escritura (fix #23): header `Idempotency-Key` en `send-now`, `schedule`, `POST /contacts`, `import`. Cada endpoint declara su **alcance** (recurso/método cubierto), fingerprint canónico del body y TTL (que cubre la ventana de replay, no solo limpieza). Reintento con misma clave+fingerprint → misma respuesta; misma clave con otro payload → `409`; solicitud concurrente mientras está `Processing` → `409/425 + Retry-After` (ver `Idempotency_Spec.md §6.1`).
- Errores: `Result.Error.ToHttpStatusCode()`: `409` conflicto de estado/idempotencia, `400/422` invariante de dominio, `403` sin permiso, `403 feature_not_enabled` sin entitlement.
- Paginación: el `PagedResult<T>` del servicio.

---

## 2. Endpoints — Campaña

| Método | Ruta | Descripción |
|---|---|---|
| `POST` | `/campaigns` | Crea Campaign `Draft`. Body: name, `channels[]`, `senders[]` (senderRef por canal), `content[]` (por canal), `audience` (clients+lists+manual), `sendMode`. |
| `GET` | `/campaigns` | Lista (filtros: status, channel). |
| `GET` | `/campaigns/{id}` | Detalle + readiness (qué falta para poder enviar). |
| `PUT` | `/campaigns/{id}` | Editar (solo `Draft`; `409` si no). |
| `POST` | `/campaigns/{id}/send-now` | **Envío inmediato**: valida + `MarkReady` interno + crea un `CampaignRun` (no hay estado `Sending`, fix #10). `Idempotency-Key`. `202 + runId`. |
| `POST` | `/campaigns/{id}/schedule` | Fija `sendMode` Scheduled/Recurring (delega el reloj al Scheduler). |
| `DELETE` | `/campaigns/{id}/schedule` | **Cancela la AGENDA** (Scheduled→Ready). No toca runs en curso. |
| `POST` | `/campaigns/runs/{runId}/cancel` | **Cancela una EJECUCIÓN** por `runId` (drena in-flight; ver límite en `Transactional_Protocol.md §3.1`). Reemplaza el `/{id}/cancel` ambiguo (fix #10). |
| `POST` | `/campaigns/{id}/archive` | Soft-archive. |

## 3. Endpoints — Detalles / reporting

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/campaigns/{id}/runs` | Historial de runs (inmutables): cuándo, `triggeredBy`, estado, contadores. |
| `GET` | `/campaigns/runs/{runId}` | Detalle de un run: snapshot, `recipientCount` (total de unidades), `RunCounters` por canal (Dispatched/Accepted/Delivered/Failed/Skipped/Unknown). |
| `GET` | `/campaigns/runs/{runId}/recipients` | Drill-down por unidad destinatario/canal: `dispatchState`, `reason`, `providerRef`, intentos (paginado, filtros por estado/canal). |

## 4. Endpoints — Contactos y Listas (los contactos)

| Método | Ruta | Descripción |
|---|---|---|
| `POST` `GET` `PUT` `DELETE` | `/campaigns/contacts[/{id}]` | CRUD de contactos (nombre, email/teléfono, canales permitidos). |
| `POST` | `/campaigns/contacts/import` | Import CSV → contactos (dedupe por email/teléfono); devuelve resumen (creados/duplicados/ inválidos). |
| `POST` | `/campaigns/contacts/{id}/opt-out` | Marca opt-out por canal (consentimiento); un contacto opt-out se **Skip** al materializar. |
| `POST` `GET` `PUT` `DELETE` | `/campaigns/lists[/{id}]` | CRUD de listas. |
| `POST` `DELETE` | `/campaigns/lists/{id}/members` | Añadir/quitar contactos de una lista. |

## 5. Endpoints — Remitentes (quién envía)

| Método | Ruta | Descripción |
|---|---|---|
| `POST` `GET` `PUT` `DELETE` | `/campaigns/senders[/{id}]` | CRUD de `SenderProfile` por canal (from/dominio, sender ID/número, número WABA, app). **Sin secretos de proveedor** (esos viven en el ejecutor). |
| `GET` | `/campaigns/senders?channel=Email` | Remitentes disponibles por canal, con su `status` (Pending/Verified/Disabled). |

La **verificación** real (SPF/DKIM del dominio, aprobación WABA, alta del número) la reporta el ejecutor por evento y actualiza el `status` del SenderProfile.

---

## 6. Contrato de eventos (no REST)

**Emite (Campaigns → bus):** `campaign.dispatch.requested.v1` por destinatario/canal (ver `Commands_And_Events.md §2`). Campaigns **no llama** a los ejecutores por HTTP; publica el evento.

**Consume (bus → Campaigns):**
- `campaign.dispatch.result.v1` de cada ejecutor (Notification/Sms/WhatsApp) → `ApplyDispatchResult` idempotente por `DispatchId`.
- `campaign.scheduler.run_due.v1` del Scheduler → `StartCampaignRun` idempotente por `occurrenceKey`.

El result **común por destinatario** generaliza el seam Notification↔Postmaster (`PostmasterEmailEvents.cs:37,104,120,137,151,169`): la correlación opaca (`CampaignId`/`DispatchId`) la devuelve el ejecutor intacta.

---

## 7. Ejemplo: crear multicanal + enviar ya

```http
POST /campaigns
Authorization: Bearer <jwt>
{ "name":"Aviso vencimiento IVA",
  "channels":["Email","Sms"],
  "senders":[{"channel":"Email","senderRef":"snd_email_01"},{"channel":"Sms","senderRef":"snd_sms_01"}],
  "content":[{"channel":"Email","scribeTemplateKey":"campaign.tax_due.v3","subject":"Tu declaración vence"},
             {"channel":"Sms","text":"Tu declaración de IVA vence pronto. Ingresa a tu portal."}],
  "audience":{"clients":["seg_9a.."],"lists":["lst_3f.."],"manual":[]},
  "sendMode":{"mode":"Immediate"} }
→ 201 { "id":"cmp_..","status":"Draft" }

POST /campaigns/cmp_../send-now
Idempotency-Key: a11e...
→ 202 { "runId":"run_..","status":"Materializing" }

GET /campaigns/runs/run_../recipients?state=Unknown
→ 200 { items:[ {recipientRef, channel:"Sms", dispatchState:"Unknown", reason:"result_timeout"} ], ... }
```

**Nota de contenido (fix #02/#18):** el `content[].text` del SMS y el `scribeTemplateKey` del Email se **congelan** como una **revisión inmutable** al crear el run; en el dispatch viajan como `ContentRef` + `Payload` tipado por canal (`SmsPayload.TemplateRef` resuelve exactamente ese texto). El consumer SMS reconstruye el mensaje sin adivinar campos; el ejemplo `content[].text` es la **entrada** de la API, no el shape del evento de dispatch (ese está en `Commands_And_Events.md §2`). Envío a **todos los canales seleccionados**: una persona en Email+Sms genera **2 unidades**.

---

## 8. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Permiso `campaigns.manage` ya existe (base del `[HasPermission]`) | `PermissionCatalog.cs:39,474-482` | VERIFIED | 96% |
| Result events con correlación opaca devuelta = modelo del contrato entrante | `PostmasterEmailEvents.cs:91-172` | VERIFIED | 97% |
| Ruta por gateway `/campaigns/{**catch-all}` (patrón de la flota) | `Gateway/appsettings.json` (catalog/sms) | VERIFIED | 90% |
| Endpoints REST de Campaigns (campañas/detalles/contactos/remitentes) | diseño (este doc) | NEW | 85% |
| Wallet/estimación diferido (sin endpoints de costo esta fase) | decisión del usuario 2026-09-16 | DECISION | 99% |
