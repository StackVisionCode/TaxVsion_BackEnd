# Plan de migración — "Product & Services" (CRM viejo → TaxVision backend nuevo)

> Estado: **PLAN aprobado en alcance** — decisiones tomadas (§10). Pendiente arrancar implementación.

## 0. Decisiones tomadas (bloqueantes, ya resueltas)

1. **Dos microservicios separados:** `TaxVision.Catalog` (productos/servicios/categorías/precio) e
   `TaxVision.Inventory` (stock/proveedores/movimientos), desacoplados. Inventory **consume eventos** de
   Catalog (referencia débil por `catalogItemId`, sin FK cross-service).
2. **Multi-moneda:** el precio es un VO `Money` (monto + `currency`).
3. **Arrancar vacío:** sin script de migración de datos (los tenants cargan su catálogo de cero). §9 = N/A.

## 1. Contexto y objetivo

Migrar el módulo de **productos y servicios** del CRM viejo a un microservicio nuevo del backend
TaxVision, respetando las convenciones actuales (Clean Arch + DDD, Wolverine CQRS, EF Core + SQL Server
por servicio, tenant desde el JWT con filtro fail-closed, RBAC por permiso, eventos en `taxvision-events`).

**Origen (viejo):** `camara/CRMTAXPROBACKEND/InventoryService` — .NET 9, **MediatR**, EF Core 9 + SQL Server,
`ApiResponse<T>`, multi-tenant por **`CompanyId`** (app-enforced, viene en el body/query, **no** del JWT),
sin eventos, standalone. Es un **catálogo de productos + inventario** (SKU, categorías en árbol, precio/costo,
stock, proveedores, ledger de movimientos).

**Destino (nuevo):** microservicio **greenfield `TaxVision.Catalog`** (`src/Services/Catalog`). En
`src/Services/` **no existe** hoy Catalog/Products/Inventory. `Billing.InvoiceLineItem` ya documenta el
seam: *"…responsabilidad del caller (o de un futuro Catalog), Billing solo lo snapshotea"* → este servicio
es el consumidor natural aguas abajo. Ojo: `Subscription.PlanCatalog` es **otro bounded context** (planes SaaS
que TaxVision vende), no el catálogo del tenant — solo colisiona el nombre.

Exemplar a calcar: **`src/Services/Sms`** (recién construido, incluye RBAC end-to-end).

## 2. Alcance (fijado por §0)

**`TaxVision.Catalog`:** productos y **servicios** (ítems sin stock), categorías (árbol), atributos/config
por ítem, **precio/costo multi-moneda (`Money`)**, activo/inactivo, soft-delete, unicidad de SKU por tenant,
búsqueda/paginación. Publica eventos de catálogo.

**`TaxVision.Inventory`** (servicio aparte, fase posterior): stock por ítem, proveedores, ledger de
movimientos, low-stock. Consume `CatalogItemCreated/Deactivated` para conocer qué ítems existen (guarda
`catalogItemId`, sin FK). No duplica el catálogo.

**Fuera:** `TaxProStore` (marketplace de plantillas — otro contexto), `SubscriptionsService` (planes SaaS →
ya cubierto por `Subscription`), y migración de datos (arrancamos vacío, §0.3).

## 3. Modelo de dominio (viejo → nuevo)

Regla transversal: **`CompanyId` (viejo) → `TenantId` (nuevo, `TenantEntity`, private-set, tomado del JWT)**
y se **elimina `CompanyId` de los contratos de request**. `TaxUserId` se conserva como auditoría
"creado/actuado por" (mapeado del `sub` del JWT).

| Viejo (`InventoryService`) | Nuevo (`TaxVision.Catalog.Domain`) | Cambios clave |
|---|---|---|
| `Product` (CompanyId, TaxUserId, Name, SKU, Barcode, CategoryId, Price, CostPrice, Stock*, Unit, IsActive, TrackInventory, ImageUrl) | `CatalogItem : TenantEntity` | `CompanyId→TenantId`; `ItemKind {Product, Service}` explícito (Service ⇒ `TrackInventory=false`); precio como VO `Money` |
| `Category` (self-tree, CompanyId, ParentCategoryId, IsActive) | `Category : TenantEntity` | árbol conservado; FK `Restrict` |
| `ProductConfiguration` (EAV: ConfigKey/Value/Type) | `CatalogItemAttribute` (owned/child) | igual (atributos/variantes) |
| `Supplier` / `ProductSupplier` | `Supplier` / `ItemSupplier` | solo si inventario entra en alcance (§10) |
| `InventoryTransaction` (+`TransactionType`) | `StockMovement` (+`StockMovementType`) | solo si inventario entra; ledger inmutable |

