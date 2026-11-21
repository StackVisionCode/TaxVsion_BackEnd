# Revisión de los últimos commits: backend, CRM, landing y portal

**Fecha:** 2026-11-20
**Alcance:** lo que entró a `Develop`/`develop` después de la revisión del 2026-11-19.
**Estado del código:** sin cambios. Revisión de solo lectura.

| Repo | Rango revisado | HEAD |
|---|---|---|
| Backend `TaxVsion_BackEnd` | `6f3f8835..1a4ba6df` (6 commits, ~30 000 líneas) | `1a4ba6df` |
| CRM `TaxVsion_Front` | `0703ed3c..556c0fa1` (37 archivos) | `556c0fa1` |
| Landing `landing` | `94e536a..3fa98d5` (4 archivos) | `3fa98d5` |
| Portal `CLIENTTAXPROFRONTEND` | sin commits desde el 2026-11-18; se revisaron `be8658d` y `dce7969` | `be8658d` |

Los cuatro están al día con su remoto.

## 1. Cómo se revisó

1. **Pruebas corridas por el coordinador:**

   | Proyecto | Resultado |
   |---|---|
   | Backend (gate del CI) | 31 proyectos, **15 102 / 0** |
   | CRM | 618 archivos, **4 421 / 0**, sin errores sin manejar |
   | Portal | **1 247 / 0** en dos corridas seguidas (en la primera, con la máquina cargada, fallaron 2 de forma intermitente) |
   | Landing | 405 de 407: **2 fallos** en `manage-booking.component.spec.ts` (ver L1) |

2. **Comprobaciones mecánicas del backend:**
   - sin Guid de permisos repetidos en `PermissionCatalog.cs`;
   - `dotnet ef migrations has-pending-model-changes` limpio en Auth, Billing, Mobile y Subscription;
   - las 8 migraciones nuevas ya están aplicadas en la base local.
3. **Lectura del código** por tres revisores independientes (commits de Wagner y Jorge; Mileage y login móvil; los tres frontends). Los hallazgos 🔴 y 🟠 marcados "confirmado" los volvió a comprobar el coordinador en el código.
4. **Merges:** se comparó cada merge contra sus dos padres para detectar líneas revertidas.

## 2. Veredicto

| Qué | Veredicto | Resumen |
|---|---|---|
| Merges de Wagner (backend `d7e9ae3f`, CRM `5cb140bd`) | ✅ | No perdieron nada. El Work Profile, los títulos y el lock de npm están intactos. El Guid duplicado (228) que entró con el merge lo corrigió él mismo en `9aa8f851` (229) |
| Backend: corrección de estado de factura (`c35945c5`) | ⚠️ | Bien construida (auditoría, permisos, pruebas), con 1 error funcional y 3 huecos de dinero (B1–B4) |
| Backend: SMS a número escrito a mano (`c35945c5`) | ⚠️ | Funciona; salta cartera y política de contacto (S1): decisión del dueño |
| Backend: Mobile como módulo (`f8792d87` + `59afe40e`) | ⚠️ | Quedó completo en HEAD. La purga de GPS no borra el recorrido (G1) |
| Backend: login móvil separado (`1a4ba6df`) | ⚠️ | Diseño sólido y probado; 2 huecos de seguridad (A1, A2) |
| CRM | ⚠️ | Coincide con el backend en rutas, permisos y campos; 2 observaciones (C1, C2) |
| Landing | ✅ | Los 11 módulos del backend tienen etiqueta. Solo falla una prueba con fecha fija (L1) |
| Portal | ⚠️ | El visor solo-vista funciona en producción por coincidencia (P1) y sin límite de memoria (P2) |

**Nada de esto rompe el gate ni el despliegue.** Lo que sigue son errores de comportamiento que las pruebas actuales no cubren.

## 3. Hallazgos graves (🔴)

### G1 🔴 La purga de GPS no borra el recorrido — confirmado
- **Dónde:** `src/Services/Mobile/TaxVision.Mobile.Infrastructure/Retention/MileageGpsRetentionJob.cs:55-64`; `TaxVision.Mobile.Domain/Trips/Trip.cs:199-209`.
- **Qué pasa:** el job solo pone en cero la última posición del viaje y lo marca como purgado. Las filas de `MobileTripPoints` (latitud, longitud, velocidad, altitud y rumbo de cada punto) quedan guardadas y siguen saliendo por `GET /mobile/trips/{id}/points`. Como el viaje queda marcado, el job no lo vuelve a visitar.
- **Por qué importa:** la oficina configura N días y cree que cumple, pero no se borra nada. Es el hallazgo R1 de la revisión anterior, resuelto a medias: borrar un viaje a mano sí elimina sus puntos.
- **Arreglo:** borrar los `TripPoints` del viaje en la misma transacción que `PurgeCoordinates`; una pasada de reparación para viajes ya marcados que aún tengan puntos; prueba `.Integration.` del job.
- **Además (🟠):** el job no tiene lock distribuido ni reintento, procesa un lote fijo de 100 viajes por oficina cada 6 h, una excepción en una oficina corta a las demás, solo cubre viajes `Completed`, y sin política configurada el plazo es "para siempre". Patrón a copiar: `ReminderRetentionJob`. Falta que el dueño decida un plazo por defecto.

