# Postman y OpenAPI

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd`. Etiquetas: **[Código verificado]**, **[Doc. histórica]**, **[Recomendación]**, **[Decisión pendiente]**.

## 1. Estado actual

Hay **dos carpetas con convenciones distintas**. **[Código verificado]**

| Carpeta | Contenido | Base URL | Token | Environment |
|---|---|---|---|---|
| `Postman_Collection/` | 23 colecciones por servicio (`TaxVision_<Servicio>.postman_collection.json`) + `TaxVision_Backend` | `{{UrlBase}}` (Gateway) y algunas `{{<Svc>DirectBase}}` | `{{accessToken}}` (humano), `{{serviceAccessToken}}`, `{{platformAdminToken}}`, `{{customerPortalAccessToken}}` | `TaxVisionBackEnd.postman_environment.json` |
| `postman/` | `TaxVision-NewServices`, `TaxVision-Campaigns`, `TaxVision-Wallet` + `README.md` | `{{gateway}}` = `http://localhost:5047` | `{{userToken}}`, `{{serviceToken}}` | `TaxVision-Local.postman_environment.json` |

- Todas son esquema Postman v2.1 (`collection.json`). **[Código verificado]**
- Wallet aparece dos veces: `Postman_Collection/TaxVision_Wallet` (1 request) y `postman/TaxVision-Wallet` (8 requests). **[Código verificado]**
- OpenAPI: 26 proyectos referencian `Microsoft.AspNetCore.OpenApi 10.0.9` y `Swashbuckle.AspNetCore 10.2.2`; se exponen **solo en Development**. **[Código verificado]**

## 2. Evidencia

### 2.1 Inventario (requests / con script de test) **[Código verificado]**

| Colección | Requests | Con tests | Auth a nivel colección |
|---|---|---|---|
| TaxVision_Auth | 107 | 31 | no |
| TaxVision_Backend | 114 | 29 | no |
| TaxVision_Calendar | 53 | 3 | no |
| TaxVision_ClientForms | 18 | 4 | bearer |
| TaxVision_CloudStorage | 58 | 42 | no |
| TaxVision_Communication | 156 | 36 | bearer |
| TaxVision_Connectors | 15 | 12 | no |
| TaxVision_Correspondence | 29 | 29 | no |
| TaxVision_Customer | 48 | 14 | no |
| TaxVision_Growth | 16 | 11 | no |
| TaxVision_Notes | 16 | 5 | no |
| TaxVision_Notification | 26 | 9 | no |
| TaxVision_Onboarding | 14 | 12 | no |
| TaxVision_PaymentApp | 15 | 4 | no |
| TaxVision_PaymentClient | 29 | 6 | no |
| TaxVision_Postmaster | 10 | 10 | no |
| TaxVision_Reminder | 17 | 17 | no |
| TaxVision_Scribe | 18 | 15 | no |
| TaxVision_Signature | 85 | 26 | no |
| TaxVision_Subscription | 89 | 25 | no |
| TaxVision_Task | 149 | 99 | no |
| TaxVision_Tenant | 35 | 29 | no |
| TaxVision_Wallet | 1 | 1 | no |
| postman/TaxVision-Campaigns | 34 | 34 | bearer |
| postman/TaxVision-NewServices | 43 | 11 | no |
| postman/TaxVision-Wallet | 8 | 2 | bearer |

Servicios **sin colección**: Billing (solo dentro de NewServices), Catalog, Inventory y Sms (en NewServices), Documents, FinancialInsights, Campaigns (en `postman/`), ContentScanner. **[Código verificado]**

### 2.2 Convenciones observadas **[Código verificado]**

- Colección de Notes (`Postman_Collection/TaxVision_Notes.postman_collection.json`): descripción que nombra el environment, los actores (`[AllowActorTypes]`) y la regla "TenantId/UserId siempre del JWT, nunca de body/query"; carpetas por superficie ("Notas (staff)", "Portal (CustomerPortal)"); nombre de request = acción (`Create`, `GetById`, `Archive`); header `Authorization: Bearer {{accessToken}}` por request; script de test:
  ```javascript
  pm.test("status 201", () => pm.response.code === 201);
  if (pm.response.code === 201) {
    const body = pm.response.json();
    pm.environment.set("notesNoteId", body.id);
  }
  ```
