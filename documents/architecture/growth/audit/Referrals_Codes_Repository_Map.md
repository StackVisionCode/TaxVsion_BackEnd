# Referrals y Codes — mapa verificado del repositorio

## Alcance y corte

- **Repositorio auditado:** `C:\Users\wagne\OneDrive\Documentos\TaxVision\TaxVsion_BackEnd`
- **CRM legado auditado:** `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND`
- **Documentación adicional auditada:** `C:\Users\wagne\OneDrive\Documentos\TaxVision\Implementaciones`
- **Fecha de corte:** 2026-07-19
- **Método:** inspección estática de solo lectura. La existencia de pruebas se verificó en código fuente; este documento no presume que hayan sido ejecutadas.

Los estados usados en este informe son exclusivamente: `VERIFIED`, `DOCUMENTED_ONLY`, `PARTIAL`, `CONTRADICTED`, `NOT_FOUND` y `UNVERIFIED`.

## Mapa ejecutivo

```text
TaxVision.Growth                                      un deployment inicial
├── TaxVision.Growth.Api                             host y composición
├── TaxVision.Growth.Infrastructure                  persistencia e infraestructura compartida
├── Codes                                            bounded context 1
│   ├── TaxVision.Codes.Domain
│   └── TaxVision.Codes.Application
└── Referrals                                        bounded context 2
    ├── TaxVision.Referrals.Domain
    └── TaxVision.Referrals.Application

TaxVision_Growth                                     una base inicial
├── codes                                            datos de Codes
├── referrals                                        datos de Referrals
├── integration                                      inbox, outbox e idempotencia
└── audit                                            auditoría
```

`Codes` y `Referrals` son bounded contexts distintos. Compartir host, infraestructura y base de datos inicial no los convierte en un solo bounded context.

## Inventario por superficie

### Solución y proyectos de Growth

| Superficie | Evidencia exacta | Lectura |
|---|---|---|
| Registro en solución | `TaxVision.slnx` | Registra los seis proyectos de Growth y `TaxVision.Growth.Tests`. |
| Dominio Codes | `src/Services/Growth/TaxVision.Codes.Domain/` | Definitions, rules, scopes, quotes, reservations, redemptions, compensations y usage counters. |
| Aplicación Codes | `src/Services/Growth/TaxVision.Codes.Application/` | Administración de códigos y protocolo quote/reserve/commit/cancel/expire/compensate. |
| Dominio Referrals | `src/Services/Growth/TaxVision.Referrals.Domain/` | Programs, referral codes, attribution, qualification, rewards y fraud review. |
| Aplicación Referrals | `src/Services/Growth/TaxVision.Referrals.Application/` | Attribution, qualification, reward grant y clawback. |
| Infraestructura compartida | `src/Services/Growth/TaxVision.Growth.Infrastructure/` | `GrowthDbContext`, mapeos EF, repositorios, idempotencia, hashing, cuota y verificación de pagos. |
| Host único | `src/Services/Growth/TaxVision.Growth.Api/` | API, autorización, tenant middleware, Wolverine, RabbitMQ, health checks y composición. |
| Referencias entre módulos | `deploy/tests/TaxVision.Growth.Tests/Architecture/BoundedContextArchitectureTests.cs` | Impide referencias directas entre Domain/Application de ambos bounded contexts. |

Los `.csproj` de Domain/Application no enlazan el otro bounded context. La infraestructura y el host sí pueden componer ambos módulos, que es el punto de integración permitido.

### BuildingBlocks

| Área | Ruta exacta | Contenido comprobado |
|---|---|---|
| Permisos humanos | `src/BuildingBlocks/Authorization/GrowthPermissions.cs` | Permisos explícitos para Codes y Referrals. |
| Scopes M2M | `src/BuildingBlocks/Authorization/GrowthServiceScopes.cs` | Scopes separados para quote, reserve, commit, cancel, compensate y operaciones de rewards/grants. |
| Eventos de pago | `src/BuildingBlocks/Messaging/PaymentIntegrationEvents/PaymentLifecycleIntegrationEvents.cs` | Contratos versionados de éxito, refund y cambio de chargeback. |
| Eventos de Codes | `src/BuildingBlocks/Messaging/GrowthIntegrationEvents/CodeIntegrationEvents.cs` | Commit, compensación y confirmación de grants. |
| Eventos de Referrals | `src/BuildingBlocks/Messaging/GrowthIntegrationEvents/ReferralIntegrationEvents.cs` | Qualification y ciclo del reward. |
| Comandos Growth | `src/BuildingBlocks/Messaging/GrowthIntegrationEvents/GrowthCommands.cs` | Comando para solicitar materialización del reward. |
| Catálogo Auth | `src/Services/Auth/Infrastructure/Persistence/PermissionCatalog.cs` | Registros de permisos Growth. |
| Token M2M | `src/Services/Auth/Application/ServiceTokens/ServiceAuthOptions.cs`; `src/Services/Auth/Infrastructure/Security/JwtTokenGenerator.cs` | Audience y scopes configurables y emitidos en JWT de servicio. |