### B1 🔴 Tras llevar una factura a borrador, no vuelve a ser cobrable online — confirmado por lectura
- **Dónde:** `src/Services/Billing/TaxVision.Billing.Domain/Invoices/Invoice.cs:759-763` (retira los enlaces), `Invoice.cs` `AttachPaymentLink` (solo refresca la URL del enlace existente), `Application/Invoices/IssueInvoice/IssueInvoiceHandler.cs:95` (no manda `Reinstate`).
- **Qué pasa:** al corregir una factura a Draft, sus enlaces de pago quedan como reemplazados y el payable se revoca en PaymentClient. Al reemitirla, nada los reactiva: el PDF sale sin botón de pago, el correo sin enlace y la URL que el cliente ya tenía responde revocada.
- **Arreglo:** que `AttachPaymentLink` reactive el enlace si está reemplazado y que `IssueInvoiceHandler` mande `Reinstate: true` cuando la factura ya tenía número. Prueba: Paid → Draft → reemitir → enlace activo.

## 4. Hallazgos importantes (🟠)

| ID | Qué pasa | Dónde | Arreglo mínimo |
|---|---|---|---|
| B2 | Se puede "resucitar" una factura anulada por reemisión: la original y su reemplazo quedan pagadas con el mismo dinero (confirmado: `OverrideStatus` no mira `ReplacedByInvoiceId`) | `Invoice.cs:698-843` | Rechazar la corrección si la factura fue reemplazada |
| B3 | Un reintento tardío puede dejar cobrable una factura en borrador | `EnsureInvoicePaymentLinkHandler.cs:36` (solo excluye `Voided`) | Excluir también `Draft` |
| B4 | Paid → corregir a Draft → borrar: desaparece una factura numerada que tuvo cobros, y el borrado solo pide `invoicing.manage` (confirmado) | `Invoice.cs` `SoftDeleteDraft` | Rechazar el borrado si la factura ya tiene número |
| B5 | Paid → Issued/Sent con cobro online: la factura vuelve a ser cobrable mientras el cargo original sigue sin reembolsar. Solo lo avisa la respuesta (el aviso del CRM se cierra a los 8 s). El permiso no está marcado como peligroso | `Invoice.cs:746-755`; `PermissionCatalog.cs:635-643` | Decisión del dueño: bloquear hasta reembolsar, o aviso persistente; marcar el permiso como peligroso |
| S1 | SMS a número escrito a mano: salta el filtro de cartera y la política de contacto del cliente (el STOP sí se respeta). Un empleado limitado a sus clientes puede escribirle a cualquiera tecleando el número | `Sms.Application/Messages/Commands/SendSmsBatch.cs:109-143` | Decisión del dueño: exigir un permiso para números manuales, o resolver teléfono → cliente y aplicar cartera y política |
| A1 | Con 10 intentos fallidos anónimos contra el login móvil usando el email de un empleado, su login central del CRM queda bloqueado 5 minutos, y se puede repetir sin fin (confirmado: sin tipo de cuenta se revisan las claves Staff y Portal, y el CRM no manda el tipo) | `Auth/Infrastructure/Security/LoginThrottler.cs:76-87,175-180`; CRM `central-login-page.component.ts:145` | Que el CRM mande `accountKind: 'Staff'`, o que sin tipo solo bloquee si todas las claves pasaron el tope. Prueba con `AccountKind = null` |
| A2 | El código MFA del login móvil no tiene tope de intentos: un fallo solo se audita (confirmado). Con la contraseña robada, el código de 6 dígitos se puede probar a 30 por minuto por IP. El mismo hueco ya existía en el login central web | `Auth/Application/CentralLogin/Commands/MobileSignIn.cs:287-308`; `IssueHandoffTicket.cs:62-100` | Contador de fallos MFA por usuario (p. ej. 5 en 5 min) usado por ambos; reglas pre-auth por IP para `/auth/discover-login` y `/auth/session/handoff` |
| C1 | CRM: un evento de una factura que no está en la lista recarga todo y vuelve a la página 1. En el Billing del perfil de un cliente, cualquier factura de otro cliente dispara la recarga | `features/billing/data-access/billing.store.ts:729` | Recargar solo si es una factura nueva; en modo embebido, ignorar ids desconocidos |
| C2 | CRM/SMS: un cliente real cuyo lookup falló o aún no llegó se trata como "número manual": la respuesta viaja sin cliente, la conversación se parte en dos y se salta cartera y política | `features/sms/components/sms-page/sms-page.component.ts:307-319` | Distinguir "no existe" de "sin resolver o error" |
| P1 | Portal: el visor solo-vista pide el motor de PDF en `/pdfjs/…` (raíz del sitio) y el portal vive en `/portal/`. En producción responde el CRM, con la misma versión: funciona por coincidencia. La copia del portal en `/portal/pdfjs/…` se sirve como `application/octet-stream` (confirmado en producción) | `shared/components/file-preview/pdf-canvas.component.ts:12,78`; `nginx.conf` | Resolver la ruta contra la base del documento y agregar a nginx el tipo `text/javascript` para `.mjs`, como ya tiene el CRM |
| P2 | Portal: el visor dibuja todas las páginas de una vez, hasta 3× de resolución. Un PDF de 80–100 páginas supera 1 GB; en iPhone deja páginas en blanco o cierra la pestaña | `pdf-canvas.component.ts:84-104` | Dibujar solo las páginas visibles, bajar la escala a 2 y liberar las lejanas |

