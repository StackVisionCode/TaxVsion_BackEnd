# TaxVision.Sms — Deployment

- **Servicio:** SMS (`TaxVision.Sms`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

## 1. Unidad de despliegue
Microservicio independiente `TaxVision.Sms` (.NET, mismo runtime que el resto del monorepo nuevo). Proyectos: `TaxVision.Sms.Domain`, `.Application`, `.Infrastructure`, `.Api`. Contenedor propio, se suma al stack (≈24º contenedor). BD PostgreSQL propia (esquema `sms` + `sms_wolverine`), no compartida.

## 2. Dependencias en runtime
| Dependencia | Tipo | Notas |
|---|---|---|
| PostgreSQL | dura | estado + outbox/inbox Wolverine |
| Bus (Wolverine transport, RabbitMQ/PG) | dura | dispatch/result + Wallet saga |
| `TaxVision.Wallet` | dura (lógica) | reserve/consume/refund; SMS no arranca envíos sin Wallet alcanzable |
| Proveedor SMS externo | dura por tenant | Twilio/AWS SNS/otro (ver `ADR.md` SMS-ADR-001) |
| Scribe | blanda | render de plantilla si el cuerpo no viaja resuelto |
| Subscription | blanda | gate `module.campaigns` lo evalúa Campaigns, no SMS (ortogonal al balance) |
| CloudStorage | blanda | MMS/media por referencia |
| ApiGateway (Ocelot) | infra | ruta pública `/api/sms/**` + webhook |

## 3. Config / secretos
- Cadena de BD, credenciales del bus, clave de cifrado (KMS/Data Protection) por variables de entorno / secret store — **no** en appsettings versionado.
- Credenciales del **proveedor SMS son por tenant**, cifradas en BD (no globales), salvo un proveedor de plataforma opcional para envíos de sistema.
- Registrar categoría `sms` en la config de RateLimit del gateway (ver `documents/RateLimit/Guia_Nuevos_Servicios_Endpoints.md`).

## 4. Ruteo (ApiGateway)
Añadir `ocelot.routes.sms.json` con las rutas de `API_Contracts.md`. Las rutas de **webhook** (`/api/sms/webhooks/**`) se exponen sin auth JWT (verificación por firma) y marcadas `[RateLimitExempt]`; deben ser alcanzables desde el proveedor (allowlist de IP si el proveedor la publica).

## 5. Migraciones
EF Core migrations con contenedor migrador dedicado (patrón del monorepo). Orden de bootstrap: `TaxVision.Wallet` debe existir/migrar antes de que SMS pueda completar sagas (dependencia dura, alineado con `07_MVP_Scope.md`).

## 6. Escalado
- Stateless salvo la BD ⇒ escala horizontal. El durable inbox de Wolverine permite múltiples instancias sin doble-procesar (dedupe + idempotencia por destinatario).
- El limitador de TPS por sender debe ser **distribuido** (no in-memory por instancia) para respetar límites del proveedor al escalar. Ver `Concurrency_Spec.md`.

## 7. Rollout
1. Desplegar con proveedor en modo sandbox/test; verificar quote/segmentación y webhook de firma.
2. Habilitar envío individual (`/api/sms/send`) antes que el fan-out de campaña.
3. Habilitar dispatch de campaña una vez Wallet + Campaigns estén verificados end-to-end.
- Flag por feature (`Sms:Enabled`, `Sms:Provider`) para rollback sin redeploy.

## 8. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Patrón migrador + Ocelot por servicio | monorepo (`ocelot.routes.*.json` legado análogo) | VERIFIED (patrón) | 90% |
| Wallet es dependencia dura de bootstrap | `05_Master_ADR.md` §Consecuencias, `07_MVP_Scope.md` | VERIFIED (política) | 94% |
| Categoría RateLimit a registrar | `documents/RateLimit/Guia_Nuevos_Servicios_Endpoints.md` | VERIFIED | 95% |
| Topología/config de deployment SMS | este documento | NEW | — |