VOs nuevos: `Sku` (validación/normalización), `Money` (monto + moneda si se agrega, §10.3),
`CatalogItemErrors` (códigos canónicos). Estados/flags: `IsActive`, soft-delete (`IsDeleted`+filtro).

## 4. Tenencia y reglas de negocio (endurecimiento)

- **Tenant del JWT, nunca del body.** Los controllers leen `ITenantContext.TenantId`; se elimina el
  `companyId` de query/body (corrige el fallo del viejo, donde el caller mandaba su propio `CompanyId`).
- **Filtro global fail-closed** por `ITenantOwned` (patrón `SmsDbContext.ApplyFailClosedTenantFilter`):
  sin tenant → `Guid.Empty` → no matchea nada.
- **Unicidad de SKU por tenant:** promover a **índice único filtrado** `(TenantId, SKU) WHERE SKU IS NOT
  NULL AND IsDeleted = 0`; confiar en la traducción de violación única → `ConflictException` (en vez del
  pre-check con carrera del viejo `ExistsBySKUAsync`).
- Conservar: verificación de categoría por tenant al crear; `Adjustment` inicial de stock al crear con
  `StockQuantity>0`; lógica de ajuste (Purchase/Return suman, Sale/Damaged restan, rechaza stock negativo,
  respeta `TrackInventory=false`); soft-delete en todo; flag low-stock (`Stock <= MinStockLevel`).
- **Precisión** `decimal(18,2)` en montos.

## 5. API (`/catalog/*`, vía Gateway)

CRUD + búsqueda, con `Result<T>` (no `ApiResponse<T>`), paginación estándar:

- `POST /catalog/items` · `GET /catalog/items` (filtros+paginado) · `GET /catalog/items/{id}` ·
  `PUT /catalog/items/{id}` · `DELETE /catalog/items/{id}` (soft-delete)
- `GET /catalog/items/low-stock` (si inventario entra) · `POST /catalog/items/{id}/adjust-inventory` (idem)
- `POST/GET/PUT/DELETE /catalog/categories` (árbol)
- `POST/GET/PUT/DELETE /catalog/suppliers` + `GET /catalog/transactions` (si inventario entra)

CQRS: comandos/queries como `record` → `IMessageBus.InvokeAsync<Result<T>>` (Wolverine), no MediatR.

## 6. RBAC

Dos capas (patrón Sms):
- `[AllowActorTypes(ActorType.Service, ActorType.TenantAdmin, ActorType.TenantEmployee)]`
- `[HasPermission("catalog.<x>")]`

Permisos nuevos en `src/BuildingBlocks/Authorization/CatalogPermissions.cs`:
`catalog.read`, `catalog.write`, `catalog.delete`, `catalog.inventory.adjust` (si aplica).
- Registrarlos en `src/Services/Auth/Domain/Roles/PermissionCatalog.cs` (`All` + `SystemRoleDefaults`:
  TenantAdmin todos; TenantEmployee `read`/`write`) + migración `AddCatalogPermissions`.
- Lado Catalog: `PermissionPolicyProvider` + `AddUserPermissionsSource("Projection")` + proyección local
  (`UserPermissionsProjection`/`RolePermissionsProjection`) con los 2 consumers **registrados explícito**
  vía `Discovery.IncludeType(typeof(...))` (gotcha de clases estáticas) + su migración.

## 7. Eventos de integración (net-new)

El viejo no publicaba nada. El nuevo publica en `taxvision-events` (namespace
`BuildingBlocks.Messaging.CatalogIntegrationEvents`): `CatalogItemCreated/Updated`, `CatalogItemPriceChanged`,
`CatalogItemDeactivated`. Payload agnóstico: `tenantId, itemId, sku?, name, unitPrice, kind, correlationId`.

**Wiring con Billing:** `Billing.InvoiceLineItem` **snapshotea** desde el catálogo (descripción, unit-amount,
tax) al emitir; guarda `catalogItemId?` como referencia débil (sin FK cross-service). Billing sigue siendo
dueño del snapshot; Catalog es la fuente. (Los precios en facturas NO cambian retroactivamente al cambiar el
catálogo — se snapshotea al momento.)

