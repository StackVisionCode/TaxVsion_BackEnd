# Arquitectura recomendada

Esta sección es prescriptiva; se formula después del comportamiento actual documentado en 03/05.

## Modelo objetivo

```text
CommercialOnboardingSaga (Auth o servicio dedicado)
  QuoteAuthorized(snapshot firmado)
    → BenefitsReservedAtomically
    → PaymentRequired ? PaymentSucceeded : NonMonetarySettled
    → BenefitsCommitted
    → InvoiceCreated
    → ProvisioningStarted
    → Completed
```

Growth debe aceptar un stack y producir una asignación determinista por instrumento en una transacción. PaymentApp debe cobrar solo un `AuthorizedQuoteId` verificable. Billing debe materializar invoice/ledger y responder con `InvoiceCreated`; el PDF es asíncrono y no bloquea negocio. Un reconciler compara onboarding, reservations, payment e invoice.

## Pre-tenant

Adoptar `SubjectType = Prospect|Tenant|User|Anonymous` y `SubjectId`; para onboarding usar `Prospect/OnboardingId`. Separar el owner legal/operativo del tenant de aislamiento. Evitar rehome destructivo: mantener subject original y añadir TenantId cuando nazca.

## Ledger

Journal append-only por operación: plan charge +100, promo −20, gift −60, cash settlement −20, luego refunds/compensations. Invoice es proyección inmutable/versionada; nunca mutar total sin adjustment.

