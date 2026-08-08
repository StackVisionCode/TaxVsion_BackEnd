# Flujos E2E y escenarios A–D

## Precondiciones comunes

`StartOnboardingCheckoutHandler.Handle` carga `TenantOnboarding`; opcionalmente `OnboardingCodeReserver.ReserveAsync` consulta precio autoritativo a Subscription y llama Growth quote/reserve secuencialmente. `ApplyOnboardingPricing` persiste gross/discount/net/currency y referencias. Sin códigos, esos valores dependen de cómo fue creado/preciado previamente el aggregate; PaymentApp vuelve a resolver el precio.

## A — plan 100, sin descuento

1. `BuildCodeInputs` devuelve vacío; no se llama Growth.
2. `FullyCovered` es falso.
3. Auth llama `PaymentAppOnboardingClient.CreateCheckoutAsync` con key estable `onboarding-checkout-{id}`, net posiblemente bruto/null.
4. `CreateOnboardingCheckoutHandler` consulta Subscription y crea Stripe Session por 100; luego persiste `SaaSPayment`.
5. Webhook exitoso publica `OnboardingPaymentSucceededIntegrationEvent`.
6. `OnboardingPaymentSucceededConsumer` completa el aggregate y encola finalize.
7. `OnboardingFinalizer` publica invoice request.
8. Billing crea invoice Paid subtotal 100, discount 0, total 100 y `PaymentId` real; Documents genera PDF.

**Resultado actual:** coincide con A en camino nominal. No existe test E2E de servicios reales que lo pruebe.

## B — plan 100, descuento 30

Growth calcula contra gross 100 y devuelve net 70. Auth guarda el ajuste y envía `NetAmountCents=7000`; PaymentApp acepta porque `0 < 7000 <= 10000` y Stripe cobra 70. Tras éxito, Billing valida `net=gross-discount` y que suma de ajustes=30; crea invoice Paid 100/30/70 vinculada al payment de 70.

**Riesgo PAY-002:** PaymentApp no recalcula el beneficio ni usa `Currency`, `DiscountAmountCents`, `CodeReservationId` o `PromotionSnapshotHash` para autorizar el override; un Auth comprometido puede elegir cualquier neto positivo menor al precio.

## C — promoción 100%

Growth devuelve residual 0. `onboarding.FullyCovered` lleva a `CompleteFullyCoveredAsync`: `MarkFullyCoveredByCode`, token/registration-ready/finalize y guardado; no se llama PaymentApp y no se crea `SaaSPayment`. La respuesta usa `PaymentId=Guid.Empty`, URL vacía y `FullyCovered=true`; internamente finalize lleva `PaymentId=null`. Billing crea invoice Paid, subtotal 100, descuento 100, total 0, settlement `FullyCoveredByCode`, payment null. La saga no espera `PaymentSucceeded` porque el mismo helper de éxito se invoca directamente.

**Resultado actual:** onboarding continúa. **REL-001:** puede continuar aunque Billing todavía no haya consumido/aceptado la invoice.

## D — gift 60, promotion 20, payment 20

`BuildCodeInputs` agrega Referral, Promo, Gift y `ReserveAsync` ordena por enum: Referral → Promo → Gift. Con Promo 20 y Gift 60: 100→80→20. Se guardan dos `OnboardingCodeReservation` y Billing recibe dos `OnboardingInvoiceAdjustmentDto`, preservando `Type`, `Code`, reservation ID y amount. PaymentApp cobra 20.

**Resultado actual:** aritmética 100−20−60=20. El orden real es promoción antes de gift, no Gift→Promotion. Billing persiste ambos componentes; que el PDF los “explique perfectamente” depende de `GenerateInvoicePdfHandler`/template de Documents y carece de test golden/contract.

## Secuencia posterior

El éxito comercial dispara registro, creación de owner, provisioning tenant, activación de subscription y login según saga Auth/Tenant. Fallos se reintentan por Wolverine/jobs, pero la invoice no es gating del onboarding.

