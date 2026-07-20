# Referrals y Codes — decisión arquitectónica

## ADR-GROWTH-001

- **Estado de la decisión:** APPROVED
- **Fecha:** 2026-07-19
- **Decisión:** opción 3, **un servicio modular inicialmente, preparado para extracción futura**.
- **Nombre del deployment:** `TaxVision.Growth`.
- **Bounded contexts:** `Codes` y `Referrals`, estrictamente distintos.
- **Base inicial:** `TaxVision_Growth`.
- **Esquemas:** `codes`, `referrals`, `integration` y `audit`.
- **Confianza en la topología recomendada:** **97 % (alta)**.

## Contexto

Se evaluaron tres opciones:

1. dos microservicios separados desde el inicio;
2. dos módulos permanentes dentro del mismo servicio;
3. un servicio modular inicial con una ruta explícita de extracción.

La decisión no parte solo de documentación: el repositorio actual ya materializa la tercera opción. Codes y Referrals tienen ciclos y lenguaje propios, pero hoy comparten un flujo de baja latencia, una base operativa, el mismo stack, un equipo y una madurez de integración todavía temprana. Separar deployment ahora añadiría fallos distribuidos y operación duplicada sin evidencia de una necesidad independiente.

## Fuerzas de decisión

- Codes participa sincrónicamente antes/durante/después del cobro.
- Referrals reacciona principalmente a hechos financieros y orquesta rewards no monetarios.
- Ambos necesitan tenant isolation, idempotencia, auditoría, inbox/outbox y seguridad M2M.
- No comparten aggregate roots, reglas, lenguaje ni ownership.
- Payment debe seguir siendo owner financiero.
- Subscription debe seguir siendo owner de precios SaaS, trials y entitlements.
- Catalog debe ser futura/actual autoridad de precio tenant-cliente, no Payment ni el frontend.
- Un futuro Ledger debe poseer saldos y rewards monetarios.
- Todavía no existen métricas de escala, equipos, SLO o cadence que justifiquen dos deployments.

## Evidencia real

| Evidencia | Ruta exacta | Qué demuestra |
|---|---|---|
| Seis proyectos Growth | `TaxVision.slnx`; `src/Services/Growth/` | Domain/Application separados y host/infraestructura compartidos. |
| Regla de dependencia | `deploy/tests/TaxVision.Growth.Tests/Architecture/BoundedContextArchitectureTests.cs` | Un bounded context no referencia al otro. |
| Esquemas separados | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/GrowthSchemas.cs`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Configurations/` | Separación física lógica dentro de la base inicial. |
| Deployment único | `deploy/docker/docker-compose.yml`; `src/Gateway/TaxVision.Gateway/appsettings.json` | Una unidad operativa `growth-api`. |
| Contratos de integración | `src/BuildingBlocks/Messaging/GrowthIntegrationEvents/`; `src/BuildingBlocks/Messaging/PaymentIntegrationEvents/` | Posibilidad de extraer usando contratos estables. |
| Legacy Payment/Codes | `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND\PaymentService\Domain\DiscountCoupon.cs` | El acoplamiento histórico que no debe repetirse. |
| Legacy Referrals/Wallet | `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND\ReferralService\Infrastructure\Context\ApplicationDbContext.cs` | El acoplamiento financiero que debe quedar fuera de Growth. |

## Evaluación de alternativas

| Criterio | 1. Dos microservicios ahora | 2. Dos módulos permanentes | 3. Servicio modular extraíble |
|---|---:|---:|---:|
| Separación de dominio | Alta | Alta si se disciplina | Alta |
| Complejidad operativa inicial | Alta | Baja | Baja |
| Latencia y fallos distribuidos | Peor | Mejor | Mejor inicialmente |
| Transacción local de infraestructura | No | Sí | Sí |
| Independencia futura | Alta inmediata | Baja/indefinida | Alta por diseño |
| Evidencia de necesidad actual | Insuficiente | Parcial | Suficiente |
| Coincidencia con código real | No | Parcial | Sí |
| Riesgo de acoplamiento accidental | Bajo por red | Alto si no hay reglas | Controlado por proyectos/tests |
| Costo de reversión | Alto | Alto si se fusionan modelos | Moderado |

### Opción 1 — dos microservicios separados

**No seleccionada ahora.**

Ventajas:

- despliegue, escalado, SLO y ownership de datos totalmente independientes;
- aislamiento fuerte por proceso/base/red;
- extracción ya consumada.

Desventajas:

- dos pipelines, dos superficies operativas y más observabilidad/seguridad;
- eventos/comandos obligatorios incluso para coordinación que hoy puede componerse en un host;
- mayor superficie de fallos mientras Payment/Subscription todavía no están integrados;
- no existen métricas de carga, equipos o cadence que justifiquen el costo.

### Opción 2 — dos módulos permanentes en el mismo servicio

**No seleccionada como destino permanente.**

Ventajas:

- simplicidad operativa;
- transacciones y diagnóstico locales;
- menos latencia.

