# Codes — Máquinas de estado

## CodeDefinition

```text
Draft ──activate──► Active ──suspend──► Suspended ──reactivate──► Active
  └──revoke────────► Revoked ◄────────────revoke───────────────┘
Active ──expiry job──► Expired
```

`Revoked` y `Expired` son terminales. Quotes previos no autorizan nuevas reservas después de revoke.

## Reservation

```text
Active ──commit──► Committed ──compensate──► Compensated
   ├──cancel──► Cancelled
   └──TTL──► Expired ──late payment success──► Committed (solo reconciler)
```

El late commit requiere verificación autoritativa de Payment, misma fingerprint y auditoría `LateCommit`. Si el cupo fue reasignado, el sistema honra el precio ya cobrado y abre incidencia/compensación operativa; no cobra diferencia.

## Guards

| Operación | Permitido | Replay igual | Replay distinto |
|---|---|---|---|
| Reserve | Quote válido, Code Active | respuesta original | 409 IdempotencyConflict |
| Commit | Active; Expired solo reconciler | redemption original | 409 |
| Cancel | Active/Expired | estado original | 409 |
| Compensate | Committed | compensation original | 409 |

Persistencia usa RowVersion, unique constraints y transacción SQL. Conflictos optimistas tienen retry acotado con jitter y luego 409/reconciliación.

