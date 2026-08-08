# Campaigns — Deployment

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Microservicio .NET independiente, mismo estilo que el resto del monorepo (`src/Services/*`), con Wolverine outbox/inbox durable sobre PostgreSQL y bus compartido. Coherente con `../08_Implementation_Plan.md` y `../07_MVP_Scope.md`.

---

## 1. Estructura de proyecto

```
src/Services/Campaigns/
  TaxVision.Campaigns.Domain/          # aggregates Campaign, CampaignRun, Recipient; VOs (Money copia local)
  TaxVision.Campaigns.Application/     # commands/handlers, saga, contratos
  TaxVision.Campaigns.Infrastructure/ # EF Core (esquema campaigns), Wolverine, repos tenant-scoped, clients M2M
  TaxVision.Campaigns.Api/            # endpoints REST + webhooks tracking
  TaxVision.Campaigns.Tests/
```

Contratos de integración (`Campaign*IntegrationEvent`, `Channel*Result`) en `BuildingBlocks.Messaging.CampaignIntegrationEvents` (compartidos con ejecutores y Wallet), igual que `PostmasterEmailEvents.cs` vive en BuildingBlocks.

---

## 2. Dependencias de runtime

| Dependencia | Tipo | Notas |
|---|---|---|
| PostgreSQL (esquema `campaigns`) | dura | estado + outbox/inbox Wolverine |
| Bus Wolverine (transporte del monorepo) | dura | dispatch/result/saga |
| **Wallet** (`TaxVision.Wallet`) | **dura** | sin Wallet, Campaigns no puede ejecutar (dependencia de arranque, ADR-CAMP-000 §Consecuencias) |
| Subscription | dura (lectura gate) | entitlement `module.campaigns` |
| Customer | dura (resolución audiencia) | materializa recipients |
| Scheduler | dura | disparo temporal / lease |
| Ejecutores (Email/SMS/WA/Push) | blanda | asíncronos; su caída no tumba Campaigns (results quedan pendientes, sweeper de timeout) |
| Scribe | indirecta | lo invoca el ejecutor, no Campaigns |

**Orden de despliegue:** Wallet debe existir y estar listo **antes** de habilitar la ejecución de Campaigns. Se puede desplegar Campaigns en modo "solo definición" (crear/editar Draft) antes de que Wallet exista, pero `trigger`/`schedule` requieren Wallet.

---

## 3. Configuración

| Config | Descripción | Secreto |
|---|---|---|
| `ConnectionStrings:Campaigns` | Postgres | sí (vault) |
| `Wolverine:*` | transporte/outbox | — |
| `M2M:ClientId/ClientSecret` | credenciales client-credentials (Wallet/Subscription/Customer) | sí (vault) |
| `Tracking:HmacKey` | firma de tokens open/click | sí (vault) |
| `Campaigns:DispatchDeadline` | timeout de recipient stuck (sweeper) | — |
| `RateLimit:*` | categorías | — |

**Ningún secreto de proveedor de canal** vive aquí (SMTP2GO/SMS/WhatsApp keys → ejecutores). **Ningún JWT de usuario** se persiste (corrige `Campaign.BackgroundAuthToken`).

---

## 4. Escalado

- **Stateless / horizontal:** N réplicas consumen la misma cola Wolverine sin doble-efecto (idempotencia + unique constraints + CAS de estado, ver `Concurrency_Spec.md`). No hay un `BackgroundService` singleton load-bearing como en el legado (`CampaignSchedulerBackgroundService.cs:9`).
- **El disparo temporal NO vive aquí** sino en Scheduler (con lease atómico); Campaigns solo reacciona a `RunDue`. Esto evita el doble-scheduler del legado incluso con múltiples réplicas de Campaigns.
- El fan-out grande (100k+ recipients) se emite por outbox con backpressure natural del bus; sin `Task.Delay` en memoria (anti-patrón legado `CampaignSchedulerBackgroundService.cs:38`).

---

## 5. Jobs de fondo (dentro del servicio, idempotentes)

| Job | Función | Frecuencia |
|---|---|---|
| Recipient-stuck sweeper | marca `Failed(timeout)` los `Dispatched` vencidos → permite cierre/refund | ~1 min |
| Rollup de contadores | recomputa `counter_*` desde recipients (auto-corrección) | batch/al cierre |
| GC de `processed_business_message` | purga filas expiradas | horario |
| Retención PII | anonimiza recipients de runs `Completed` > N días | diario |
| Reconciliación financiera | verifica `reserve == consume + refund` por run; alerta desbalance | horario |

Estos jobs son idempotentes y seguros ante múltiples réplicas (guards + CAS), a diferencia del `BackgroundService` volátil legado.

---

## 6. Migraciones y arranque

- EF Core migrations aplicadas en despliegue (esquema `campaigns`), tablas de outbox/inbox de Wolverine incluidas.
- Health checks: DB, bus, y disponibilidad M2M de Wallet/Subscription (readiness que refleja la dependencia dura de Wallet).
- Feature flag de rollout: `Campaigns:ExecutionEnabled` (permite desplegar en "solo definición" hasta que Wallet esté listo).

---

## 7. Local dev

Se integra al stack local (23 contenedores, gateway) — ver memoria `project_local_dev_stack_and_login`. Requiere Postgres + bus + stubs/instancias de Wallet/Subscription/Customer/Scheduler. Ejecutores pueden correr en modo logging/stub para probar el loop sin proveedores reales (igual que `LoggingSmsSender` existente en Notification).

---

## 8. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Wallet es dependencia de arranque de la ejecución | ADR-CAMP-000 §Consecuencias | DOCUMENTED_ONLY | 92% |
| Legado depende de BackgroundService singleton en memoria | `CampaignSchedulerBackgroundService.cs:9,38` | VERIFIED | 95% |
| Contratos de integración van en BuildingBlocks | `PostmasterEmailEvents.cs` (precedente) | VERIFIED | 96% |
| Sin secretos de proveedor / sin JWT aquí | `../02_Context_Map.md §Fronteras`; `Campaign.cs:87` (anti-patrón) | VERIFIED | 94% |
| Estructura de proyecto + jobs + flags | diseño (este doc) | NEW | 84% |
