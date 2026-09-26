# Guía DEVS — Firma del preparador / oficina (My Signature)

Firma **reutilizable** del preparador que se **estampa** en el documento sellado (cuño estilo Form 8879);
el **firmante NO la ve**. Tres piezas: (1) **perfil** de firma reutilizable (personal u oficina),
(2) **campo del preparador** colocado sobre el PDF, (3) **identidad 8879** opcional que referencia al
preparador en el certificado de finalización.

Servicios: **Signature** (perfiles, campos, identidad, sellado, certificado), **Notification** (correos),
frontend `features/signature`. La imagen PNG vive en **CloudStorage** (solo el metadato + `FileId` en Signature).

---

## 1. Perfiles de firma (personal vs oficina)

- Entidad `SignatureProfile` (`TenantEntity`): `OwnerUserId` (con valor = **personal**; **null = oficina**),
  `Label`, `FileId` (PNG en CloudStorage), `Width/Height`, `IsDefault`, `IsArchived`.
- Ámbito = `(TenantId, OwnerUserId)`. Tope **10 activas por ámbito** (`SignatureProfile.MaxActiveProfilesPerScope`;
  archivar libera cupo). La **primera activa** del ámbito queda default.
- Autoría (`SignatureProfileAuthorization`): la **oficina** solo la gestiona un **admin**; la **personal**, uno mismo (o un admin).
- **Firma efectiva** (`EffectiveSignatureResolver`): default personal si el tenant lo permite, si no la de oficina.

## 2. Gobernanza: `AllowEmployeeOwnSignature` (toggle del tenant)

- Vive en `TenantSignatureSettings.AllowEmployeeOwnSignature` (default `true`), editable en `PUT /signature/settings`.
- **OFF** = el **empleado** (no-admin) **no** puede ver/usar/crear su firma **personal**; solo la de **oficina**.
  **Admins exentos**; la firma de oficina siempre está disponible para todos.
- Política central `SignatureVisibilityPolicy.CanUsePersonal(actorIsAdmin, settings)` aplicada en 3 puntos
  (el resolver es la 2ª línea de defensa):
  - **Listado** `ListVisibleAsync(..., includePersonal)` → solo oficina para empleado con OFF. La respuesta trae
    **`canManageOwnSignature`** para que el front oculte la sección personal.
  - **Estampado explícito** (`SetPreparerSignature`) valida el `FileId` contra esa lista filtrada.
  - **Crear personal** bloqueado → `Signature.Profile.OwnSignatureDisabled` (403).

## 3. Campo del preparador (request y plantilla)

- Es una **parte aparte**, no un firmante: entidades `PreparerField` (request) y `TemplatePreparerField` (plantilla)
  — solo `Kind` + posición normalizada `[0..1]` + `Label?`, **sin imagen**. Se modelaron **separadas** de
  `SignatureField` (que es hijo obligatorio de un `Signer`) para no corromper los campos de firmante.
- La plantilla guarda **solo la posición**; al instanciar (`InheritPreparerFieldsAsync`) se heredan las posiciones y
  la **imagen se resuelve por-preparador** con la firma efectiva de quien instancia. Así una plantilla la reutiliza
  cualquier preparador con **su propia** firma; nunca se hornea una imagen concreta en la plantilla.
- **Multipágina soportado**: `FieldPosition.Page` (1-based, sin tope) → el sellado estampa en `pdf.Pages[Page-1]`
  con bounds-check. (Caveat: una página fuera de rango se descarta en silencio al sellar.)

## 4. Identidad 8879 + certificado

- `PreparerInfo` (owned en `SignatureRequest`): `PtinOrEfin`, `DisplayName`, `TitleLabel?`. Se fija con
  `PUT /signature/requests/{id}/preparer` mientras la request está en **Draft/Ready** (metadata inmutable tras enviar).
  Se captura **inline en el wizard** (no obliga a guardar como borrador primero).
- **Certificado de finalización**: referencia al preparador (nombre + **PTIN/EFIN enmascarado** + timestamp)
  **solo cuando** la identidad 8879 está fijada **y** hay firma del preparador estampada (`BuildPreparerEntry`).
  Sin 8879 el acta **no** lo referencia. **Decisión cerrada: referenciar solo con 8879** (no imagen en el acta).

## 5. Sellado (PdfSharp)

- Un PNG transparente se sella **NEGRO** en PdfSharp → se **aplana sobre blanco** con ImageSharp
  (`FlattenOntoWhite`) antes de estampar. Iniciales/valores auto-ajustan tamaño (`FitFontSize`).
