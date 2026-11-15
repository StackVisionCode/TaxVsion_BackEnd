# Subdominios y resolución de tenant (oficina)

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\` (`src/...` = código, `deploy/tests/...` = pruebas).
> Etiquetas: **[Código verificado]** (leído en el repo hoy), **[Documentación histórica]** (memoria/guías, puede estar desfasada), **[Recomendación]**, **[Decisión pendiente]**.

## 1. Estado actual

**[Código verificado]** La resolución de oficina por host tiene tres capas y una sola regla compartida:

| Capa | Componente | Qué hace | Si falla |
|---|---|---|---|
| Regla única de host | `BuildingBlocks.Platform.PlatformSettingsSnapshot.TryGetTenantSlug` | `{slug}.{BaseDomain}` con una sola etiqueta y no reservada → slug | — |
| Fuente de verdad del dominio | Tenant, agregado `PlatformSettings` (`GET platform/settings/public`, evento `PlatformSettingsChangedIntegrationEvent`) | Marca, `BaseDomain`, `ApiHost`, correos | Cada servicio sigue con su foto (versión) vigente |
| Fuente de verdad host→tenant | Auth, tabla `TenantDomains` + `ITenantResolver` | Slug → `TenantDomain` activo → tenant activo | 404 (enforced) |
| Borde | Gateway `TenantHostGuardMiddleware` + `HostTenantResolver` | Host no registrado → 404; JWT de otra oficina → 403; propaga `X-Resolved-Tenant` | **Fail-open** si Auth no responde |
| Servicio | `JwtTenantContextMiddleware` / `IntegrationEventTenantMiddleware` | El tenant autoritativo sale del JWT (HTTP) o del payload del evento (consumers) | Sin claim → filtro por `Guid.Empty` (0 filas) |

Hosts de la plataforma (derivados de `BaseDomain`, hoy `taxoffix.io` por defecto de arranque):
- `app.{dominio}` = CRM y login central (`AppHost`), `client.{dominio}` = portal (`ClientHost`), `www.`/apex = landing, `api.{dominio}` = API y tenant Platform.
- Subdominios reservados: `api`, `app`, `www`, `admin`, `client`.
- Oficina: `{slug}.{dominio}`; el CRM se sirve en la raíz y el portal del cliente bajo `/portal` (ver `Tenant_Links.md`).
- Dominio propio (custom hostname) de la oficina: soportado en Auth (`TenantDomainType.CustomHostname`, aprovisionamiento con Cloudflare).

## 2. Evidencia

### 2.1 Regla de host y foto de plataforma
- `src/BuildingBlocks/Platform/PlatformSettingsSnapshot.cs:9-17` record con `Version`, `BrandName`, `LegalName`, `BaseDomain`, `ApiHost`, `SupportEmail`, `BillingEmail`.
- `:20` `AppHost`, `:23` `ClientHost`, `:32` `LandingUrl(path)`, `:36` `TenantHost(slug)`.
- `:42-43` `ReservedSubdomains = { api, app, www, admin, client }`.
- `:50-64` `TryGetTenantSlug`: normaliza, exige sufijo `.{BaseDomain}`, una sola etiqueta y no reservada. Comentario: "Es la única regla de 'esto es un host de oficina': Auth y el Gateway la usan igual".
- `:70-83` `IsSharedHost` (apex, api, reservados). `:120-127` `IsPlatformHttpsOrigin` (CORS). `:130-137` `NormalizeHost` (minúsculas, sin puerto, sin punto final).
- `src/BuildingBlocks/BuildingBlocks.Web/Platform/PlatformSettingsProvider.cs:21-31` `TryApply` solo aplica una versión mayor (evento y refresco pueden llegar en cualquier orden).
- `src/BuildingBlocks/BuildingBlocks.Web/Platform/PlatformSettingsRefreshService.cs:22` lee `platform/settings/public`; `:37-50` refresca cada `RefreshInterval` (5 min) y reintenta cada 30 s mientras Tenant no contestó.
- `src/BuildingBlocks/BuildingBlocks.Web/Platform/PlatformSettingsRegistration.cs:15` `AddPlatformSettings`, `:29` `ListenForPlatformSettings` (van juntos; sin el primero el handler revienta con el primer mensaje).
- `src/BuildingBlocks/BuildingBlocks.Web/Platform/PlatformSettingsOptions.cs:15` `TenantBaseUrl` (vacía = solo valores locales), `:25` `BaseDomain = "taxoffix.io"` solo como arranque.
- Tenant: `src/Services/Tenant/TaxVision.Tenant.Api/Controllers/PlatformSettingsController.cs` (`[Route("platform/settings")]`, PUT) y `PublicPlatformSettingsController.cs` (`[AllowAnonymous]`).

### 2.2 Auth: host → tenant
- `src/Services/Auth/Api/Middleware/TenantHostResolutionMiddleware.cs`
  - `:24-26` solo lee `Request.Host`, nunca `X-Forwarded-Host` directo (la confianza la decide `ForwardedHeadersMiddleware`).
  - `:47-64` rutas exentas (`/health`, `/auth/service-token`, `/auth/.well-known`, `/auth/subdomains/check-availability|reserve`, `/auth/tenant-resolution/by-email`, `/internal`).
  - `:87` hosts de sistema (apex, www, app, client, admin y `SystemHosts` extra) pasan sin buscar.
  - `:93-99` resuelto → `IResolvedTenantContext.SetResolvedTenant`.
  - `:101-117` no resuelto → audit `TenantResolutionFailed` siempre + 404 si `EnforceHostResolution`.
  - `:159-196` evento `TenantResolutionFailedIntegrationEvent` deduplicado por host (ventana 5 min, `IRateCounter`).
- `src/Services/Auth/Infrastructure/Tenancy/TenantResolver.cs:25-57`: `ApiHost` → `PlatformTenant.Id` (`:36-39`); slug de oficina → `GetSubdomainBySlugAsync`; otro host → custom hostname por host exacto (`:61-65`, solo `DomainType == CustomHostname`); exige `TenantDomainStatus.Active` (`:45`) y `tenant.IsActive` (`:51-53`); cachea (`:55`).
- `src/Services/Auth/Application/Abstractions/ITenantResolver.cs:4-14` motivos `HostMissing`, `HostUnknown`, `TenantInactive`; comentario: "Nunca produce un 'tenant por defecto'".
- `src/Services/Auth/Infrastructure/Tenancy/TenantResolutionCache.cs:8` TTL 5 min, `:17` clave `tenant-resolution:host:{host}` (ICacheService/Redis). `InvalidateAsync` lo usan `ChangeSubdomain`/`DisableTenantDomain`.
- `src/Services/Auth/Application/TenantDomains/TenantDomainOptions.cs:19` `EnforceHostResolution = true` (en local `deploy/docker/docker-compose.local.yml:16` lo pone en `false`), `:30` `SystemHosts`, `:37-52` `IsSystemHost`.
- `src/Services/Auth/Api/Controllers/TenantResolutionController.cs:24-40` `GET auth/tenant-resolution/by-host` (anónimo, limiter `tenant-lookup`): expone el tenant ya resuelto; `:50` `POST by-email` siempre 202 (anti-enumeración).
- `src/Services/Auth/Api/Common/EffectiveLoginTenantResolver.cs:25-36`: con enforcement, el `TenantId` del body del login se descarta; solo vale el del host.
- Custom hostnames: `src/Services/Auth/Domain/TenantDomains/TenantDomain.cs:12,52,110,148` y `Api/Controllers/TenantDomainsController.cs:23` (`auth/tenant-domains`).

### 2.3 Gateway
- `src/Gateway/TaxVision.Gateway/Middleware/TenantHostGuardMiddleware.cs`
  - `:32` header `X-Resolved-Tenant`; `:40` se borra siempre lo entrante (anti-spoofing).
  - `:46` si el host no es de oficina (`TryGetTenantSlug` falso), OPTIONS o guard apagado → pasa.
  - `:57-66` `NotRegistered` → 404 `{ error: "office_not_found", message: "This office is not available." }`.
  - `:69-89` `tenant_id` del JWT ≠ tenant del host → 403 `tenant_mismatch`.
  - `:94` propaga `X-Resolved-Tenant`. `:97-99` `Unavailable` → fail-open.
- `src/Gateway/TaxVision.Gateway/Middleware/HostTenantResolver.cs:76-98` caché stale-while-revalidate; `:120-154` llama a `auth/tenant-resolution/by-host` reenviando el host como header `Host` (`:126`); 404 → `NotRegistered` (`:130-131`); cualquier otro fallo → `Unavailable`, nunca se cachea (`:114-118`).
- `src/Gateway/TaxVision.Gateway/Middleware/TenantHostGuardOptions.cs:20` positivo 15 min, `:26` revalida a los 30 s, `:30` negativo 1 min.
- `src/Gateway/TaxVision.Gateway/Middleware/TenantPropagationMiddleware.cs:21` borra `X-Tenant-Id` entrante; ya no lo propaga (GW-11: ningún servicio lo lee).
- `src/Gateway/TaxVision.Gateway/Program.cs:50` CORS con `IsPlatformHttpsOrigin`; `:124` `AddPlatformSettings` (Gateway sin Wolverine: solo refresco HTTP); `:134` resolver con timeout 3 s; `:177` guard después de `UseAuthentication`.
- Proxies de confianza: `src/BuildingBlocks/BuildingBlocks.Web/Hosting/ClientIpForwardingExtensions.cs:46` acepta `X-Forwarded-For|Proto|Host` solo desde `KnownProxies/KnownNetworks`; si no se configuran, redes privadas (`ReverseProxyTrustOptions`).

### 2.4 Servicios y consumers sin HTTP
- `src/BuildingBlocks/Tenancy/ITenantContext.cs` (`TenantId`, `HasTenant`, `SetTenant`).
- `src/BuildingBlocks/BuildingBlocks.Web/Tenancy/IntegrationEventTenantMiddleware.cs:38-44` toma `TenantId` del payload; `Guid.Empty` → `InvalidOperationException` (falla cerrado) y estampa `bus.TenantId`.
- `JwtTenantContextMiddleware` (mismo directorio): tenant del claim `tenant_id`.
- Defensa en profundidad en flujos anónimos: `src/Services/Signature/TaxVision.Signature.Api/Middleware/PublicSignatureHostGuardMiddleware.cs:13-65` compara `X-Resolved-Tenant` con el tenant del token de firma → 403 `tenant_host_mismatch`; sin header no valida.
- Client Forms público: `src/Services/Tasks/TaxVision.Tasks.Infrastructure/ClientForms/Security/FormPublicTenantHostValidator.cs:27` `MatchesTenantAsync` llama a `by-host` con caché (5 min resuelto, 30 s no resuelto, 10 s fallo) y **falla cerrado**.
- Tenant Platform: `src/BuildingBlocks/Tenancy/PlatformTenant.cs:11` Id fijo, subdominio `platform-internal`.

### 2.5 Cambio de subdominio y de dominio
- **[Código verificado]** `TenantSubdomainChangedIntegrationEvent` (`src/BuildingBlocks/Messaging/AuthIntegrationEvents/TenantSubdomainChangedIntegrationEvent.cs:11-21`: `OldHost`, `NewHost`, `OldSlug`, `NewSlug`, `IsPrimary`). Consumers: Tenant, Signature, Scribe, Postmaster, PaymentApp, PaymentClient y Notification (invalida caché de host).
- `PlatformSettingsChangedIntegrationEvent` → `PlatformSettingsChangedHandler` (BuildingBlocks.Web) en los servicios con Wolverine.

## 3. Problemas

1. **[Código verificado] Fail-open en el Gateway.** Si Auth no contesta, un JWT de la oficina A pasa por el host de la oficina B (`TenantHostGuardMiddleware.cs:97-99`; prueba `Auth_no_disponible_hace_fail_open_aunque_el_JWT_no_cuadre`). Los datos siguen aislados por el `tenant_id` del JWT en cada servicio, pero el host deja de ser señal fiable durante el corte. Decisión de operación documentada en el propio código.
2. **[Código verificado] Custom hostnames fuera del guard del Gateway y de CORS.** `TryGetTenantSlug` solo reconoce `*.{BaseDomain}`; un dominio propio no se valida como oficina en el Gateway (pasa como host "no de oficina") y `IsPlatformHttpsOrigin` lo rechaza en CORS. Auth sí lo resuelve. No se encontró evidencia de dominios propios en uso.
3. **[Código verificado] Cachés con vida distinta sin invalidación común.** Gateway 15 min con revalidación a 30 s (sin bus); Auth 5 min (Redis, invalidado por comandos); Notification 15 min (invalidado por evento); Calendar 30 min en memoria (`src/Services/Calendar/TaxVision.Calendar.Infrastructure/Staff/TenantHostResolver.cs:22`) **sin** consumer de `TenantSubdomainChanged`.
4. **[Código verificado] Tenant suspendido.** `TenantResolver` solo mira `tenant.IsActive`. El bloqueo por cobro (`SetBillingBlockedAsync` en `ITenantRegistry`) no forma parte de la resolución por host; lo gobiernan otras capas (sesión/acceso).
5. **[Código verificado] Dos patrones de validación Host↔token en flujos públicos.** Signature usa el header del Gateway (tolerante sin header); Tasks llama a Auth por su cuenta (cerrado ante fallo). Un servicio nuevo no tiene una pieza compartida.
6. **[Documentación histórica]** La memoria del 2026-10-13 dice que `TenantSubdomainChangedIntegrationEvent` tenía "CERO consumers" y que `InvalidateAsync` nadie lo llamaba. **Hoy está corregido** (F4: 7 consumers; Change/Disable invalidan).

## 4. Buenas prácticas observadas

- Una sola regla de host (`TryGetTenantSlug`) usada por Auth, Gateway, PaymentClient y CORS.
- Nunca "tenant por defecto": host desconocido → 404 (Auth) y prueba que lo fija.
- El host es un **candidato**; el tenant autoritativo es el claim del JWT (HTTP) o el `TenantId` del evento (consumers).
- Headers de confianza saneados en el borde (`X-Resolved-Tenant`, `X-Tenant-Id`).
- Respuestas de error genéricas (sin detalle de topología).
- Auditoría de cada host desconocido + evento deduplicado (evita inundar el fanout).
- El dominio de la plataforma es dato runtime (Tenant), no config literal por servicio.

## 5. Recomendación

- **[Recomendación]** Un servicio nuevo **no resuelve tenant por host**: lee `tenant_id` del JWT (`JwtTenantContextMiddleware`) y, en consumers, el `TenantId` del evento (`IntegrationEventTenantMiddleware`).
- **[Recomendación]** Si tiene una ruta pública anónima con token (link de correo), validar Host↔token con el header `X-Resolved-Tenant` como `PublicSignatureHostGuardMiddleware`, y que el tenant de escritura salga siempre del token, nunca del host.
- **[Recomendación]** Nunca leer `X-Forwarded-Host`, `X-Tenant-Id` ni `X-Resolved-Tenant` sin pasar por el Gateway/ForwardedHeaders.
- **[Recomendación]** Para "es host de oficina" usar `IPlatformSettings.Current.TryGetTenantSlug`; no parsear sufijos a mano.
- **[Decisión pendiente]** Si se quiere cerrar el fail-open del Gateway para rutas sensibles (o una pieza BB compartida para la validación Host↔token pública).
- **[Decisión pendiente]** Soporte de custom hostnames en Gateway/CORS si se van a vender.

## 6. Riesgos

- Corte de Auth = guard del Gateway desactivado de hecho (mitigado por JWT).
- Rename de subdominio: links con host viejo hasta 30 min en Calendar; los demás se invalidan o revalidan.
- Un servicio que confíe en un header de tenant sin sanear abre acceso cruzado.
- `EnforceHostResolution=false` en local acepta `TenantId` del body: no copiar ese valor a otros ambientes.

## 7. Pruebas

### Existentes
| Archivo | Casos clave |
|---|---|
| `deploy/tests/TaxVision.Gateway.Tests/Middleware/TenantHostGuardMiddlewareTests.cs` | `Deja_pasar_los_hosts_de_sistema_sin_consultar_a_Auth`, `Subdominio_no_registrado_devuelve_404_y_no_pasa`, `JWT_de_otro_tenant_en_el_host_de_una_oficina_devuelve_403_y_no_pasa`, `Auth_no_disponible_hace_fail_open_aunque_el_JWT_no_cuadre`, `Tras_cambiar_el_dominio_el_host_viejo_ya_no_se_valida_como_oficina` |
| `deploy/tests/TaxVision.Gateway.Tests/Middleware/HostTenantResolverTests.cs` | `Un_host_deshabilitado_deja_de_resolver_tras_la_revalidacion`, `Un_fallo_de_Auth_no_se_cachea`, `La_cache_es_compartida_entre_instancias_del_resolver` |
| `deploy/tests/TaxVision.Auth.Tests/Application/TenantResolverTests.cs` | `Unknown_host_never_resolves_to_a_default_tenant`, `Host_of_an_inactive_tenant_is_unresolved`, `A_host_under_the_previous_domain_no_longer_resolves`, `The_api_host_resolves_to_the_platform_tenant_without_a_domain_row`, `An_office_custom_hostname_resolves_by_its_exact_host` |
| `deploy/tests/TaxVision.Auth.Tests/Api/TenantHostResolutionMiddlewareTests.cs` | `Unknown_host_returns_404_never_calls_next_and_is_audited_when_enforced`, `Spoofed_X_Forwarded_Host_header_is_ignored`, `Repeated_failures_from_the_same_host_are_audited_every_time_but_published_once` |
| `deploy/tests/TaxVision.Auth.Tests/Api/EffectiveLoginTenantResolverTests.cs` | `Enforced_mode_always_uses_the_resolved_tenant_even_if_the_body_claims_a_different_one` |
| `deploy/tests/TaxVision.Signature.Tests/PublicSignatureHostGuardMiddlewareTests.cs` | `Blocks_with_403_when_host_tenant_differs_from_token_tenant`, `Does_not_mask_an_invalid_token_with_403` |
| `deploy/tests/TaxVision.Tasks.Tests/ClientForms/FormPublicTenantHostValidatorTests.cs` | `Auth_rejection_fails_closed_without_throwing`, `Development_hosts_are_ignored_outside_development` |
| `deploy/tests/TaxVision.BuildingBlocks.Tests/Platform/PlatformSettingsProviderTests.cs` | `An_older_or_equal_version_never_overwrites_a_newer_one`, `A_failing_refresh_keeps_the_current_version` |

### Las que debe tener un servicio nuevo
- JWT de la oficina A llamando con datos de la oficina B → 0 filas / 404 (filtro de tenant).
- Consumer con `TenantId = Guid.Empty` → falla cerrado (no procesa).
- Ruta pública con token: host de la oficina A + token de la oficina B → 403; sin header → valida solo el token.
- Header `X-Tenant-Id`/`X-Resolved-Tenant` enviado por el cliente → ignorado.
- Token inválido o expirado en ruta pública → su propio error, no 403 enmascarado.
- Tenant inválido/inexistente en el payload → error explícito, no tenant por defecto.

## 8. Dependencias

Tenant (`PlatformSettings`), Auth (`TenantDomains`, `by-host`, `primary-host`), Gateway, Redis (caché de Auth y `IRateCounter`), RabbitMQ (eventos de plataforma y subdominio), Cloudflare (custom hostnames).

## 9. Material para la guía

1. Registrar la foto de plataforma (si el servicio necesita dominio o marca):
   ```csharp
   builder.Services.AddPlatformSettings(builder.Configuration);
   // dentro de UseWolverine:
   options.ListenForPlatformSettings();
   ```
   Config: `PlatformSettings__TenantBaseUrl` (en compose, ancla `x-platform-settings-env`).
2. Tenant de la request: `app.UseMiddleware<JwtTenantContextMiddleware>()` después de autenticación. Tenant en consumers:
   ```csharp
   options.Policies.ForMessagesOfType<BuildingBlocks.Messaging.IIntegrationEvent>()
       .AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.IntegrationEventTenantMiddleware));
   options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));
   ```
3. ¿Es host de oficina? `platform.Current.TryGetTenantSlug(host, out var slug)`. Host de oficina: `platform.Current.TenantHost(slug)`.
4. Ruta pública con token: copiar el patrón de `PublicSignatureHostGuardMiddleware` (leer `X-Resolved-Tenant`, verificar token, comparar `TenantId`).
5. Nunca: leer `X-Forwarded-Host` directo, aceptar `TenantId` del body, caer a un tenant por defecto.
