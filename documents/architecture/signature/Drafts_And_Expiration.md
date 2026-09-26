# Guía DEVS — Borradores y expiración de Signature (Backlog Punto 10)

Cómo funcionan los **borradores** de solicitudes de firma (crear sin enviar, continuar/editar,
borrar) y el **modelo de expiración** (el reloj de firma corre desde el envío, no desde la creación).

Servicios: **Signature** (`/signature/requests`), frontend `features/signature`.

---

## 1. Ciclo de vida y qué es un "borrador"

`SignatureRequestStatus`: `Draft → Ready → InProgress → Completed | Rejected | Canceled | Expired`.

- **Draft**: recién creada; el documento puede estar aún procesándose en CloudStorage.
- **Ready**: el documento ya está disponible/validado y la solicitud *podría* enviarse — **pero aún no
  se ha enviado**. Sigue siendo editable. Draft→Ready es **automático** cuando el archivo queda
  disponible (`MarkReadyForSending`, más el `ReadyReconciliationScheduler` como red de seguridad).
- **Draft y Ready = "borrador sin enviar"**: ambos son editables (`EnsureCanBeEdited`) y forman la
  pestaña **Drafts** del front (ver `editableOnly` abajo). Solo `Send` (Ready→InProgress) los envía.

## 2. Expiración: el reloj corre desde el envío

- `SignatureRequest.Send(sentAtUtc)` fija `ExpiresAtUtc = sentAtUtc + TokenExpirationHours`. Los
  borradores **no expiran** por el reloj de firma (su `ExpiresAtUtc` en Draft/Ready es provisional y
  se recalcula al enviar).
- `ExpirationScheduler` (cada 15 min) solo marca `Expired` a las **InProgress** vencidas
  (`ISignatureRequestRepository.ListExpiredCandidatesAsync` = `Status==InProgress && ExpiresAtUtc<=now`).
- **Retención de borradores**: `DraftRetentionScheduler` (diario) borra EN FIRME los Draft/Ready sin
  `LegalHold` no tocados desde hace `RetentionDays` (default **30**, como DocuSign). Config
  `Signature:DraftRetention` (`Enabled` default true, `RetentionDays`, `BatchSize`). Es la única
  limpieza de borradores; no expiran por firma.

## 3. Endpoints nuevos (staff, `/signature/requests`)

| Método | Ruta | Qué hace | Permiso |
|---|---|---|---|
| `GET` | `/signature/requests?editableOnly=true` | Lista solo borradores editables (Draft+Ready). | `signature.request.read` |
| `PUT` | `/signature/requests/{id}` | Edita metadata de un borrador (Draft/Ready). | `signature.request.create` + ownership |
| `DELETE` | `/signature/requests/{id}` | Borra en firme un borrador sin enviar. | `signature.request.create` + ownership |

- **PUT** body: `title`, `description`, `category`, `tokenExpirationHours` (requeridos) y
  `sendSignedDocumentToSigners`, `sendCertificateToSigners`, `autoRemindersEnabled`,
  `reminderIntervalHours` **opcionales** (null/omitido = no tocar → *partial update*). `GenerateCertificate`
  y el documento **no** se editan (decisiones de creación); `sendCertificateToSigners=true` requiere que
  la solicitud genere certificado. Solo Draft/Ready; enviada/terminal → 400 `Signature.Request.NotEditable`.
- **DELETE**: solo Draft/Ready. Enviada/terminal → 400 `Signature.Request.NotDeletable`
  ("Only an unsent draft can be deleted…") — esas se **cancelan**, no se borran.
- El **detalle** (`GET /{id}`) ahora devuelve `SendSignedDocumentToSigners`, `SendCertificateToSigners`,
  `AutoRemindersEnabled`, `ReminderIntervalHours` (antes eran write-only) para poder rehidratar el wizard.

## 4. Caché de la lista

`CachedSignatureRequestReadService` cachea el listado por tenant con versión (se bumpea al mutar). La
clave **incluye `editableOnly`** además de status/category/page — si no, Draft (editableOnly=true) y All
colisionaban (comparten `Status=null`) y la pestaña Drafts devolvía todo. Al cambiar el modelo de
respuesta se bumpea `CacheKeyVersion`.

## 5. Frontend (`features/signature`)

- **Save as draft** (wizard): crea la solicitud sin enviar (`create → signers → fields → PIN`, sin `send`).
  Reusa el mismo `WizardSendState`, así que enviarla luego no duplica nada. Cae en la pestaña **Drafts**.
- **Continue editing**: rehidrata el wizard desde `getById` (cliente, metadata, bytes del PDF vía
  `getDownloadUrl`, firmantes y campos con inverse-normalize de coordenadas) y al guardar/enviar calcula
  un **diff** (`commitEditedDraft`): PUT metadata+flags, borra lo quitado, agrega lo nuevo, reordena y —
  si se envía — envía.
- **Drafts tab / Edit details / Delete**: acciones de fila para Draft/Ready. **Send** solo se ofrece si
  la solicitud está Ready **y tiene al menos un campo de firma**. **Cancel/Extend** solo para enviadas.

### Limitaciones (por falta de endpoint en el backend, no bugs)
- El **valor del PIN** no se rehidrata (nunca se devuelve por seguridad); el PIN persiste en el backend
  (`requiresPractitionerPin`) pero el campo del editor aparece vacío.
- **Editar canal/teléfono/idioma de un firmante existente** o **mover un campo ya colocado** al continuar
  no se persiste (no hay `updateSigner`/`updateField`). Sí: add/remove/reorder de firmantes, add/remove de
  campos y la metadata/flags.
