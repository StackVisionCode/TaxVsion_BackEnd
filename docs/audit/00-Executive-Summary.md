# Auditoría técnica — resumen ejecutivo

Fecha de corte: 2026-08-07. Alcance: solución `TaxVision.slnx`, `src/`, `deploy/`, `scripts/`, pruebas y documentación. Método: lectura estática de implementación, contratos, migraciones, composición y pruebas; no se modificó código ni datos. Las conclusiones describen primero el comportamiento actual.

## Veredicto

**NO** está listo para producción financiera sin condiciones adicionales. El diseño contiene buenas defensas (outbox/inbox durable, índices únicos, `rowversion`, M2M, aislamiento por tenant y aggregates con transiciones), pero los flujos comerciales A–D carecen de pruebas integradas y poseen ventanas de consistencia distribuida que pueden dejar onboarding completado sin invoice, reservas parciales huérfanas o una sesión de Stripe sin registro local.

## Resultado especial A–D

| Caso | Comportamiento actual demostrado por código | Resultado |
|---|---|---|
| A: 100, sin descuento | Auth no reserva códigos; PaymentApp vuelve a consultar precio a Subscription, crea `SaaSPayment` por 100 y Stripe Checkout; tras webhook exitoso Auth finaliza y Billing crea invoice Paid: subtotal 100, descuento 0, total 100, vinculada al payment. | Camino nominal correcto; no existe E2E que demuestre toda la cadena. |
| B: 100 − 30 | Auth cotiza/reserva en Growth, persiste bruto/descuento/neto; PaymentApp valida que 70 sea `>0` y `<=100`, y cobra 70. Billing recibe 100/30/70 y un ajuste por código. | Correcto nominalmente; PaymentApp ignora moneda/descuento recibidos salvo el neto y no verifica criptográficamente la reserva. |
| C: promoción 100% | Auth detecta `FullyCovered`, no llama PaymentApp, usa `Guid.Empty` solo en la respuesta, completa onboarding y publica finalize. Billing crea invoice Paid de total 0, `PaymentId=null`, método Other y settlement `FullyCoveredByCode`. Onboarding continúa sin `PaymentSucceeded`. | La bifurcación existe; falta prueba E2E y la invoice es eventual, no prerrequisito del avance. |
| D: gift 60 + promotion 20 | El orden real no es el solicitado: `Referral → Promo → Gift`; por tanto Promo 20 se aplica antes y Gift absorbe 60 del residual, dejando 20. Billing conserva dos ajustes separados y PaymentApp cobra 20. | Los importes pueden coincidir, pero la semántica/orden es Promo antes de Gift. La invoice solo “explica perfectamente” si Documents renderiza `Adjustments`; debe verificarse mediante prueba de contrato/render. |

Detalles y referencias en [03-E2E-Flows.md](03-E2E-Flows.md) y [05-Billing-Payments-Audit.md](05-Billing-Payments-Audit.md).

## Top 10 problemas

1. **REL-001 HIGH/P1**: onboarding puede avanzar antes de que Billing cree la invoice; la publicación es eventual y el consumidor puede rechazar silenciosamente un settlement/ajuste inválido.
2. **PAY-001 HIGH/P1**: Stripe puede crear sesión y fallar la persistencia local; el propio handler documenta esta ventana.
3. **GRO-001 HIGH/P1**: reservas apiladas se hacen en llamadas separadas; un fallo en la segunda/tercera deja reservas previas sin compensación inmediata.
4. **TST-001 HIGH/P1**: no hay E2E real para A, B, C ni D ni para doble entrega/fallo intermedio.
5. **TST-002 HIGH/P0**: la suite Auth no compila: un test usa `OnboardingFinalizeCommand.PaidAmountCents`, miembro eliminado.
6. **PAY-002 HIGH/P1**: PaymentApp acepta `NetAmountCents` de Auth y solo valida rango; no recalcula el descuento contra Growth ni valida snapshot/reserva.
7. **ARCH-001 MEDIUM/P1**: pre-tenant se hospeda bajo `PlatformTenant` y luego se “rehome”; mezcla ownership operativo y exige filtros excepcionales.
8. **REL-002 MEDIUM/P1**: `FinalizeAsync` hace commits HTTP secuenciales antes de publicar invoice; los efectos no forman una transacción distribuida.
9. **BIL-002 MEDIUM/P2**: invoice cero nace `Paid` y `PaymentMethod.Other`; es trazable, pero “Other” confunde redención no monetaria con método de pago.
10. **DOC-001 MEDIUM/P2**: README/comentarios contienen afirmaciones de fases y garantías que no están cubiertas por pruebas de sistema.

## Top 10 mejoras arquitectónicas

1. Convertir finalización comercial en una saga persistida con hitos `CodesCommitted`, `InvoiceCreated`, `Provisioned`.
2. Exigir `InvoiceCreated` antes de marcar el onboarding financieramente finalizado, sin bloquear registro por el PDF.
3. Introducir una operación Growth atómica para reservar el stack completo o compensar automáticamente.
4. Hacer que PaymentApp valide un quote firmado/opaque emitido por Growth, no un neto confiado.
5. Persistir intención local antes de llamar Stripe y reconciliar sesiones por idempotency key.
6. Modelar `Prospect/Onboarding` como subject explícito, evitando ownership ficticio de PlatformTenant.
7. Separar `SettlementInstrument` (cash, promo, gift) de `PaymentMethod`.
8. Publicar evento `OnboardingInvoiceCreated` y registrar inbox/fingerprint de payload.
9. Aplicar contracts versionados y pruebas de compatibilidad entre Auth/Growth/PaymentApp/Billing/Documents.
10. Crear un ledger inmutable de ajustes, pagos, redenciones, refunds y compensaciones.

## Top 10 E2E faltantes

1. A completo con invoice/payment 100.
2. B completo con subtotal/descuento/total/payment 100/30/70.
3. C completo sin entidad Payment y con invoice cero + onboarding terminado.
4. D completo, dos ajustes renderizados y payment 20.
5. Doble POST checkout concurrente.
6. Doble webhook Stripe.
7. Crash después de crear sesión Stripe y antes de guardar.
8. Crash después de primer código reservado en un stack.
9. Billing caído durante finalize y recuperación posterior.
10. Último uso de código disputado por dos onboarding.

## Top 10 riesgos de producción

Estado comercial sin documento; sesión cobrada no reconciliada; reserva huérfana; invoice duplicada por carrera previa al índice; huecos/conflictos de numeración; descuento manipulado por servicio comprometido; evento poison ignorado; rehome incompleto; PDF sin desglose; y retry no idempotente en límites no cubiertos.
