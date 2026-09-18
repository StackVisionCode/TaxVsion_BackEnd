# Campaigns — Deployment

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-09-16 (revisión: **sin dependencia de Wallet; Campaign solo campaña**)
- **Estado:** DISEÑO — no implementado

Microservicio .NET independiente, mismo estilo que el resto del monorepo (`src/Services/*`), con Wolverine outbox/inbox durable sobre PostgreSQL y bus compartido. Coherente con `../08_Implementation_Plan.md` y `../07_MVP_Scope.md`.

> **Regla dura:** Campaign no depende de ningún servicio de dinero para desplegar ni para ejecutar. No hay dependencia de arranque a un Wallet, ni M2M a un Wallet, ni job de reconciliación financiera.

---

## 1. Estructura de proyecto

```
src/Services/Campaigns/
  TaxVision.Campaigns.Domain/          # aggregates Campaign, CampaignRun, Recipient, Contact, ContactList, SenderProfile (sin VO Money)
  TaxVision.Campaigns.Application/     # commands/handlers, saga de dispatch, contratos
  TaxVision.Campaigns.Infrastructure/ # EF Core (esquema campaigns), Wolverine, repos tenant-scoped, clients M2M (Subscription/Customer)
  TaxVision.Campaigns.Api/            # endpoints REST (campañas, contactos, remitentes, detalles)
  TaxVision.Campaigns.Tests/
```

Contratos de integración (`CampaignDispatchRequested`, `CampaignDispatchResult`, `campaign.run.*`) en `BuildingBlocks.Messaging.CampaignIntegrationEvents` (compartidos con los ejecutores; y disponibles para un consumidor externo futuro como el Wallet), igual que `PostmasterEmailEvents.cs` vive en BuildingBlocks.

---

## 2. Dependencias de runtime

| Dependencia | Tipo | Notas |
|---|---|---|
| PostgreSQL (esquema `campaigns`) | dura | estado + outbox/inbox Wolverine |
| Bus Wolverine (transporte del monorepo) | dura | dispatch/result/saga |
| Subscription | dura (lectura gate) | entitlement `module.campaigns` |
| Customer | dura (resolución audiencia) | materializa recipients (fuente Clients) |
| Scheduler | blanda/dura según modo | disparo temporal (Scheduled/Recurring); Immediate no lo requiere |
| Ejecutores (Notification/Email, TaxVision.Sms, WhatsApp/Push) | blanda | asíncronos; su caída no tumba Campaigns (results quedan pendientes, sweeper de timeout) |
| Scribe | indirecta | lo invoca el ejecutor, no Campaigns |

**No hay dependencia a un Wallet.** Campaign despliega y ejecuta sin ningún servicio de dinero. (Si en el futuro se agrega un Wallet, se despliega **por separado** y se suscribe a los eventos de Campaign; no cambia el orden de despliegue de Campaign.)

---

## 3. Configuración

| Config | Descripción | Secreto |
|---|---|---|
| `ConnectionStrings:Campaigns` | Postgres | sí (vault) |
| `Wolverine:*` | transporte/outbox | — |
| `M2M:ClientId/ClientSecret` | credenciales client-credentials (Subscription/Customer) | sí (vault) |
| `Campaigns:DispatchDeadline` | timeout de recipient stuck (sweeper) | — |
| `RateLimit:*` | categorías | — |

**Ningún secreto de proveedor de canal** vive aquí (SMTP/SMS/WhatsApp/push keys → ejecutores). **Ningún JWT de usuario** se persiste (corrige `Campaign.BackgroundAuthToken`). **Ningún secreto/credencial de dinero** (no aplica).

---

## 4. Escalado

- **Stateless / horizontal:** N réplicas consumen la misma cola Wolverine sin doble-efecto (idempotencia + unique constraints + CAS de estado, ver `Concurrency_Spec.md`). No hay un `BackgroundService` singleton load-bearing como en el legado (`CampaignSchedulerBackgroundService.cs:9`).
- **El disparo temporal NO vive aquí** sino en Scheduler (con lease atómico); Campaigns solo reacciona a `RunDue`. Esto evita el doble-scheduler del legado incluso con múltiples réplicas de Campaigns.
- El fan-out grande (100k+ recipients) se emite por outbox con backpressure natural del bus; sin `Task.Delay` en memoria (anti-patrón legado `CampaignSchedulerBackgroundService.cs:38`).

