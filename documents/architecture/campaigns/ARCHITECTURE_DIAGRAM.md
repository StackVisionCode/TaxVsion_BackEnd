# Campaigns Suite — Diagrama de Arquitectura (estado objetivo)

> Refleja ADR-CAMP-001 (APPROVED, 2026-09-16/17): **Campaign = orquestador agnóstico de canal, SIN dinero**; canales = **consumers en servicios existentes**; **dinero = interceptor/PEP + Wallet externos y DIFERIDOS**.

## Vista de bloques

```mermaid
flowchart TB
  U["👤 Staff (TenantAdmin/Employee)"]

  subgraph ACCESS["Acceso — YA cableado"]
    P["Permiso campaigns.manage<br/>(PermissionCatalog)"]
    E["Entitlement module.campaigns<br/>(Subscription)"]
  end

  subgraph CAMP["TaxVision.Campaigns — ORQUESTADOR agnóstico · SIN dinero · no envía"]
    A1["Campaign (definición, Draft)"]
    A2["CampaignRun (inmutable)"]
    A3["Recipients"]
    A4["Contact / ContactList propias<br/>(no-clientes, opt-out)"]
    A5["SenderProfile (remitente x canal)"]
    A6["Details / Reporting"]
  end

  SCH["⏱ Scheduler<br/>lease atómico<br/>Immediate/Scheduled/Recurring"]
  CUS["Customer<br/>(audiencia: Clients)"]

  U --> P --> E --> CAMP
  SCH -- "RunDue" --> CAMP
  CAMP -- "resuelve audiencia (ref)" --> CUS

  BUS{{"Contrato común dispatch/result<br/>campaign.dispatch.requested/result.v1<br/>(por destinatario, dispatch_id opaco)"}}
  CAMP == "dispatch.requested (fan-out)" ==> BUS
  BUS -. "dispatch.result (Delivered/Failed/Skipped)" .-> CAMP

  BUS --> EM["✉ Email = consumer EN Notification<br/>(SendEmailCommand)"]
  BUS --> SM["💬 SMS = consumer EN TaxVision.Sms<br/>(ya M2M)"]
  BUS --> PU["🔔 Push = consumer EN Notification<br/>(FcmPushSender + bulk)"]
  BUS --> WA["🟢 WhatsApp = TaxVision.WhatsApp<br/>(NUEVO, fase posterior)"]
  BUS --> IN["📲 In-app = Communication"]

  EM --> SMTP[("SMTP / SMTP2GO")]
  SM --> SGW[("SMS gateway")]
  PU --> FCM[("FCM")]
  WA --> WABA[("WhatsApp Business")]
  SCR["Scribe (render Fluid)"] --- EM
  CST[("CloudStorage (assets x ref)")] --- EM

  subgraph MONEY["💲 DINERO — EXTERNO y DIFERIDO · Campaign NO lo toca"]
    PEP["Interceptor / PEP<br/>autoriza por saldo ANTES de ejecutar<br/>(calcula #destinatarios + recurrencia,<br/>cobra/reserva, VETA si no alcanza)"]
    WAL["Wallet / Ledger<br/>saldo USD · movimientos inmutables"]
    PAY["PaymentApp (top-up)"]
    PEP -- "consulta/cobra" --> WAL
    PAY -- "credit-on-paid" --> WAL
  end

  U -. "trigger (interceptado)" .-> PEP
  SCH -. "RunDue (interceptado)" .-> PEP
  PEP -. "sólo triggers AUTORIZADOS" .-> CAMP

  classDef deferred stroke-dasharray:6 5,fill:#fff7ed,stroke:#c2681c,color:#7c3d06;
  classDef reuse fill:#eef6ff,stroke:#2f6db3,color:#123;
  classDef core fill:#eafbf0,stroke:#1f9d55,color:#093;
  class MONEY,PEP,WAL,PAY,WA deferred;
  class EM,SM,PU,IN,SCR,CST,CUS reuse;
  class CAMP,A1,A2,A3,A4,A5,A6 core;
```

## Leyenda

- **Verde (core):** el servicio nuevo `TaxVision.Campaigns` — solo orquesta; sin dinero.
- **Azul (reuse):** servicios existentes que hospedan el consumer del canal (Notification, TaxVision.Sms, Communication) o colaboran (Customer, Scribe, CloudStorage).
- **Naranja punteado (diferido):** lo que **no** entra ahora — WhatsApp (nuevo, fase posterior) y toda la capa de dinero (PEP + Wallet + top-up), que vive **fuera** de Campaign e intercepta el trigger/RunDue.

## Flujo de una ejecución (hoy, sin dinero)

```mermaid
sequenceDiagram
  participant U as Staff / Scheduler
  participant C as Campaigns (orquestador)
  participant B as Bus (dispatch/result)
  participant X as Consumer de canal<br/>(Notification / TaxVision.Sms)
  participant P as Proveedor

  U->>C: trigger / RunDue (campaigns.manage + module.campaigns enforce)
  C->>C: crea CampaignRun (+DispatchRun durable), materializa UNIDADES (destinatario/canal) por páginas
  loop por unidad (destinatario × canal)
    C->>B: campaign.dispatch.requested.v1 (dispatch_id por intento, ContentRef)
    B->>X: consume (ActorType.Service)
    X->>P: entrega (render Scribe / secreto del proveedor)
    X-->>B: campaign.dispatch.result.v1 (Accepted/Delivered/Failed/Skipped)
    B-->>C: aplica result (idempotente por dispatch_id)
  end
  C->>C: timeouts → Unknown (reconciliable); cierra por total congelado → Completed/PartiallyFailed/Failed
  Note over C: Sin reserva/consumo/cobro. El dinero (si entra)<br/>lo intercepta un PEP externo ANTES del trigger.
```