## 5. Hallazgos menores (🟡)

**Backend**
- El motivo de la corrección de estado no tiene tope de largo (columna de 500): un motivo largo da error 500 (`Invoice.cs:710-714`).
- Al pasar a Draft se publica el evento de "anulada"; un consumidor futuro lo leería como anulación (`OverrideInvoiceStatusHandler.cs:108-117`).
- El evento `invoice.updated` llega a todo el staff de la oficina, incluidos quienes no pueden ver facturas (lleva id, número y estados).
- Sin pruebas: los dos avisos nuevos de Notification (factura pagada, cobro sin aplicar), el job de retención y los endpoints de retención.
- Mobile: `MileageRetentionPolicy` lanza excepción en vez de devolver `Result`; `MileageConfigurationController` usa `MobileDbContext` directo; dos `PUT` simultáneos dan 500; `DeletePointsAsync` carga todos los puntos en memoria.
- Login móvil: el token de acceso no distingue móvil de web (falta un claim de superficie); el desafío MFA no queda atado a la superficie; presentar un token web en la ruta móvil no se audita; un cliente cuyo único MFA sea email o SMS no puede entrar a la app.
- Sigue abierto de la revisión anterior: `:?` en el secret de Mobile dentro del bloque de Auth (O1), runbook (O5) y M1–M13.

**CRM**
- El detalle abierto de una factura no se refresca en tiempo real (historial y pagos).
- SMS: el compose no limpia el número manual al reiniciar; el mismo cliente y su número manual pueden entrar los dos (doble SMS y doble cobro).
- Sin pruebas: diálogo de corrección de estado, bloque de pagos del detalle y flujo de número manual.

**Portal**
- `pdf-canvas.component.ts` está guardado con la codificación rota (`Loading previewâ€¦`, `mÃ¡ximo`).
- El botón "Enlarge" aparece en iPhone, donde no hay pantalla completa, y lanza error (`file-viewer-modal.component.ts:85`).
- El PDF solo-vista queda como imágenes: un lector de pantalla no lee el contenido.
- El commit `dce7969` dice "payments" pero solo arregla el QR de MFA (correcto). El portal no tiene código de pagos.

**Landing**
- **L1.** Las 2 pruebas fallidas usan un horario fijo del 2026-11-19, que desde hoy es pasado y el componente ya no lo muestra (`manage-booking.component.spec.ts:31`). No es un error de los cambios nuevos. Arreglo: usar una fecha relativa a hoy.

## 6. Lo que está bien

- Corrección de estado: devuelve `Result`/`Error`, exige permiso propio (fuera del rol de empleado), deja auditoría con motivo y actor, no toca montos, guarda y publica en la misma transacción, y un webhook repetido de un pago revertido no se reaplica. Trae 22 pruebas.
- Login móvil: superficie y canal propios, refresh atado a usuario, oficina, sesión y superficie con rotación y detección de reutilización, respuestas genéricas (sin enumerar cuentas), logs sin secretos. El flujo web no cambió por accidente.
- Módulo Mileage: gate de módulo, proyecciones y reconciliación completos; arranca en solo-registro, así que hoy no deja a ninguna oficina sin acceso.
- CRM ↔ backend: rutas, verbos, permisos y campos coinciden.

## 7. Antes de desplegar

- Secrets en `production-extra`: `MOBILE_DB_CONNECTION`, `MOBILE_SERVICE_CLIENT_ID`, `MOBILE_SERVICE_CLIENT_SECRET` (obligatorios) y `MOBILE_MODULE_GATE_ENFORCE` (opcional; vacío = gate apagado).
- No encender `MOBILE_MODULE_GATE_ENFORCE=true` hasta confirmar que corrió la revisión de catálogo de Mileage: los clientes de portal de planes sin el módulo recibirían 403.
- La app móvil debe iniciar sesión de nuevo una vez (las sesiones anteriores eran de otra superficie).
- Antes de dar la app a clientes: G1, A1 y A2.

## 8. Correcciones aplicadas (2026-11-20, sin commit)

Aprobadas por el dueño: los dos hallazgos 🔴. Ciclo prueba en rojo → arreglo → verde. Gate completo después: **15 106 / 0**.