---

## 5. Jobs de fondo (dentro del servicio, idempotentes)

| Job | Función | Frecuencia |
|---|---|---|
| Unit-stuck sweeper | marca `Unknown` (no `Failed`) las unidades `Dispatched` vencidas → permite cierre (fix #04) | ~1 min |
| Reconciliador de cierre | reevalúa el predicado de cierre por si dos results concurrentes no dispararon el CAS (fix #01) | ~1 min |
| **Wake de diferidas (quiet hours)** | emite unidades `Pending` con `eligible_at_utc <= now` (§7.5) | ~1 min |
| **GC de `contact_send_ledger`** | purga ventanas de frequency-cap vencidas | horario |
| Rollup de contadores | recomputa `counter_*` desde las unidades (fuente de verdad) | batch/al cierre |
| GC de `processed_business_message` | purga filas expiradas | horario |
| Retención PII | anonimiza recipients/contactos de runs terminales > N días | diario |

**Sin job de reconciliación financiera** (no hay dinero que reconciliar en Campaign). Estos jobs son idempotentes y seguros ante múltiples réplicas (guards + CAS).

---

## 6. Migraciones y arranque

- EF Core migrations aplicadas en despliegue (esquema `campaigns`), tablas de outbox/inbox de Wolverine incluidas.
- **Readiness con degradación (fix #26):** DB y bus son **duras** (readiness real). Subscription/Customer se consultan **según ruta**: su caída **no** debe sacar de servicio consultas históricas ni una campaña basada solo en contactos propios. Se degrada por capacidad (p.ej. bloquear solo la materialización de audiencia `Clients`), no un `503` global. El gate de plan usa su proyección local (no un HTTP síncrono en el hot path).
- **Continuidad (fix #26):** procedimientos explícitos de **DLQ/replay** (con límite de reintentos y presupuesto), restauración de BD y broker, compatibilidad de eventos en despliegues graduales, y **RTO/RPO**. **Restaurar la BD a un punto anterior NO deshace mensajes ya enviados por el proveedor:** la reconciliación (por `dispatch_id`/`provider_ref`) debe **prevenir reenvíos** tras un restore, no tratar todo el trabajo restaurado como nuevo.
- **Volumen (fix #12):** materialización y fan-out **paginados con checkpoint**; **cuotas por tenant/canal**, límite de runs activos, `prefetch`/routing de RabbitMQ configurados explícitamente (no derivados de poner `Channel` en el body).
- Despliegue del servicio nuevo: los 6 sitios de `Guia_Creacion_Microservicio.md §9` (slnx, Dockerfile, compose block, gateway route/cluster/DNS/loadshed, `CAMPAIGNS_DB_CONNECTION` ×3 + `apply-migrations.sh`, secretos CI en el entorno `production`).

---

## 7. Local dev

Se integra al stack local (23 contenedores, gateway) — ver memoria `project_local_dev_stack_and_login`. Requiere Postgres + bus + stubs/instancias de Subscription/Customer/Scheduler. Los ejecutores pueden correr en modo logging/stub para probar el loop sin proveedores reales (igual que `LoggingSmsSender` existente en Notification), y en la fase actual los canales reales son **Email (consumer en Notification) + SMS (consumer en TaxVision.Sms)**.

---

## 8. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Campaign no depende de un Wallet para ejecutar | ADR-CAMP-001 D1 (decisión del usuario) | DECISION | 99% |
| Legado depende de BackgroundService singleton en memoria | `CampaignSchedulerBackgroundService.cs:9,38` | VERIFIED | 95% |
| Contratos de integración van en BuildingBlocks | `PostmasterEmailEvents.cs` (precedente) | VERIFIED | 96% |
| Sin secretos de proveedor / sin JWT aquí | `../02_Context_Map.md §Fronteras`; `Campaign.cs:87` (anti-patrón) | VERIFIED | 94% |
| 6 sitios de wiring de despliegue | `Guia_Creacion_Microservicio.md §9` | DOCUMENTED_ONLY | 90% |
| Estructura de proyecto + jobs + flags | diseño (este doc) | NEW | 84% |
