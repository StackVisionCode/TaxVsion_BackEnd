# Auth

Auth es la fuente de verdad de usuarios, actor types, roles y permisos. Los servicios consumidores mantienen proyecciones locales mediante eventos y reconciliacion paginada.

## Reconciliacion de audiencias

Notification usa estos endpoints internos para sembrar y reparar sus proyecciones de owner del tenant y cuentas del portal:

- `GET /internal/users/owners/reconciliation?page=1&size=200`
- `GET /internal/users/portal-accounts/reconciliation?page=1&size=200`

Ambos endpoints:

- requieren un token M2M con `actor_type=Service`;
- aceptan solamente el `PlatformTenant` (`8f58a521-4c25-4d91-9f4e-7ad5df14c001`);
- recorren todos los tenants con `IgnoreQueryFilters()` y orden estable;
- incluyen registros activos e inactivos para reparar bajas perdidas;
- no se exponen por Gateway.

En desarrollo local, Auth escucha en `http://localhost:5124`. La coleccion `postman/TaxVision-NewServices.postman_collection.json` incluye el token de plataforma y las dos llamadas directas.
