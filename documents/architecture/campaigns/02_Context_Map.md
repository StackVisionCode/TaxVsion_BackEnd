# Campaigns Suite — Context Map

Fecha: 2026-07-28. Relaciones entre bounded contexts y servicios de la suite y con el resto del monorepo. `VERIFIED` = existe hoy; `NEW` = a construir; `REUSE` = servicio existente que se integra.

## Diagrama de contextos

```
                                 ┌───────────────────────────┐
        module.campaigns (gate)  │      SUBSCRIPTION          │  entitlements (VERIFIED)
        ────────────────────────►│  (owner de plan/precio)    │
                                 └───────────────────────────┘
   ┌──────────────┐  top-up charge   ┌───────────────┐  credit-on-paid   ┌───────────────────┐
   │  PaymentApp  │◄─────────────────│   CAMPAIGNS/UI │──────────────────►│  WALLET / LEDGER  │
   │ (SaaSPayment)│  (VERIFIED+new   │   (NEW)        │  reserve/consume/ │  (NEW, indep.)    │
   └──────────────┘   SaaSPaymentType)│  creador/orq. │  refund (saga)    │  saldo inmutable  │
                                     └──────┬────────┘                   └───────────────────┘
                                            │ dispatch/result (contrato común por destinatario)
              ┌──────────────┬──────────────┼───────────────┬───────────────┐
              ▼              ▼              ▼               ▼               ▼
      ┌────────────┐ ┌────────────┐ ┌────────────┐  ┌────────────┐  ┌────────────┐
      │ EMAIL      │ │ SMS        │ │ WHATSAPP   │  │ PUSH        │  │ IN-APP     │
      │ SMTP2GO    │ │ (NEW)      │ │ (NEW)      │  │ =Notification│ │ =Communication│
      │ (NEW)      │ │            │ │ Meta/WABA  │  │  (REUSE)    │  │  (REUSE)   │
      └─────┬──────┘ └─────┬──────┘ └─────┬──────┘  └────────────┘  └────────────┘
            │ render        │              │
            ▼               ▼              ▼
      ┌────────────┐   proveedores externos (SMTP2GO / SMS gw / WhatsApp Business)
      │  SCRIBE    │ (REUSE, Fluid render)
      └────────────┘
   Audiencia: CUSTOMER (REUSE) — resolución de segmentos/contactos.  Assets: CLOUDSTORAGE (REUSE).
```

## Relaciones (X→Y = X depende de / llama a Y)

| Relación | Tipo | Estado | Notas |
|---|---|---|---|
| Campaigns → Subscription | consulta entitlement `module.campaigns` | REUSE (VERIFIED) | gate de uso; no es el balance |
| Campaigns → Wallet | reserve → consume/refund (M2M + saga) | NEW | Campaigns nunca muta saldo; solo pide movimientos |
| Wallet → PaymentApp | acredita saldo al recibir `SaaS payment succeeded` (top-up) | NEW | nuevo `SaaSPaymentType` para top-up |
| Campaigns → Customer | resolver audiencia (segmento/lista/manual) | REUSE | no copiar contactos como snapshot stale (anti-patrón legado) |
| Campaigns → Scheduler | agenda/dispara runs | NEW | lease atómico |
| Campaigns → {Email,SMS,WhatsApp,Push,In-app} | dispatch por destinatario (evento) | NEW contrato | ejecutores reportan result |
| Email(SMTP2GO)/SMS/WhatsApp/Push → Scribe | render del cuerpo (Fluid) | REUSE | ejecutor no re-renderiza si el cuerpo ya viaja |
| Ejecutores → Wallet | (envío individual, ej. SMS suelto) reserve/consume | NEW | Wallet reutilizable fuera de Campaigns |
| Ejecutores → CloudStorage | assets (logos/adjuntos) por referencia | REUSE | nunca bytes por el bus |
| Push → Notification (FcmPushSender) / In-app → Communication | entrega | REUSE | se agrega contrato bulk/campaña |

## Fronteras (qué NO cruza)

- **Postmaster NO se usa para campañas** (exclusivo de la app principal). Email de campañas = SMTP2GO nuevo.
- **Solo Wallet muta saldo** (movimientos inmutables). Campaigns/SMS/ejecutores jamás editan el balance.
- **Sin FK entre contexts** (Campaigns ↔ Wallet ↔ ejecutores se refieren por IDs opacos + eventos, como Growth Codes↔Referrals).
- Campaigns **no** integra proveedores ni tiene secretos de proveedor; eso vive en cada ejecutor de canal (cifrado).
- Subscription posee precio de plan; **Wallet/Campaigns** posee el **precio por mensaje/canal** (no el frontend).

## Correlación existente reutilizada

El sistema nuevo ya propaga `CampaignId` end-to-end en el pipeline de email (`Notification → Postmaster → result events`, `PostmasterEmailEvents.cs`), sin que el transporte lo interprete. Ese patrón (definidor pone `CampaignId`, ejecutor lo devuelve intacto) es el modelo del contrato dispatch/result de esta suite — generalizado a los 5 canales.
