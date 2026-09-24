# Guía DEVS — Categorías de firma por tenant (Backlog Punto 14.5)

Cómo funcionan las **categorías** de las solicitudes/plantillas de firma: un set fijo de **sistema**
más las **custom** que cada tenant crea, renombra y archiva. El preparador puede añadir una nueva desde
el propio Wizard y el listado se refresca al vuelo.

Servicios: **Signature** (`/signature/categories`), frontend `features/signature`.

---

## 1. Modelo: sistema ∪ custom, guardado como texto congelado

- **Sistema** (fijas en código): `SignatureCategoryDefaults.Names = Enum.GetNames<SignatureCategory>()`
  → `Fiscal, EngagementLetter, ConsentToDisclose, BankAuth, Other`. No se pueden renombrar ni archivar.
- **Custom** (por tenant): entidad `TenantSignatureCategory` (`TenantEntity`) en la tabla
  `SignatureCategories`. Campos: `Name`, `NormalizedName` (trim + colapsar espacios + UPPER), `IsArchived`.
  Índice **único `(TenantId, NormalizedName)`** → dedup a nivel de BD (incluye archivadas). Nombre 2–60 chars;
  rechaza los nombres reservados de sistema.
- **La categoría se guarda en cada `SignatureRequest`/`SignatureTemplate` como STRING (nombre) CONGELADO**,
  no como FK. Por eso renombrar o archivar una categoría **NO reescribe el histórico** ni las métricas: la
  tabla es solo el *pick-list*. `Category` en el aggregate pasó de enum a `string` con guarda de forma
  (`MaxCategoryLength=64`, no vacío) — el enum `SignatureCategory` se conserva solo para sistema/consent/analytics.
- **Efectivo en el picker** = sistema ∪ custom-no-archivadas.

## 2. Validación al crear/editar una solicitud o plantilla

`ISignatureCategoryResolver` valida el texto entrante contra **sistema ∪ custom-no-archivada** y devuelve
el **nombre canónico** (sistema → nombre del enum; custom → `Name` tal como lo definió el tenant). Cableado
en los 4 handlers: create/update de request y create/updateMetadata de template. Una categoría inexistente
o archivada → error de validación (no se guarda basura).

## 3. Endpoints (staff, `/signature/categories`)

| Método | Ruta | Qué hace | Permiso |
|---|---|---|---|
| `GET` | `/signature/categories` | Lista sistema + custom **no archivadas**. | `signature.request.read` |
| `GET` | `/signature/categories?includeArchived=true` | Incluye también las custom archivadas (pantalla de gestión). | `signature.request.read` |
| `POST` | `/signature/categories` | Crea una categoría custom. | `signature.request.create` |
| `PUT` | `/signature/categories/{id}` | Renombra una custom. | `signature.request.create` |
| `POST` | `/signature/categories/{id}/archive` | Archiva (soft) una custom: la saca del picker. | `signature.request.create` |
| `POST` | `/signature/categories/{id}/unarchive` | La vuelve a mostrar en el picker. | `signature.request.create` |

- **Sin permiso nuevo**: se reusa `signature.request.create` (crear/renombrar/archivar) y
  `signature.request.read` (listar) — decisión de diseño: *cualquier preparador* que crea solicitudes puede
  añadir/gestionar categorías. Actor types: `TenantEmployee`, `TenantAdmin`, `PlatformAdmin`.
- **GET** → `{ "categories": [{ "id": <guid|null>, "name": <string>, "isSystem": <bool>, "isArchived": <bool> }] }`.
  Las de sistema traen `id: null` e `isSystem: true`.
- **POST** body `{ "name": "Payroll" }`. `200` con el `SignatureCategoryResponse` creado. Errores:
  `400 Signature.Category.Duplicate` (ya existe, case/espacios-insensible o choca con sistema),
  `400` por longitud (< 2 o > 60).
- **PUT** body `{ "name": "Payroll US" }` → `204`. `400 Signature.Category.Duplicate` / `404 Signature.Category.NotFound`.
- **archive / unarchive** → `204` (sin body). `404 Signature.Category.NotFound`.

## 4. Consent y analytics con categorías custom

- **Consent** (`IConsentTextProvider.Resolve(string category, lang)`): mapea la categoría a un enum de
  sistema con `SignatureCategoryParsing.ToSystemCategory`; una **custom cae en `Other` → texto genérico**.
  La redacción legal específica por categoría es alcance de 14.4 (no se inventa aquí).
- **Analytics**: el snapshot sigue con grano por enum. Los call sites envuelven el string con
  `ToSystemCategory`, así que una **custom cuenta como `Other`** en las métricas by-category. Sin cambio de esquema.

## 5. Persistencia / migraciones

- `20260923140304_AddSignatureCategories` — tabla `SignatureCategories` + índice único `(TenantId, NormalizedName)`.
- `20260923142705_WidenSignatureCategoryToTenantNames` — `Request.Category`/`Template.Category` pasan a
  `nvarchar(64)` sin `HasConversion` (antes enum→`nvarchar(32)`).
- Repo: `IgnoreQueryFilters()` + `TenantId` explícito (mismo criterio que el resto del read-path de Signature).

## 6. Frontend (`features/signature`)

- **`signature-category-picker`** (reusable): dropdown sistema + custom del tenant, con **"＋ New"** inline
  (input → `createCategory` → selecciona la recién creada). Muestra siempre el valor actual aunque sea custom
  o archivado, para no perder la selección al continuar un borrador. Cableado en: paso final del Wizard,
  modal *Edit details*, editor de plantillas y página de plantillas.
- **`signature-category-manager`** (modal de gestión, F4): renombrar y archivar/restaurar las custom; las de
  sistema como chips solo-lectura. Se abre desde el botón **"Categories"** del header de la página de Signature.
- **Store** `SignatureStore`: `categories()` (todas, incl. archivadas), `activeCategories()` (no archivadas, para
  el picker), `loadCategories(force)` (carga con `includeArchived=true`), `createCategory` / `renameCategory` /
  `archiveCategory` / `unarchiveCategory` (cada uno refresca).
- El copy es siempre en inglés; el valor guardado es el **nombre**, no un id.