BuildingBlocks contiene contratos y constantes compartidos; no contiene repositorios ni tablas de negocio de Codes o Referrals.

### PaymentApp y PaymentClient

| Servicio | Ruta exacta | Resultado |
|---|---|---|
| Cobro SaaS | `src/Services/PaymentApp/` | No contiene dominio ni persistencia actual de Codes. Tampoco integra todavía el protocolo de Growth. |
| Cobro tenant-cliente | `src/Services/PaymentClient/TaxVision.PaymentClient.Api/Controllers/TenantPaymentsController.cs` | Recibe `AmountCents` desde la solicitud. |
| Command de cobro tenant-cliente | `src/Services/PaymentClient/TaxVision.PaymentClient.Application/TenantPayments/Commands/ChargeTenantPayment/ChargeTenantPaymentCommand.cs` | No exige `QuoteId`, `ReservationId` ni snapshot promocional. |
| Webhook PaymentApp | `src/Services/PaymentApp/TaxVision.PaymentApp.Application/SaaSPayments/Commands/ProcessStripeWebhook/ProcessStripeWebhookHandler.cs` | Distingue transición inválida como stale y calcula delta de refund acumulado. |
| Webhook PaymentClient | `src/Services/PaymentClient/TaxVision.PaymentClient.Application/TenantPayments/Commands/ProcessTenantWebhook/ProcessTenantWebhookHandler.cs` | Distingue stale y calcula delta de refund acumulado. |
| Persistencia Payment | `src/Services/PaymentApp/TaxVision.PaymentApp.Infrastructure/Persistence/PaymentAppDbContext.cs`; `src/Services/PaymentClient/TaxVision.PaymentClient.Infrastructure/Persistence/PaymentClientDbContext.cs` | Traduce conflictos de unicidad, pero no se encontró traducción de `DbUpdateConcurrencyException`. |

Los contratos de lifecycle de pago existen, pero no se encontraron productores de esos contratos ni consumidores Growth conectados al flujo de negocio.

### Subscription, Catalog y ownership

| Ownership | Ruta exacta | Resultado |
|---|---|---|
| Precio SaaS | `src/Services/Subscription/TaxVision.Subscription.Domain/Plans/SubscriptionPlanVersion.cs` | Subscription mantiene precio/versiones SaaS. |
| Trials | `src/Services/Subscription/TaxVision.Subscription.Domain/Subscriptions/TenantSubscription.cs` | Subscription mantiene el lifecycle del trial. |
| Entitlements | `src/Services/Subscription/TaxVision.Subscription.Domain/Entitlements/TenantEntitlementSnapshot.cs` | Subscription mantiene el snapshot de entitlements. |
| Reward lifecycle | `src/Services/Growth/TaxVision.Referrals.Domain/Rewards/ReferralRewardCase.cs` | Referrals mantiene intención, estado, grant y clawback. |
| Materialización del reward | `src/Services/Subscription/` | No se encontró consumidor de `GrantReferralRewardCommand`. |
| Catálogo tenant-cliente | `src/Services/` | No se encontró un servicio Catalog/ProductsAndServices implementado. |
| Ledger/wallet actual | `src/Services/Growth/` | No se encontró wallet, TaxCoin ni ledger financiero en Growth. |

### API, Gateway y despliegue

