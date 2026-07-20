# TaxVision.Growth — Resumen ejecutivo

Fecha: 2026-07-19  
Estado final de diseño: **NOT_READY**

## Resultado

Se aprueba `TaxVision.Growth` como deployment inicial y `TaxVision_Growth` como base de datos inicial. Dentro del deployment existen dos bounded contexts, no uno:

- **Codes**: definición, reglas, scopes, quote, reserva, redemption y compensación promocional.
- **Referrals**: programa, código de referido, atribución, calificación, fraude y reward lifecycle.

Comparten API host, infraestructura, base y operación, pero no modelos de dominio ni foreign keys entre aggregates. La estructura objetivo es:

```text
src/Services/Growth/
├── TaxVision.Growth.Api
├── TaxVision.Growth.Infrastructure
├── TaxVision.Codes.Domain
├── TaxVision.Codes.Application
├── TaxVision.Referrals.Domain
└── TaxVision.Referrals.Application
```

## Evidencia y confianza

| Área | Resultado | Evidencia | Clasificación | Confianza |
|---|---|---|---|---:|
| Deployment modular | Growth único, extracción futura | Los diseños discrepan entre persistencia Codes duplicada y servicio propio; no hay implementación actual | PARTIAL | 90% |
| Separación Codes/Referrals | Dos bounded contexts | El CRM legado separó ReferralService, pero Payment mezcló cupones; los lenguajes e invariantes divergen | VERIFIED | 92% |
| Precio SaaS | Subscription | `src/Services/Subscription/TaxVision.Subscription.Domain/Plans/SubscriptionPlanVersion.cs`; `src/Services/Subscription/TaxVision.Subscription.Infrastructure/Persistence/SubscriptionPlanCatalogSeeder.cs` | VERIFIED | 98% |
| Dinero | minor units (`long`) + ISO currency | `src/Services/PaymentClient/TaxVision.PaymentClient.Domain/ValueObjects/Money.cs` | VERIFIED | 98% |
| Delivery | at-least-once + idempotencia + constraints + guards + outbox/inbox | `src/Services/PaymentApp/TaxVision.PaymentApp.Api/Program.cs:107`; `src/Services/PaymentClient/TaxVision.PaymentClient.Api/Program.cs:130`; `src/Services/Subscription/TaxVision.Subscription.Api/Program.cs:86` | VERIFIED | 92% |
| Precio tenant-cliente | Falta autoridad aprobada | PaymentClient acepta `AmountCents`; Catalog solo está diseñado | PARTIAL | 95% |
| M2M | Audience existe; scopes/client credentials Growth no están aprobados | `src/BuildingBlocks/BuildingBlocks.Web/Security/JwtAuthenticationRegistration.cs`; documentos externos | PARTIAL | 85% |

## DESIGN_BLOCKER

1. **GDR-007** — autoridad server-side del precio tenant-cliente y contrato `OfferSnapshot`.
2. **GDR-008** — contrato M2M real de Auth: grant, audience, scopes y rotación.
3. **GDR-009** — matriz comercial exacta de compensación por beneficio/refund/chargeback.
4. **GDR-010** — reglas finales del programa tenant-to-tenant: ventana, espera, máximos y antifraude.
5. **GDR-011** — clasificación, retención y pseudonimización de PII para taxpayer-to-taxpayer.

Mientras exista cualquiera, no se autoriza scaffolding.

## Alcance MVP

Incluye porcentaje/fijo, scopes, límites, quote/reserve/commit/cancel/reconcile, compensación de refunds, tenant-to-tenant y rewards no monetarios. Excluye wallet, TaxCoin, cash, gift con saldo, payout y taxpayer-to-taxpayer productivo.

## Riesgos actuales

- Los handlers de webhook Payment ignoran `Result` de transiciones y pueden marcar `Applied` una transición rechazada.
- El dedupe webhook usa check-then-insert y requiere tratar el unique conflict como replay.
- Payment no contiene `PromotionSnapshotId` ni `ReservationId`.
- No existe contrato de compensación hacia Growth.
- El bypass de `PlatformAdmin` observado en `ClaimsPrincipalExtensions` contradice el modelo de permiso explícito deseado para Growth.

## Fuentes no disponibles

Los cinco informes `Referrals_Codes_*.md` exigidos por el encargo no estaban presentes ni en la raíz ni bajo `C:/Users/wagne/OneDrive/Documentos/TaxVision/` durante esta ejecución. Su ausencia se registra como `NOT_IMPLEMENTED` documental; no se simuló su lectura.
