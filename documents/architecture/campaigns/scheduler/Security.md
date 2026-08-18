# Scheduler — Security

Servicio: **TaxVision.Campaigns.Scheduler**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**

## 1. Superficie de ataque mínima por diseño

El Scheduler **no** expone API pública, **no** entrega mensajes, **no** integra proveedores, **no** guarda secretos de proveedor ni PII de destinatarios. Solo conoce identificadores opacos (`CampaignId`, `TenantId`, `OccurrenceId`) y reglas de tiempo. Esto reduce drásticamente el riesgo frente al monolito legado, que en la misma capa alojaba secretos SMTP y JWT de usuario.

## 2. Anti-patrón corregido: nunca JWT de usuario persistido

El legado guardaba `Campaign.BackgroundAuthToken` (JWT de usuario) en BD en texto plano para poder llamar a otros servicios "en nombre del usuario" desde el background (`../05_Master_ADR.md §Anti-patrones 5`). **Prohibido aquí.** El Scheduler actúa con su **propia identidad de servicio (M2M client-credentials)**, no con la del usuario. `StartCampaignRun` lleva `TenantId` como dato, no un token de usuario; Campaigns re-valida autorización en su borde. Ningún token de usuario cruza al Scheduler ni se persiste.

## 3. AuthN/AuthZ

- **Entrada (puerto interno):** si módulo → llamada in-process desde Campaigns (mismo trust boundary). Si servicio propio → M2M OAuth client-credentials, audience `scheduler.api`, scopes `scheduler:write`/`scheduler:read`. **No** acepta JWT de usuario final en ningún endpoint.
- **Salida:** `StartCampaignRun` viaja por el bus interno (Wolverine); Campaigns valida que el `TenantId`/`CampaignId` correspondan y que el gate `module.campaigns` siga activo al **momento del run** (el Scheduler no re-chequea entitlements — es ortogonal, `../00_Overview_And_Index.md:49`).
- **RBAC acumulativo:** las acciones de tenant (crear/pausar/cancelar schedule) se autorizan en **Campaigns** con `[HasPermission]` + tenant + ownership antes de invocar al Scheduler. El Scheduler confía en el borde de Campaigns pero **igual** aplica tenant-scoping fail-closed en sus datos (defensa en profundidad).

## 4. Aislamiento multi-tenant (fail-closed)

- Query filter global por `TenantId` en `schedule_entries` y `trigger_occurrences`; repos tenant-scoped.
- Los **barridos de infraestructura** (dequeue de lease, reconciliación) cruzan tenants por necesidad; usan `.IgnoreQueryFilters()` **con `TenantId` explícito fijado en el scope Wolverine por cada ítem procesado** (nunca un `.Where` global suelto — regla dura, `Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`; corrige `../05_Master_ADR.md §Anti-patrones 9`). Un disparo nunca se emite sin `TenantId` explícito y validado.
- `StartCampaignRun` siempre porta `TenantId`; Campaigns rechaza cualquier comando cuyo `CampaignId` no pertenezca al `TenantId` declarado.

## 5. Rate limiting

Si servicio propio, todo endpoint con `[RateLimit(categoría)]` (M2M) o `[RateLimitExempt]` (health), sin excepción (`RateLimit/Guia_Nuevos_Servicios_Endpoints.md`). El `Schedule` M2M lleva `[RateLimit("m2m-write")]` para acotar creación masiva de schedules. El tick interno no es un endpoint (no aplica rate limit; su cota es el `LIMIT @batch`, ver `Concurrency_Spec.md §6`).

## 6. Abuso temporal / DoS lógico

- **Cota de horizonte:** materialización **una-a-una** (no pre-generar series infinitas) impide que una regla `Daily interval=1 sin EndDate` genere millones de filas.
- **Validación de spec:** `Interval>0`, `MaxOccurrences` con techo configurable, `EndAtUtc` obligatorio-o-tope para recurrentes sin `MaxOccurrences` (evita series eternas por descuido).
- **Catch-up acotado:** disparos vencidos más allá de la gracia se `Skipped`, no se disparan en masa (evita que un downtime se convierta en una tormenta de envíos costosos — que además consumen Wallet real).

## 7. Datos en reposo / PII

El Scheduler no almacena PII (ni emails, ni teléfonos, ni contenido). Solo IDs y reglas de tiempo. No hay secretos que cifrar en su dominio (los secretos de proveedor viven cifrados en cada ejecutor de canal, no aquí). No se colocan IDs sensibles en query strings (regla de privacidad); todo va en cuerpo/eventos.

## 8. Evidencia

| Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Legado persistía JWT de usuario en BD | `../05_Master_ADR.md §Anti-patrones 5` (`Campaign.BackgroundAuthToken`) | DOCUMENTED_ONLY | 88% |
| Gate `module.campaigns` es ortogonal al Scheduler | `../00_Overview_And_Index.md:49`; `../05_Master_ADR.md §Decisiones 5` | VERIFIED | 93% |
| Legado: tenant por `.Where` manual | `CampaignSchedulerService.cs:56-74` | VERIFIED | 92% |
| Modelo M2M + fail-closed propuesto | este documento | NEW | — |
