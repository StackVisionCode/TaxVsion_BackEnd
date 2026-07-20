# Referrals y Codes — checklist de readiness

## Resultado por gate

| Gate | Resultado | Explicación |
|---|---|---|
| Decisión de arquitectura | APROBADA | Un `TaxVision.Growth`, dos bounded contexts y extracción futura. |
| Technical scaffolding | CUMPLIDO | Proyectos, solución, host, infraestructura, Gateway, compose y tests fuente existen. |
| Domain implementation | EN CURSO / SUSTANCIAL | Codes porcentaje/importe fijo y núcleo tenant-to-tenant Referrals están implementados. |
| Integration | NO LISTO | Payment no produce aún el lifecycle canónico ni usa reservation/snapshot; rewards/grants no llegan a Subscription. |
| Production review | NO LISTO | Faltan validación de migración aplicada, SQL/contract/E2E/concurrency tests, reconciler y dispute lifecycle. |

**Estado global conservador:** `READY_FOR_DOMAIN_IMPLEMENTATION`.

Este estado no significa que deba repetirse el scaffolding. Significa que la frontera técnica está creada y que el trabajo seguro restante empieza por cerrar dominio/adapters y los prerequisitos de integración.

## Checklist de arquitectura y ownership

| ID | Severidad | Afirmación documental | Evidencia del código | Ruta exacta | Estado | Impacto | Recomendación | ¿Bloquea scaffolding? |
|---|---|---|---|---|---|---|---|---|
| RCL-001 | INFO | Deployment inicial `TaxVision.Growth`. | Solución, Dockerfile, compose y Gateway registran Growth. | `TaxVision.slnx`; `src/Services/Growth/TaxVision.Growth.Api/Dockerfile`; `deploy/docker/docker-compose.yml`; `src/Gateway/TaxVision.Gateway/appsettings.json` | VERIFIED | La unidad desplegable está definida. | Conservar nombre y ruta canónicos. | No; cumplido. |
| RCL-002 | INFO | Codes y Referrals son bounded contexts distintos. | Domain/Application están separados y existe dependency test. | `src/Services/Growth/TaxVision.Codes.Domain/`; `src/Services/Growth/TaxVision.Referrals.Domain/`; `deploy/tests/TaxVision.Growth.Tests/Architecture/BoundedContextArchitectureTests.cs` | VERIFIED | Evita un modelo combinado “Referrals & Codes”. | Ejecutar regla en CI. | No. |
| RCL-003 | INFO | No hay FKs cruzadas. | Mappings y migración solo contienen FKs internas a su esquema. | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Configurations/`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Migrations/` | VERIFIED | Preserva extraction seam. | Añadir metamodel test. | No. |
| RCL-004 | INFO | Codes no vive en Payment. | Projects Growth propios; no hay persistencia Codes en Payment actual. | `src/Services/Growth/TaxVision.Codes.Domain/`; `src/Services/PaymentApp/`; `src/Services/PaymentClient/` | VERIFIED | Ownership promocional correcto. | Integrar por contratos, no referencias de proyecto. | No. |
| RCL-005 | INFO | Wallet/TaxCoin/cash reward quedan fuera. | Growth no contiene ledger/wallet y rewards enum son beneficios Subscription. | `src/Services/Growth/`; `src/Services/Growth/TaxVision.Referrals.Domain/Rewards/ReferralRewardType.cs` | VERIFIED | No se crea pasivo financiero sin Ledger. | Mantener fuera del MVP. | No. |

## Checklist Codes

