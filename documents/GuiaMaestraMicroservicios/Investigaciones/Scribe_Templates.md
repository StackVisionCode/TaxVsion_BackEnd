# Scribe: plantillas, render, idioma y fechas tipadas

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\`.
> Etiquetas: **[Código verificado]**, **[Documentación histórica]**, **[Recomendación]**, **[Decisión pendiente]**.

## 1. Estado actual

**[Código verificado]** Scribe es el dueño de las plantillas (email y push) y del render Fluid/Liquid. Notification es su único cliente de render (M2M). Postmaster no renderiza: recibe el HTML ya armado.

Modelo:
- **EventKey** (string con puntos, minúsculas, p. ej. `tenant.wisp_review_due.v1`) → `EventTemplateMapping` → **TemplateKey** → `EmailTemplate` (scope `System` o `Tenant`) → versión `Published` pineada a un **layout** (`system-base` o `tenant-base`) y a su versión.
- Override por oficina: una plantilla `Tenant` con el mismo `TemplateKey` gana sobre la `System`; un mapping `Tenant` gana sobre el `System`.
- Idioma: el mapping puede llevar `Locale`; prioridad Tenant+Locale > Tenant > System+Locale > System.
- Fechas: el emisor manda `{"$instant": "...Z"}` o `{"$date": "YYYY-MM-DD"}`; Scribe las imprime en la zona e idioma de quien lee.
- El **layout** decide logo y carril de envío: `tenant-base` → logo de la oficina y `DispatchScope = TenantPreferred`; `system-base` → `System` (salvo override de PlatformAdmin).
- Las plantillas de sistema viven en código (`NotificationTemplateSeedSource`) y el seeder las publica al arrancar cuando cambia su **hash de contenido**.

## 2. Evidencia

### 2.1 Endpoint de render (M2M)
- `src/Services/Scribe/TaxVision.Scribe.Api/Controllers/RenderController.cs:24` `[Route("scribe/render")]`, `:26` `[AllowActorTypes(ActorType.Service)]`, `:43` `[HasPermission(ScribePermissions.Render)]` (`scribe.render`), `:44` `[RateLimit("scribe.j.render")]`.
- `:29-40` cuerpo: `EventKey`, `TenantId`, `Locale`, `Variables`, `LogoScope`, `Channel` (`Email`/`Push`), `Audience` (`Staff`/`Client`), `TimeZoneId`.
- `:63-64` timeout de render → 503 (Notification lo reintenta).
- Respuesta `RenderedContent` (`src/Services/Scribe/TaxVision.Scribe.Application/Rendering/RenderingContracts.cs:34`): `Subject`, `Html`, `Text`, `InlineAssets`, `DispatchScope` (string), `TemplateKey`, `VersionNumber`, `LayoutVersionNumber`. `:51` `DispatchScopes.System|TenantPreferred`.
- `src/Services/Scribe/TaxVision.Scribe.Infrastructure/Persistence/Repositories/PublishedTemplateReader.cs:10-13` usa `IgnoreQueryFilters`: el render es M2M sin claim `tenant_id`; el aislamiento lo da el `TenantId` explícito del cuerpo. `:16-46` `GetByKeyAsync` busca Tenant y cae a System; `:67-72` solo versiones `Published`.

### 2.2 Pipeline de render
- `src/Services/Scribe/TaxVision.Scribe.Application/EventMappings/EventTemplateResolver.cs:13-30` cuatro niveles de prioridad (tenant/locale).
- `src/Services/Scribe/TaxVision.Scribe.Infrastructure/Rendering/FluidTemplateRenderer.cs`
  - `:93-177` `RenderAsync`: mapping → plantilla publicada → canal → zona del lector → variables normalizadas → push o email con layout pineado.
  - Sin mapping → `EmailRenderer.NoMapping`; sin versión publicada → `EmailRenderer.NoPublishedVersion`.
  - Override de otro canal no reemplaza a la plantilla de sistema de este canal (bloque tras `GetByKeyAsync`).
  - `:510-511` `ResolveDispatchScope`: override de la plantilla, si no `tenant-base` → `TenantPreferred`, si no `System`.
  - `:520-547` inyecta `tenant_name` (nombre comercial de la proyección) y `tenant_address`.
  - `:551-559` inyecta `product_name`, `platform_legal_name`, `platform_website`, `platform_support_email` desde `IPlatformSettings` (pisa lo que mande el caller).
  - `:56` timeout de render 5 s; `:61` `MaxRecursion = 100`, `MaxSteps = 10000`.
- Validación de claves: `src/Services/Scribe/TaxVision.Scribe.Domain/ValueObjects/EventKey.cs:13` máx. 200, `:43` `^[a-z0-9]+(?:[._-][a-z0-9]+)*$`; `Locale.cs:13` máx. 10.

### 2.3 Zonas horarias y fechas tipadas (P18)
- Emisor: `src/Services/Notification/TaxVision.Notification.Application/Common/ScribeDates.cs:12` `Instant(DateTime)` → `{"$instant": "O"}`; `:24` `Date(DateOnly)` → `{"$date": "yyyy-MM-dd"}`.
- Lector: `src/Services/Notification/TaxVision.Notification.Application/Abstractions/ScribeReader.cs:9-35` (`Locale`, `TimeZoneId`; `Office` = idioma por defecto y zona de la oficina; un idioma inválido no viaja).
- Scribe: `FluidTemplateRenderer.cs:786-800` `NormalizeVariables`; `:805-834` `TypedDate` (diccionario de una clave `$instant`/`$date` → `ZonedDate`); `:836-843` `ReaderZoneAsync`: zona pedida si es IANA válida → zona de la oficina (`ITenantTimeZoneResolver`) → UTC rotulado.
- `:63-69` filtros opcionales `office_date`, `office_time`, `office_deadline`; con texto plano devuelven el texto (tolerancia a emisores viejos).
- `src/Services/Scribe/TaxVision.Scribe.Infrastructure/Rendering/ZonedDate.cs:38-41` formato P15 (`Wed, Nov 18, 2026, 9:00 AM · New York time, GMT-5`; civil `Nov 18, 2026`), `:56-70` plazo relativo P14, español si el locale empieza por `es`.
- Zona de la oficina: `src/BuildingBlocks/TenantTimeZones/TenantTimeZoneResolver.cs:11-44` proyección local, caché 5 min (1 min si cae a UTC), `Forget` al cambiar.
- **[Documentación histórica]** Vault `CommunicationCorrecciones/Proyectos/TenantTimeZones/Decisiones/Aprobadas.md`: P15 (formato), P14 (plazos), P18 (plantillas personalizadas sin migración: fechas tipadas). `Despliegue_Checklist.md` B.3: **Scribe antes que Notification** (un Scribe viejo imprimiría el marcador como texto raro).

### 2.4 Siembra y versionado
- `src/Services/Scribe/TaxVision.Scribe.Application/Templates/Seed/NotificationTemplateSeedSource.cs`: record `NotificationTemplateSeed(EventKey, TemplateKey, Name, Subject, Html, Variables, ContentVersion, LayoutKey = "system-base", Channel = Email, Text)`; `ContentHash` = SHA-256 de asunto, HTML, texto, variables, layout y canal (fines de línea normalizados). Historial de versiones en comentarios (v2…v32). `All` lista las plantillas (más `NotificationTemplateSeedSource.PublicDemo.cs`).
- Ejemplo real: `WispReviewDue` (`NotificationTemplateSeedSource.cs:514`), `ContentVersion: 23`, variables `is_overdue`, `due_date`, `settings_url`, `product_name`.
- `src/Services/Scribe/TaxVision.Scribe.Infrastructure/Seed/ScribeNotificationTemplateSeeder.cs:241` `contentChanged = SeedContentHash != definition.ContentHash`; `:243-246` re-pinea si el layout publicó versión nueva; un fallo por plantilla no frena las demás. Comentario del record: `ContentVersion` es "Solo etiqueta legible: el seeder republica por ContentHash".
- Layouts: `ScribeBaseLayoutSeeder` + `Templates/BaseLayouts/BaseLayoutHtml.cs` (`SystemBaseVersion`, `TenantBaseVersion`).

### 2.5 Administración, preflight y publicación
- `src/Services/Scribe/TaxVision.Scribe.Api/Controllers/EmailTemplatesController.cs`: `[Route("scribe/templates")]`, actores TenantEmployee/TenantAdmin/PlatformAdmin; `POST` (crear), `POST {id}/versions` (borrador), `POST {id}/versions/{versionId}/publish`, `.../preview`, `.../validate` (`scribe.templates.read`), `PUT {templateKey}/dispatch-scope` (solo PlatformAdmin).
- `EventTemplateMappingsController.cs`: `scribe/event-mappings` (CRUD, `scribe.event_mappings.*`). `EmailLayoutsController.cs`: `scribe/layouts`.
- Preflight: `src/Services/Scribe/TaxVision.Scribe.Application/Templates/Validation/TemplateVersionValidator.cs:16-22` todo placeholder debe estar declarado + HTML pasa `EmailHtmlSafetyValidator`; `PushTemplateLength.cs` para push.
- Permisos: `src/BuildingBlocks/Authorization/ScribePermissions.cs:10-27` (`scribe.templates.read|write`, `scribe.layouts.*`, `scribe.event_mappings.*`, `scribe.render`).

### 2.6 Lado cliente (Notification)
- `src/Services/Notification/TaxVision.Notification.Application/Abstractions/IScribeRenderClient.cs:63-90` `RenderAsync(eventKey, tenantId, variables[, audience])`, `RenderTextAsync` (push).
- `src/Services/Notification/TaxVision.Notification.Infrastructure/Scribe/ScribeRenderClient.cs` token M2M por tenant; 400/404/422 **con código de Scribe** → `Scribe.RenderRejected`; el resto → error transitorio.
- `.../Abstractions/ScribeRenderResultExtensions.cs:23` `EnsureRendered`: rechazo → `ScribeRenderRejectedException` (deriva de `ArgumentException`, `ScribeRenderRejectedException.cs:10`) → cola de errores sin reintento; transitorio → `ScribeRenderFailedException` → reintento.
- `.../Abstractions/ScribeRenderedEmailExtensions.cs` `ToDispatchRequest` copia asunto, cuerpos, logos, carril y versiones al pedido de envío.

## 3. Problemas

1. **[Documentación histórica] vs [Código verificado] — versión de contenido.** La memoria `feedback_scribe_subir_version_plantilla.md` y la guía `documents/Guia_Conectar_Microservicio_a_Notificaciones_Email.md` (Paso 4a: "si el TemplateKey ya existe, lo salta") dicen que sin subir `ContentVersion` no se republica. **El código actual republica por `ContentHash`** (prueba `Changed_html_without_bumping_content_version_is_republished`). Subir `ContentVersion` sigue siendo la convención de etiqueta y lo exigen las pruebas por plantilla, pero ya no es lo que dispara la publicación.
2. **[Documentación histórica] — EventKey ≠ MessageIdentity.** La misma guía dice que el `event_key` debe ser "exactamente el mismo string" que `[MessageIdentity]`. En código, `OfficeWispReviewDueIntegrationEvent` tiene identidad `BuildingBlocks.Messaging.TenantIntegrationEvents.OfficeWispReviewDueIntegrationEvent` y el EventKey de Scribe es `tenant.wisp_review_due.v1` (`OfficeWispReviewDueConsumer.cs:23`). Lo que debe coincidir es la constante del consumer, el seed y el mapping.
3. **[Código verificado] Override sin publicar.** Si existe una plantilla `Tenant` con el mismo `TemplateKey` pero sin versión publicada, `GetByKeyAsync` la devuelve y el render falla con `NoPublishedVersion` (no cae a la de sistema). Solo hay caída al sistema cuando el override es de otro canal.
4. **[Código verificado] Fechas aún como texto.** Hay consumers que mandan fechas ya formateadas (p. ej. `due_date` del WISP: `"MMMM d, yyyy"` en inglés, `OfficeWispReviewDueConsumer.cs:211`). Funciona porque es fecha civil, pero no respeta idioma del lector.
5. **[Código verificado] Idioma.** Las plantillas sembradas son en inglés por defecto; solo algunas (Signature, Client Forms) son bilingües por ramas dentro de la plantilla o por locale. No hay mapping por locale sembrado para todas.
6. **[Código verificado] `TenantId` del cuerpo como autoridad.** El render confía en el `TenantId` que manda el servicio (solo actor `Service` con `scribe.render`). Correcto para M2M, pero cualquier servicio con ese permiso puede pedir el render de otra oficina.

## 4. Buenas prácticas observadas

- Un único punto de conversión render → envío (`ToDispatchRequest`) y render → excepción (`EnsureRendered`).
- Rechazo determinista a la cola de errores; transitorio reintentado.
- Variables de plataforma y de oficina inyectadas por el renderer (no por el caller): marca y nombre siempre correctos.
- Fechas tipadas: ninguna plantilla sabe de zonas; los nombres de variable no cambian.
- Preflight antes de publicar (placeholders declarados + HTML seguro).
- Versiones pineadas (plantilla ↔ layout) y versión usada registrada en `NotificationLog` (`RenderedTemplateVersion/LayoutVersion`).

## 5. Recomendación

- **[Recomendación]** Plantilla de un servicio nuevo = entrada en `NotificationTemplateSeedSource` (System) con `ContentVersion` explícito y mayor que el último usado, `LayoutKey` según quién habla (`system-base` plataforma, `tenant-base` oficina), variables declaradas con tipo y descripción, y prueba de "declara todas sus variables y lleva su versión".
- **[Recomendación]** Toda fecha a Scribe con `ScribeDates.Instant`/`ScribeDates.Date`; nunca "UTC" escrito ni formato propio.
- **[Recomendación]** Audiencia explícita (`ScribeRenderAudience.Client` para clientes, portal o externos) y `ScribeReader.From(idioma, zona)` cuando se conoce al lector.
- **[Recomendación]** Desplegar Scribe antes que Notification cuando se agregan plantillas o variables tipadas.
- **[Decisión pendiente]** Si un override de oficina sin versión publicada debe caer a la plantilla de sistema.
- **[Decisión pendiente]** Estrategia de idioma (mapping por locale vs. ramas dentro de la plantilla) para plantillas nuevas al cliente.

## 6. Riesgos

- Scribe caído → correos en reintento y luego DLQ (no se pierden en silencio).
- Plantilla con variable no declarada → no publica (preflight) o, en seed, la prueba lo detecta.
- Cambio de layout → todas las plantillas se re-pinean al siguiente arranque.
- Plantillas personalizadas por oficina con fechas en texto (P18 las tolera; medición en `Despliegue_Checklist.md` A3).

## 7. Pruebas

### Existentes
| Archivo | Casos |
|---|---|
| `deploy/tests/TaxVision.Scribe.Tests/Templates/Seed/NotificationTemplateSeedSourceTests.cs` | `All_seed_html_and_subjects_parse_as_fluid_and_pass_safety`, `No_seed_still_mentions_the_old_brand`, `Wisp_review_template_declares_every_variable_it_uses_and_carries_version_23`, `Offboarding_templates_declare_every_variable_and_carry_their_own_version`, `Client_form_emails_for_clients_and_externals_render_in_spanish` |
| `.../Templates/Seed/ScribeNotificationTemplateSeederTests.cs` | `Changed_html_without_bumping_content_version_is_republished`, `Same_content_is_not_republished_on_second_run`, `Same_hash_on_a_stale_layout_is_still_republished`, `One_failing_template_does_not_stop_the_rest` |
| `.../Templates/Seed/TypedDateTemplatesTests.cs` | `A_template_with_a_typed_date_does_not_write_utc_and_is_republished`, `A_civil_due_date_is_inclusive_so_the_copy_says_by` |
| `.../Rendering/ZonedDateTests.cs` | `A_short_deadline_is_relative_in_minutes`, `A_deadline_in_spanish` |
| `.../EventMappings/EventTemplateResolverTests.cs` | 5 casos de prioridad tenant/locale/system |
| `.../Persistence/TenantIsolationTests.cs` | `NullableTenantFilter_isolates_tenant_A_Tenant_scope_template_from_tenant_B` (namespace `.Persistence.`: el gate de CI lo excluye) |
| `.../Rendering/OfficeNameInjectionTests.cs`, `TenantLayoutLogoOwnerTests.cs`, `RenderControllerTests.cs`, `FluidTemplateRendererPushTests.cs` | Nombre de oficina, logo por layout, contrato HTTP, push |
| `.../Architecture/TenantTimeZoneAdoptionTests.cs` | Adopción de la proyección de zona |
| `deploy/tests/TaxVision.Notification.Tests/ScribeRenderFailureTests.cs` | `No_mapping_goes_straight_to_error_queue`, `Server_error_is_retried`, `Rejected_render_is_not_retried_by_the_standard_policy` |
| `deploy/tests/TaxVision.Notification.Tests/ScribeReaderTests.cs`, `ScribeRenderClientTests.cs` | Locale/zona del lector y contrato del cliente |

### Las que debe tener un servicio nuevo
- La plantilla declara todas sus variables y lleva su `ContentVersion` (como la del WISP).
- Renderiza cada rama (`if/else`) con valores reales.
- Fecha tipada: no contiene "UTC" y sale en la zona de la oficina A (p. ej. Nueva York) y de la oficina B (p. ej. Los Ángeles).
- Cliente vs. staff: `Audience = Client` para el cliente.
- Override de la oficina A no afecta a la oficina B.
- Consumer: Scribe sin mapping → cola de errores; 5xx → reintento.

## 8. Dependencias

CloudStorage (HTML de plantillas y logos), Redis (caché de fuente), Tenant (proyecciones de perfil/logo, `PlatformSettings`, zona vía `TenantTimeZones`), Auth (token M2M y permisos), Notification (cliente).

## 9. Material para la guía

1. Seed (fragmento real, recortado):
   ```csharp
   private static NotificationTemplateSeed WispReviewDue { get; } =
       new(
           EventKey: "tenant.wisp_review_due.v1",
           TemplateKey: "tenant.wisp_review_due.v1",
           Name: "Tenant — WISP annual review due",
           Subject: "{% if is_overdue %}Your security plan review is overdue{% else %}Your security plan review is due {{ due_date }}{% endif %}",
           Html: """ ... <a href="{{ settings_url }}">Open Compliance settings</a> ... {{ product_name }} ... """,
           Variables:
           [
               ("is_overdue", VariableType.Bool, true, "false", "true when the review date already passed."),
               ("due_date", VariableType.String, true, null, "Review due date, already formatted (en-US)."),
               ("settings_url", VariableType.Url, true, null, "Office settings → Compliance in the office CRM."),
               ("product_name", VariableType.String, false, "TaxOffix", "Branding del producto (lo inyecta el renderer desde PlatformSettings)."),
           ],
           ContentVersion: 23
       );
   ```
   Agregarla a `NotificationTemplateSeedSource.All`.
2. Render desde el consumer:
   ```csharp
   var render = (await scribe.RenderAsync(EmailEventKey, tenantId,
           new Dictionary<string, object?> { ["due_at"] = ScribeDates.Instant(dueAtUtc), ["link"] = url },
           ScribeRenderAudience.Client, ct))
       .EnsureRendered(EmailEventKey);
   ```
3. Plantilla administrada (sin deploy): `POST scribe/templates` → `POST .../versions` → `POST .../versions/{id}/validate` → `POST .../publish` → `POST scribe/event-mappings`.
4. Orden de despliegue: Scribe → Notification.
