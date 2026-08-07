# Estrategia y brechas de pruebas

Hay suites xUnit por 17 servicios/building blocks, integración con factories y SQL/compose, architecture tests y k6. Billing presenta cobertura especialmente escasa para onboarding (`ScaffoldSmokeTests`); Auth prueba el handler normal, pero la búsqueda no mostró asserts de FullyCovered/ajustes A–D completos.

## Matriz mínima faltante

| Escenario | Unit | Integration SQL | Contract | E2E multi-service |
|---|---:|---:|---:|---:|
| A 100/0/100 | parcial | faltante | faltante | faltante |
| B 100/30/70 | parcial por dominio | faltante | faltante | faltante |
| C invoice 0 sin Payment | faltante | faltante | faltante | faltante |
| D dos instrumentos/payment 20 | faltante | faltante | faltante | faltante |
| doble checkout/webhook | parcial | faltante | n/a | faltante |
| last-use concurrent redemption | dominio/fake | faltante real | n/a | faltante |

## Casos obligatorios

Duplicate request, concurrent redemption, 100% discount, expired code/reservation, payment retry, duplicate webhook/event, consumer crash antes/después de SaveChanges, DB/broker/provider unavailable, rehome retry, refund con restauración de gift y golden PDF con ajustes.

### TST-001

**HIGH/P1/Large.** Sin estos tests no puede afirmarse que la arquitectura soporta A–D en ejecución; solo que los caminos nominales están codificados.

### TST-002 — suite Auth rota

**HIGH/P0/Small.** El comando `dotnet test deploy/tests/TaxVision.Auth.Tests/TaxVision.Auth.Tests.csproj --no-restore --filter ...` del 2026-08-07 falla en compilación: `OnboardingPaymentSucceededConsumerTests.cs:90` intenta leer `OnboardingFinalizeCommand.PaidAmountCents`, que ya no existe. Componentes: Auth tests/Application. Impacto: CI no puede validar regresiones del consumer que conecta payment con finalize. Solución: actualizar el assert al contrato vigente (`Gross/Discount/Net`) y hacer obligatoria la suite en CI.

## Verificación ejecutada

La compilación alcanzó BuildingBlocks, Auth Domain/Application/Infrastructure/API y emitió además dos warnings `CS0108` por `TenantId` oculto en eventos de Signature; se detuvo en `TST-002`. No se afirma que las demás suites pasen.
