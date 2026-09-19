# Campaigns Suite — Context Map

> **REVISIÓN 2026-09-16/17 (ADR-CAMP-001, APPROVED).** Campaign es un **orquestador agnóstico de canal SIN dinero**; los canales son **consumers dentro de servicios existentes**; el **dinero (PEP de autorización + Wallet) es externo y DIFERIDO**. El diagrama y las relaciones ya reflejan esto.

Fecha: 2026-07-28 (original) · revisado 2026-09-17. Relaciones entre bounded contexts. `VERIFIED` = existe hoy; `NEW` = a construir; `REUSE` = servicio existente que se integra.

## Diagrama de contextos

```
        ┌───────────────────────────┐
        │       SUBSCRIPTION        │  entitlement module.campaigns (VERIFIED)
        │  (owner plan/entitlement) │◄───────── gate (¿tiene la feature?) ────────┐
        └───────────────────────────┘                                            │
                                                                                  │
        ┌───────────────────────────┐  resuelve audiencia (Clients)    ┌──────────┴───────┐
        │        CUSTOMER           │◄─────────────────────────────────│   CAMPAIGNS      │
        │  (owner datos de contacto)│                                  │   (NEW)          │
        └───────────────────────────┘                                  │  orquestador     │
                                                                       │  agnóstico       │
        ┌───────────────────────────┐  disparo temporal (lease)        │  SIN dinero      │
        │   SCHEDULER (NEW/módulo)  │─────────RunDue──────────────────►│  no envía        │
        └───────────────────────────┘                                  └───────┬──────────┘
                                                                                │ campaign.dispatch.requested.v1
                             (contrato común dispatch/result, por destinatario) │  ▲ campaign.dispatch.result.v1
              ┌──────────────┬──────────────┬───────────────┬───────────────┐  │  │
              ▼              ▼              ▼               ▼               ▼  ▼  │
      ┌────────────┐ ┌────────────┐ ┌────────────┐  ┌────────────┐  ┌────────────┐
      │ EMAIL      │ │ SMS        │ │ WHATSAPP   │  │ PUSH        │  │ IN-APP     │
      │ =consumer  │ │ =consumer  │ │ =TaxVision │  │ =consumer   │  │ =Communica-│
      │ en         │ │ en         │ │ .WhatsApp  │  │ en          │  │ tion       │
      │ Notification│ │ TaxVision  │ │ (NEW, fase │  │ Notification│  │ (REUSE)    │
      │ (REUSE)    │ │ .Sms(REUSE)│ │ posterior) │  │ +bulk(REUSE)│  └────────────┘
      └─────┬──────┘ └─────┬──────┘ └─────┬──────┘  └────────────┘
            │ render(Scribe)│              │        Assets: CLOUDSTORAGE (REUSE, por referencia)
            ▼               ▼              ▼        Audiencia: CUSTOMER (REUSE)
        proveedores externos (SMTP / SMS gw / WhatsApp Business) — secretos EN el ejecutor

  ┌───────────────────────────────────────────────────────────────────────────────┐
  │  DINERO — EXTERNO y DIFERIDO (no toca a Campaign):                             │
  │   [PEP de autorización de ejecución] delante del trigger/RunDue ── consulta ──►│
  │   [WALLET/LEDGER] (saldo USD, movimientos inmutables) ◄── top-up ── PaymentApp │
  │  El PEP calcula recipientCount(+recurrencia), cobra/reserva ANTES, veta si no  │
  │  alcanza. Campaign solo recibe triggers ya autorizados. Hoy: seam ABIERTO.     │
  └───────────────────────────────────────────────────────────────────────────────┘
```

## Relaciones (X→Y = X depende de / llama a Y)

| Relación | Tipo | Estado | Notas |
|---|---|---|---|
| Campaigns → Subscription | consulta entitlement `module.campaigns` | REUSE (VERIFIED) | gate de feature; **no** es dinero |
| Campaigns → Customer | resolver audiencia (Clients/segmento/lista) | REUSE | no copiar contactos como snapshot stale |
| Scheduler → Campaigns | `RunDue` (disparo agendado/recurrente) | NEW | lease atómico; un run por disparo |
| Campaigns → {Email,SMS,WhatsApp,Push,In-app} | `campaign.dispatch.requested.v1` por destinatario | NEW contrato | cada canal es un **consumer**; responde `...result.v1` |
| Email(consumer) → Notification / SMS(consumer) → TaxVision.Sms | entrega reusando el servicio existente | REUSE | Email reusa `SendEmailCommand`; SMS ya M2M |
| Push(consumer) → Notification (FcmPushSender) / In-app → Communication | entrega | REUSE | se agrega contrato bulk |
| Ejecutores → Scribe | render del cuerpo (Fluid) | REUSE | ejecutor no re-renderiza si el cuerpo ya viaja |
| Ejecutores → CloudStorage | assets por referencia | REUSE | nunca bytes por el bus |
| **PEP externo → Wallet** | autoriza/cobra por saldo antes de ejecutar | **NEW, DIFERIDO** | fuera de Campaign; intercepta el trigger/RunDue |
| **Wallet → PaymentApp** | top-up (nuevo `SaaSPaymentType`) | **NEW, DIFERIDO** | acredita al recibir payment-succeeded |

## Fronteras (qué NO cruza)

- **Campaign no toca dinero.** Ni saldo, ni monto, ni cobro, ni llamadas a un Wallet. El dinero vive en el **PEP externo + Wallet** (diferidos).
- **Los canales no son servicios dedicados nuevos** (salvo WhatsApp): son consumers dentro de `Notification` / `TaxVision.Sms` existentes.
- **Postmaster NO se usa para campañas** (exclusivo de la app principal).
- **Sin FK entre contexts:** IDs opacos + eventos (como Growth Codes↔Referrals).
- Campaigns **no** integra proveedores ni guarda secretos: eso vive en cada ejecutor (cifrado). El `SenderRef` que Campaign referencia es un id opaco, no un secreto.

## Correlación existente reutilizada

El sistema ya propaga `CampaignId` end-to-end en el pipeline de email (`Notification → Postmaster → result events`, `PostmasterEmailEvents.cs:37,104`) sin que el transporte lo interprete. Ese patrón (el definidor pone el id, el ejecutor lo devuelve intacto) es el modelo del contrato dispatch/result — generalizado a todos los canales con `dispatch_id`.
