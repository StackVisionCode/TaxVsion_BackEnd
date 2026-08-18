# Campaigns Suite — Preguntas Abiertas (Open Questions)

Fecha: 2026-07-28. Decisiones pendientes que bloquean fases concretas. Cada una: contexto, opciones, quién decide, y fase que desbloquea. `BLK-x` refiere a `01_Executive_Summary §5`.

## OQ-1 — Proveedor SMS (BLK-4)
**Contexto:** el ejecutor `TaxVision.Sms` (fase 2) necesita un gateway. El legado usó **Textmaxx**; el diseño nuevo no lo fija.
**Opciones:** Textmaxx (continuidad) · Twilio · Amazon SNS · otro regional.
**Impacto:** determina costeo por segmento, reintentos, y el shape del secreto cifrado.
**Decide:** negocio + plataforma. **Desbloquea:** Fase 7 (SMS).

## OQ-2 — Costeo y onboarding WhatsApp (BLK-4)
**Contexto:** `TaxVision.WhatsApp` usa Meta/WhatsApp Business API, que **factura por conversación** (no por mensaje) y exige **plantillas pre-aprobadas** + onboarding WABA por tenant.
**Preguntas:** ¿el tenant trae su propia cuenta WABA o es centralizada? ¿cómo se mapea "por conversación" al modelo por-mensaje del Ledger? ¿quién aprueba plantillas?
**Impacto:** cambia el modelo de precio y posiblemente la unidad de reserva.
**Decide:** negocio + producto. **Desbloquea:** Fase 7 (WhatsApp).

## OQ-3 — Precio por canal y moneda (BLK-3) — bloquea MVP
**Contexto:** el precio por mensaje es owner de Campaigns/Wallet (no frontend), pero **no hay valor con autoridad**. La config del legado difiere de las notas de diseño de este proyecto:

| Canal | `appsettings.json` legado (E15) | Notas de diseño del proyecto |
|---|---|---|
| Email | `0.001` (`:138`) | 0.001 |
| SMS | `0.05` /segment (`:139`) | 0.015 |
| WhatsApp | `0.01` /msg (`:141`) | 0.005 |
| Push | (no cobrado) | 0 |

**Preguntas:** ¿qué valores rigen? ¿margen sobre costo de proveedor o precio fijo? ¿por segmento (SMS) o por mensaje? ¿configurable por tenant/plan?
**Impacto:** bloquea la **estimación de costo** → bloquea el `reserve` → bloquea Fase 4 (Email MVP).
**Decide:** negocio. **Desbloquea:** Fase 4 (al menos el precio Email).

## OQ-4 — Política de refund por no-entregado (BLK-5)
**Contexto:** `06 §4` fija los casos claros (`Suppressed`/`Failed`/`ProviderNotConfigured` → refund). Los **`Bounced`** (soft/hard, posteriores al accept del proveedor) quedan abiertos.
**Preguntas:** si el proveedor cobra por intento aceptado, ¿se reembolsa un bounce posterior? ¿difiere por canal (SMS/WhatsApp cobran el intento; Email no)? ¿ventana de tiempo para reconciliar bounces tardíos vs cierre del run?
**Impacto:** define qué se consume vs devuelve al cierre; afecta I4/reconciliación.
**Decide:** negocio + finanzas. **Desbloquea:** cierre correcto de runs con bounce; obligatorio antes de Fase 7.

## OQ-5 — Scheduler: ¿servicio propio o módulo de Campaigns?
**Contexto:** ADR-CAMP-000 lo dejó explícito para `scheduler/ADR.md`.
**Opciones:** microservicio `TaxVision.Campaigns.Scheduler` independiente · módulo dentro de Campaigns.
**Trade-off:** aislamiento/escala del reloj vs simplicidad y menos coordinación.
**Decide:** arquitectura. **Desbloquea:** Fase 5 (detalle de deployment).

## OQ-6 — Consumo: batch al cierre vs incremental por-recipient
**Contexto:** `06 §3` default = consumir/reembolsar en **batch al cierre** del run.
**Trade-off:** batch = menos movimientos, más capital reservado durante runs largos; incremental = libera capital antes, más movimientos y más carga en Wallet.
**Decide:** arquitectura (con dato de volumen). **Desbloquea:** optimización post-MVP (no bloquea MVP).

## OQ-7 — Top-up: montos, mínimos y auto-recarga
**Contexto:** `SaaSPaymentType` de top-up (BLK-2) necesita reglas de negocio.
**Preguntas:** ¿montos fijos o libres? ¿mínimo de recarga? ¿auto-recarga al bajar de un umbral? ¿reembolso de saldo no usado?
**Decide:** negocio. **Desbloquea:** UX de Fase 2 (el crédito del Ledger no cambia).

## OQ-8 — Reuso de audiencia de Customer: forma del criterio
**Contexto:** la audiencia se resuelve por ref a Customer (no snapshot). Falta fijar el **contrato del criterio** (segmento por query vs lista explícita de IDs vs manual) y consentimiento/opt-out por canal.
**Preguntas:** ¿Customer expone un endpoint de resolución de segmento M2M? ¿dónde vive el opt-out (supresión) por canal — Customer, ejecutor, o ambos?
**Decide:** arquitectura + legal (consentimiento). **Desbloquea:** Fase 3 (materialización de Recipients).

## OQ-9 — Retry por-attempt: política por canal
**Contexto:** un retry es un **nuevo attempt** (`03`/`06 §3`). Falta la política: cuántos attempts, backoff, y qué results son reintenables (transient vs permanente).
**Decide:** arquitectura por canal. **Desbloquea:** robustez de fan-out (Fase 4+).
