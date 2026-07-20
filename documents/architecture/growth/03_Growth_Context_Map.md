# Growth — Context map

## Mapa

```text
Subscription ── Offer/Grant confirmation ──► Growth.Codes
      ▲                                      │
      │ Grant command                        │ Quote/Reservation snapshot
      │                                      ▼
PaymentApp ◄── payment result/refund/dispute ─┤
PaymentClient ◄─ payment result/refund/dispute┤
      ▲                                      │
      │ server-side offer snapshot           ▼
Future Catalog                         Growth.Referrals
                                             │
                                             ├─ command GrantReferralReward ─► Subscription
                                             └─ notification fact ───────────► Notification

Auth/Tenant ── identity, permissions, tenant status ─► Growth API
Future Ledger ◄─ monetary reward command (fuera de MVP) ─ Growth.Referrals
```

## Relaciones

| Upstream | Downstream | Contrato | Relación | Estado/evidencia |
|---|---|---|---|---|
| Subscription | Codes | SaaS offer/version y confirmación de grant | Customer/Supplier | VERIFIED: `src/Services/Subscription/TaxVision.Subscription.Domain/Plans/SubscriptionPlanVersion.cs`; `src/Services/Subscription/TaxVision.Subscription.Domain/Subscriptions/TenantSubscription.cs` |
| Future Catalog | Codes | tenant-client offer snapshot | Customer/Supplier | DOCUMENTED_ONLY: `ProductsAndServices_Service_Analysis_And_Design.md` |
| Codes | PaymentApp/Client | quote/reservation snapshot | Published Language | NOT_IMPLEMENTED |
| PaymentApp/Client | Codes | success/failure/refund/chargeback | Published Language | PARTIAL: estados existen, contratos Growth no |
| Codes | Referrals | code use/qualification candidate por IDs | Application contract | NOT_IMPLEMENTED |
| Referrals | Subscription | `GrantReferralReward` / confirmation | Conformist + command/result | NOT_IMPLEMENTED |
| Referrals | Ledger | reward cash | Anticorruption layer | DEFERRED |
| Auth/Tenant | Growth | JWT, tenant status, permissions | Conformist | PARTIAL |

## Guardrails

- Codes y Referrals no comparten aggregate, repository ni FK.
- Payment nunca recalcula descuento.
- Subscription nunca delega ownership de precio/trial/entitlement.
- Growth no accede a DB ajena.
- Comandos internos no se publican por Gateway.
