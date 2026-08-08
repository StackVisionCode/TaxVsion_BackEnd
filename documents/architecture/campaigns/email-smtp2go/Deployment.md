# Email (SMTP2GO) — Deployment

- Servicio: **TaxVision.Campaigns.Email**
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

## 1. Forma del despliegue
- Microservicio .NET independiente `TaxVision.Campaigns.Email` (mismo runtime/stack que el resto de la suite; el legado era .NET8/9).
- **BD propia** PostgreSQL (schema `campaigns_email`); NO comparte BD con Postmaster/Notification/Campaigns.
- Se integra al stack de contenedores local (23 contenedores, gateway :5047; ver memoria `project_local_dev_stack_and_login.md`).
- Escala horizontal N réplicas detrás del bus Wolverine (ver `Concurrency_Spec.md`).

## 2. Dependencias de runtime
| Dependencia | Uso | Tipo |
|---|---|---|
| PostgreSQL | estado del servicio + outbox/inbox Wolverine | infra |
| Broker Wolverine (RabbitMQ) | consume `dispatch_requested`, emite results | infra |
| SMTP2GO API (`api.smtp2go.com/v3`) | entrega de email | externo |
| Scribe | render Fluid (solo si el cuerpo no viaja) | REUSE |
| CloudStorage | assets inline / adjuntos (por referencia) | REUSE |
| Customer | (indirecto, vía Campaigns) | no directo |
| KMS/DPAPI | envelope encryption de credenciales | infra |
| Redis (opcional) | rate limiter distribuido por credencial | infra |

## 3. Configuración
| Setting | Descripción |
|---|---|
| `ConnectionStrings:CampaignsEmailDb` | Postgres del servicio |
| `Wolverine:*` | broker, outbox/inbox durable |
| `Encryption:KekProvider` | KMS/DPAPI para DEK |
| `Smtp2Go:System:*` | credencial de plataforma (scope System) — la key va por secreto del entorno, **no** en appsettings en claro |
| `RateLimit:*` | límites de las categorías nuevas |
| `Reconciler:PendingTtl` | umbral de barrido de huérfanos |

**Prohibido** poner la API key en `appsettings` versionado (el legado tenía `Smtp2GoSettings.ApiKey` como property con default vacío pero poblada por config, `Smtp2GoSettings.cs:6`). Va por secreto del entorno/vault, cifrada en BD para scope Tenant.

## 4. Migraciones
- EF Core migrations propias del schema (dispatch, suppression, provider_credential, inbound_webhook_event, processed_business_message, [tracking]).
- Seed: registrar categorías `[RateLimit]` nuevas; sembrar credencial System si aplica.

## 5. Endpoints expuestos por el gateway
- Público (con firma/token + rate limit): `POST /api/email/webhooks/smtp2go`, `GET /api/email/t/{o,c}/{token}`.
- Tenant (JWT+RBAC): `/api/email/providers/smtp2go*`, `/api/email/suppressions*`.
- Interno M2M: `/internal/email/dispatches/{id}`.
Configurar el webhook de SMTP2GO apuntando al endpoint público del gateway.

## 6. Orden de arranque / dependencias de la suite
- **Wallet/Ledger** debe existir para cerrar la saga (consume/refund), pero el ejecutor Email **arranca y opera** sin Wallet (solo emite results); la saga los consume cuando Wallet esté. Dependencia blanda para el ejecutor, dura para el flujo de cobro (ver `../07_MVP_Scope.md`).
- **Campaigns** debe emitir `dispatch_requested` para que el ejecutor tenga trabajo.
- **Scribe** requerido solo si se usa el fallback de render server-side.

## 7. Health / readiness
- `/health/live`, `/health/ready` (Postgres + broker + KMS reachable).
- Readiness incluye poder descifrar la credencial System (falla temprano si KMS no responde).

## 8. Rollout
- Feature-flaggable a nivel Campaigns (canal Email on/off por tenant), análogo a `Notification:UsePostmasterDispatch` (`PostmasterEmailEvents.cs:6-11`). Permite habilitar el canal gradualmente.
- Rollback: desactivar el flag ⇒ Campaigns deja de emitir dispatch de email; los dispatches en vuelo drenan por el reconciliador.

## 9. Evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado API key poblada por config | `Smtp2GoSettings.cs:6` | VERIFIED | 90% |
| Flag de rollout análogo (Postmaster) | `PostmasterEmailEvents.cs:6-11` | VERIFIED | 92% |
| Stack local 23 contenedores + gateway :5047 | memoria `project_local_dev_stack_and_login.md` | DOCUMENTED_ONLY | 80% |
| Dependencia Wallet dura para cobro | `../05_Master_ADR.md`, `../07_MVP_Scope.md` | VERIFIED | 85% |
| Despliegue/infra concretos | este diseño | NEW | n/a |