- Ids encadenados con prefijo del servicio en el environment (`notesNoteId`, `reminderId`, `scribeTemplateId`, `connectorsAccountId`...).
- Headers usados: `Authorization`, `Content-Type`, `Idempotency-Key` (Growth 14, Backend, Calendar, Customer), `Origin` (Auth, Tasks), `Stripe-Signature` (webhooks), `X-Form-Session` (Tasks), `If-None-Match` (CloudStorage).
- **Nadie manda `X-Tenant-Id`**: el tenant sale del JWT (o del Host en Auth). **Nadie manda `X-Correlation-Id`** (el middleware lo genera si falta). **[Código verificado]** (grep sin resultados)

### 2.3 Environments (solo nombres; valores revisados sin imprimir)

- `TaxVisionBackEnd.postman_environment.json`: 160+ variables; todas las URLs y tokens están **vacíos**; los sensibles con `type: secret` (`serviceAccessToken`, `serviceClientSecret`, `connectorsManual*Password`, `postmasterProviderPassword`, `onboarding*Token`). **[Código verificado]**
- `postman/TaxVision-Local.postman_environment.json`: `gateway` (`http://localhost:5047`), `authService`, `platformTenantId`, `tenantId`, `serviceClientId`, `serviceClientSecret` (secret, vacío), `userEmail`, `userPassword` (secret, vacío). **[Código verificado]**
- El README dice que los secretos "nunca se commitean" y se llenan a mano (`postman/README.md:57-60`). **[Doc. histórica]**

### 2.4 Flujo documentado

`postman/README.md` **[Doc. histórica]**: probar **contra el Gateway** (`:5047`), no contra cada puerto (3-4); `Auth → Service token (M2M)` va directo a Auth (`{{authService}}`, `:5124`), porque el Gateway no publica el grant, y llena `{{serviceToken}}`; `Auth → Login` llena `{{userToken}}` si no hay MFA (MFA no incluido porque el TOTP es interactivo, 95-96); endpoints `internal` no van en la colección porque el gateway no los expone (49-51); webhooks anónimos firmados por el proveedor (97-98); 403 sin permiso y 429 con `X-RateLimit-*` (99-105). "Extender a otros servicios": duplicar una carpeta, cambiar prefijo y `Authorization`, tomar el contrato del controller (107-112).

### 2.5 OpenAPI / Swagger **[Código verificado]**

`src/Services/Notes/TaxVision.Notes.Api/Program.cs`:
- 48-49: `builder.Services.AddOpenApi(); builder.Services.AddSwaggerGen();`
- 181-186: `if (app.Environment.IsDevelopment()) { app.MapOpenApi(); app.UseSwagger(); app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Notes API v1")); }`

Mismo patrón en los 26 Program.cs listados por grep. El gateway no publica Swagger. No hay generación de colección Postman desde OpenAPI.

## 3. Problemas

1. **Dos convenciones incompatibles** (`UrlBase/accessToken` vs `gateway/userToken`) y dos environments. **[Código verificado]**
2. **Wallet duplicado** en ambas carpetas. **[Código verificado]**
3. **Cobertura de tests desigual**: Calendar 3/53, PaymentApp 4/15, Notes 5/16. **[Código verificado]**
4. `postman/README.md:101-105` dice que `RateLimit:EnforceTierQuotas` está "OFF por default", pero el compose de producción lo pone `"true"` en servicios como wallet (`deploy/docker/docker-compose.yml:1709`). **[Doc. histórica]** vs **[Código verificado]**
5. `postman/README.md:109-110` cita prefijos `/subscription` y `/tenant`; el gateway usa `/subscriptions`, `/plans`, `/tenants`, etc. **[Doc. histórica]** vs **[Código verificado]** (`Gateway_YARP.md` §2.2)
6. **Swagger solo en Development** y con los dos paquetes (OpenApi + Swashbuckle) a la vez: duplica generadores. **[Código verificado]** **[Decisión pendiente]**
7. Ninguna colección se ejecuta en CI (no hay Newman). **[Código verificado]**