### G1 ✅ La purga de GPS borra el recorrido
- **Cambio:** `src/Services/Mobile/TaxVision.Mobile.Infrastructure/Retention/MileageGpsRetentionJob.cs`.
  - Por cada oficina con plazo, borra los `MobileTripPoints` de los viajes vencidos **antes** de marcarlos. Si el marcado falla, el viaje sigue siendo candidato y la siguiente pasada lo completa.
  - Pasada de reparación: borra los puntos de viajes que una corrida anterior marcó como purgados y conservaron su recorrido.
  - Cada oficina va aislada: si una falla, o si otra réplica ganó el marcado, se registra un aviso y siguen las demás. Esto salió al probar: dos corridas simultáneas chocaban por `RowVersion` y una abortaba con todas las oficinas pendientes.
- **Prueba nueva:** `deploy/tests/TaxVision.Mobile.Tests/Integration/MileageGpsRetentionJobIntegrationTests.cs` (7 casos, contra SQL real): vencido pierde sus puntos; dentro del plazo los conserva; oficina sin plazo conserva todo; reparación de viajes ya marcados; dos pasadas seguidas y otra oficina intacta; corridas simultáneas. Rojo antes del arreglo: 3 de 6, y la de simultáneas 2 de 3 corridas. Verde: 7 de 7, cinco veces seguidas.
- **También:** `InternalsVisibleTo` para las pruebas en `TaxVision.Mobile.Infrastructure.csproj` (el job es `internal`).
- **Sigue abierto de G1 (🟠):** lock distribuido y reintento, lote fijo de 100 por oficina cada 6 h, solo viajes `Completed`, y el plazo por defecto (hoy "para siempre" si la oficina no configura nada; decisión del dueño). `TRACKING_API.md:102` todavía dice que no hay purga física.

### B1 ✅ Una factura devuelta a borrador vuelve a ser cobrable al reemitirla
- **Cambios en Billing:**
  - `Domain/Invoices/InvoicePaymentLink.cs` + `Invoice.cs` (`AttachPaymentLink`): al asegurar el mismo payable, el enlace retirado vuelve a activo.
  - `Application/Invoices/IssueInvoice/IssueInvoiceHandler.cs`: si el borrador ya tenía número (fue reabierto), la reemisión pide `Reinstate` para que PaymentClient reinstale el cobro revocado con la misma URL.
- **B3 ✅ incluido**, porque el arreglo anterior lo habría agravado: `EnsureInvoicePaymentLinkHandler.cs` ya no asegura el cobro de una factura en borrador (antes solo excluía las anuladas).
- **Pruebas nuevas (4):** el enlace vuelve a activo sin duplicarse (`InvoiceStatusOverrideTests`); la reemisión de un borrador reabierto pide reinstalar y la primera emisión no (`IssueInvoiceHandlerTests`); un borrador numerado no se vuelve cobrable (`EnsureInvoicePaymentLinkHandlerTests`). Rojo antes: 3 de 3 (la cuarta es el control). Verde: Billing 299 / 0 y `.Integration.` 19 / 0.
- **Falta en vivo:** Paid → corregir a Draft → reemitir → abrir el enlace de pago y el PDF (necesita la flota arriba, PaymentClient incluido).

### Observado al correr la integración de Mobile (sin tocar)
- 3 pruebas de API dan 500 si Redis no está arriba: el gate de módulo lee su caché de Redis y no tolera la caída. Con la flota abajo fallan siempre.
- `TripPersistenceIntegrationTests.Tracking_migration_preserves_existing_vehicles` espera 2 migraciones aplicadas y ya hay 5: prueba desactualizada.

## 9. Segunda tanda de correcciones (2026-11-20, sin commit)

Aprobadas por el dueño: dinero y login. Mismo ciclo (prueba en rojo → arreglo → verde). Gate completo después: **15 119 / 0**; integración de Auth 39 / 0 y de Billing 19 / 0.

### B2 ✅ Una factura reemplazada ya no se puede revivir
- `Billing.Domain/Invoices/Invoice.cs` (`OverrideStatus`): si la factura tiene `ReplacedByInvoiceId`, la corrección responde `Billing.Invoice.Replaced` ("corrige el reemplazo"). Dejarla en su mismo estado (Voided) sigue siendo un no-op.
- `GetInvoiceDetailQuery.OverridableStatusesFor` devuelve lista vacía para esas facturas (espejo de la regla del dominio; sin prueba propia).
- Prueba: `A_replaced_invoice_cannot_be_brought_back` (4 destinos). Rojo: 4 de 4.

### B4 ✅ Un borrador reabierto que conserva su número no se puede borrar
- `Invoice.SoftDeleteDraft`: con `InvoiceNumber` responde `Billing.Invoice.NumberedDraft` ("anúlala en vez de borrarla"). Un borrador que nunca se emitió se sigue borrando igual.
- Pruebas: `A_reopened_draft_that_keeps_its_number_cannot_be_deleted` (rojo) y `A_draft_that_was_never_issued_can_still_be_deleted` (control). Billing: 305 / 0.

