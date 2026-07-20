# Propuesta de AGENTS.md

No existe `AGENTS.md` en el repositorio al 2026-07-19. No se crea en esta fase. Contenido propuesto:

```markdown
# TaxVision repository instructions

## Arquitectura
- .NET 10, Clean Architecture, CQRS/Wolverine.
- Cada servicio posee dominio y DB. Sin acceso DB cross-service.
- BuildingBlocks contiene primitives/contratos técnicos, no repositories/tablas de negocio.
- Growth contiene dos bounded contexts: Codes y Referrals; sin FK ni aggregate compartido.

## Comandos seguros
- Lectura: `git status`, `git diff`, `dotnet sln list`, `rg`.
- Build/test solo tras revisar cambios del usuario: `dotnet build TaxVision.slnx`, `dotnet test ...`.
- No formatters, migraciones, commits, push o comandos destructivos sin solicitud.

## Capas y DDD
- Domain no referencia Infrastructure/API/provider SDK.
- Mutaciones mediante métodos de aggregate que retornan Result.
- Un archivo/clase; IDs application-assigned; domain events internos.
- Dinero en minor units + currency en Payment/Growth; nunca float/double.

## EF Core
- Configurations por entidad; RowVersion para aggregates concurrentes.
- Unique/check constraints reflejan invariantes.
- Tenant query filter + repository scope; índices incluyen TenantId cuando corresponde.
- Migraciones solo con aprobación, revisión SQL y rollback/backfill.

## Seguridad
- Permission + TenantId + resource ownership; sin admin bypass genérico.
- M2M audience/scope explícitos; internos fuera Gateway.
- No secretos, PII, gift token completo o PAN en código/log/evento.

## Mensajería
- At-least-once; handlers idempotentes; inbox/outbox y state guards.
- Contratos versionados con correlation/causation/trace/aggregate version.
- No publicar cambios internos innecesarios.

## Commits
- No mezclar cambios ajenos. Commits pequeños, mensaje descriptivo, tests indicados.
- No modificar contratos, migraciones, Gateway, Auth permissions o Payment sin aprobación.

## Definition of Done
- Invariantes y ownership documentados.
- Unit/integration/contract/concurrency/replay/security tests.
- Observabilidad y redacción sensible.
- Migration/rollback/reconciliation cuando aplique.
- Cero DESIGN_BLOCKER para iniciar scaffolding.

## Archivos protegidos
- `.env`, `dev-keys/`, migration snapshots, `TaxVision.slnx`, Gateway routes,
  BuildingBlocks messaging contracts y Auth PermissionCatalog requieren aprobación explícita.
```

Estado de esta propuesta: **DOCUMENTED_ONLY**.

