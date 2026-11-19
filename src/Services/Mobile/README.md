> Actualizacion 2026-11-19: el servicio ahora incluye recorridos, GPS, estados, estadisticas y tarifas versionadas. El contrato vigente de esa ampliacion esta en [TRACKING_API.md](TRACKING_API.md). Las secciones siguientes documentan la entrega inicial de vehiculos; su exclusion de tracking corresponde a ese alcance original.

# Mobile — vehicle maintenance

Mobile owns vehicles and trips for authenticated TaxVision customers, including GPS synchronization, state history, statistics and office mileage estimates. See [TRACKING_API.md](TRACKING_API.md) for the current contract and [VALIDATION_TRACKING.md](VALIDATION_TRACKING.md) for verification. Automatic driving detection runs on the device.

## Discovery and architecture

Analysis was completed before implementation against `documents/GuiaMaestraMicroservicios/GUIA_MAESTRA_CREACION_MICROSERVICIOS_TAXVISION.md`, its entry point, and the real Notes, Customer, FinancialInsights, Auth and Gateway implementations. The mobile client was found at `D:/TaxPro-Milage` (not in the backend repository).

The service follows the existing four projects: Domain (sealed entities and validated details), Application (commands/queries, static Wolverine handlers, repository ports and typed DTOs), Infrastructure (SQL Server EF Core, configuration, repositories and permission recovery), Api (thin authenticated controllers). It reuses Result/Error, explicit HTTP error mapping, actor authorization, fail-closed tenant filters, UTC conversions, IUnitOfWork, Wolverine SQL transactions/durable inbox/outbox, static handler generation, Redis rate limits/session denylist, Serilog, OpenTelemetry and development OpenAPI.

Customer identity follows PortalCustomerController: `tenant_id`, authenticated user and `customer_id` come from verified JWT claims. Every vehicle lookup additionally filters both tenant and customer, including when Wolverine has no ambient tenant context. No client owner identifiers are accepted in the DTO. Unknown JSON properties are ignored by normal MVC binding; supplying `tenantId`, `customerId` or `companyId` cannot change ownership. Inaccessible resources return the same 404 as missing resources.

Permissions are `mobile.vehicles.read` and `mobile.vehicles.manage`, CustomerPortal-only, added to Auth's default portal role. The Auth migration inserts permission catalog rows only; existing system roles are synchronized by Auth at startup. Mobile consumes effective permission snapshots and lifecycle events, with M2M pull recovery on a missing snapshot. Role permission unions are never reconstructed locally. Auth's existing reconciliation republishes effective permissions. Employee offboarding affects only the local permissions projection: vehicles belong to customers and have no staff references or work to reassign. Mobile's lifecycle consumer is registered in Auth's offboarding participants.

Assumption: vehicle maintenance is a basic portal capability, with no new commercial plan/module or price. Base safety rate limits apply (read 300/min per tenant/user, write 60/min; tenant overlays 3000/600). Tiered plan quotas are disabled deliberately: this foundation has no plan projection.

## HTTP contract

Use the Gateway (`http://localhost:5047` in development). All requests require `Authorization: Bearer <CustomerPortal access token>` selected for the customer's office. No tenant headers or owner fields are needed.

| Method | Route | Success | Permission |
| --- | --- | --- | --- |
| POST | `/mobile/vehicles` | 201 + vehicle + Location | mobile.vehicles.manage |
| GET | `/mobile/vehicles/{id}` | 200 + vehicle | mobile.vehicles.read |
| PUT | `/mobile/vehicles/{id}` | 200 + replacement vehicle | mobile.vehicles.manage |
| DELETE | `/mobile/vehicles/{id}` | 204, empty body | mobile.vehicles.manage |
| GET | `/mobile/vehicles?page=1&size=50` | 200 + PagedResult | mobile.vehicles.read |

Create and PUT share the same editable fields. PUT replaces the details; omitted optional fields become null, omitted mileage becomes 0 and omitted isDefault becomes false.

```json
{
  "plate": "ABC-123",
  "make": "Toyota",
  "model": "Corolla",
  "year": 2024,
  "color": "Blue",
  "photo": "https://example.com/car.jpg",
  "currentMileage": 12500.25,
  "isDefault": true
}
```

Only plate is required (trimmed, uppercase, 1–32 characters). Make/model: at most 100 characters; color: 50; year: 1886–2100 or null; currentMileage: 0–999999999.99, at most two decimal places, stored as an entered value without calculations. Photo: optional HTTPS URL, at most 2048 characters. Binary/base64 upload is not part of this service. No external photo URL is fetched by the server.

201/200 vehicle response (no success/data envelope, consistent with the current portal APIs):

