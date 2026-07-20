# Growth — Estrategia de migración

## Principio

El CRM legado es fuente para inventario y reconciliación, no modelo a copiar. Evidencia: `ReferralService` contiene referral/reward/wallet/TaxCoin; `PaymentService` contiene DiscountCoupon/CouponUsage.

## Fases

1. Inventario read-only: counts, activos, expirados, owners, códigos, usages, rewards, wallet/TaxCoin.
2. Clasificación `keep/transform/retire/quarantine`.
3. Mapping `CompanyId→TenantId`, IDs legacy→Growth, timestamps/timezones y status.
4. Shadow import a tablas staging fuera de aggregates finales.
5. Validación: uniqueness, tenant ownership, recomputación de usages/rewards.
6. Import Codes elegibles desactivados; no importar secretos en claro.
7. Import referrals como historia/attribution según policy aprobada.
8. No importar balances como verdad: snapshot + transacciones + diferencias a conciliación.
9. Dual-read/shadow comparison; no dual-write cross-DB sin outbox.
10. Cutover por cohort, rollback lógico, cierre y reporte.

## Exclusiones

Wallet, TaxCoin, gift balance y campaign monetary balances no pasan a Growth MVP. Se exportan a archivo controlado o futuro Ledger tras conciliación.

## Criterios

100% rows accounted, cero orphan tenant, códigos conflictivos quarantined, balances firmados por negocio/finanzas, reversible antes de activar. La existencia/volumen de datos productivos es **UNVERIFIED** y constituye **PRODUCTION_BLOCKER** para un cutover legacy, no un blocker del diseño o del MVP greenfield.
