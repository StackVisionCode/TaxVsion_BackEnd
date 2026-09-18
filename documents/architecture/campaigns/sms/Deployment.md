# TaxVision.Sms — Deployment

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — SMS NO es un servicio nuevo; `TaxVision.Sms` YA EXISTE y ya es M2M** (`SendSmsBatchCommand`, `POST /sms/messages`, `ActorType.Service`). El canal SMS es un **CONSUMER dentro de `TaxVision.Sms`** que procesa `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un microservicio SMS nuevo y/o un Wallet queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- **Servicio:** SMS (`TaxVision.Sms`) — consumer del canal SMS **dentro de `TaxVision.Sms` (ya existente)**
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

## 1. Unidad de despliegue
**El canal SMS NO es un contenedor nuevo:** vive como un **consumer dentro de `TaxVision.Sms` (ya existente)** (.NET, mismo runtime del monorepo). Proyectos existentes `TaxVision.Sms.Domain`/`.Application`/`.Infrastructure`/`.Api`; el consumer se añade a ellos. BD PostgreSQL propia (esquema `sms` + `sms_wolverine`), no compartida. — (removido/superseded: antes se describía como microservicio independiente nuevo, ≈24º contenedor).

## 2. Dependencias en runtime
| Dependencia | Tipo | Notas |
|---|---|---|
| PostgreSQL | dura | estado + outbox/inbox Wolverine |
| Bus (Wolverine transport, RabbitMQ/PG) | dura | dispatch/result (`campaign.dispatch.requested.v1`/`.result.v1`) |
| ~~`TaxVision.Wallet`~~ | — | — (removido: sin dinero en el canal; ver banner — antes reserve/consume/refund). La autorización por balance es un interceptor/PEP externo y DIFERIDO. |
| Proveedor SMS externo | dura por tenant | Twilio/AWS SNS/otro (ver `ADR.md` SMS-ADR-001) |
| Scribe | blanda | render de plantilla si el cuerpo no viaja resuelto |
| Subscription | blanda | gate `module.campaigns` lo evalúa Campaigns, no el canal SMS |
| CloudStorage | blanda | MMS/media por referencia |
| ApiGateway (Ocelot) | infra | ruta pública `/api/sms/**` + webhook |

## 3. Config / secretos
- Cadena de BD, credenciales del bus, clave de cifrado (KMS/Data Protection) por variables de entorno / secret store — **no** en appsettings versionado.
- Credenciales del **proveedor SMS son por tenant**, cifradas en BD (no globales), salvo un proveedor de plataforma opcional para envíos de sistema.
- Registrar categoría `sms` en la config de RateLimit del gateway (ver `documents/RateLimit/Guia_Nuevos_Servicios_Endpoints.md`).

## 4. Ruteo (ApiGateway)
Añadir `ocelot.routes.sms.json` con las rutas de `API_Contracts.md`. Las rutas de **webhook** (`/api/sms/webhooks/**`) se exponen sin auth JWT (verificación por firma) y marcadas `[RateLimitExempt]`; deben ser alcanzables desde el proveedor (allowlist de IP si el proveedor la publica).

## 5. Migraciones
EF Core migrations con contenedor migrador dedicado (patrón del monorepo). — (removido: sin dinero en el canal; ver banner — antes orden de bootstrap con `TaxVision.Wallet` como dependencia dura para completar sagas de saldo).

## 6. Escalado
- Stateless salvo la BD ⇒ escala horizontal. El durable inbox de Wolverine permite múltiples instancias sin doble-procesar (dedupe + idempotencia por destinatario).
- El limitador de TPS por sender debe ser **distribuido** (no in-memory por instancia) para respetar límites del proveedor al escalar. Ver `Concurrency_Spec.md`.

## 7. Rollout
1. Desplegar con proveedor en modo sandbox/test; verificar quote/segmentación y webhook de firma.
2. Habilitar envío individual (`/api/sms/send`) antes que el fan-out de campaña.
3. Habilitar dispatch de campaña una vez Campaigns esté verificado end-to-end. — (removido: sin dinero en el canal; ver banner — antes también Wallet).
- Flag por feature (`Sms:Enabled`, `Sms:Provider`) para rollback sin redeploy.

## 8. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Patrón migrador + Ocelot por servicio | monorepo (`ocelot.routes.*.json` legado análogo) | VERIFIED (patrón) | 90% |
| `TaxVision.Sms` ya existe (consumer, no contenedor nuevo) | `MessagesController.cs:21`, `SendSmsBatch.cs` | VERIFIED | 96% |
| Categoría RateLimit a registrar | `documents/RateLimit/Guia_Nuevos_Servicios_Endpoints.md` | VERIFIED | 95% |
| Topología/config de deployment SMS | este documento | NEW | — |
