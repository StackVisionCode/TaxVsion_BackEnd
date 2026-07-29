# ADR-002 — Documents aloja el recibo de onboarding (`OnboardingReceipt`)

Estado: **APPROVED**
Fecha: 2026-07-29

## ID y contexto

**ID:** PFDR-002. Tras un pago exitoso de onboarding (Fase 9-11 del plan maestro), el comprador debe
recibir un recibo formal (PDF/HTML) por email, con un link de descarga estable. En ese momento **no
existe un tenant real todavía** — el pago llegó, pero el `TenantOnboarding` sigue en
`PaymentCompleted`/`RegistrationPending`, y el tenant recién se crea varios pasos después (Fase 15,
la Saga). Había que decidir dónde generar y almacenar ese documento: un `DocumentType` nuevo en
`TaxVision.Documents` (el servicio que ya genera PDFs para el resto del sistema), o un mecanismo
propio dentro de Auth/PaymentApp.

## Evidencia real

- `TaxVision.Documents` ya es el dueño de generación de documentos formales del sistema — otros
  `DocumentType` existentes siguen el mismo patrón (template + datos → PDF, entregado vía
  CloudStorage). No hay ningún otro servicio en el monorepo con esa responsabilidad.
- El recibo necesita datos del emisor (`Documents:PlatformIssuer` en `appsettings.json` — nombre
  legal, TaxId, dirección, logo) que son responsabilidad de facturación/legal, no de Auth ni de
  PaymentApp.
- El owner (`TenantEntity`) del documento generado no puede ser el tenant real (no existe aún) —
  Documents ya tenía que resolver este problema de todas formas, y lo resolvió registrando la
  generación bajo `PlatformTenant.Id` (nunca un tenant real) con un `GenerationOwner` cuyo
  `OwnerType` es la constante de string `"Onboarding"` (`OwnerTypeOnboarding` en
  `GenerateOnboardingReceiptDocumentHandler`/`ProcessOnboardingReceiptGenerationHandler`) — el mismo
  patrón de anclaje a `PlatformTenant.Id` que `TenantOnboarding`, `EmailVerificationChallenge` y
  `TermsVersion` usan del lado de Auth para aggregates pre-tenant.
- El flujo real implementado (Fase 11): Auth's `OnboardingPaymentSucceededConsumer` llama M2M a
  `POST internal/document-generations/onboarding-receipts` (Documents) de forma fire-and-forget con
  `Idempotency-Key`; Documents genera el PDF de forma asíncrona y responde con
  `DocumentGenerationCompletedIntegrationEvent`, que Auth consume para fijar el
  `ReceiptFileId` en el propio `TenantOnboarding` y publicar `OnboardingReceiptReadyIntegrationEvent`
  hacia Notification.

Clasificación: **VERIFIED** — código ya implementado en Fases 10-11; este ADR documenta el motivo a
posteriori, cerrando la deuda de documentación de Fase 19.

## Alternativas

1. **`TaxVision.Documents` con `DocumentType="OnboardingReceipt"` nuevo**, con el `GenerationOwner`
   marcado `OwnerType="Onboarding"` y registrado bajo `PlatformTenant.Id` en vez de un tenant real.
2. **Generar el PDF dentro de Auth mismo**, usando una librería de PDF embebida (como hace Signature
   para el sellado de documentos), sin pasar por Documents.
3. **Generar el PDF dentro de PaymentApp**, ya que es quien procesa el pago y tiene los montos/
   moneda a mano de primera mano.

## Opción seleccionada y motivo

Opción 1. Un recibo de compra es, por definición de dominio, un documento formal — exactamente lo
que `TaxVision.Documents` ya existe para producir. Duplicar generación de PDF en Auth (opción 2)
hubiera significado una segunda implementación de layout/branding/emisor legal divergiendo con el
tiempo de la de Documents. La opción 3 (PaymentApp) mezclaba dos responsabilidades sin relación —
PaymentApp sabe procesar pagos, no sabe (ni debería saber) nada sobre `PlatformIssuer`, plantillas de
documento o convenciones de nombrado de archivos legales.

