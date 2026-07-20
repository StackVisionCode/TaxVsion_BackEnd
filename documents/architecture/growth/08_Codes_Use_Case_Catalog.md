# Codes — Catálogo de casos de uso

| Caso | Actor | Permiso/scope | Idempotencia | Resultado |
|---|---|---|---|---|
| CreateCode | Platform/Tenant admin | `codes.manage` + ownership | requerida | Draft |
| PublishRuleVersion | autorizado | `codes.manage` | requerida | versión inmutable |
| ActivateCode | autorizado | `codes.activate` | requerida | Active |
| RevokeCode | autorizado | `codes.revoke` | requerida | Revoked |
| IssueGift | autorizado | `codes.issue` | requerida | token seguro; solo una exposición |
| CreateQuote | consumidor/M2M | scope quote | requerida | quote expirable |
| ReserveCode | Payment M2M | `growth.codes.reserve` | requerida | reservation Active |
| CommitReservation | Payment M2M/evento | `growth.codes.commit` | requerida | redemption única |
| CancelReservation | Payment M2M/evento | `growth.codes.cancel` | requerida | cupo liberado |
| CompensateRedemption | Payment M2M/evento | `codes.compensation.manage` | requerida | compensation |
| ReconcileReservations | system actor | interno | key determinista | convergencia |
| ApplyGrant | Subscription M2M | `growth.grants.confirm` | `GrantId` | confirm/reject |
| ReadAudit | humano | `codes.audit.read` | n/a | vista redacted |

Todos los mutadores validan role/permission/TenantId/resource ownership; un rol no reemplaza el permiso.

