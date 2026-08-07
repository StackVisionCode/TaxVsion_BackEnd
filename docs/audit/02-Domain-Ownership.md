# Bounded contexts y ownership

| Concepto | Dueño actual demostrado | Observación |
|---|---|---|
| Tenant | Tenant | Auth mantiene identidad/dominios y participa en provisioning. |
| Onboarding | Auth | Correcto como proceso, pero orquesta efectos de cuatro dominios. |
| User/Identity/OTP | Auth | Notification entrega mensajes, no debe decidir verificación. |
| Plan/Subscription | Subscription | Auth y PaymentApp consultan precio; Billing recibe snapshot. |
| Invoice/sequence/adjustments | Billing | Correcto; Documents solo renderiza. |
| SaaS Payment/refund/attempt | PaymentApp | PaymentClient cubre pagos de clientes finales, contexto distinto. |
| Code/Promotion/Gift/Redemption | Growth | Auth solo guarda referencias/snapshots de reservas. |
| Referral attribution/qualification | Growth | Separado de discount redemption en el modelo. |
| Document binary/metadata | Documents/CloudStorage según flujo | Necesita contrato claro; Billing no debe poseer binario. |
| Notification intent/preferences/log | Notification | Postmaster posee transporte/sending. |
| Ledger | Ningún dueño completo | **ARCH-002 HIGH**: invoice, payment y redemption forman trazabilidad distribuida sin journal común. |

## Violaciones y tensiones

### ARCH-001 — pre-tenant bajo PlatformTenant

**Severidad MEDIUM, prioridad P1, complejidad Large.** Auth usa `PlatformTenant.Id` para Growth, PaymentApp y Billing; `OnboardingId` se modela como subject Anonymous/payment source. No es `TenantId = OnboardingId`, lo cual es positivo, pero hospedar activos financieros temporales bajo un tenant plataforma obliga a rehome y filtros cross-tenant. Archivos: `GrowthOnboardingClient`, `OnboardingInvoiceRequestedIntegrationEvent`, `OnboardingInvoiceRequestedConsumer`.

### ARCH-002 — ausencia de ledger unificado

**HIGH/P1/Large.** Billing conserva ajustes; PaymentApp el cobro; Growth la redención. Se puede reconstruir por IDs si todos los eventos llegan, pero no hay un registro append-only que garantice la equivalencia entre los tres.

No se encontró evidencia de que Auth modifique directamente tablas de Billing/Growth/PaymentApp; las interacciones son HTTP/eventos. Por ello no se declara una `CRITICAL ARCHITECTURAL VIOLATION` de escritura cross-service en estos flujos.