## 4. Buenas prácticas ya vigentes

- Todo por el Gateway; tenant del JWT; ids encadenados con scripts de test; secretos vacíos y marcados `secret`.
- Una colección por servicio en `Postman_Collection/` con descripción que documenta actores y permisos.

## 5. Recomendación

- **[Decisión pendiente]** Elegir una sola convención. Por volumen, `Postman_Collection/` con `{{UrlBase}}` + `{{accessToken}}` y el environment `TaxVisionBackEnd` es la mayoritaria (23 de 26 colecciones).
- **[Recomendación]** Colección por servicio: `Postman_Collection/TaxVision_<Svc>.postman_collection.json`, variables nuevas en el environment con prefijo del servicio, sin valores.
- **[Recomendación]** Usar `/openapi/v1.json` del servicio en local (Development) como fuente del contrato al escribir requests.

## 6. Riesgos

- Exportar el environment desde Postman con tokens ya llenos y commitearlo filtra credenciales. Hoy están vacíos.
- Requests contra el puerto directo del servicio saltan el gateway (CORS, rate limit pre-auth, seat gate, host guard): no prueban lo que ve el usuario.
- Webhooks: firmar a mano solo con secretos de prueba locales.

## 7. Pruebas

No hay pruebas automáticas sobre las colecciones. Los scripts `pm.test` solo corren dentro de Postman.

## 8. Dependencias

- Gateway (`:5047` local); Auth directo (`:5124`) para el service token; MFA, permisos del catálogo de Auth.

## 9. Material para la guía

### 9.1 Pasos para la colección de un servicio nuevo

1. Copiar `Postman_Collection/TaxVision_Notes.postman_collection.json` como `TaxVision_<Svc>.postman_collection.json` y cambiar `info.name` y `info.description` (environment, actores `[AllowActorTypes]`, permisos `[HasPermission]`, regla de tenant del JWT).
2. Carpetas por superficie: staff, portal (`CustomerPortal`), público/anónimo, webhooks. Sin carpeta `internal`.
3. URL `{{UrlBase}}/<prefijo>/...` (el mismo prefijo de la ruta YARP).
4. Header `Authorization: Bearer {{accessToken}}` (o `{{customerPortalAccessToken}}`, `{{serviceAccessToken}}`, `{{platformAdminToken}}` según el actor). `Idempotency-Key` donde el endpoint lo pida.
5. En cada `POST` de creación, script de test que valide el código y guarde el id en `<svc><Entidad>Id`.
6. Agregar las variables nuevas a `TaxVisionBackEnd.postman_environment.json` **vacías**; marcar `type: secret` las sensibles.
7. Probar el 401 sin token, el 403 sin permiso y el 404 de otro tenant.

### 9.2 Variables comunes

| Variable | Uso |
|---|---|
| `UrlBase` | Gateway (`http://localhost:5047` en local) |
| `accessToken` / `refreshToken` | Login humano (`/auth/login`) |
| `serviceAccessToken` | Token M2M (client credentials) |
| `platformAdminToken` | PlatformAdmin |
| `customerPortalAccessToken` | Portal del cliente |
| `tenantId` | Solo informativo: el tenant real va en el JWT |
| `<Svc>DirectBase` | Excepción para endpoints sin ruta en el gateway; evitar |

CorrelationId: opcional; si se manda `X-Correlation-Id` debe cumplir `^[A-Za-z0-9._-]{1,128}$` o el servicio genera otro (`src/BuildingBlocks/BuildingBlocks.Web/Middleware/CorrelationIdMiddleware.cs:12-26`).