| Superficie | Ruta exacta | Resultado |
|---|---|---|
| Endpoints humanos Codes | `src/Services/Growth/TaxVision.Growth.Api/Controllers/CodesController.cs` | Ruta pública `growth/codes` con permisos explícitos. |
| Endpoints M2M Codes | `src/Services/Growth/TaxVision.Growth.Api/Controllers/InternalCodesController.cs` | Ruta `internal/codes` para el protocolo transaccional. |
| Endpoints Referrals | `src/Services/Growth/TaxVision.Growth.Api/Controllers/` | No se encontró controller de Referrals al corte. |
| Ruta pública | `src/Gateway/TaxVision.Gateway/appsettings.json` | El Gateway publica únicamente `/growth/{**catch-all}` para Growth. |
| Compose real | `deploy/docker/docker-compose.yml` | Define `growth-api`, conexión, audience, pepper, dependencias y cluster del Gateway. |
| Compose en raíz | `docker-compose.yml` | No existe en la raíz; el archivo operativo está bajo `deploy/docker/`. |
| Runner de migraciones | `deploy/docker/migrations/apply-migrations.sh` | Invoca el proyecto de infraestructura Growth. |
| Migraciones Growth | `src/Services/Growth/TaxVision.Growth.Infrastructure/Migrations/` | No existe al corte. |

Como el Gateway solo hace match de `/growth/**`, los endpoints `/internal/codes/**` no quedan publicados por esa ruta.

### Persistencia