Desventajas:

- convierte una elección inicial en frontera permanente sin evidencia;
- favorece FKs, repositorios o tipos cruzados con el tiempo;
- dificulta separar SLO/retención/escala si Referrals y Codes divergen.

### Opción 3 — servicio modular inicial con extracción futura

**Seleccionada.**

Combina simplicidad operativa actual con bounded contexts reales. La extracción no es una promesa abstracta: proyectos, namespaces, schemas, dependency tests y contratos dejan seams verificables.

## Decisión detallada

1. `TaxVision.Growth` es una sola unidad de despliegue inicial.
2. Codes y Referrals conservan Domain/Application separados.
3. La infraestructura común puede implementar puertos de ambos contextos, pero no trasladar reglas entre ellos.
4. La base inicial usa esquemas separados.
5. No se permiten foreign keys, navegación EF ni aggregate references entre Codes y Referrals.
6. La comunicación entre contextos usa IDs, puertos de aplicación, comandos internos o eventos.
7. Payment no referencia Domain/Infrastructure de Growth; usa contratos/API M2M.
8. Growth no escribe bases de Payment, Subscription, Catalog ni futuro Ledger.
9. BuildingBlocks contiene contratos/constants, no repositorios ni persistencia de negocio.
10. La entrega distribuida se describe como **at-least-once con operaciones idempotentes**, no como exactly-once.

## Ownership aprobado

| Capacidad | Owner |
|---|---|
| Rules, eligibility, quote, reservation, redemption, promotional compensation | Codes |
| Referral program/code, attribution, qualification, fraud review, reward lifecycle/clawback | Referrals |
| Authorization, capture, provider references, refund, chargeback, financial reconciliation | PaymentApp/PaymentClient |
| Plan/version/price SaaS, trial, add-on, entitlement y materialización del beneficio SaaS | Subscription |
| Producto/servicio y precio tenant-cliente | Catalog/ProductsAndServices |
| Balance, crédito monetario, débito, transfer, cash reward y reconciliación contable | futuro Ledger |
| Email/notificación | Notification |
| Identity, roles, permissions y tenant status | Auth/Tenant |

## Consecuencias

### Positivas

- una operación y un deployment mientras madura el producto;
- fronteras de dominio verificables desde el primer día;
- reutilización de tenancy, seguridad, observabilidad e inbox/outbox sin duplicarlas;
- baja latencia para casos de uso internos;
- extracción futura con menor reescritura;
- Codes queda fuera de Payment y wallet queda fuera de Referrals.

### Negativas

- host, base y release compartidos crean un blast radius común;
- el `GrowthDbContext` puede tentar a crear queries/FKs cruzados;
- ambos módulos escalan juntos al inicio;
- una falla de infraestructura común puede afectar los dos dominios;
- la extracción exigirá duplicar infraestructura y mover datos aun con buenos seams.

### Mitigaciones

- dependency tests obligatorios;
- schemas y migraciones con ownership explícito;
- ausencia de FKs cruzadas validada por metamodelo;
- contracts versionados y sin tipos Domain;
- métricas por módulo, operación y tenant;
- budgets/SLO separados aunque el proceso sea compartido;
- catálogo de triggers de extracción revisado trimestralmente.

## Triggers de extracción

Extraer Codes o Referrals a un deployment independiente solo cuando al menos uno de estos factores sea persistente y medido:

1. equipos/ownership y cadence de release realmente independientes;
2. perfil de carga o escalado materialmente distinto;
3. SLO, disponibilidad o blast radius incompatible;
4. requisitos regulatorios, PII o retención distintos;
5. necesidad de aislamiento de datos/base/credenciales;
6. cambios de un módulo bloquean repetidamente releases del otro;
7. contratos de integración ya son estables y están cubiertos por contract/E2E tests.

Antes de extraer:

- cero FKs/queries cruzadas;
- todos los flujos cruzados modelados por comandos/eventos;
- backfill y dual-read/write evitados o diseñados explícitamente;
- reconciliación y replay probados;
- observabilidad permite comparar antes/después;
- plan de cutover y rollback aprobado.

## Hallazgos de decisión

