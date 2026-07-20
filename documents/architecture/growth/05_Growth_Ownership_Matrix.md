# Growth — Matriz de ownership

| Capacidad/dato | Owner | Consumidores | Evidencia | Estado |
|---|---|---|---|---|
| Precio base SaaS, plan/version | Subscription | Codes, PaymentApp | `src/Services/Subscription/TaxVision.Subscription.Domain/Plans/SubscriptionPlanVersion.cs`; `src/Services/Subscription/TaxVision.Subscription.Infrastructure/Persistence/SubscriptionPlanCatalogSeeder.cs` | VERIFIED |
| Trial, add-on, entitlement | Subscription | Auth, Codes | `src/Services/Subscription/TaxVision.Subscription.Domain/Subscriptions/TenantSubscription.cs`; `src/Services/Subscription/TaxVision.Subscription.Domain/Entitlements/TenantEntitlementSnapshot.cs` | VERIFIED |
| Precio tenant-cliente | Future Catalog/sistema comercial server-side | Codes, PaymentClient | Fuera del MVP; cualquier owner futuro debe emitir `OfferSnapshot` server-side versionado | DEFERRED |
| Definición/regla/scope de código | Codes | API, Referrals | Diseño Growth | APPROVED |
| Quote/reservation/redemption/compensation | Codes | Payment, Referrals | Diseño Growth | APPROVED |
| Programa/atribución/calificación | Referrals | Codes, Payment | Diseño Growth | APPROVED |
| Reward lifecycle/vesting/clawback | Referrals | Subscription/Ledger | Diseño Growth | APPROVED |
| Autorización/captura/refund/chargeback | PaymentApp/PaymentClient | Growth | `src/Services/PaymentApp/TaxVision.PaymentApp.Domain/SaaSPayments/SaaSPayment.cs`; `src/Services/PaymentClient/TaxVision.PaymentClient.Domain/TenantPayments/TenantPayment.cs` | VERIFIED |
| Balance/crédito/débito/cash reward | Future Ledger | Referrals | CRM legacy muestra wallet no contable | DEFERRED |
| Email/notificación | Notification | Growth | `src/Services/Notification/TaxVision.Notification.Application/` | VERIFIED |
| Identidad/auth/roles/permisos | Auth | Growth | `src/Services/Auth/Domain/Roles/Permission.cs`; `src/Services/Auth/Infrastructure/Security/JwtTokenGenerator.cs` | VERIFIED |
| Estado/scope tenant | Tenant | Growth | `src/Services/Tenant/TaxVision.Tenant.Domain/Tenant.cs` | VERIFIED |

## Invariantes

- Un owner decide y persiste; otros conservan snapshots/referencias.
- Codes registra grant, pero Subscription materializa trial/entitlement.
- Referrals solicita reward; el owner del beneficio confirma o rechaza.
- Payment persiste monto cobrado y referencias Growth, nunca reglas promocionales.
