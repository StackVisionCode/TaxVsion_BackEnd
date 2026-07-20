# Growth — Especificación de concurrencia

| Carrera | Transacción/guard | Constraint | Resultado |
|---|---|---|---|
| Dos reservan último uso | update contador + insert reservation en una transacción | límite/check lógico + RowVersion | uno gana; otro 409 NoAvailability |
| Misma key | insert business message primero | unique scope/key | replay o 409 fingerprint |
| Dos reservations mismo payment | transacción | unique source/payment | una reserva |
| Commit simultáneo | state guard + insert redemption | unique ReservationId | un efecto; replay |
| Commit vs cancel | RowVersion | terminal guards | primero gana; segundo 409 |
| Commit vs expiry | lock optimista; reconciler autoritativo | state/version | late-success policy |
| Dos qualifying events | guard attribution | unique attribution/event | una qualification |
| Dos reward grants | guard RewardCase | unique beneficiary/reward | uno |
| Refund durante grant | monotonic state + inbox | source event unique | clawback pending |
| Chargeback durante clawback | dispute version | unique event/version | actualización monotónica |

## Retry

Solo errores transitorios/`DbUpdateConcurrencyException`, máximo 3 intentos con jitter. No se reintenta invalid state ni fingerprint conflict. Tras agotamiento: 409 para síncrono o mensaje retry/DLQ + reconciliación para asíncrono.

## SQL Server

Read Committed Snapshot para lecturas; transacción explícita para contadores/reservas; `rowversion`; índices únicos como árbitro final. No se usan locks distribuidos para invariantes que la DB puede garantizar.