## 8. Wiring de plataforma (puntos de edición exactos)

1. `TaxVision.slnx` — folder `/src/Services/Catalog/` + 4 `<Project>` (patrón Sms/Notes).
2. `src/Gateway/TaxVision.Gateway/appsettings.json` — route `catalog` (`/catalog/{**catch-all}`) + cluster
   `catalog` (destino `http://localhost:<port>/`, health `/health/live`) + criticidad `"catalog":"Standard"`.
3. `deploy/docker/docker-compose.yml` — servicio `catalog-api` (calca `sms-api`) + `CATALOG_DB_CONNECTION`;
   `ServiceAuth__Clients__…__Permissions__N` si un caller M2M necesita `catalog.*`.
4. `deploy/docker/migrations/apply-migrations.sh` — bloque `"Catalog" <Infra> <Api> "$CATALOG_DB_CONNECTION"`
   + env en el servicio `migrations`.
5. `.env` — `CATALOG_DB_CONNECTION=…`.

Puerto dev sugerido: 5480. DB: `TaxVision_Catalog`.

## 9. Migración de datos (opcional, §10.4)

Si hay datos productivos en la DB vieja `InventoryService`:
- Script idempotente old→new por tenant: `Products→CatalogItems` (`CompanyId→TenantId`), `Categories`,
  `ProductConfigurations→CatalogItemAttributes`, (`Suppliers`, `ProductSuppliers`, `InventoryTransactions`
  si inventario entra). Preservar `Id` (GUID) para trazabilidad; recomputar índices únicos; conservar
  timestamps y soft-delete.
- Requiere **mapa `CompanyId (viejo) → TenantId (nuevo)`** — hay que definirlo (¿son el mismo GUID?).
- Validación: conteos por tenant, unicidad de SKU, integridad de árbol de categorías.

## 10. Decisiones (resueltas — ver §0)

1. **Inventario:** servicio **separado** (`TaxVision.Inventory`), fase posterior. Catalog primero. ✅
2. **"Services":** basta `ItemKind=Service` + `TrackInventory=false` (sin campos extra por ahora). ✅
3. **Moneda:** **multi-moneda** vía VO `Money` (monto + currency). ✅
4. **Migración de datos:** **arrancar vacío**, sin script. ✅

## 11. Fases (con verificación)

1. **Scaffold** `TaxVision.Catalog` (4 proyectos) + health + wiring de plataforma (slnx/gateway/compose/
   migrations/.env). ✔ `dotnet build`; gateway enruta `/catalog` (401 sin token = ruta OK).
2. **Dominio** `CatalogItem`/`Category`/`CatalogItemAttribute` (+ inventario si §10.1) + VOs + errores.
3. **EF + migración `InitialCreate`** (tablas, índice único filtrado de SKU, filtro fail-closed).
4. **CQRS**: crear/actualizar/borrar/consultar ítems + categorías (Wolverine, `Result<T>`).
5. **RBAC**: `CatalogPermissions` + catálogo Auth + proyección + `[HasPermission]`. ✔ M2M 200/403 + humano.
6. **Eventos** `CatalogIntegrationEvents` + publicación.
7. **(Opcional) Inventario**: ajuste de stock transaccional + ledger + low-stock (si §10.1).
8. **Billing wiring**: `InvoiceLineItem` referencia/snapshotea del catálogo.
9. **(Opcional) Migración de datos** old→new + validación.
10. **Tests** (dominio + handlers + RBAC + eventos) al estilo `deploy/tests/TaxVision.Sms.Tests`.

## 12. Riesgos / notas

- El viejo mezcla catálogo + inventario en un solo servicio; separar bien el bounded context evita arrastrar
  acoplamiento (decisión §10.1).
- `CompanyId`-en-body del viejo es un hueco de aislamiento multi-tenant; el nuevo lo cierra tomando el tenant
  del JWT — **no** portar ese contrato.
- Facturación: snapshot al emitir (no precios retroactivos) — dejarlo explícito para no romper facturas
  históricas al cambiar el catálogo.
- Imágenes stale de Docker (ver `../sms/SMS_Service_Guide.md` §12): verificar el build antes de recrear.