### A1 ✅ Atacar la puerta del cliente ya no bloquea al empleado
- **Solo backend, sin tocar el CRM.** `Auth/Application/CentralLogin/CentralLoginAuthentication.cs`: `CheckThrottleAsync` devuelve un `LoginGate` con los tipos de cuenta que el intento puede probar. Sin tipo de cuenta (login central del CRM), un tipo frenado **se salta** en vez de bloquear al otro; solo espera si no queda ninguno.
- Consecuencias cuidadas:
  - la contraseña del tipo frenado no se puede seguir probando por la puerta sin tipo (el tope de 10 por tipo se mantiene);
  - un fallo solo suma a los tipos que se probaron;
  - entrar bien como empleado no limpia el freno de la puerta del cliente.
- `DiscoverLogin.cs` y `MobileSignIn.cs` usan el `LoginGate`. `LoginThrottler` (Redis) no cambió.
- Pruebas nuevas en `CentralLoginHandlersTests`: `Central_sign_in_without_account_kind_is_not_blocked_by_failures_on_the_client_door`, `A_throttled_account_kind_is_skipped_instead_of_guessed` (rojo ambas) y `With_every_account_kind_throttled_the_central_sign_in_waits` (control).

### A2 ✅ El código MFA del login central (web y móvil) tiene tope de intentos
- Cada código errado cuenta contra el bloqueo de **esa cuenta** (`User.RegisterFailedLogin`, la misma política del login clásico: 10 fallos → 15 minutos). Es por cuenta porque la contraseña ya se validó para ese usuario. Bloqueada, ni el código correcto pasa (`Auth.LockedOut` con espera) y una nueva vuelta con la contraseña tampoco la encuentra. Un código correcto limpia los fallos previos.
- Cambios: `CentralMfaAttempts` (en `CentralLoginAuthentication.cs`), `IssueHandoffTicket.cs` (ahora recibe `IUserRepository`) y `MobileSignIn.cs`.
- Codegen de Auth regenerado; quedó modificado solo `IssueHandoffTicketCommandHandler1249510623.cs`.
- Pruebas nuevas (4): bloqueo en web y en móvil, cuenta bloqueada no entra con una vuelta nueva, y el código correcto limpia fallos. Rojo comprobado desactivando el registro del fallo: 3 de 4. Auth: 1 561 / 0.
- **No incluido:** reglas pre-auth por IP en el Gateway para `/auth/discover-login` y `/auth/session/handoff` (configuración del Gateway).

### Nota de proceso: regenerar el codegen de un handler que cambia de firma
1. El archivo generado viejo deja de compilar y el script no puede correr. Se destraba poniendo un argumento provisional en ese archivo; el script lo reescribe después.
2. El script reescribe toda la carpeta `Internal/Generated`. En el repo esos archivos están guardados con finales de línea mezclados, así que git marca como modificados los ~136 que no cambiaron. Se devolvieron a su estado exacto del checkout (bytes y fechas del índice) con `restore_generated.ps1`, guardado junto a este informe; `git status` quedó solo con el archivo que cambió de verdad.

## 10. Tercera tanda: CRM y portal (2026-11-20, sin commit)

Resultado después: backend **15 120 / 0**, CRM **4 427 / 0** (tipos limpios), portal **1 262 / 0** (tipos limpios).

### C1 ✅ La lista de facturas ya no se recarga por eventos ajenos
- `TaxVsion_Front/src/app/features/billing/data-access/billing.store.ts`:
  - un aviso de una factura que no está en la lista y **no es nueva** se ignora (es de otro cliente, de otra página o de otra cartera);
  - una factura **nueva** (`trigger: 'Created'` o `fromStatus: null`) relee la lista en silencio: sin spinner, sin mover la página y sin mostrar error si falla. El servidor decide si esa factura es de quien mira;
  - las facturas que sí están en la lista se siguen refrescando fila por fila.
- Pruebas (`billing.store.live.spec.ts`): 3 nuevas, las 3 en rojo antes. La prueba vieja que fijaba la recarga completa se reemplazó. Billing del CRM: 120 / 0.

### C2 ✅ Responder en una conversación de SMS ya no adivina si es cliente o número manual
- **Backend** (`Sms.Application/Messages/Commands/SendSmsBatch.cs`): un ítem es "número manual" si llega sin cliente **o** si su id es el derivado de ese mismo teléfono (el id de la conversación de un número manual). Prueba: `Replying_with_the_id_of_a_typed_number_conversation_is_still_a_typed_number` (rojo antes). Sms: 301 / 0.
- **CRM** (`features/sms/data-access/sms.model.ts`, `components/sms-page/sms-page.component.ts`): al responder desde una conversación sin ficha resuelta, el destinatario lleva el id de la conversación (`threadReplyContact`) en vez de viajar "sin cliente". Si era un cliente cuyo directorio no había llegado o falló, el backend aplica cartera y política de contacto y la conversación no se parte.
- Pruebas del CRM: 4 nuevas (2 del helper, 2 de la página); la de la página, en rojo antes.
- No cambia S1: escribir un número a mano en un mensaje nuevo sigue saltando cartera y política (decisión del dueño).