- El PAdES/CMS opera sobre el PDF entero y es page-agnostic. El firmante nunca ve el campo del preparador.

## 6. Endpoints

Auth: **Bearer** JWT; `TenantId`/`UserId` salen del token. Actor types `TenantEmployee`/`TenantAdmin`/`PlatformAdmin`
(admins hacen bypass de `[HasPermission]`).

### Perfiles — `/signature/profiles`
| Método | Ruta | Body | Permiso |
|---|---|---|---|
| GET | `/effective` | — | `signature.request.read` |
| GET | `/?includeArchived=false` | — | `signature.request.read` |
| POST | `/` | `{ label, scope, imageBase64 }` (`scope="office"`=oficina/admin; otro=personal; PNG base64 sin prefijo) | `signature.request.create` |
| PUT | `/{id}` | `{ label }` | `signature.request.create` |
| POST | `/{id}/default` | — | `signature.request.create` |
| POST | `/{id}/archive` · `/{id}/unarchive` | — | `signature.request.create` |
| DELETE | `/{id}` | — | `signature.request.create` |

- `SignatureProfileResponse`: `{ id, ownerUserId, isOffice, label, fileId, width, height, isDefault, isArchived }`.
- `GET /` → `{ profiles: SignatureProfileResponse[], canManageOwnSignature: bool }`.

### Preparador en la request — `/signature/requests/{id}`
| Método | Ruta | Body | Permiso |
|---|---|---|---|
| POST | `/preparer-fields` | `{ kind, page, x, y, width, height, label? }` (x/y/w/h `[0..1]`) | `signature.document.prepare` |
| DELETE | `/preparer-fields/{fieldId}` | — | `signature.document.prepare` |
| PUT | `/preparer-signature` | `{ signatureFileId? }` (null = firma efectiva) | `signature.document.prepare` |
| PUT | `/preparer` | `{ ptinOrEfin, displayName, titleLabel? }` (identidad 8879) | `signature.request.create` |
| DELETE | `/preparer` | — (limpia `PreparerInfo`) | `signature.request.create` |
| POST | `/preparer/sign` | — (estampa; captura IP/UA) | `signature.document.sign` |

- `PreparerFieldResponse`: `{ id, kind, page, x, y, width, height, label? }`.

### Plantilla — `/signature/templates/{id}`
| Método | Ruta | Body | Permiso |
|---|---|---|---|
| POST | `/preparer-fields` | `{ kind, page, x, y, width, height, label? }` | `signature.template.update` |
| DELETE | `/preparer-fields/{fieldId}` | — | `signature.template.update` |

- Respuesta del POST: `TemplatePreparerFieldCreatedResponse { id }`.

### Settings — `/signature/settings`
| Método | Ruta | Permiso |
|---|---|---|
| GET | `/signature/settings` | `signature.settings.manage` |
| PUT | `/signature/settings` (reemplazo total) | `signature.settings.manage` |

- Incluye `allowEmployeeOwnSignature: bool` (ver §2). El resto de campos son la config general del tenant.

## 7. Migraciones (Signature)

`20260924000943_AddSignatureProfiles` · `20260924002235_AddAllowEmployeeOwnSignature` ·
`20260924003738_AddPreparerFields` · `20260924012045_AddTemplatePreparerFields`.
El fix de idempotencia de correo (Notification) **no** llevó migración (el índice `RelatedEventId` no es único).

## 8. Frontend (`features/signature`)

- **My Signatures** (`signature-profiles-manager`): CRUD de firmas personales + oficina (admin), default, archivar;
  toggle de gobernanza (admin). Con `canManageOwnSignature=false` oculta la sección personal del empleado.
- **Editor** (`signature-pdf-editor`): bloque **"Preparer signature"** plegable → selector de firma + **"Place my signature"**
  + card **8879** (revelada al colocar la firma; nombre **autollenado** del perfil vía `AuthService.currentUser()`,
  reactivo). "Add signer" usa un **buscador** de clientes (typeahead del directorio compartido `core/customers`, avatares).
- **Editor de plantillas** (`signature-template-editor`): botones **"Preparer: Signature/Initials/Date"** colocan el placeholder.
- La vista del firmante (`sign-document-view`) **no** muestra el campo del preparador.

## 9. Correo al completar (nota Notification)

Al completar, Signature publica **un** evento por tipo (documento firmado / certificado) con **todos** los firmantes;
Notification hace **fan-out** de 1 correo por firmante. La idempotencia de Notification es **por destinatario**
(`RelatedEventId + TemplateKey + Recipient`): sin el destinatario colapsaba el fan-out y el **último firmante no recibía**.