El obstáculo real de esta decisión no era "quién genera el PDF" sino "cómo referencia Documents a un
dueño que todavía no es un tenant" — se resolvió reusando el mismo patrón de anclaje a
`PlatformTenant.Id` que Auth ya usaba para sus propios aggregates pre-tenant, en vez de inventar un
mecanismo nuevo específico de Documents.

## Consecuencias

Positivas:

- Una sola fuente de verdad para generación de documentos formales — el recibo de onboarding se
  beneficia de cualquier mejora futura al pipeline de Documents (branding, formatos, retención) sin
  trabajo adicional.
- El acoplamiento entre Auth y Documents es asíncrono y ya existía como patrón (M2M
  fire-and-forget + evento de vuelta) — no se introdujo un mecanismo de comunicación nuevo.
- El patrón `OwnerType="Onboarding"` + `PlatformTenant.Id` es reusable para cualquier documento
  futuro que necesite generarse antes de que exista un tenant (ej. un contrato pre-firma), sin
  volver a resolver el problema de "qué es el owner cuando no hay tenant".

Negativas:

- Documents ahora tiene una dependencia conceptual del concepto "onboarding" que en principio no le
  pertenece (Onboarding vive en Auth, ADR-001) — mitigado porque Documents solo conoce
  `OwnerType.Onboarding` como un enum value más, no importa ningún tipo del módulo Onboarding de
  Auth.
- El mediador de descarga (`GET onboarding/receipts/{fileId}/download` en Auth, §44.1 del README)
  añade un hop extra frente a enlazar directo a una URL de CloudStorage — se aceptó porque las URLs
  presignadas expiran en minutos y el link vive en un email que puede abrirse días después; el costo
  (una llamada HTTP más) es bajo comparado con el beneficio (el link nunca muere).

## Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Documents importa tipos del módulo Onboarding de Auth (violación del ADR-001) | No ocurre — Documents solo recibe un payload JSON plano (`OnboardingReceiptPayload`) vía M2M, sin ninguna referencia de ensamblado a Auth. |
| Generación asíncrona deja el recibo "perdido" si el evento de vuelta se pierde | `Idempotency-Key` en el request M2M permite reintento seguro; el `ReceiptFileId` queda `null` en `TenantOnboarding` hasta que `DocumentGenerationCompletedIntegrationEvent` llega — un gap observable, no silencioso. |
| El mediador de descarga en Auth queda huérfano si Documents cambia su esquema de storage | El mediador solo conoce `fileId` (opaco) + el endpoint preexistente de CloudStorage — no acopla a la implementación interna de Documents. |

## Criterios de aceptación

- `OwnerType.Onboarding` documentado en `TaxVision.Documents.Domain` sin ninguna referencia a tipos
  de `TaxVision.Auth.*`.
- El link de descarga del email nunca vence (verificado por diseño: `GET
  onboarding/receipts/{fileId}/download` resuelve una URL presignada fresca en cada click).
- `Idempotency-Key` en `POST internal/document-generations/onboarding-receipts` — un reintento del
  mismo `OnboardingId` no genera un segundo documento.

## Archivos afectados

`src/Services/Documents/TaxVision.Documents.Api/Controllers/InternalOnboardingReceiptsController.cs`,
`src/Services/Auth/Application/Onboarding/TenantOnboardings/IntegrationEvents/` (consumer del pago y
del `DocumentGenerationCompletedIntegrationEvent`),
`src/Services/Auth/Api/Controllers/OnboardingReceiptDownloadController.cs`,
`src/Services/Auth/Application/Onboarding/ReceiptDownload/Queries/`.

## Estado

**APPROVED**. Decisión ya implementada en Fases 10-11; este ADR es el registro formal requerido por
Fase 19 del plan maestro.