| ID | Severidad | Afirmación documental | Evidencia del código | Ruta exacta | Estado | Impacto | Recomendación | ¿Bloquea scaffolding? |
|---|---|---|---|---|---|---|---|---|
| RCL-006 | INFO | Porcentaje e importe fijo con moneda/minimum/scope/limits. | Domain, create handler y mappings implementan ambas reglas y limits global/tenant/subject. | `src/Services/Growth/TaxVision.Codes.Domain/Definitions/`; `src/Services/Growth/TaxVision.Codes.Application/Definitions/CreateCodeDefinition/`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Configurations/Codes/` | VERIFIED | Núcleo promocional MVP disponible. | Completar integration/SQL tests. | No. |
| RCL-007 | INFO | Quote es snapshot sin consumo. | `CodeQuote` conserva rule version, offer, amounts, snapshot y TTL. | `src/Services/Growth/TaxVision.Codes.Domain/Quotes/CodeQuote.cs`; `src/Services/Growth/TaxVision.Codes.Application/Quotes/CreateQuote/` | VERIFIED | Cotización reproducible. | Verificar oferta/precio server-side. | No. |
| RCL-008 | INFO | Reserve/commit/cancel/expire/compensate están modelados. | Handlers, aggregates, counters y endpoints internos existen. | `src/Services/Growth/TaxVision.Codes.Application/Reservations/`; `src/Services/Growth/TaxVision.Codes.Application/Compensations/`; `src/Services/Growth/TaxVision.Growth.Api/Controllers/InternalCodesController.cs` | VERIFIED | State machine local completa. | Probar carreras y conectar Payment. | No. |
| RCL-009 | MEDIUM | Gift/Prelaunch/Trial/Feature están listos para producción. | Enums/VO existen; create handler rechaza beneficios distintos de Percentage/FixedAmount y no existe IssueGift/grant end-to-end. | `src/Services/Growth/TaxVision.Codes.Domain/Definitions/CodeBenefitType.cs`; `src/Services/Growth/TaxVision.Codes.Application/Definitions/CreateCodeDefinition/CreateCodeDefinitionHandler.cs` | CONTRADICTED | Habilitar por enum produciría una capacidad incompleta. | Mantener feature-disabled hasta issuance, recipient binding, rate limit y confirmación Subscription. | No. |
| RCL-010 | BLOCKER | El quote usa un precio/offer autoritativo. | El command acepta gross/offer/snapshot del caller; no existe resolver Catalog/Subscription en el handler. | `src/Services/Growth/TaxVision.Codes.Application/Quotes/CreateQuote/CreateQuoteCommand.cs`; `src/Services/Growth/TaxVision.Codes.Application/Quotes/CreateQuote/CreateQuoteHandler.cs` | DOCUMENTED_ONLY | El quote puede basarse en input no autoritativo. | Implementar resolver server-side antes de integración PaymentClient. | No; bloquea integración. |

## Checklist Referrals

| ID | Severidad | Afirmación documental | Evidencia del código | Ruta exacta | Estado | Impacto | Recomendación | ¿Bloquea scaffolding? |
|---|---|---|---|---|---|---|---|---|
| RCL-011 | INFO | Tenant-to-tenant: platform scope, PaymentApp, first success, waiting, quota y reward no monetario. | Policy default, program/attribution/qualification/reward y quota SQL reflejan esos invariants. | `src/Services/Growth/TaxVision.Referrals.Domain/Programs/ReferralProgramPolicy.cs`; `src/Services/Growth/TaxVision.Referrals.Domain/`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Repositories/Referrals/SqlReferralRewardQuota.cs` | VERIFIED | El flow MVP tiene núcleo propio. | Mantener policy versionada y decidir gross vs net para minimum payment. | No. |
| RCL-012 | INFO | Taxpayer-to-taxpayer se modela pero no se activa. | `Activate` rechaza `TaxpayerToTaxpayer`. | `src/Services/Growth/TaxVision.Referrals.Domain/Programs/ReferralProgram.cs`; `deploy/tests/TaxVision.Growth.Tests/Domain/ReferralsDomainTests.cs` | VERIFIED | Reduce riesgo de PII/pricing prematuro. | Mantener guard hasta Catalog, privacidad y antifraude. | No. |
| RCL-013 | MEDIUM | Existe provisioning seguro de programs y referral codes. | El dominio y repositorios soportan programs/codes; los casos de uso y delivery deben validarse como un flujo completo, incluida entrega del token en claro una sola vez. | `src/Services/Growth/TaxVision.Referrals.Application/Programs/`; `src/Services/Growth/TaxVision.Referrals.Application/Codes/`; `src/Services/Growth/TaxVision.Growth.Api/Controllers/` | PARTIAL | Sin provisioning/delivery completo no puede iniciarse un programa real de forma segura. | Completar permisos platform/tenant, generator criptográfico, redacción y replay sin persistir token. | No; bloquea activación funcional. |
| RCL-014 | INFO | Attribution pública deriva referee/tenant del JWT. | Controller no acepta RefereeId/TenantId del payload y el handler verifica program/code. | `src/Services/Growth/TaxVision.Growth.Api/Controllers/ReferralsController.cs`; `src/Services/Growth/TaxVision.Referrals.Application/Attributions/CreateReferralAttribution/CreateReferralAttributionHandler.cs` | VERIFIED | Evita IDOR/spoofing básico en self-service. | Añadir rate limit, abuso y pruebas HTTP negativas. | No. |
| RCL-015 | MEDIUM | Qualification y confirmaciones son accesibles solo por M2M. | Controller interno exige scopes y queda fuera del match público del Gateway. | `src/Services/Growth/TaxVision.Growth.Api/Controllers/InternalReferralsController.cs`; `src/Gateway/TaxVision.Gateway/appsettings.json`; `src/BuildingBlocks/Authorization/GrowthServiceScopes.cs` | VERIFIED | La entrada de hechos/callbacks no es pública por Gateway. | Añadir network policy y contract tests. | No. |
| RCL-016 | HIGH | FraudReview tiene workflow operativo. | Aggregate/mapping existen, pero no se encontró handler/controller/consumer que abra y resuelva review. | `src/Services/Growth/TaxVision.Referrals.Domain/Fraud/ReferralFraudReview.cs`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Configurations/Referrals/ReferralFraudReviewConfiguration.cs`; `src/Services/Growth/TaxVision.Referrals.Application/` | PARTIAL | Chargeback o señal antifraude no abre un caso operativo. | Implementar use cases/permisos/auditoría antes de chargeback-driven referrals. | No; bloquea esa integración. |

## Checklist de persistencia, idempotencia y concurrencia

| ID | Severidad | Afirmación documental | Evidencia del código | Ruta exacta | Estado | Impacto | Recomendación | ¿Bloquea scaffolding? |
|---|---|---|---|---|---|---|---|---|
| RCL-017 | INFO | Modelo EF usa schemas, constraints, índices y RowVersion. | Configurations y migration source los materializan. | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Configurations/`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Migrations/` | VERIFIED | El esquema lógico es explícito. | Revisar SQL generado y aplicar en entorno efímero. | No. |
| RCL-018 | MEDIUM | Migración inicial y permisos Auth están probados aplicándose desde cero y sobre upgrade. | Existen migrations source, pero esta auditoría no verificó su aplicación a SQL Server ni rollback/upgrade. | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Migrations/`; `src/Services/Auth/Infrastructure/Persistence/Migrations/`; `deploy/docker/migrations/apply-migrations.sh` | UNVERIFIED | Un error de nullability/constraint puede aparecer solo al guardar/aplicar. | Ejecutar migration tests clean/upgrade y comprobar no pending model changes. | No; bloquea despliegue. |
| RCL-019 | INFO | Idempotencia Codes y Referrals es tenant-scoped, insert-first, fingerprinted y replayable. | Ambos puertos usan `SqlBusinessIdempotencyExecutor` y el índice único de `ProcessedBusinessMessages`. | `src/Services/Growth/TaxVision.Growth.Infrastructure/Idempotency/SqlBusinessIdempotencyExecutor.cs`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Idempotency/SqlReferralIdempotencyExecutor.cs`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Configurations/ProcessedBusinessMessageConfiguration.cs` | VERIFIED | Reduce doble efecto por retry/redelivery. | Probar con dos conexiones SQL y cleanup/retention. | No. |
| RCL-020 | INFO | Último cupo y quota anual no hacen oversubscription. | Check constraints/RowVersion protegen counters Codes; quota usa locks/range y update condicionado. | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Configurations/Codes/CodeUsageCounterConfiguration.cs`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Repositories/Referrals/SqlReferralRewardQuota.cs` | VERIFIED | Hay estrategia de exclusión correcta en código. | Demostrarla en SQL Server bajo concurrencia. | No. |
| RCL-021 | MEDIUM | Toda la matriz de carreras está probada. | No se encontraron tests SQL para commit/cancel/expire, refund/grant o quota simultáneos. | `deploy/tests/TaxVision.Growth.Tests/`; `documents/architecture/growth/18_Growth_Concurrency_Spec.md` | DOCUMENTED_ONLY | Los guards pueden fallar en interleavings no simulados. | Implementar los escenarios del spec con transacciones reales. | No; bloquea readiness productivo. |