### P1 ✅ El portal usa su propio motor de PDF
- `CLIENTTAXPROFRONTEND/src/app/shared/components/file-preview/pdf-engine.ts` (nuevo) y `pdf-canvas.component.ts`: el worker y las fuentes se resuelven contra la base del documento (`/portal/pdfjs/…` desplegado, `/pdfjs/…` en desarrollo).
- `nginx.conf` del portal: bloque `location ~* ^/portal/.*\.mjs$` con `default_type text/javascript` (antes se servía como `application/octet-stream`) y `text/javascript` en `gzip_types`. **No se pudo validar con `nginx -t`** (no hay nginx local y no se toca Docker): se valida en el deploy.
- **Hallazgo extra, arreglado:** zone.js reemplaza `Promise` por una versión sin `Promise.try`, que pdf.js 6.1 usa para responderle al worker. Un PDF con fuentes estándar (por ejemplo las marcas de casilla) se quedaba cargando para siempre. Se define `Promise.try` solo si falta, antes de cargar pdf.js. El CRM no usa zone.js, así que no le afecta.

### P2 ✅ El visor dibuja solo las páginas cercanas
- Cada página reserva su hueco con el tamaño real (el scroll no salta) y su canvas se dibuja al acercarse al área visible (`IntersectionObserver`, una pantalla de margen); al alejarse se cancela el render, se libera el canvas y se llama `page.cleanup()`. Tope de densidad 2 (antes 3).
- De paso: codificación del archivo corregida (`Loading preview…`), ancho nunca negativo, el botón "Enlarge" no aparece donde no hay pantalla completa (iPhone), y el contenedor se puede recorrer con teclado.
- Pruebas: 15 nuevas (13 del visor, 1 del motor, 1 del modal); 12 de 16 en rojo antes. El agente lo comprobó además con pdf.js real y un PDF de 40 páginas (spec temporal, borrada).

### Decisiones y verificaciones pendientes del portal
1. iPhone con iOS anterior a 18.2: el worker de pdf.js 6.1 también usa `Promise.try` y ahí tiene que ser nativo. Si hay que soportarlos, evaluar `pdfjs-dist/legacy`. Sin verificar.
2. PDF escaneados (JPEG2000 / JBIG2): el portal no copia los decodificadores `wasm` de pdf.js. Sin probar.
3. En vivo, desplegado bajo `/portal/`: que `/portal/pdfjs/pdf.worker.min.mjs` responda `text/javascript`, que no haya pedidos a `/pdfjs/…` en la raíz, y un PDF de 80–100 páginas en un iPhone.

## 11. Decisiones del dueño (✅ decididas 2026-11-20)

| Tema | Decisión | Alternativas descartadas |
|---|---|---|
| Retención de GPS por defecto | **3 años** cuando la oficina no configura nada. El resumen del viaje se conserva siempre; se borra solo el recorrido punto por punto | 18 meses (recomendada), 90 días, sin plazo |
| B5: reabrir una factura pagada en línea | **Solo aviso fijo**: el aviso de reembolso no se cierra solo. No se exige confirmar el reembolso. El permiso `invoicing.status.override` queda **delegable** y por eso no se marca como peligroso (segunda decisión del mismo día) | Confirmar reembolso (recomendada), no permitir reabrir, dejar igual |
| S1: SMS a un número escrito a mano | **Permiso aparte** (`sms.send_any_number`): admin por defecto; sin él, un empleado solo escribe a sus clientes | Reconocer al cliente por teléfono, las dos cosas, dejar igual |

### Retención de GPS de 3 años ✅ (sin commit)
- `Mobile.Domain/Trips/MileageRetentionPolicy.cs`: `DefaultRetentionDays = 1095` y `EffectiveDays(configurado)`. Sin fila de política o con el plazo vacío rige el por defecto: el recorrido nunca queda para siempre.
- `Mobile.Infrastructure/Retention/MileageGpsRetentionJob.cs`: además de las oficinas con plazo propio, procesa las que no configuraron nada y tienen viajes más viejos que el plazo por defecto.
- `MileageConfigurationController` (GET/PUT `retention`): la respuesta agrega `effectiveRetentionDays` (aditivo; `retentionDays` y `configured` no cambian).
- Pruebas: `MileageRetentionPolicyTests` (4, nuevas) y el caso de integración `Office_without_a_configured_period_uses_the_three_year_default` (con y sin fila; rojo antes). Job: 11 de 11, tres veces seguidas. Mobile: 75 / 0.
- Una oficina puede fijar su propio plazo entre 1 y 3 650 días; ya no existe "sin purga".

