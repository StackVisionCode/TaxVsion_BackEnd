# Growth — Política de refunds y chargebacks

Estado: **APPROVED** para el MVP por GDR-009. La política queda versionada para
permitir excepciones futuras sin cambiar el default conservador.

| Evento | Discount uso | Benefit grant | Referral reward | Fraude |
|---|---|---|---|---|
| Refund completo | conservar uso por defecto; solo restaurar cuando la policy versionada lo declare | revocar si reversible | clawback si granted; cancelar si pending | señal |
| Refund parcial | compensación proporcional del valor; uso se conserva | reducir/revocar según owner | reevaluar minimum payment | señal por velocity |
| Chargeback abierto | uso se conserva | suspender si posible | freeze/clawback pending | abrir FraudReview |
| Chargeback ganado por merchant | no restaurar automáticamente | owner puede reactivar | cancelar clawback si no ejecutado; compensar si ejecutado | cerrar approved |
| Chargeback perdido | conservar uso consumido | revocar | clawback | review escalado |
| Pago cancelado antes de commit | cancelar reserva y liberar | no grant | no qualify | ninguna |
| Pago duplicado | un payment ref puede redimir una vez | compensar cargo duplicado en Payment | una qualification | alerta |

Eventos de refund deben incluir monto acumulado y delta; chargeback incluye dispute version/status/amount. Payment es autoridad financiera; Growth aplica política versionada y auditable.

No se crea saldo negativo ni cash debt sin Ledger.

## Reglas de cálculo MVP

- La compensación proporcional se calcula en minor units contra el principal
  efectivamente cobrado y nunca puede superar el descuento original acumulado.
- Cada movimiento usa `SourceEventId` único y el monto acumulado autoritativo de
  Payment para tolerar duplicados y eventos fuera de orden.
- `KeepConsumed` es el default para cupo promocional. `RestoreAvailability`
  requiere una excepción explícita de la policy y compensación total.
- Un chargeback ganado no reactiva silenciosamente un beneficio ni un reward:
  cancela una reversión pendiente o crea una operación compensatoria idempotente
  cuando la reversión ya fue materializada.
