# Campaigns Suite — Alcance del MVP

Fecha: 2026-07-28. Define qué entra al primer entregable ejecutable end-to-end y qué se difiere. Regla rectora: **el MVP prueba el ciclo completo de dinero real (top-up → reserve → dispatch → consume/refund) sobre UN canal**, no la amplitud de canales.

## 1. IN (MVP)

| # | Incluido | Por qué es imprescindible | Estado base |
|---|---|---|---|
| M1 | **Wallet/Ledger real (USD)** — movimientos inmutables `TopUp/Reservation/Consume/Refund/Adjustment`, saldo derivado, idempotencia `(operation,scopeId,key)`. | Sin saldo real no hay nada que reservar/consumir; es la **dependencia dura** (BLK-1). | NEW |
| M2 | **Top-up vía PaymentApp** — nuevo `SaaSPaymentType`, evento payment-succeeded → `TopUp` idempotente. | Única entrada de dinero; sin él no se puede cargar saldo para probar. | NEW glue (BLK-2) |
| M3 | **Campaigns (creador/orq.)** — `Campaign` + `CampaignRun` inmutable + `Recipients` + audiencia por ref (Customer) + estimación de costo + saga reserve/consume/refund. | Es el corazón; orquesta todo lo demás. | NEW |
| M4 | **Email SMTP2GO** (`TaxVision.Campaigns.Email`) — ejecutor del canal Email, render Scribe, result events. | Canal de menor costo/riesgo y sin proveedor por decidir; valida el contrato dispatch/result. | NEW |
| M5 | **Scheduler con lease atómico** — Immediate + Scheduled + Recurring, un solo ejecutor al escalar. | Sin él se repite el doble-scheduler del legado; Recurring exige `CampaignRun` por disparo. | NEW |
| M6 | **Reuse Push (Notification/FcmPushSender)** + **contrato bulk** | Demuestra el reuso y el mismo contrato dispatch/result sobre un canal ya existente, a bajo costo. | REUSE + glue (BLK-6) |
| M7 | **Gate `module.campaigns`** (consulta a Subscription) | Ortogonal al balance; ya sembrado (`SubscriptionPlanCatalogSeeder.cs:59,83`), solo hay que consultarlo. | REUSE (VERIFIED) |
| M8 | **Contrato dispatch/result común + correlación `CampaignId`** | Generaliza el seam ya probado en `PostmasterEmailEvents.cs:37`; base de todos los canales. | NEW contrato |

**Definición de "hecho" del MVP:** un tenant con `module.campaigns` carga saldo (top-up real), crea una Campaign Email, la agenda, el Scheduler dispara un `CampaignRun`, Wallet reserva, el ejecutor SMTP2GO entrega, y Wallet consume los entregados y devuelve los no-entregados — todo idempotente y resiliente a reinicio.

## 2. OUT / DIFERIDO

| Diferido | Fase | Motivo |
|---|---|---|
| **Canal SMS** (`TaxVision.Sms`) | Fase 2 | Proveedor sin decidir (OQ-1); costo por segmento y reintentos añaden complejidad no esencial al ciclo de dinero. |
| **Canal WhatsApp** (`TaxVision.WhatsApp`, Meta/WABA) | Fase 2 | Onboarding WABA + plantillas aprobadas + costeo por conversación sin definir (OQ-2). |
| **In-app (Communication)** como canal de campaña | Fase 2 | Reuso directo, pero no aporta al ciclo de dinero del MVP. |
| **A/B testing / variantes** | Post-MVP | Requiere segmentación de audiencia y stats comparadas; ortogonal al núcleo. |
| **Monedas virtuales / TaxCoin / créditos promocionales** | Post-MVP (si acaso) | Explícitamente reemplazado por USD real; no reintroducir el modelo TXC del legado. |
| **Segmentación avanzada / audiencias dinámicas complejas** | Post-MVP | MVP resuelve segmento/lista/manual simple por ref a Customer. |
| **Consumo incremental por-recipient** | Post-MVP | MVP consume/reembolsa en batch al cierre del run (ver `06` §3, OQ-6). |
| **Tracking de opens/clicks / webhooks de engagement** | Post-MVP | El MVP rastrea entrega (delivery), no engagement. |
| **Multi-provider por canal (fallback SMTP)** | Post-MVP | MVP: un proveedor por canal (SMTP2GO). |

## 3. Dependencia dura (orden no negociable)

```
   Wallet real (M1) ── debe existir ANTES de ──► cualquier ejecución de Campaigns (M3/M4)
         ▲                                              │
         └── Top-up PaymentApp (M2) ── carga saldo para probar el ciclo
```

**BLK-1:** ejecutar Campaigns sin Wallet repetiría el TOCTOU del legado (cobro no-atómico, `CreateCampaignCommandHandler.cs:278`). Por eso `08_Implementation_Plan.md` pone **Wallet en Fase 1**, antes que el ejecutor y antes que el fan-out.

## 4. Riesgos del MVP y mitigación

| Riesgo | Mitigación en MVP |
|---|---|
| Precio por canal indefinido (BLK-3/OQ-3) bloquea la estimación | Fijar precio Email por decisión de negocio antes de M3; configurable, owner Campaigns/Wallet (no frontend). |
| `SaaSPaymentType` de top-up inexistente (BLK-2) | Incluido explícitamente como M2; sin él no hay prueba end-to-end. |
| Contrato bulk de Push inexistente (BLK-6) | M6 es "reuse + glue"; si el contrato bulk se atrasa, Push sale del MVP sin afectar M1–M5. |
| Política de refund por bounce (BLK-5/OQ-4) | MVP Email: los casos claros (Suppressed/Failed/ProviderNotConfigured → refund) alcanzan; bounce se resuelve con la decisión de negocio antes de SMS/WhatsApp. |
