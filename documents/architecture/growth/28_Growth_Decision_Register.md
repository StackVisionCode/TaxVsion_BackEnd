# Growth — Registro de decisiones

Cada entrada usa el formato obligatorio.

## GDR-001 — Deployment modular

- Contexto: dos dominios necesitan salida inicial.
- Evidencia real: ausencia Growth; legacy separado/mezclado; diseños contradictorios.
- Alternativas: dos servicios, persistencia Payment, un contexto, dos contextos/un deployment.
- Seleccionada: Growth con Codes y Referrals separados.
- Motivo: consistencia local y seams.
- Positivas: menor operación, owner único.
- Negativas: blast radius común.
- Riesgos: acoplamiento.
- Mitigaciones: proyectos/schemas/tests.
- Aceptación: cero FK/dependency domain cross-context.
- Archivos afectados: futuros `src/Services/Growth`.
- Estado: **APPROVED**.

## GDR-002 — Dinero

- Contexto: descuentos precisos.
- Evidencia real: `PaymentClient.Domain/ValueObjects/Money.cs`.
- Alternativas: decimal, floating point, minor units.
- Seleccionada: `long AmountCents + Currency`; percentage basis points.
- Motivo: patrón Payment real.
- Positivas: sin redondeo binario.
- Negativas: currency exponent future handling.
- Riesgos: overflow/conversión Subscription decimal.
- Mitigaciones: checked arithmetic y adapters.
- Aceptación: no float/double, currency siempre.
- Archivos: futuros VOs/mappings.
- Estado: **APPROVED**.

## GDR-003 — Protocolo

- Contexto: descuento antes de resultado financiero.
- Evidencia: Apply inmediato/dry-run/reserve conflictivos en diseños.
- Alternativas: apply, validate/redeem, quote/reserve/commit/cancel.
- Seleccionada: protocolo completo + reconciler.
- Motivo: evita oversubscription y consumo perdido.
- Positivas: auditable/idempotente.
- Negativas: estados/jobs adicionales.
- Riesgos: late success.
- Mitigaciones: late commit policy.
- Aceptación: E2E y races verdes.
- Archivos: Growth + futuros Payment contracts.
- Estado: **APPROVED**.

## GDR-004 — Tenant isolation

- Contexto: datos platform/tenant.
- Evidencia: TenantEntity y repos scoped; ausencia uniforme de filters.
- Alternativas: filters, repos, ambos.
- Seleccionada: ambos + elevation explícita.
- Motivo: defense in depth.
- Positivas: reduce omisión.
- Negativas: testing/elevación complejos.
- Riesgos: filtro global sobre platform resource.
- Mitigaciones: scope discriminator y pruebas.
- Aceptación: matriz negativa.
- Archivos: futuros DbContext/repos.
- Estado: **APPROVED**.

## GDR-005 — Rewards monetarios

- Contexto: legacy Wallet/TaxCoin.
- Evidencia: balance mutable legacy; no Ledger actual.
- Alternativas: Growth balance, Payment balance, Ledger, excluir.
- Seleccionada: excluir MVP; futuro Ledger.
- Motivo: ownership contable.
- Positivas: menor riesgo financiero.
- Negativas: menos tipos reward.
- Riesgos: expectativas legacy.
- Mitigaciones: migration inventory/communication.
- Aceptación: cero balance/cash table Growth.
- Archivos: scope/migration.
- Estado: **APPROVED**.

## GDR-006 — Taxpayer referrals

- Contexto: alto PII/fraude y precio no resuelto.
- Evidencia: diseño externo, no código.
- Alternativas: MVP, pilot, diseño-only.
- Seleccionada: diseño-only, fuera producción.
- Motivo: dependencias abiertas.
- Positivas: T2T puede avanzar.
- Negativas: feature diferida.
- Riesgos: presión de scope.
- Mitigaciones: gate explícito.
- Aceptación: no endpoint productivo MVP.
- Archivos: roadmap.
- Estado: **DEFERRED**.

## GDR-007 — Autoridad precio tenant-cliente

- Contexto: PaymentClient acepta AmountCents.
- Evidencia: `ChargeTenantPaymentCommand.cs`; Catalog solo documental.
- Alternativas: Catalog, invoice/order owner, signed snapshot híbrido.
- Seleccionada: pendiente owner; snapshot server-side obligatorio.
- Motivo: negocio debe ratificar sistema comercial.
- Consecuencias positivas: evita frontend pricing.
- Negativas: bloquea integración.
- Riesgos: cobro manipulado.
- Mitigaciones: no scaffold contracts definitivos.
- Aceptación: owner+schema+signature/version.
- Archivos: futuros Catalog/Payment contracts.
- Estado: **DESIGN_BLOCKER**.

## GDR-008 — Auth M2M

- Contexto: endpoints internos Growth.
- Evidencia: JWT audience existe; grant/scopes Growth no.
- Alternativas: client credentials, signed service JWT, event-only.
- Seleccionada: pendiente validación Auth; audience/scopes obligatorios.
- Motivo: no inventar patrón.
- Positivas: mínimo privilegio.
- Negativas: coordinación Auth.
- Riesgos: servicio impersonation.
- Mitigaciones: internos no Gateway.
- Aceptación: threat model y token tests.
- Archivos: Auth/Growth futuros.
- Estado: **DESIGN_BLOCKER**.

## GDR-009 — Compensación comercial

- Contexto: refunds/chargebacks.
- Evidencia: Payment soporta estados; Growth inexistente.
- Alternativas: restaurar, conservar, proporcional por benefit.
- Seleccionada: framework versionado; matriz exacta pendiente negocio.
- Motivo: depende economics/fraud.
- Positivas: mecanismo definido.
- Negativas: reglas sin cerrar.
- Riesgos: abuso/pérdida.
- Mitigaciones: default conservador.
- Aceptación: matriz aprobada por tipo.
- Archivos: policy/domain.
- Estado: **DESIGN_BLOCKER**.

## GDR-010 — Programa tenant-to-tenant

- Contexto: MVP referral.
- Evidencia: Subscription/PaymentApp reales; reglas comerciales no.
- Alternativas: fixed defaults, configurable, postpone.
- Seleccionada: configurable dentro de límites; valores pendientes.
- Motivo: no inventar economics.
- Positivas: dominio preparado.
- Negativas: blocker.
- Riesgos: self-referral/farming.
- Mitigaciones: wait/fraud/max.
- Aceptación: ventana/minimum/wait/max/reward aprobados.
- Archivos: ReferralProgram policy.
- Estado: **DESIGN_BLOCKER**.

## GDR-011 — Privacidad taxpayer

- Contexto: programa futuro contiene PII.
- Evidencia: diseño externo; no clasificación legal.
- Alternativas: raw PII, pseudónimo, external identity refs.
- Seleccionada: pseudónimo/external refs; retención pendiente.
- Motivo: minimización.
- Positivas: menor exposición.
- Negativas: investigación más compleja.
- Riesgos: reidentificación.
- Mitigaciones: encryption/RBAC/audit.
- Aceptación: privacy review y retention.
- Archivos: future referrals.
- Estado: **DESIGN_BLOCKER**.