| ID | Severidad | Afirmación documental | Evidencia del código | Ruta exacta | Estado | Impacto | Recomendación | ¿Bloquea scaffolding? |
|---|---|---|---|---|---|---|---|---|
| ADR-001 | INFO | Deben ser dos microservicios separados desde el inicio. | Solo existe un host/compose/cluster Growth; no hay evidencia de escala, equipos o SLO separados. | `src/Services/Growth/TaxVision.Growth.Api/`; `deploy/docker/docker-compose.yml`; `src/Gateway/TaxVision.Gateway/appsettings.json` | CONTRADICTED | Forzar dos servicios ahora elevaría complejidad sin resolver los gaps de integración reales. | No separar deployments todavía. | No. |
| ADR-002 | MEDIUM | Codes y Referrals pueden ser un solo bounded context porque comparten deployment. | Domain/Application, lenguaje, aggregates y pruebas de referencia están separados. | `src/Services/Growth/TaxVision.Codes.Domain/`; `src/Services/Growth/TaxVision.Referrals.Domain/`; `deploy/tests/TaxVision.Growth.Tests/Architecture/BoundedContextArchitectureTests.cs` | CONTRADICTED | Fusionarlos produciría modelos ambiguos y ownership cruzado. | Declarar siempre “dos bounded contexts, un deployment”. | No. |
| ADR-003 | INFO | Un servicio modular inicialmente con extracción futura es adecuado. | La estructura de proyectos, schemas, contratos y deployment coincide con esa opción. | `TaxVision.slnx`; `src/Services/Growth/`; `src/BuildingBlocks/Messaging/GrowthIntegrationEvents/`; `deploy/docker/docker-compose.yml` | VERIFIED | Minimiza costo inicial sin cerrar la puerta a independencia. | Adoptar como arquitectura vigente. | No. |
| ADR-004 | HIGH | Compartir base autoriza foreign keys o repositorios cruzados. | No existen FKs cruzadas ni referencias Domain/Application; el diseño vigente las prohíbe. | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Configurations/`; `documents/architecture/growth/02_Growth_Final_ADR.md` | CONTRADICTED | Un acoplamiento de datos eliminaría la capacidad de extracción. | Validar el metamodelo y review de migraciones en CI. | No. |
| ADR-005 | HIGH | Codes debe estar dentro de Payment para aplicar antes de cobrar. | Codes reside en Growth y expone un protocolo M2M; Payment sigue siendo owner financiero. | `src/Services/Growth/TaxVision.Growth.Api/Controllers/InternalCodesController.cs`; `src/Services/Growth/TaxVision.Codes.Application/`; `src/Services/PaymentApp/`; `src/Services/PaymentClient/` | CONTRADICTED | Mezclarlo con Payment reproduce el legado y duplica reglas. | Mantener Growth como owner y exigir quote/reserve/commit/cancel por contrato. | No. |
| ADR-006 | HIGH | Referrals puede poseer wallet/TaxCoin porque el CRM anterior lo hacía. | El CRM mezclaba esas tablas; Growth actual no contiene saldo/moneda y el ownership aprobado los asigna a futuro Ledger. | `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND\ReferralService\Infrastructure\Context\ApplicationDbContext.cs`; `src/Services/Growth/`; `documents/architecture/growth/05_Growth_Ownership_Matrix.md` | CONTRADICTED | Reintroducirlo convierte reward lifecycle en contabilidad financiera. | Mantener rewards no monetarios hasta disponer de Ledger. | No. |
| ADR-007 | MEDIUM | La extracción futura ya está garantizada solo por usar carpetas distintas. | Hay seams sólidos, pero host/base/infraestructura siguen compartidos y la integración externa aún no está completa. | `src/Services/Growth/`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/GrowthDbContext.cs`; `src/Services/Growth/TaxVision.Growth.Api/Program.cs` | PARTIAL | Sin vigilancia pueden aparecer dependencias de datos/operación que encarezcan la extracción. | Mantener guardrails, métricas y triggers medibles. | No. |

## Nivel de confianza por área

| Área | Confianza | Motivo |
|---|---:|---|
| Topología y bounded contexts | 97 % | La decisión coincide con proyectos, referencias, schemas, deployment y pruebas fuente. |
| Ownership de dominio | 94 % | Código y documentación vigente convergen; Catalog/Ledger aún son capacidades futuras. |
| Modelo Codes | 90 % | Protocolo y guards locales existen; autoridad de precio y reconciliation externos faltan. |
| Modelo Referrals | 86 % | Núcleo, idempotencia y reward lifecycle existen; adapters/eventos/materialización faltan. |
| Integración distribuida | 48 % | Contratos e infraestructura base existen, pero producers/consumers/reconciliation no están completos. |
| Readiness productivo | 42 % | Se requieren migraciones validadas, suites SQL/E2E y operación de disputes/rewards. |

## Decisiones que deben resolverse antes de integrar

La topología ya no es un blocker. Sí deben resolverse:

1. autoridad server-side de precio para PaymentClient;
2. contrato obligatorio Payment↔Growth para snapshot, reservation y net amount;
3. verificación autoritativa de late payment y reconciler;
4. routing/productores/consumidores de eventos financieros;
5. política ejecutable de refund y chargeback opened/won/lost;
6. comando/confirmación de rewards y grants en Subscription;
7. programa inicial, límites y provisioning seguro de referral codes;
8. privacidad/antifraude antes de taxpayer-to-taxpayer;
9. migración, backfill y retiro de datos legados;
10. criterios cuantitativos de producción y extracción.

## Decisión final

**Recomendada y aprobada:** `TaxVision.Growth` como servicio modular inicial, con **Codes** y **Referrals** como bounded contexts estrictamente separados y con extracción futura gobernada por evidencia.