### S1 ✅ Permiso aparte para SMS a números escritos a mano (sin commit)
- Permiso nuevo `sms.send_any_number` ("Send SMS to numbers that are not clients"), Guid `a1000000-0000-0000-0000-000000000230`, módulo `sms`. Lo tiene el admin; no está en el bundle del empleado; se puede delegar.
- **Migración de Auth** `20261120134848_AddSmsSendAnyNumberPermission`, aplicada en local. Solo inserta el permiso nuevo.
- **Sms**: `SendSmsBatchCommand.CanSendToAnyNumber`, que el controller calcula con el permiso del usuario. Un número manual sin permiso falla solo con `sms.manualNumberNotAllowed`: no crea fila, no llama al proveedor y no reserva wallet. Solo se le mide a una persona: campañas, avisos automáticos y llamadas de servicio (`ActorUserId` nulo) siguen como antes.
- **CRM**: el campo para escribir un número a mano solo aparece con el permiso. Responder en la conversación de un número manual sin permiso muestra: "You don't have permission to text numbers that are not clients. Ask an administrator for access."
- Pruebas: Sms 312 / 0 (integración 18 / 0), Auth 1 565 / 0 (integración 39 / 0), CRM 4 446 / 0.

### B5 ✅ Aviso fijo al reabrir una factura pagada en línea (sin commit)
- **CRM**: el aviso de reembolso ya no se cierra solo; queda hasta que el usuario pulse "OK" o la X. Opción aditiva `persistent` en el servicio de toasts; los demás avisos se siguen cerrando como antes.
- **Backend: sin cambio en el permiso.** Marcarlo `IsDangerous` obliga a que deje de ser delegable (`PermissionCeiling` y el guardarraíl `Platform_only_and_dangerous_permissions_are_never_assignable_by_a_tenant`). El dueño eligió que **siga delegable**, así que `invoicing.status.override` queda como estaba: no peligroso, delegable, en el bundle del admin y fuera del empleado. Lo fija la prueba `Invoicing_status_override_can_be_delegated_and_comes_with_the_admin_bundle`.
- La primera migración (que además lo marcaba peligroso) nunca llegó a un commit: se deshizo en local, se quitó y se regeneró solo con el permiso de SMS.

### Resultado final del día
Backend **15 139 / 0** (31 proyectos), CRM **4 446 / 0**, portal **1 262 / 0**. Nada commiteado, nada desplegado, nada probado en vivo.

Para probar en vivo hay que reiniciar en Release los servicios tocados: Auth (reparte el permiso nuevo al rol admin), Sms, Billing y Mobile; y recompilar CRM y portal.

## 12. Cuarta tanda: job de purga y menores (2026-11-20, sin commit)

Cuatro agentes en paralelo, uno por grupo de servicios; revisión y corridas finales del coordinador.

**Resultado final:** backend **15 229 / 0** (31 proyectos), CRM **4 522 / 0** (tipos limpios), landing **407 / 0**, portal **1 262 / 0**. Bases locales al día (Auth 158 migraciones, Billing 28, Mobile 5).

### Mobile — job de purga reforzado
- **Una réplica a la vez:** lock en Redis (`mobile:lock:job:mileage-gps-retention`). Sin lock la pasada no hace nada. Con Redis caído no se purga: la pasada cuenta como fallida y se reintenta a los 2 minutos.
- **Drena:** repite lotes por oficina hasta agotar los vencidos o llegar al tope. Opciones `Mobile:GpsRetention` (validadas al arrancar): `Enabled` true, `IntervalHours` 6, `BatchSize` 100, `MaxBatchesPerOffice` 20, `LockTtlMinutes` 60. Por defecto, 2 000 viajes por oficina y pasada.
- **Viajes sin terminar:** un cancelado cuenta desde que terminó; uno en curso o en pausa, desde su última actividad según el reloj del servidor (`UpdatedAtUtc`). Uno con actividad reciente no se toca. El borrado vuelve a comprobar en SQL que el viaje sigue vencido.
- **Cambio de dominio:** si un viaje ya purgado recibe puntos nuevos, deja de estar marcado como purgado (`Trip.Append`); si no, la reparación borraba los puntos recientes de un viaje abandonado que volvía a reportar.
- Reloj inyectado (`TimeProvider`) y reintento rápido cuando la pasada falla entera.
- **Menores hechos:** `MileageRetentionPolicy` devuelve `Result`; GET/PUT `retention` pasan por handlers de Wolverine con el error estándar; regla "los controllers no dependen de Infrastructure"; `DeletePointsAsync` borra en lote; warning en `MobileServiceTokenAcquirer` sin configuración; `AT TIME ZONE` solo recibe zonas que SQL Server entiende; `appsettings.Development.json` nuevo; `RabbitMq:Uri` vacío en `appsettings.json`; la fábrica de diseño sigue el orden de Notes; comentario en cada `IgnoreQueryFilters()`; pruebas de aislamiento entre oficinas; se corrigió la prueba que esperaba 2 migraciones y la frase de `TRACKING_API.md`.
- Pruebas: Mobile 115 / 0; integración 31 de 34 (las 3 que fallan necesitan Redis y ya fallaban).

