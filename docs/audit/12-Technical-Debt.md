# Deuda técnica y complejidad

## Hallazgos

- **DEBT-001 MEDIUM/P2:** comentarios extensos de “Fase N/auditoría” actúan como ADR embebido y envejecen; trasladar decisiones a ADR y dejar invariantes concisas.
- **DEBT-002 MEDIUM/P2:** Auth conoce orden/tipos de beneficios, pricing, settlement strings y contratos de tres servicios.
- **DEBT-003 LOW/P3:** strings `Paid|Mixed|FullyCoveredByCode` cruzan frontera y se parsean; contrato enum/versionado.
- **DEBT-004 MEDIUM/P2:** manejo de errores HTTP reduce respuestas a códigos genéricos; dificulta compensación vs retry.
- **DEBT-005 MEDIUM/P2:** código compilado `dist/` coexiste con TypeScript source en workers, riesgo de drift si CI no regenera/verifica.
- **DEBT-006 LOW/P3:** `catch` amplios en jobs/fallbacks requieren inventario de métricas y alertas; varios son deliberados, no todos defectos.

La búsqueda encontró TODO/FIXME/NotImplemented principalmente en comentarios y test doubles; no se puede declarar muerto solo por texto. Se recomienda cobertura/uso estático por proyecto antes de eliminación.

## Documentación vs realidad

| Documentación/comentario dice | Implementación/prueba realmente muestra | Severidad |
|---|---|---|
| Billing crea invoice en todos los casos | El camino existe, pero payload inválido retorna sin invoice y sin error al bus | HIGH |
| Checkout es idempotente | DB/Stripe keys existen; aún hay ventana Stripe-session-before-local-save | HIGH |
| Stack de códigos conserva identidad | Sí persiste ajustes separados; no es atómico y ordena Promo antes de Gift | HIGH |
| Carril $0 continúa sin Payment | Sí, llama directamente al helper de éxito | INFO |
| Durable outbox/inbox protege eventos | Está configurado; falta demostrar enlistment transaccional por handler | MEDIUM |
| Tests validan onboarding payment | La suite Auth no compila por contrato obsoleto | HIGH |
