# WhatsApp — State Machines

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

## 1. `WhatsAppMessage` (por destinatario)

Estado del intento de entrega. Transiciones **solo** por métodos del aggregate que devuelven `Result`; cada avance está guardado por `RowVersion` (optimistic lock, ver `Concurrency_Spec.md`). Los estados post-envío llegan **asíncronos por webhook** de Meta.

```
                 (dispatch recibido, validado, reserva Wallet OK)
   [Pending] ──────────────────────────────────────────────► [Accepted]
        │                                                          │
        │ (validación falla: sin plantilla, sesión cerrada,        │ POST Cloud API
        │  número inválido, sin reserva)                           │
        ▼                                                          ▼
   [Rejected] (terminal, sin costo)              (Meta 200 + wamid)│  (Meta 4xx/5xx)
                                                                   ▼        │
                                                              [Sent] ◄──────┘ (error) ► [Failed]
                                                                   │                       (terminal)
                                        webhook status=delivered   ▼
                                                              [Delivered]
                                                                   │ webhook status=read
                                                                   ▼
                                                              [Read] (terminal-éxito)
                                                                   
        cualquier estado no-terminal + webhook status=failed ─────► [Failed]
```

### Estados

| Estado | Significado | Terminal | Efecto Wallet |
|---|---|---|---|
| `Pending` | Dispatch aceptado, aún no validado/enviado | No | reserva ya tomada por Campaigns (o por este servicio en envío individual) |
| `Accepted` | Validado (plantilla aprobada + sesión/HSM OK + número E.164 + reserva); listo para POST | No | — |
| `Sent` | Meta aceptó (`wamid` asignado) | No | — (aún no consume: el costo real llega por webhook) |
| `Delivered` | Webhook `delivered` | No | candidato a **consume** (entrega confirmada) |
| `Read` | Webhook `read` | Sí | — (ya consumido en delivered) |
| `Failed` | Meta rechazó el POST **o** webhook `failed` | Sí | **refund** de la reserva |
| `Rejected` | Validación local falló antes de tocar Meta | Sí | **refund** (nunca se gastó) |

### Reglas de transición (invariantes)
- `Pending → Accepted` exige: plantilla `Approved` (o sesión abierta para free-form), `Category` resuelta, número E.164 válido, reserva Wallet vigente.
- `Accepted → Sent` **solo** tras respuesta 200 de Meta con `wamid`. El `wamid` se persiste de forma idempotente (un POST reintentado no crea dos `Sent`).
- `Sent → Delivered → Read` monotónico; un webhook fuera de orden **no** retrocede el estado (guard: solo avanza). Un `read` que llega antes que `delivered` (posible) promueve a `Read` y marca delivered implícito.
- Cualquier `*_failed` webhook en estado no-terminal ⇒ `Failed`. Un `failed` que llega tras `Delivered`/`Read` se **ignora** (ya entregado; solo se loguea; no doble-refund).
- **Punto de consumo del costo**: se dispara `WhatsAppMessageBilled` en `Delivered` (o en `Sent` si la política del tenant es "cobra al enviar" — decisión en ADR-WA-004). El `refund` se dispara en `Failed`/`Rejected`. Nunca ambos (idempotencia por `DispatchId`).

## 2. Ventana de sesión (`SessionWindow`)

```
   (inbound del usuario, webhook messages)
        │
        ▼
   [Open] ── (now > OpenedAt+24h) ──► [Expired]
        ▲                                  │
        └──── (nuevo inbound) ─────────────┘  (reabre; nueva ventana 24h)
```

- Regla de admisión de un dispatch:
  - `Open` → free-form **o** plantilla permitidos.
  - `Expired` / inexistente → **solo plantilla aprobada**; free-form ⇒ `Rejected` (`FailureCode = SESSION_CLOSED`).
- La ventana es una **proyección derivada** de webhooks inbound; no la mutan comandos de envío.

## 3. `WhatsAppTemplate` (catálogo local)

```
   [Pending] ─ webhook approved ─► [Approved] ─ paused ─► [Paused] ─ resumed ─► [Approved]
        │                              │
        │ rejected                     │ disabled/deleted
        ▼                              ▼
   [Rejected] (terminal)          [Disabled] (terminal)
```

- Solo `Approved` es enviable. Un dispatch que referencia una plantilla no-`Approved` ⇒ `WhatsAppMessage.Rejected` (`FailureCode = TEMPLATE_NOT_APPROVED`).
- Estado sincronizado desde Meta (Graph API pull periódico + webhook `message_template_status_update`). Meta es la fuente de verdad; el catálogo local es índice de validación.

## 4. Comparación con el legado (anti-patrones corregidos)

| Aspecto | Legado (`CampaignService`) | Diseño nuevo |
|---|---|---|
| Estado de envío | `RecipientStatus` sobre `CampaignRecipient` con setters sueltos; `CampaignStatus` incluye `Sending` no-atómico (`CampaignStatus.cs:6`) | máquina por mensaje + guards + `RowVersion`; sin estado global no-atómico |
| Confirmación de entrega | inexistente (simulado, `Task.Delay`, `WhatsAppCampaignSender.cs:78`) | webhooks reales `sent/delivered/read/failed` |
| Sesión 24h / HSM | ausente | invariante de admisión |
| Costo | plano al "enviar" simulado | por conversación/categoría en `Delivered`, desde webhook `pricing` |

## 5. Evidencia

| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado sin estados de entrega reales | `WhatsAppCampaignSender.cs:77-101` | VERIFIED | 97% |
| Legado marca estado global `Sending` | `CampaignStatus.cs:6` | VERIFIED | 96% |
| Estados/orden de webhook (sent/delivered/read/failed) | Meta Cloud API docs | DOCUMENTED_ONLY | 88% |
| Ventana 24h + HSM obligatorio fuera de sesión | Meta docs | DOCUMENTED_ONLY | 88% |
