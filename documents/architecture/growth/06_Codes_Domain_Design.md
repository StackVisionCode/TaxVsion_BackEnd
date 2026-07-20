# Codes — Diseño de dominio

## Aggregate roots

### CodeDefinition

Posee estado, kind, valor hasheado/prefix/last-four cuando sea sensible, vigencia, reglas versionadas y scopes. Mutaciones: create, activate, suspend, revoke, publish-rule-version. No contiene Payment ni Referral aggregates.

### CodeReservation

Posee la state machine de disponibilidad provisional. Referencia `QuoteId`, `CodeId`, tenant/sujeto y payment mediante IDs opacos. Produce como máximo una Redemption.

### CodeCompensation

Registra la decisión idempotente de restaurar/conservar/revocar/proporcionar ante evento financiero. No altera Payment.

## Value objects

- `Money(long AmountCents, Currency)` siguiendo `PaymentClient.Domain/ValueObjects/Money.cs`.
- `PercentageBasisPoints` entre 1 y 10_000; evita porcentaje ambiguo.
- `CodeTokenHash`, `CodeDisplay(prefix,lastFour)`.
- `OfferReference(OfferId, OfferVersion, Owner)`.
- `SubjectReference(Type, Id)`.
- `SnapshotHash` SHA-256 de serialización canónica.

## Invariantes

1. descuento `0 <= DiscountAmount <= GrossAmount`;
2. currency de gross/discount/net coincide;
3. net = gross − discount;
4. quote usa una regla publicada e inmutable;
5. reserva activa única por `(PaymentSource, RelatedPaymentId)`;
6. commit solo desde Active o Expired con política de late-success;
7. cancel no opera sobre Committed/Compensated;
8. redemption única por ReservationId;
9. límites se contabilizan bajo transacción/lock optimista;
10. código completo sensible no aparece en log/evento público.

## Tipos

MVP: `Percentage`, `FixedAmount`, `BenefitGift`, `PrelaunchGrant`, `TrialExtension`, `FeatureUnlock`. Fuera: `StoredValueGift`, wallet, moneda convertible.

## Evidencia

El dominio no está implementado (**NOT_IMPLEMENTED**). El patrón `Money` y `RowVersion` es **VERIFIED** en `src/Services/PaymentClient/TaxVision.PaymentClient.Domain/ValueObjects/Money.cs` y `src/Services/PaymentClient/TaxVision.PaymentClient.Domain/TenantPayments/TenantPayment.cs:49`; las reglas externas son **DOCUMENTED_ONLY**.
