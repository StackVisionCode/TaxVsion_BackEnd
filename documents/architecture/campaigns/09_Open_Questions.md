# Campaigns Suite — Preguntas Abiertas (Open Questions)

> **REVISIÓN 2026-09-16/17 (ADR-CAMP-001, APPROVED).** Las OQ de **dinero** (precio, refund, consumo, top-up) ya **NO bloquean nada de Campaign**: pertenecen a la capa **PEP + Wallet externa y DIFERIDA**. Quedan como preguntas de esa capa futura, marcadas abajo. Se añade **OQ-10** sobre dónde colocar el PEP.

Fecha: 2026-07-28 (original) · revisado 2026-09-17. Cada una: contexto, opciones, quién decide, y qué desbloquea.

## OQ-1 — Proveedor SMS (BLK-4)
**Contexto:** el ejecutor `TaxVision.Sms` (fase 2) necesita un gateway. El legado usó **Textmaxx**; el diseño nuevo no lo fija.
**Opciones:** Textmaxx (continuidad) · Twilio · Amazon SNS · otro regional.
**Impacto:** determina costeo por segmento, reintentos, y el shape del secreto cifrado.
**Decide:** negocio + plataforma. **Desbloquea:** Fase 7 (SMS).

## OQ-2 — Onboarding WhatsApp (BLK-4) — **premisa de precio corregida (#28)**
**Contexto:** `TaxVision.WhatsApp` usa Meta/WhatsApp Business Platform. **Corrección (#28):** el modelo vigente de Meta es **facturación por mensaje entregado** (con categorías/condiciones), **no** "por conversación" como decía la versión previa. Sigue exigiendo **plantillas pre-aprobadas** + onboarding WABA por tenant. Esto es premisa del **PEP/Wallet futuros**, no del dominio Campaigns (que no toca dinero) y no bloquea Email/SMS.
**Preguntas:** ¿el tenant trae su propia WABA o es centralizada? ¿quién aprueba plantillas? ¿cómo mapea el PEP/Wallet futuro el costo por-mensaje-entregado?
**Impacto:** afecta solo al adaptador de uso / Wallet futuro, no al modelo de Campaign.
**Decide:** negocio + producto. **Desbloquea:** Fase 7 (WhatsApp).

## OQ-3 — Precio por canal y moneda — **DIFERIDO (capa PEP/Wallet; NO bloquea Campaign)**
**Contexto:** el precio por mensaje es owner del **Wallet/PEP externos**, nunca de Campaign ni del frontend. Ya **no bloquea el MVP** (Campaign ejecuta sin dinero). La config del legado difiere de las notas de diseño de este proyecto:

| Canal | `appsettings.json` legado (E15) | Notas de diseño del proyecto |
|---|---|---|
| Email | `0.001` (`:138`) | 0.001 |
| SMS | `0.05` /segment (`:139`) | 0.015 |
| WhatsApp | `0.01` /msg (`:141`) | 0.005 |
| Push | (no cobrado) | 0 |

**Preguntas:** ¿qué valores rigen? ¿margen sobre costo de proveedor o precio fijo? ¿por segmento (SMS) o por mensaje? ¿configurable por tenant/plan?
**Impacto:** bloquea la **estimación de costo** → bloquea el `reserve` → bloquea Fase 4 (Email MVP).
**Decide:** negocio. **Desbloquea:** Fase 4 (al menos el precio Email).

## OQ-4 — Política de refund por no-entregado — **DIFERIDO (capa PEP/Wallet)**
**Contexto:** `06 §4` fija los casos claros (`Suppressed`/`Failed`/`ProviderNotConfigured` → refund). Los **`Bounced`** (soft/hard, posteriores al accept del proveedor) quedan abiertos.
**Preguntas:** si el proveedor cobra por intento aceptado, ¿se reembolsa un bounce posterior? ¿difiere por canal (SMS/WhatsApp cobran el intento; Email no)? ¿ventana de tiempo para reconciliar bounces tardíos vs cierre del run?
**Impacto:** define qué se consume vs devuelve al cierre; afecta I4/reconciliación.
**Decide:** negocio + finanzas. **Desbloquea:** cierre correcto de runs con bounce; obligatorio antes de Fase 7.

## OQ-5 — Scheduler: ¿servicio propio o módulo de Campaigns?
**Contexto:** ADR-CAMP-000 lo dejó explícito para `scheduler/ADR.md`.
**Opciones:** microservicio `TaxVision.Campaigns.Scheduler` independiente · módulo dentro de Campaigns.
**Trade-off:** aislamiento/escala del reloj vs simplicidad y menos coordinación.
**Decide:** arquitectura. **Desbloquea:** Fase 5 (detalle de deployment).

## OQ-6 — Consumo: batch al cierre vs incremental por-recipient — **DIFERIDO (capa PEP/Wallet)**
**Contexto:** `06 §3` default = consumir/reembolsar en **batch al cierre** del run.
**Trade-off:** batch = menos movimientos, más capital reservado durante runs largos; incremental = libera capital antes, más movimientos y más carga en Wallet.
**Decide:** arquitectura (con dato de volumen). **Desbloquea:** optimización post-MVP (no bloquea MVP).

## OQ-7 — Top-up: montos, mínimos y auto-recarga — **DIFERIDO (capa PEP/Wallet)**
**Contexto:** `SaaSPaymentType` de top-up necesita reglas de negocio.
**Preguntas:** ¿montos fijos o libres? ¿mínimo de recarga? ¿auto-recarga al bajar de un umbral? ¿reembolso de saldo no usado?
**Decide:** negocio. **Desbloquea:** UX de la capa de dinero futura (no bloquea Campaign).

## OQ-10 — ¿Dónde vive el PEP de autorización por saldo? (capa futura)
**Contexto:** D7 define un interceptor externo delante de la ejecución (trigger manual y cada `RunDue`). Falta decidir su ubicación física.
**Opciones:** (a) middleware en el **YARP gateway** sobre las rutas de trigger; (b) **delegating handler / interceptor** en el borde de Campaigns que llama al Wallet antes de aceptar el trigger (Campaign no lo implementa, lo envuelve); (c) un paso en el **Scheduler** sobre el `RunDue`. En todos, la lógica de dinero vive en el PEP/Wallet, no en Campaign.
**Trade-off:** (a) uniforme y central pero lejos del count real de destinatarios; (b) tiene el count a mano pero acopla el borde; (c) natural para recurrentes pero no cubre el trigger manual.
**Decide:** arquitectura. **Desbloquea:** la capa de dinero (diferida); no bloquea Campaign.

## OQ-8 — Reuso de audiencia de Customer: forma del criterio
**Contexto:** la audiencia se resuelve por ref a Customer (no snapshot). Falta fijar el **contrato del criterio** (segmento por query vs lista explícita de IDs vs manual) y consentimiento/opt-out por canal.
**Preguntas:** ¿Customer expone un endpoint de resolución de segmento M2M? ¿dónde vive el opt-out (supresión) por canal — Customer, ejecutor, o ambos?
**Decide:** arquitectura + legal (consentimiento). **Desbloquea:** Fase 3 (materialización de Recipients).

## OQ-9 — Retry por-attempt: política por canal
**Contexto:** un retry es un **nuevo attempt** (`03`/`06 §3`). Falta la política: cuántos attempts, backoff, y qué results son reintenables (transient vs permanente).
**Decide:** arquitectura por canal. **Desbloquea:** robustez de fan-out (Fase 4+).