## Checklist de seguridad y tenancy

| ID | Severidad | Afirmación documental | Evidencia del código | Ruta exacta | Estado | Impacto | Recomendación | ¿Bloquea scaffolding? |
|---|---|---|---|---|---|---|---|---|
| RCL-022 | INFO | Tenant isolation falla cerrado. | Global filters para `ITenantOwned`, repos exactos y tenant middleware; pruebas A/B/no-tenant. | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/GrowthDbContext.cs`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Repositories/`; `deploy/tests/TaxVision.Growth.Tests/Persistence/TenantIsolationTests.cs` | VERIFIED | Reduce fuga cross-tenant. | Añadir mutation/SQL/HTTP tests. | No. |
| RCL-023 | INFO | Admins necesitan permisos explícitos; no hay bypass de rol. | Policies exactas y test de PlatformAdmin sin permiso. | `src/Services/Growth/TaxVision.Growth.Api/Authorization/GrowthAuthorization.cs`; `deploy/tests/TaxVision.Growth.Tests/Security/GrowthAuthorizationTests.cs` | VERIFIED | Evita privilegio implícito. | Auditar toda operación cross-tenant. | No. |
| RCL-024 | INFO | M2M valida actor, audience y scope. | Auth emite y Growth valida los claims; compose configura clientes. | `src/Services/Auth/Infrastructure/Security/JwtTokenGenerator.cs`; `src/Services/Growth/TaxVision.Growth.Api/Authorization/GrowthAuthorization.cs`; `deploy/docker/docker-compose.yml` | VERIFIED | Token genérico no basta. | Añadir rotación y network controls. | No. |
| RCL-025 | INFO | Tokens Codes/Referrals no se guardan completos. | HMAC con peppers obligatorios separados; display fragments y DTO redaction. | `src/Services/Growth/TaxVision.Growth.Infrastructure/Security/`; `src/Services/Growth/TaxVision.Codes.Domain/ValueObjects/CodeDisplay.cs`; `src/Services/Growth/TaxVision.Growth.Api/Controllers/` | VERIFIED | Reduce exposición por base/log. | Probar request logging y rotación de pepper. | No. |

