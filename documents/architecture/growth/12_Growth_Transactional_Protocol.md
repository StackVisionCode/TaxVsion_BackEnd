# Growth — Protocolo transaccional

## Secuencia canónica

```text
Offer owner → Codes.Quote → Codes.Reserve → Payment authorize/capture
                                       ├─ success → Codes.Commit
                                       └─ failure → Codes.Cancel
Jobs/events → Reconcile → Commit/Cancel/ManualReview
Refund/chargeback → Codes.Compensate → Referrals clawback/fraud review
```

## Quote

No consume cupo. Es inmutable, expira y congela `CodeRuleVersion`, `OfferId/Version`, tenant, sujeto, currency, gross/discount/net, `SnapshotHash` y timestamps. Hash: SHA-256 de JSON canónico UTF-8 con nombres/orden fijados y montos en minor units.

## Reserve

En una transacción SQL:

1. valida quote/hash/TTL y código activo;
2. obtiene/actualiza contadores con RowVersion;
3. inserta Reservation Active;
4. persiste mensaje outbox;
5. confirma.

Unique constraints evitan misma key, mismo payment y oversubscription lógica. La última disponibilidad se decide por update condicional/version token.

## Payment

Payment recibe `GrossAmountCents`, `DiscountAmountCents`, `NetAmountCents`, currency, `QuoteId`, `ReservationId`, `PromotionSnapshotId/Hash` y cobra exactamente net. Si la validación falla, no autoriza.

## Commit/Cancel

Ambos exigen idempotency key + fingerprint. Commit crea una sola Redemption y no acepta Cancelled/Compensated. Cancel libera una vez y no acepta Committed. Replays iguales devuelven la respuesta almacenada.

## Reconcile

Job con distributed lock, batches y checkpoint:

| Caso | Acción |
|---|---|
| Active + Payment success | commit |
| Active + failed/cancelled | cancel |
| Expired + pending | mantener Expired y reconsultar hasta límite |
| Expired + success | late commit auditado |
| commit duplicado | devolver resultado original |
| evento tardío/fuera de orden | comparar aggregate version/estado; stale no muta |
| financiero desconocido | retry con backoff; luego ManualReview |
| timeout | consulta autoritativa a Payment; nunca adivina |

## Garantía observable

Entrega at-least-once + operaciones idempotentes + unique constraints + state guards + outbox/inbox produce un único efecto de negocio observable. No se asume unicidad del transporte.