| Esquema | Ruta exacta | Entidades verificadas |
|---|---|---|
| `codes` | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Configurations/Codes/` | Definitions, rules, scopes, quotes, reservations, redemptions, compensations y usage counters. |
| `referrals` | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Configurations/Referrals/` | Programs, codes, attributions, qualifications, rewards, attempts, fraud reviews y cuotas. |
| `integration` | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Configurations/ProcessedBusinessMessageConfiguration.cs`; `src/Services/Growth/TaxVision.Growth.Api/Program.cs` | Idempotencia de negocio y persistencia Wolverine de inbox/outbox. |
| `audit` | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Configurations/GrowthAuditEntryConfiguration.cs` | Audit entries append-only a nivel de `GrowthDbContext`. |
| Aislamiento tenant | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/GrowthDbContext.cs` | Filtro global fail-closed para toda entidad `ITenantOwned`. |

No se encontraron foreign keys entre aggregates de Codes y Referrals. El `GrowthDbContext` compartido no debe interpretarse como autorización para referencias de dominio cruzadas.

### Pruebas

El proyecto `deploy/tests/TaxVision.Growth.Tests/` contiene:

- arquitectura: `Architecture/BoundedContextArchitectureTests.cs`;
- dominio Codes: `Domain/CodesDomainTests.cs`;
- dominio Referrals: `Domain/ReferralsDomainTests.cs`;
- aplicación Codes: `Application/CodesAdministrationApplicationTests.cs`, `CodesQuoteApplicationTests.cs` y `CodesReservationApplicationTests.cs`;
- tenant isolation: `Persistence/TenantIsolationTests.cs`;
- autorización: `Security/GrowthAuthorizationTests.cs`;
- extracción de tenant JWT: `Security/JwtTenantContextMiddlewareTests.cs`.

No se encontraron suites Growth de integración real contra SQL Server, contract tests entre Payment/Growth/Subscription, E2E, carga, fault injection, orden fuera de secuencia ni pruebas de migración.

### CRM legado

| Hallazgo legado | Ruta exacta | Lectura |
|---|---|---|
| Codes dentro de Payment | `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND\PaymentService\Domain\DiscountCoupon.cs`; `...\CouponUsage.cs`; `...\CouponRequest.cs` | PaymentService persistía cupón, uso y solicitud. |
| API de cupones | `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND\PaymentService\Applications\Controllers\DiscountCouponController.cs` | Administración de descuentos estaba dentro de PaymentService. |
| Referrals | `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND\ReferralService\Domain\Referral.cs`; `...\ReferrerCode.cs`; `...\ReferralReward.cs` | Un servicio separado modelaba referral, código y reward. |
| Wallet/TaxCoin mezclado | `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND\ReferralService\Domain\Wallet.cs`; `...\TaxCoin.cs`; `...\Infrastructure\Context\ApplicationDbContext.cs` | El mismo contexto persistía referrals, wallet y moneda virtual. |
| Acoplamiento financiero | `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND\ReferralService\Application\Handlers\Wallets\`; `...\Handlers\TaxCoins\` | El servicio legado aplicaba débitos, créditos, transfers y TaxCoin. |

El legado es evidencia de capacidades y riesgos de migración, no una frontera que deba conservarse.

### Documentación inspeccionada

Se inspeccionó la serie completa:

`documents/architecture/growth/01_Growth_Executive_Summary.md` a `documents/architecture/growth/30_AGENTS_MD_Proposal.md`.

También se inspeccionaron:

- `C:\Users\wagne\OneDrive\Documentos\TaxVision\Implementaciones\PaymentServices_Analysis_And_Design.md`
- `C:\Users\wagne\OneDrive\Documentos\TaxVision\Implementaciones\PaymentServices_ExternalSecurityReview_Scope.md`
- `C:\Users\wagne\OneDrive\Documentos\TaxVision\Implementaciones\ProductsAndServices_README.md`
- `C:\Users\wagne\OneDrive\Documentos\TaxVision\Implementaciones\ProductsAndServices_Service_Analysis_And_Design.md`
- `C:\Users\wagne\OneDrive\Documentos\TaxVision\Implementaciones\Referrals_README.md`
- `C:\Users\wagne\OneDrive\Documentos\TaxVision\Implementaciones\Referrals_Service_Analysis_And_Design.md`
- `C:\Users\wagne\OneDrive\Documentos\TaxVision\Implementaciones\Subscription_Service_Analysis_And_Design.md`
- las copias 06–11 y 27 existentes en `Implementaciones`.

Los PDF `PaymentServices_Analysis_And_Design.pdf` y `Subscription_Service_Analysis_And_Design.pdf` existen, pero su equivalencia con los Markdown homónimos no fue verificada de forma independiente.

## Hallazgos trazables

| ID | Severidad | Afirmación documental | Evidencia del código | Ruta exacta | Estado | Impacto | Recomendación | ¿Bloquea scaffolding? |
|---|---|---|---|---|---|---|---|---|
| MAP-001 | INFO | Growth debe ser un deployment con dos bounded contexts extraíbles. | Existen seis proyectos, con Domain/Application separados y un host/infraestructura comunes; una prueba impide referencias cruzadas. | `TaxVision.slnx`; `src/Services/Growth/`; `deploy/tests/TaxVision.Growth.Tests/Architecture/BoundedContextArchitectureTests.cs` | VERIFIED | La estructura implementada materializa la opción arquitectónica recomendada. | Mantener dependency rules y revisarlas en CI. | No; ya existe. |
| MAP-002 | INFO | Codes no debe vivir dentro de PaymentApp ni PaymentClient. | No se encontraron proyectos, entidades ni repositorios Codes en los servicios Payment actuales; Codes reside en Growth. | `src/Services/Growth/TaxVision.Codes.Domain/`; `src/Services/Growth/TaxVision.Codes.Application/`; `src/Services/PaymentApp/`; `src/Services/PaymentClient/` | VERIFIED | El ownership promocional dejó de estar acoplado al cobro. | Prohibir dependencias directas de Payment hacia Domain/Infrastructure de Codes; integrar por contrato. | No. |
| MAP-003 | HIGH | Growth usa base inicial con esquemas `codes`, `referrals`, `integration` y `audit`. | Los mappings y Wolverine declaran esos esquemas, pero no existe migración Growth que los materialice. | `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/GrowthSchemas.cs`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Persistence/Configurations/`; `src/Services/Growth/TaxVision.Growth.Api/Program.cs`; `src/Services/Growth/TaxVision.Growth.Infrastructure/Migrations/` | PARTIAL | El modelo existe en código, pero una instalación limpia no puede crear una base versionada y repetible. | Crear y revisar la migración inicial solo en la fase de migraciones autorizada; añadir prueba de migración limpia y upgrade. | No; bloquea despliegue, no scaffolding. |
| MAP-004 | INFO | El deployment inicial debe llamarse `TaxVision.Growth`. | Compose, solución, Dockerfile, Gateway y runner registran Growth. | `deploy/docker/docker-compose.yml`; `src/Services/Growth/TaxVision.Growth.Api/Dockerfile`; `src/Gateway/TaxVision.Gateway/appsettings.json`; `deploy/docker/migrations/apply-migrations.sh` | VERIFIED | Hay una unidad de despliegue coherente. | Mantener un solo deployment hasta que se cumpla un trigger explícito de extracción. | No. |
| MAP-005 | MEDIUM | Endpoints internos no deben exponerse por Gateway. | Gateway publica `/growth/**`; los endpoints M2M usan `/internal/codes/**`. | `src/Gateway/TaxVision.Gateway/appsettings.json`; `src/Services/Growth/TaxVision.Growth.Api/Controllers/InternalCodesController.cs` | VERIFIED | El contrato interno no coincide con la ruta pública. | Añadir prueba de routing/deployment y controles de red; la ausencia de match no sustituye segmentación de red. | No. |
| MAP-006 | HIGH | Referrals debe estar disponible dentro del deployment Growth. | Domain/Application y repositorios existen, pero no se encontró controller ni consumidor de eventos de pago para iniciar sus casos de uso. | `src/Services/Growth/TaxVision.Referrals.Domain/`; `src/Services/Growth/TaxVision.Referrals.Application/`; `src/Services/Growth/TaxVision.Growth.Api/Controllers/` | PARTIAL | El modelo puede compilar, pero no tiene entrada funcional end-to-end. | Definir API administrativa/atribución y consumidores de lifecycle de pago con autorización e idempotencia. | No; bloquea integración funcional. |
| MAP-007 | MEDIUM | Debe existir una cobertura verificable de concurrencia, replay y aislamiento. | Hay unit tests y pruebas in-memory; faltan SQL Server integration, contract, E2E, out-of-order, migration, load y failure-injection tests. | `deploy/tests/TaxVision.Growth.Tests/` | PARTIAL | Varias garantías dependen del comportamiento real de SQL Server y Wolverine y aún no están demostradas. | Implementar la pirámide de pruebas de `22_Growth_Test_Strategy.md` y conservar resultados CI. | No; bloquea readiness productivo. |
| MAP-008 | HIGH | Payment y Subscription deben integrarse por eventos/comandos de Growth. | Los contratos están en BuildingBlocks, pero no se encontraron productores de Payment lifecycle, consumidores Growth ni consumidor Subscription de reward grant. | `src/BuildingBlocks/Messaging/PaymentIntegrationEvents/PaymentLifecycleIntegrationEvents.cs`; `src/BuildingBlocks/Messaging/GrowthIntegrationEvents/`; `src/Services/PaymentApp/`; `src/Services/PaymentClient/`; `src/Services/Subscription/` | DOCUMENTED_ONLY | El flujo distribuido termina en límites del módulo; no hay efecto observable completo. | Implementar routing, producers, consumers, inbox/idempotencia y confirmaciones antes de habilitar descuentos o rewards. | No; bloquea integración. |
| MAP-009 | MEDIUM | El legado puede reutilizarse como diseño de frontera actual. | El legado mezcla cupones con Payment y referrals con wallet/TaxCoin; el código actual separa esas responsabilidades. | `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND\PaymentService\Domain\DiscountCoupon.cs`; `C:\Users\wagne\OneDrive\Documentos\cloudtax\Develop\CRMTAXPROBACKEND\ReferralService\Infrastructure\Context\ApplicationDbContext.cs`; `src/Services/Growth/` | CONTRADICTED | Copiar el legado reintroduciría acoplamiento promocional y financiero. | Migrar datos/semántica explícitamente; no portar fronteras, tablas ni wallet a Growth. | No. |
| MAP-010 | LOW | Los PDF homónimos contienen la misma decisión que los Markdown revisados. | Se verificó su existencia, pero no se extrajo ni comparó su contenido. | `C:\Users\wagne\OneDrive\Documentos\TaxVision\Implementaciones\PaymentServices_Analysis_And_Design.pdf`; `C:\Users\wagne\OneDrive\Documentos\TaxVision\Implementaciones\Subscription_Service_Analysis_And_Design.pdf` | UNVERIFIED | Puede existir drift documental no detectado. | Comparar texto/versión de los PDF con los Markdown o declarar Markdown como fuente canónica. | No. |
| MAP-011 | INFO | Debe inspeccionarse un `docker-compose.yml` en la raíz. | No existe en raíz; el compose activo está en `deploy/docker/docker-compose.yml`. | `docker-compose.yml`; `deploy/docker/docker-compose.yml` | NOT_FOUND | Una ruta equivocada puede causar auditorías o scripts incompletos. | Documentar `deploy/docker/docker-compose.yml` como ubicación canónica. | No. |

## Resultado del mapa

La frontera actual ya no corresponde ni al CRM legado ni a los documentos que proponen Codes dentro de Payment o un único servicio `TaxVision.Referrals` dueño de todo. El repositorio materializa un **servicio modular `TaxVision.Growth`, con dos bounded contexts estrictos y extraíbles**. La topología está lista; la integración Payment/Growth/Subscription, la autoridad de precio tenant-cliente, los consumers Referrals y las migraciones siguen pendientes.