## Checklist de mensajería e integración

| ID | Severidad | Afirmación documental | Evidencia del código | Ruta exacta | Estado | Impacto | Recomendación | ¿Bloquea scaffolding? |
|---|---|---|---|---|---|---|---|---|
| RCL-026 | INFO | Host Growth usa durable outbox/inbox. | Wolverine SQL persistence, durable outbox/inbox y EF transactions están configurados. | `src/Services/Growth/TaxVision.Growth.Api/Program.cs` | VERIFIED | Base para delivery at-least-once. | Definir provisioning/versionado físico de tablas Wolverine y probar recovery. | No. |
| RCL-027 | HIGH | Payment publica el lifecycle canónico con reservation/snapshot/referral attribution. | Contratos y consumers Growth de success/failure/cancel existen; no se encontraron producers Payment de esos tipos. | `src/BuildingBlocks/Messaging/PaymentIntegrationEvents/PaymentLifecycleIntegrationEvents.cs`; `src/Services/Growth/TaxVision.Growth.Api/IntegrationEvents/PaymentLifecycleConsumers.cs`; `src/Services/PaymentApp/`; `src/Services/PaymentClient/` | PARTIAL | Consumers no reciben hechos reales. | Implementar producer outbox en ambos Payment con contrato/monto/version exactos. | No; bloquea integración. |
| RCL-028 | BLOCKER | Payment exige reservation y verifica monto cotizado/cobrado. | Commands/endpoints de Payment no contienen reservation/promotion snapshot obligatorios. | `src/Services/PaymentClient/TaxVision.PaymentClient.Application/TenantPayments/Commands/ChargeTenantPayment/ChargeTenantPaymentCommand.cs`; `src/Services/PaymentApp/` | NOT_FOUND | Puede haber cobro sin descuento o con monto distinto. | Cambiar contrato de cobro después de aprobar el diseño y añadir no-silent-fallback. | No; bloquea integración. |
| RCL-029 | BLOCKER | Reconciliation converge timeouts y resultados desconocidos. | Verifier tardío falla closed; no hay job/reconciler. | `src/Services/Growth/TaxVision.Growth.Infrastructure/Payments/FailClosedPaymentOutcomeVerifier.cs`; `src/Services/Growth/TaxVision.Growth.Api/` | NOT_FOUND | Estados divergentes pueden quedar indefinidos. | Implementar verifier y job con lease/backoff/métricas. | No; bloquea producción. |
| RCL-030 | HIGH | Duplicados/fuera de orden se clasifican sin retry infinito. | Business idempotency existe, pero consumers financieros no usan AggregateVersion para stale/gap; transitions inválidas se elevan como excepción. | `src/Services/Growth/TaxVision.Growth.Api/IntegrationEvents/PaymentLifecycleConsumers.cs`; `src/BuildingBlocks/Messaging/PaymentIntegrationEvents/PaymentLifecycleIntegrationEvents.cs` | PARTIAL | Un cancel tardío o success equivalente con otro EventId puede reintentarse sin converger. | Persistir ordering y clasificar applied/duplicate/stale/deferred/poison. | No; bloquea robustez productiva. |
| RCL-031 | HIGH | Refund y chargeback disparan compensation/clawback/fraud review. | Contratos y mecanismos locales existen; no se encontraron consumers refund/chargeback completos ni lifecycle dispute en Payment. | `src/BuildingBlocks/Messaging/PaymentIntegrationEvents/PaymentLifecycleIntegrationEvents.cs`; `src/Services/Growth/TaxVision.Codes.Application/Compensations/`; `src/Services/Growth/TaxVision.Referrals.Application/Rewards/`; `src/Services/PaymentApp/`; `src/Services/PaymentClient/` | PARTIAL | El sistema no compensa un evento financiero real end-to-end. | Implementar matrix versionada y tests partial/full/opened/won/lost. | No; bloquea producción. |
| RCL-032 | HIGH | Subscription materializa y confirma reward/grant. | Contrato y lifecycle Referrals existen; no se encontró consumer Subscription ni publicación Growth completa. | `src/BuildingBlocks/Messaging/GrowthIntegrationEvents/GrowthCommands.cs`; `src/Services/Growth/TaxVision.Referrals.Application/Rewards/`; `src/Services/Subscription/` | DOCUMENTED_ONLY | Reward queda pendiente sin efecto. | Implementar command/ack/reject idempotentes por GrantId. | No; bloquea rewards. |