```json
{
  "id": "be3ff498-d523-41f4-924d-830f9ee3b791",
  "plate": "ABC-123",
  "make": "Toyota",
  "model": "Corolla",
  "year": 2024,
  "color": "Blue",
  "photo": "https://example.com/car.jpg",
  "currentMileage": 12500.25,
  "isDefault": true,
  "createdAt": "2026-11-18T20:00:00Z",
  "updatedAt": "2026-11-18T20:00:00Z"
}
```

List response:

```json
{
  "items": [], "page": 1, "size": 50, "totalCount": 0,
  "totalPages": 0, "hasMore": false, "hasPrevious": false
}
```

Page must be positive; size is 1–100. Order: default first, then creation time and ID. Empty collections return 200. IDs are GUIDs; a malformed GUID does not match the resource route (404).

Errors: 400 invalid input/model binding/pagination; 401 missing, invalid, revoked or stale token, or missing customer identity; 403 disallowed actor/permission/surface; 404 missing or other owner's vehicle; 409 duplicate plate/default race or concurrent row update; 429 rate limit. Gateway limits bodies to 16 KiB (413 when exceeded). Business errors use `{ "code": "Vehicle.NotFound", "message": "Vehicle was not found." }`; framework/auth failures follow the shared middleware's ProblemDetails/validation format. Responses use `Cache-Control: no-store` after identity validation.

Plates are unique per tenant/customer. At most one vehicle per tenant/customer can be default; selecting one clears the previous default in the same SQL transaction. Concurrent conflicting writes return 409. A customer may have no explicit default; deleting a default does not silently select another. The app falls back to the first listed vehicle for its existing profile lookup. RowVersion protects overlapping database writes; this API does not introduce an ETag or client edit-version protocol.

## Persistence and operation

`MobileDbContext` owns Vehicles and UserPermissionsProjections. The initial migration includes owner indexes, unique plate/default indexes, check constraints and rowversion. Migrations are never applied on API startup. Apply both Auth's AddMobileVehiclePermissions and Mobile's InitialCreate before using the endpoints; restart Auth so SystemRolePermissionsSyncService grants the new portal defaults. Clients may need to refresh tokens after Auth changes permission versions.

Development port: 5280. Configure Mobile user-secrets (`taxvision-mobile-development`) with isolated development values:

- `ConnectionStrings:Default`: dedicated Mobile SQL database.
- `ConnectionStrings:Redis` and `RabbitMq:Uri`: sandbox infrastructure.
- `Jwt:PublicKeyPath` or `Jwt:PublicKeyPem`, `Jwt:Issuer`, `Jwt:Audience`: matching sandbox Auth.
- `ServiceAuthClient:AuthBaseUrl`, `ClientId`, `ClientSecret`: the same dedicated mobile-worker client configured in sandbox Auth.

Production names: `MOBILE_DB_CONNECTION`, `MOBILE_SERVICE_CLIENT_ID` (default mobile-worker), `MOBILE_SERVICE_CLIENT_SECRET`. Load these into GitHub environment `production-extra`; deploy.yml writes them, Compose passes them to Mobile/Auth and the migration runner. Database and secret are required by Compose. No secret values were created or copied. The service uses only internal expose:8080, with the existing JWT public key mount; no new public hostname or Cloudflare change is needed. Gateway routing, cluster health, load-shedding criticality and Compose dependencies are registered. Fleet start/restart/stop and codegen lists include Mobile.

```powershell
dotnet ef database update --project src/Services/Mobile/TaxVision.Mobile.Infrastructure --startup-project src/Services/Mobile/TaxVision.Mobile.Api
# Or use the repository migration wrapper after configuring sandbox user-secrets:
./scripts/migrate-local.ps1 -Apply -Yes -Service Mobile
```

Health endpoints are `/health/live` and `/health/ready` on the direct service. Ready checks SQL connectivity and RabbitMQ TCP; it is not proof that every dependency or deployed schema works. OpenAPI/Swagger are exposed directly only in Development. Postman collection: `Postman_Collection/TaxVision_Mobile.postman_collection.json`.

## Mobile app compatibility

The old vehicle routes `/client/tracking/createVehicle` and `/client/tracking/getVehicle` had no matching backend implementation. The app now uses `/mobile/vehicles` for all vehicle operations. Its service strips noneditable fields from writes, consumes typed responses, follows all list pages, and returns a vehicle or null for the existing getProfileVehicle helper. Profile loading uses the list directly; create consumes Vehicle rather than boolean; edit/delete/default selection persist through HTTP. Trip's existing vehicle consumer reads the same typed vehicle. Vehicle photo editing accepts HTTPS references instead of falsely reporting a local camera image as saved remotely. Other tracking endpoints and behavior remain outside this implementation.

Existing unrelated changes in backend and mobile worktrees were preserved. See VALIDATION.md and FILES_CHANGED.md for the checks and exact implementation files. Live production rollout, real-device checks, real Auth login, Redis/RabbitMQ behavior and full fleet readiness remain separate from isolated automated tests.
