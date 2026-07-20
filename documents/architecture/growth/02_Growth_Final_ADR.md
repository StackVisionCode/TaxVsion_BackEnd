# ADR-GROWTH-001 — Deployment modular para Codes y Referrals

Estado: **APPROVED**  
Fecha: 2026-07-19

## ID y contexto

**ID:** GDR-001. TaxVision necesita promociones y referidos consumidos por PaymentApp y PaymentClient sin convertir Payment en pricing engine ni forzar consistencia distribuida prematura entre Codes y Referrals.

## Evidencia real

- `src/Services/PaymentApp/` y `src/Services/PaymentClient/` no implementan Codes.
- Subscription posee precio SaaS, trial y entitlements.
- El CRM legado tiene `ReferralService` separado y cupones en `PaymentService`.
- `PaymentServices_Analysis_And_Design.md:1058` propone alternativas incompatibles de ubicación.
- `Referrals_Service_Analysis_And_Design.md:122` propone owner único independiente.

Clasificación: **VERIFIED** para el código actual; **DOCUMENTED_ONLY** para las propuestas.

## Alternativas

1. Dos microservicios desde el inicio.
2. Codes duplicado en PaymentApp y PaymentClient.
3. Un deployment con dominios fusionados.
4. Un deployment con dos bounded contexts y seams de extracción.

## Opción seleccionada y motivo

Opción 4: `TaxVision.Growth`, DB `TaxVision_Growth`, schemas `codes`, `referrals`, `integration`, `audit`. Reduce operación y coordinación distribuida inicial sin mezclar lenguaje, aggregates ni ownership.

## Consecuencias

Positivas:

- transacciones locales para reserva/redemption y coordinación interna;
- una autoridad promocional;
- extracción posterior medible;
- Payment permanece ejecutor financiero.

Negativas:

- blast radius y ciclo de despliegue compartidos;
- disciplina necesaria para impedir joins/FK cross-context;
- infraestructura compartida puede ocultar acoplamiento.

## Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Monolito accidental | proyectos Domain/Application separados, arquitectura tests, schemas separados |
| Acceso directo cross-context | interfaces de aplicación, IDs y mensajes internos; sin repositorios compartidos |
| Extracción costosa | contratos internos versionados y telemetría por bounded context |
| Doble autoridad | una DB/owner; BuildingBlocks solo contratos técnicos |

## Criterios de aceptación

- dependencias Codes↔Referrals verificadas por architecture tests;
- cero FK entre aggregates de ambos contextos;
- cero persistencia Codes en Payment;
- API interna separada de rutas Gateway;
- cada evento indica owner y versión.

## Archivos afectados

Futuros: `src/Services/Growth/`, `deploy/tests/TaxVision.Growth.Tests/`, Gateway, compose y runner de migraciones. Ninguno se modifica en esta fase.

## Estado

**APPROVED**. Los blockers comerciales/M2M impiden scaffolding, no invalidan la topología.
