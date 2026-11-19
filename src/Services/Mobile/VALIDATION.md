# Validation record — Mobile

Executed on 2026-11-18 from the backend root unless another directory is stated. Installed SDK: 10.0.303, accepted by global.json roll-forward from 10.0.300. Package versions were kept aligned with the repository.

## Completed checks

| Command | Result |
| --- | --- |
| `dotnet restore TaxVision.slnx --nologo` | Passed. |
| `dotnet build TaxVision.slnx --no-restore --nologo` | Passed, 0 errors, 130 warnings on the first complete build. Subsequent gate builds include the final changes. |
| `dotnet build src/Services/Mobile/TaxVision.Mobile.Api/TaxVision.Mobile.Api.csproj --nologo` | Passed, 0 errors and 0 warnings on the initial service build. |
| `dotnet ef migrations add InitialCreate --project src/Services/Mobile/TaxVision.Mobile.Infrastructure --startup-project src/Services/Mobile/TaxVision.Mobile.Api --output-dir Persistence/Migrations` | Generated Mobile schema and snapshot; subsequently validated by applying it in a temporary SQL database. |
| `dotnet ef migrations add AddMobileVehiclePermissions --project src/Services/Auth/Infrastructure --startup-project src/Services/Auth/Api --output-dir Persistence/Migrations` | Generated two permission catalog inserts only; no grants in RolePermissions. |
| `powershell -ExecutionPolicy Bypass -File scripts/codegen/regenerate-wolverine-code.ps1 -Service Mobile` | Passed; generated handlers are included in the implementation. Both production codegen checks were first observed failing before generation. |
| `dotnet test deploy/tests/TaxVision.Mobile.Tests --nologo` | 34 passed at that point, including 2 SQL tests. A third SQL/Wolverine test was subsequently added and passed separately. |
| `dotnet test deploy/tests/TaxVision.Mobile.Tests --nologo --filter 'FullyQualifiedName~.Integration.'` | 3 passed on local SQL Server development instance. |
| `dotnet csharpier format <implementation paths>` | Formatted 52 C# files plus the Auth permission migration (1 file); generated files and snapshots follow the repository ignore rules. |
| JSON/YAML parsing and PowerShell AST parsing | Gateway/Mobile/Postman JSON, Compose/workflow YAML and changed fleet/codegen scripts parsed successfully. No Docker command was executed. |
| `git diff --check` | Passed. |

SQL tests use `MOBILE_DB_CONNECTION` solely to locate a local development SQL instance and its Windows authentication. BuildingBlocks TemporaryTestDatabase creates and deletes a unique `mob_tint_*` database per process. No existing service database was migrated or modified. The initial default SQL instance rejected the Windows login; the development instance worked. Its first migration execution caught an invalid filter on a computed index; this was corrected before the successful runs.

SQL scenarios cover migration application, CRUD, query translation, unique plate enforcement, rowversion conflicts, owner isolation, unique default switching and rollback. The real HTTP/Wolverine/SQL test checks a 201 create, 409 duplicate, rollback preserving the old default, successful default replacement, read and delete. RabbitMQ transports are disabled in tests and Redis counters are substituted. These tests do not claim live RabbitMQ or Redis verification.

HTTP gate tests exercise the real authentication/authorization/MVC pipeline with test JWTs and a direct test bus over EF InMemory. They verify 401/403, session denylist, all REST actions, invalid inputs and cross-owner/tenant 404s, including spoofed owner fields in a create body. Separate SQL tests exercise the real Wolverine transaction pipeline.

## Mobile app checks (D:/TaxPro-Milage)

- `npm run build`: passed.
- `npm run lint`: passed.
- `npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/services/tracking-api.service.spec.ts`: 4 passed in Chrome Headless. Covers field stripping, create/update/delete methods, pagination and typed default/empty selection.

## Full repository gate

Exact command:

```powershell
dotnet test TaxVision.slnx --nologo --filter "FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence.&FullyQualifiedName!~ReminderRetentionJobTests"
```

**Full gate: incomplete, not green.** The first attempt overlapped another build and hit Windows compiler file locks. The sequential rerun reported the two failures below, then stopped producing results; that owned test process tree was terminated. No unrelated source was changed:

- `TaxVision.BuildingBlocks.Tests.Imaging.ConcurrencyLimitedAvatarImageNormalizerTests.NormalizeAsync_CanceladoMientrasEspera_NoConsumeTurno`: expected cancellation, no exception. Isolated rerun: **1 passed**, 18 ms.
- `TaxVision.Connectors.Tests.RateLimit.InMemoryProviderRateLimiterTests.WaitForSlotAsync_WithinLimit_DoesNotDelay`: elapsed time exceeded the test's 200 ms bound. Isolated rerun: **1 passed**, 40 ms.

The full run completed Gateway (**219 passed**), DataCollection.Engine (**378 passed**) and ContentScanner (**20 passed**) before termination. It reported one skipped Redis counter test. Other project totals were not completed, so no full-suite pass is claimed.

Additional completed commands:

```powershell
dotnet test deploy/tests/TaxVision.Mobile.Tests --no-build --nologo --filter 'FullyQualifiedName!~.Integration.'
# 32 passed, including production generated-code freshness/loading checks.

dotnet test deploy/tests/TaxVision.BuildingBlocks.Tests --no-build --nologo --filter 'FullyQualifiedName~.Deploy.|FullyQualifiedName~.Offboarding.'
# 212 passed, 0 skipped.

dotnet test deploy/tests/TaxVision.Auth.Tests --no-build --nologo --filter 'FullyQualifiedName~PermissionCatalogTests|FullyQualifiedName~PermissionCeilingTests|FullyQualifiedName~SystemRolePermissionsSyncServiceTests|FullyQualifiedName~DeclaredPermissionsAreEnforcedTests|FullyQualifiedName~EnforcedPermissionsFitnessTests|FullyQualifiedName~ReservedPermissionsFitnessTests'
# 117 passed.

dotnet test deploy/tests/TaxVision.BuildingBlocks.Tests --no-build --nologo --filter 'FullyQualifiedName~NormalizeAsync_CanceladoMientrasEspera_NoConsumeTurno'
dotnet test deploy/tests/TaxVision.Connectors.Tests --no-build --nologo --filter 'FullyQualifiedName~WaitForSlotAsync_WithinLimit_DoesNotDelay'
# 1 passed in each isolated rerun.

dotnet ef migrations has-pending-model-changes --no-build --project src/Services/Mobile/TaxVision.Mobile.Infrastructure --startup-project src/Services/Mobile/TaxVision.Mobile.Api
dotnet ef migrations has-pending-model-changes --no-build --project src/Services/Auth/Infrastructure --startup-project src/Services/Auth/Api
# Both: no changes since the latest migration.
```

Final mobile lint was rerun after adding its contract tests and passed.

## Deployment and verification limits

- Auth's migration has been generated but not applied to an existing Auth database. Auth must run its normal system-role synchronization after deployment.
- Mobile's migration was applied to temporary test databases; no persistent local Mobile database or secrets were provisioned.
- Required deployment values are `MOBILE_DB_CONNECTION`, `MOBILE_SERVICE_CLIENT_ID` and `MOBILE_SERVICE_CLIENT_SECRET` in production-extra. No production environment, secret, container or account was changed.
- Real Auth login/refresh, a physical mobile device, Gateway-to-service traffic over the running fleet, Redis rate/denylist behavior, RabbitMQ consumption and manual offboarding handover were not tested live.
- Vehicle photos use HTTPS references; binary uploads are outside this service's scope.
- The three backend Node services and sibling Angular frontends were not changed; their test suites were not run.
