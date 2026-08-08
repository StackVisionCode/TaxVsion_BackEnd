# TaxVision.Sms — Observability

- **Servicio:** SMS (`TaxVision.Sms`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

## 1. Correlación
Se propaga `CampaignId`/`CampaignRunId`/`RecipientId`/`Attempt`/`DispatchId` en todo log, span y evento — el mismo seam opaco que ya viaja Notification→Postmaster (`PostmasterEmailEvents.cs:37`). Un `traceId` OpenTelemetry cruza HTTP → outbox → handler → llamada al proveedor → webhook DLR, permitiendo reconstruir el ciclo de vida completo de un envío.

## 2. Métricas (OpenTelemetry / Prometheus)

| Métrica | Tipo | Labels | Uso |
|---|---|---|---|
| `sms_dispatch_total` | counter | `tenant`, `channel_class`, `outcome`, `provider` | volumen y tasa de éxito |
| `sms_segments_total` | counter | `tenant`, `encoding` | costo agregado y detección de Unicode inesperado |
| `sms_cost_cents_total` | counter | `tenant`, `outcome` | gasto real vs. reservado |
| `sms_dispatch_duration` | histogram | `provider`, `outcome` | latencia envío→accepted |
| `sms_dlr_lag` | histogram | `provider` | tiempo accepted→delivered (DLR) |
| `sms_provider_errors_total` | counter | `provider`, `http_status`, `error_code` | salud del proveedor |
| `sms_wallet_reserve_denied_total` | counter | `tenant` | saldo insuficiente (señal de negocio) |
| `sms_optin_suppressed_total` | counter | `tenant`, `reason` (stop/no-optin/blocked) | cumplimiento |
| `sms_webhook_signature_rejected_total` | counter | `provider` | ataques/misconfig |
| `sms_idempotency_replay_total` | counter | `operation` | duplicados absorbidos |
| `sms_reconciliation_actions_total` | counter | `action` (resend/refund/consume) | red de seguridad activa |

## 3. Alertas
- `outcome=failed` ratio > umbral por proveedor/tenant (proveedor caído o sender bloqueado).
- `sms_wallet_reserve_denied_total` en alza (tenant sin saldo — CTA top-up).
- `sms_dlr_lag` p95 alto o DLR ausentes (webhook roto).
- `sms_webhook_signature_rejected_total` > 0 sostenido (firma mal configurada o abuso).
- Dispatch `Accepted` sin DLR tras TTL (backlog de reconciliación).

## 4. Logging estructurado
- Nunca loggear el **cuerpo del SMS** completo ni PII del destinatario en claro más allá del `phone` enmascarado (`+1512•••0123`) — el body puede contener datos sensibles (ver `Security.md`).
- **Nunca** loggear credenciales/`WebhookSecret` (el legado loggeaba respuestas raw del proveedor, `RawResponse`, `SmsSendLog.cs:40` — se acota a metadatos, no secretos).
- Niveles: `Info` por transición terminal; `Warn` por retry/reserve-denied; `Error` por excepción de proveedor no recuperable.

## 5. Auditoría
- Cada transición de `SmsDispatch` y de `SmsOptInRegistry` queda como evento/registro inmutable (auditable) — clave para probar consentimiento (STOP/opt-in) ante disputas TCPA/carrier. `consent_source`/`consent_proof_ref` lo respaldan.
- Los movimientos de dinero son auditables en Wallet (fuente de verdad del saldo), no aquí.

## 6. Health checks
- `/health/live`, `/health/ready` (dependencias: BD, bus Wolverine, alcanzabilidad del proveedor por tenant activo — degradado, no fail-hard, si un proveedor está caído).

## 7. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Seam de correlación reutilizable | `PostmasterEmailEvents.cs:37` | VERIFIED | 97% |
| Legado persiste respuestas raw del proveedor | `SmsSendLog.cs:40` | VERIFIED | 95% |
| Métricas/alertas/logging SMS propuestos | este documento | NEW | — |