### Billing y Notification
- **Tope del motivo (500)** en corrección de estado, anular, reemitir y cambio de estado (`Billing.Invoice.ReasonTooLong`).
- **Crédito arrastrado revertido:** el pago revertido lleva `creditLeftUnapplied: true` en la respuesta y en el detalle. No cambia saldos (opción A; ver decisiones).
- **Facturas antiguas sin filas de pago:** al reabrirlas, lo pagado entra como pago `Legacy` ya revertido, con constancia en el historial; si se cobró en línea, marca reembolso.
- **Evento al anular por corrección de estado:** ahora lleva autor y cliente (queda en la actividad del equipo). Al devolver a borrador sigue sin autor a propósito: es una revocación del cobro, no una anulación.
- **Notification:** 16 pruebas nuevas de los dos avisos al personal; el logger de `InvoiceSentConsumer` usa un ancla propia (`InvoiceSentLog`).
- Pruebas: Billing 327 / 0 (integración 20 / 0); Notification 697 / 0 (integración 28 / 0).

### Auth — menores del login móvil
- El desafío MFA queda atado a la superficie con la que se pidió (migración `20261120140106_AddMfaChallengeSurface`: columna `MfaChallenges.Surface`, por defecto `Workspace`; aplicada en local).
- Un refresh token presentado por la otra puerta se audita (`auth.token.surface_mismatch`) sin revocar nada.
- La puerta móvil con un email de empleado suma al freno por IP y ya no se distingue de un email desconocido.
- El token de acceso de la app lleva `channel=mobile`; los tokens web no cambian. No se usó `surface` porque un filtro de BuildingBlocks rechazaría el token en todos los servicios.
- Pruebas: Auth 1 575 / 0 (integración 42 / 0).

### CRM y landing
- **Detalle de factura en vivo:** si la factura abierta cambia, su historial y sus pagos se refrescan en silencio. De paso se corrigió una carrera: abrir la factura A y enseguida la B podía pintar el historial de A sobre B.
- **SMS:** el compose se limpia al reabrir; no se agrega dos veces el mismo teléfono (se conserva el destinatario que es cliente).
- **Directorio de clientes:** un 404 se recuerda 5 minutos; los errores de red o de permisos se reintentan.
- **Corrección de estado:** el diálogo usa los destinos que manda el backend; una factura reemplazada no ofrece ninguno y lo explica.
- **Avisos nuevos:** crédito arrastrado que quedó sin aplicar (aviso fijo y nota en el pago) y etiqueta "Paid before payment history" para el pago `Legacy`.
- **Pruebas nuevas:** diálogo de corrección (23), bloque de pagos (11), tabla de facturas (5), store en vivo, compose de SMS y directorio.
- **Landing:** la prueba de reservas fija su reloj; ya no caduca.

### Script `restore_generated.ps1`
Ahora prueba el contenido tal cual (checkout en LF, como Notification) y convertido a CRLF (como Auth), según cuál coincida con el tamaño del índice.

### Decisiones pendientes del dueño
1. **Crédito arrastrado revertido (Billing):** hoy solo avisa. Alternativas: devolverlo al saldo de crédito para que la reemisión lo aplique sola, bloquear la corrección, o rastrear si la original se cobró en línea para pedir reembolso.
2. **Viajes abandonados (Mobile):** se les purga el recorrido pero siguen en curso o en pausa. ¿Se cierran solos al pasar el plazo? ¿Hace falta un plazo mínimo aparte para viajes sin terminar (una oficina con plazo de 1 día purga uno en pausa al día siguiente)?
3. **MFA por email o SMS en la app (Auth):** un cliente cuyo único método fuera ese no podría entrar. Hoy es un caso latente: el único alta que existe es con app autenticadora. Conviene contar en producción antes de decidir.
4. **Dos clientes con el mismo teléfono (CRM/SMS):** ahora solo entra uno como destinatario, para no enviar ni cobrar dos veces al mismo número.
5. **Idioma de los avisos al personal (Notification):** los de facturas están en español y los de wallet y suscripción en inglés.
6. **Archivos de despliegue que no se tocaron (necesitan el OK del dueño):** `:?` del secret de Mobile en el bloque de Auth del compose, la variable `RateLimit__EnforceTierQuotas` que nadie lee, y comentarios mal ubicados en compose y `deploy.yml`.
7. **Sin hacer por ser diseño:** el aviso `invoice.updated` llega a todo el staff; el PDF solo-vista del portal no tiene capa de texto para lectores de pantalla; refactors de dominio de Mobile (agregado e hijos, eventos, value objects, tarifas); reglas pre-auth por IP del Gateway para el login central.

### Para desplegar
- Migraciones nuevas de Auth: `AddSmsSendAnyNumberPermission` y `AddMfaChallengeSurface`. Ambas aditivas.
- El job de purga de Mobile necesita Redis.
- La app móvil no cambia de contrato; su token gana el claim `channel`.
- Para probar en vivo: reiniciar en Release Auth, Sms, Billing, Notification y Mobile; recompilar CRM, landing y portal.
