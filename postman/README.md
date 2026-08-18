# TaxVision — Postman collections

Colecciones para probar los microservicios **contra el Gateway** (`http://localhost:5047`), sin
pegarle directo a cada puerto. Cubren los servicios construidos/verificados en esta tanda:
**Auth**, **SMS**, **Catalog**, **Inventory** y el wiring de **Billing** con el catálogo.

## Archivos

| Archivo | Qué es |
|---|---|
| `TaxVision-NewServices.postman_collection.json` | Colección v2.1: carpetas Auth / SMS / Catalog / Inventory / Billing. |
| `TaxVision-Local.postman_environment.json` | Environment con `gateway`, `tenantId` y los secretos (vacíos, se llenan a mano). |

## Importar

1. Postman → **Import** → arrastrá los dos `.json`.
2. Arriba a la derecha, seleccioná el environment **TaxVision — Local**.
3. Llená los secretos (nunca se commitean):
   - `serviceClientSecret` → valor de `.env` (`NOTIFICATION_SERVICE_CLIENT_SECRET`).
   - `userPassword` → password del admin de bootstrap (o del usuario del tenant).
   - `tenantId` → el tenant real que estés probando.

## Variables

Se guardan a nivel colección; los scripts de test las rellenan solas:

| Variable | Origen |
|---|---|
| `gateway` | `http://localhost:5047` (fijo). |
| `tenantId` | tu tenant. |
| `serviceClientId` / `serviceClientSecret` | credencial M2M (del `.env`). |
| `serviceToken` | lo guarda **Auth → Service token** automáticamente. |
| `userToken` | lo guarda **Auth → Login** (si no hay MFA de por medio). |
| `categoryId`, `catalogItemId`, `supplierId`, `itemSupplierId`, `invoiceId` | los guardan los `POST` de creación. |

## Flujo recomendado (end-to-end)

1. **Auth → Service token (M2M)** — puebla `{{serviceToken}}` (lo usan SMS/Catalog/Inventory).
2. **Catalog → Categories → Create category** — guarda `{{categoryId}}`.
3. **Catalog → Items → Create item** — guarda `{{catalogItemId}}`. Si `kind = Product` y
   `trackInventory = true`, Inventory abre un `StockLevel` automáticamente (vía evento).
4. **Inventory → Stock → Adjust stock (Purchase)** — sube existencias del ítem.
5. **Inventory → Suppliers → Create supplier** + **Item-Suppliers → Upsert link**.
6. **SMS → Send SMS** — requiere `sms.send`; el proveedor activo lo define `Sms:DefaultProvider`.
7. **Billing → Create invoice draft** — usa `{{userToken}}` (actor TenantAdmin/PlatformAdmin) y
   referencia `{{catalogItemId}}` en la línea (traza débil catálogo→factura, Fase 8).

## Notas importantes

- **Autenticación:** SMS/Catalog/Inventory corren con **token de servicio (M2M)**; Billing con
  **token de usuario** (necesita rol + `billing.manage`). Cada carpeta ya trae su `Authorization:
  Bearer` apuntando a la variable correcta.
- **MFA:** si el tenant tiene MFA de 2 pasos activo, `POST /auth/login` devuelve un challenge; hay
  que completar `/auth/mfa/verify` (no incluido aquí porque el TOTP es interactivo).
- **Webhooks SMS:** las requests de webhook son **anónimas** (las firma el proveedor). Cambiá el
  segmento `{provider}` en la URL (`fake` → `infobip`/`twilio`) según lo que estés probando.
- **RBAC:** un token sin el permiso correcto responde **403**; ver la matriz de permisos en
  `documents/architecture/catalog/` y `documents/architecture/sms/`.
- **Rate limiting:** Catalog/Inventory/SMS ya aplican cuota por tenant/usuario (`[RateLimit]`). Si
  excedés la cuota vas a ver **429** con headers `X-RateLimit-*` + `Retry-After` (p.ej. `catalog.g.write`
  = 60/min, `sms.h.send` = 30/min). La escala por plan (`TenantPlanCodeProjection`) está cableada pero
  detrás del flag `RateLimit:EnforceTierQuotas` (OFF por default). Ver
  `documents/architecture/ratelimiting/RateLimit_NewServices_Plan.md`.

## Extender a otros servicios

Cada servicio expone sus rutas por el Gateway con su prefijo (`/auth`, `/billing`, `/catalog`,
`/inventory`, `/sms`, `/subscription`, `/tenant`, …). Para añadir requests de otro servicio:
duplicá una carpeta, cambiá el prefijo de ruta y el `Authorization`, y tomá el contrato exacto del
controller correspondiente en `src/Services/<Servicio>/.../Controllers`.