## Checklist de pruebas y operación

| ID | Severidad | Afirmación documental | Evidencia del código | Ruta exacta | Estado | Impacto | Recomendación | ¿Bloquea scaffolding? |
|---|---|---|---|---|---|---|---|---|
| RCL-033 | INFO | Existen unit/application/architecture/security/tenant tests. | Proyecto Growth Tests contiene esas categorías y replay Referrals. | `deploy/tests/TaxVision.Growth.Tests/` | VERIFIED | Hay regresión básica de dominio y límites. | Mantener suite verde en CI. | No. |
| RCL-034 | HIGH | Existen contract, SQL integration, E2E, migration, out-of-order, load y failure-injection tests. | No se encontraron esas suites completas. | `deploy/tests/TaxVision.Growth.Tests/`; `documents/architecture/growth/22_Growth_Test_Strategy.md` | DOCUMENTED_ONLY | No hay evidencia de garantías distribuidas/operativas. | Implementarlas antes de production review. | No; bloquea producción. |
| RCL-035 | MEDIUM | Audit y observabilidad cubren todos los casos sensibles. | Audit entity append-only y métricas base existen; no se encontró escritura de audit para cada handler/admin/fraud/cross-tenant ni dashboards/alerts. | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Audit/GrowthAuditEntry.cs`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Observability/GrowthMetrics.cs`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/GrowthDbContext.cs` | PARTIAL | Operaciones sensibles pueden carecer de rastro/alerta de negocio completo. | Añadir audit writer, métricas por resultado y alertas de reconciliation/poison/idempotency conflict. | No; bloquea production review. |
| RCL-036 | MEDIUM | La migración desde CRM legado está lista. | Se identificaron fuentes DiscountCoupon/Referral/Wallet/TaxCoin, pero no scripts de backfill/cutover/reconciliation. | `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND\PaymentService\Domain\DiscountCoupon.cs`; `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND\ReferralService\Infrastructure\Context\ApplicationDbContext.cs`; `documents/architecture/growth/23_Growth_Migration_Strategy.md` | DOCUMENTED_ONLY | Riesgo de duplicar códigos/rewards o importar saldos fuera de ownership. | Diseñar mapping, dry-run, reconciliación y exclusión explícita de wallet/TaxCoin. | No; bloquea cutover, no scaffold. |

## Bloqueadores antes de integración

- autoridad server-side de oferta/precio para PaymentClient;
- reservation/snapshot/net amount obligatorios en Payment;
- producers outbox de Payment lifecycle;
- decisión explícita: minimum referral payment sobre gross, net o captured amount;
- ordering por AggregateVersion/DisputeVersion y clasificación stale/duplicate;
- verifier de resultado tardío y reconciliation job;
- refund/chargeback policy ejecutable;
- Subscription consumer para grant/reward.

## Bloqueadores antes de production review

- aplicar y probar migrations clean/upgrade;
- SQL concurrency/replay tests;
- contract y E2E Payment–Growth–Subscription;
- out-of-order/failure injection/poison handling;
- audit coverage y alerting;
- load/retention/cleanup;
- migration/cutover del legado;
- privacidad/antifraude si se considera taxpayer-to-taxpayer.

## Decisiones pendientes

1. Fuente de verdad de precio tenant-cliente.
2. Gross vs net vs captured amount para qualification.
3. Policy mapper refund/chargeback por beneficio.
4. Semántica de dispute opened/won/lost por provider.
5. Catálogo y materialización de rewards no monetarios.
6. Retención de idempotency, attribution, audit y PII.
7. Provisioning de tablas Wolverine en entornos sin permisos DDL.
8. Triggers cuantitativos de extracción Codes/Referrals.

## Criterio de salida

No avanzar a `READY_FOR_INTEGRATION` hasta cerrar RCL-010, RCL-027, RCL-028 y acordar la semántica de qualification. No avanzar a `READY_FOR_PRODUCTION_REVIEW` hasta cerrar reconciliation, compensation/chargeback, Subscription rewards, migraciones aplicadas y las suites distribuidas.
