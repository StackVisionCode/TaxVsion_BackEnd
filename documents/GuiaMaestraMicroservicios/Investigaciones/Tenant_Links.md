# Generación de enlaces por oficina (APIs, workers, consumers, jobs)

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\`.
> Etiquetas: **[Código verificado]**, **[Documentación histórica]**, **[Recomendación]**, **[Decisión pendiente]**.

## 1. Estado actual

**[Código verificado]** Ningún consumer arma links con el host del request. El host de la oficina se obtiene de una de estas formas:

| Forma | Dónde | Cuándo se usa |
|---|---|---|
| Pull M2M a Auth `GET internal/tenants/{tenantId}/primary-host` | `ITenantHostResolver` en Notification y en Calendar | Consumers y handlers sin el slug en el evento |
| Slug que viaja en el evento + `IPlatformSettings.Current.TenantHost(slug)` | Notification (`InvitationCreatedConsumer`, `SeatSlotEmailConsumers`), Auth (avisos de suscripción), PaymentClient | Cuando el productor ya conoce el subdominio |
| Host del request (solo como último recurso documentado) | `PayablePublicUrls.UnavailableCheckoutUrl` | Link anulado/vencido: no hay payable del que sacar el tenant |
| Landing de la plataforma | `PlatformSettingsSnapshot.LandingUrl(path)` | Correos de cuenta/suscripción y demo pública |

Forma de las URLs de oficina (helper `TenantEmailLinks` de Notification):
- Personal (CRM): `https://{slug}.{dominio}` (raíz).
- Cliente (portal): `https://{slug}.{dominio}{ClientPathPrefix}` con `ClientPathPrefix = "/portal"`.
- Públicas por token bajo el host de la oficina: `/signature/public/{token}`, `/d/{token}`, `/s/{token}`, `/storage/public/{token}`, `/f/{token}`, `/rsvp#token=...`, `/pay/{token}`.
- Sin host resuelto: cae a una base fija de config (`PortalOptions.BaseUrl`/`ClientBaseUrl`) — link degradado, el correo sale igual.

## 2. Evidencia

### 2.1 Fuente del host
- `src/Services/Auth/Api/Controllers/InternalTenantPrimaryHostController.cs:20-23` `[Route("internal/tenants/{tenantId:guid}/primary-host")]`, `[Authorize(Policy = "ServiceOnly")]`, `[AllowActorTypes(ActorType.Service)]`.
- `src/Services/Auth/Application/TenantDomains/Internal/Queries/GetTenantPrimaryHostQuery.cs:11` respuesta `TenantPrimaryHostResponse(string Host, string? TimeZoneId)`; `:43-49` elige el subdominio primario activo (o cualquier subdominio activo); **los custom hostnames no se devuelven** ("el portal del cliente vive bajo el subdominio de plataforma"); `:56` arma el host con el slug y el dominio **vigente** (no el guardado).
- `src/Services/Notification/TaxVision.Notification.Application/Abstractions/ITenantHostResolver.cs:9-12` contrato: nunca lanza, `null` si no resolvió.
- `src/Services/Notification/TaxVision.Notification.Infrastructure/Storage/TenantHostResolver.cs:35-45` caché + `:47-77` pull con token M2M; fallo → `null`.
- `src/Services/Notification/TaxVision.Notification.Application/Abstractions/TenantHostCache.cs:12` TTL 15 min (singleton).
- `src/Services/Notification/TaxVision.Notification.Application/Consumers/TenantHostCacheInvalidationConsumer.cs:15` rename → olvida la oficina; `:28` `PlatformSettingsChanged` → limpia todo.
- `src/Services/Notification/TaxVision.Notification.Api/Program.cs:156-164` registro del resolver (base = `ServiceAuthClientOptions.AuthBaseUrl`, timeout 15 s).
- `src/Services/Calendar/TaxVision.Calendar.Infrastructure/Staff/TenantHostResolver.cs:12-22` misma llamada, caché en memoria 30 min, sin invalidación por evento.

### 2.2 Constructores de URL
- `src/Services/Notification/TaxVision.Notification.Application/Common/TenantEmailLinks.cs`
  - `:12-13` `StaffBase(host, portal)` → `https://{host}` o `PortalOptions.BaseUrl`.
  - `:16-19` `ClientBase` → `https://{host}{ClientPathPrefix}` o `PortalOptions.ClientBaseUrl`.
  - `:26-27` `ClientDocuments` (`/client/documents`), `:29-33` `ClientForms`/`ClientForm`, `:35-36` `PublicFormAccess` (`/f/{token}`), `:43-44` `SigningLink`, `:51-58` `SignatureDeliveryPage` (`/d/{token}?item=`), `:65-70` `PublicShareDownloadLink` (`/storage/public/{token}?email=`), `:78-83` `PublicSharePageLink` (`/s/{token}?email=`).
- `src/Services/Notification/TaxVision.Notification.Application/Common/PortalOptions.cs:10` `BaseUrl` (CRM), `:17` `ClientBaseUrl` (portal, solo fallback), `:21` `ClientPathPrefix = "/portal"`. Compose: `deploy/docker/docker-compose.yml:940-945` (`PORTAL_BASE_URL`, `CLIENT_PORTAL_BASE_URL`, `CLIENT_PORTAL_PATH_PREFIX`).
- `src/Services/Notification/TaxVision.Notification.Application/Common/CalendarRsvpLinks.cs` token en el **fragmento** (`/rsvp#token=...&answer=`): no viaja al servidor ni queda en logs/Referer.
- `src/Services/PaymentClient/TaxVision.PaymentClient.Api/Common/PayablePublicUrls.cs:15-20` URL estable de factura, `:23-28` checkout `/pay/{token}`, `:35-45` URL de link no disponible desde el host del request, `:52-56` `SubDomainFromHost` (misma regla `TryGetTenantSlug`), `:58-66` fallback a base por path si `UseTenantHosts` está apagado.
- `src/BuildingBlocks/Platform/PlatformSettingsSnapshot.cs:32` `LandingUrl`, `:36` `TenantHost`.

### 2.3 Cliente → portal, empleado → CRM
- `src/Services/Notification/TaxVision.Notification.Application/Consumers/AuthEventConsumers.cs:129-131` reset de contraseña: `AuthAccountCopy.IsPortal(actorType)` → `{ClientBase}/client/auth/reset-password/new?token=` ; si no → `{StaffBase}/reset-password?token=`. `:313` confirmación de correo del portal con `ClientBase`.
- `AuthEventConsumers.cs:58-61` invitación: host desde `evt.TenantSubdomain` + `TenantHost`, link `{host}/accept-invitation?token=` (misma ruta para cualquier `ActorType`; la audiencia del render sí cambia con `AuthAccountCopy.Audience`).
- `src/Services/Notification/TaxVision.Notification.Application/Consumers/OfficeWispReviewDueConsumer.cs:70-71` aviso a staff: `{StaffBase}/company/settings#compliance`.

### 2.4 Tokens con expiración
- **[Código verificado]** Los tokens los emite el servicio dueño (Auth, Signature, Tasks, CloudStorage, PaymentClient, Calendar); Notification solo los inserta. Para cuenta (invitación, reset, cambio de correo) llegan cifrados: `src/Services/Notification/TaxVision.Notification.Application/Consumers/AuthAccountTokenReader.cs` (`ProtectedToken` con schema + binding de tenant/usuario; `RawToken` solo como respaldo).
- La expiración viaja tipada a la plantilla: `["expires_at"] = ScribeDates.Instant(evt.ExpiresAtUtc)` (`AuthEventConsumers.cs:73`, `:140`, `:328`).
- Correos con token se marcan `Confidential = true` → van directo a la cola de Postmaster, no al fanout (`EventBasedEmailDispatchGateway.cs:202-203`).

## 3. Problemas

1. **[Código verificado] Dos modelos de "portal" conviven.** `PlatformSettingsSnapshot.ClientHost = client.{dominio}` existe y `PortalOptions.ClientBaseUrl` lo usa como fallback, mientras el link real va a `{slug}.{dominio}/portal`. Un fallback a `client.{dominio}` pierde la oficina.
2. **[Código verificado] Fallback silencioso a base fija.** Si Auth no responde, el link va a `PortalOptions.BaseUrl` (en compose, `PORTAL_BASE_URL`, que por defecto es `http://localhost:4200`). El correo sale con un link sin oficina; solo queda un `LogInformation`/`LogWarning`.
3. **[Código verificado] Datos personales en la URL.** `PublicShareDownloadLink`/`PublicSharePageLink` ponen `?email=` en la query (va a logs de proxy y Referer). Los tokens de cuenta (`?token=`) también van en query; RSVP sí usa fragmento.
4. **[Código verificado] Calendar cachea el host 30 min sin invalidación** por rename.
5. **[Código verificado] Lógica de link repartida.** Notification (`TenantEmailLinks`), PaymentClient (`PayablePublicUrls`), Auth (consumers de suscripción con `TenantHost` directo) y Calendar. No hay una pieza BB.
6. **[Código verificado] Cobertura de pruebas de `TenantEmailLinks`** limitada a firma y share (`TenantEmailLinksTests.cs`); `ClientBase` se cubre indirectamente en `AuthAccountEmailsTests`.

## 4. Buenas prácticas observadas

- El host sale del dominio **vigente** (`TenantHost(slug)`), no de un FQDN guardado: un cambio de dominio no deja links viejos.
- Resolver que nunca lanza: un link degradado es mejor que un consumer en DLQ.
- Invalidación por evento (`TenantSubdomainChanged`, `PlatformSettingsChanged`).
- Token en fragmento para acciones de un clic (RSVP) y confirmación con un clic (evita que el escáner de correo responda).
- Correos con secretos fuera del fanout (`Confidential`).
- El tenant de un link público lo da el token, y el host solo se valida contra él (Signature).

## 5. Recomendación

- **[Recomendación]** Un servicio nuevo no arma links de correo: publica el evento con el `TenantId` (y, si lo tiene, el slug) y el **consumer en Notification** compone la URL con `ITenantHostResolver` + `TenantEmailLinks`.
- **[Recomendación]** Si el servicio debe devolver una URL pública en su propia API (como PaymentClient), usar `IPlatformSettings.Current.TenantHost(slug)`; el host del request solo para el caso "no hay entidad de la que sacar el tenant".
- **[Recomendación]** Destino por actor: `CustomerPortal` → `ClientBase`; staff → `StaffBase`. Añadir helpers nuevos a `TenantEmailLinks` en vez de concatenar.
- **[Recomendación]** Tokens con expiración emitidos por el servicio dueño, cifrados/ligados al tenant y al destinatario; preferir fragmento (`#token=`) cuando la página lo pueda leer en el cliente; nunca correo en query.
- **[Decisión pendiente]** Qué hacer si no hay host: ¿enviar con base fija (hoy) o reintentar/fallar? Y si `client.{dominio}` sigue siendo un destino válido.

## 6. Riesgos

- Link a localhost en producción si falta `PORTAL_BASE_URL` y Auth falla.
- Links con host viejo tras un rename en servicios sin invalidación.
- Fuga de correo del destinatario por `?email=`.
- Custom hostname de la oficina nunca aparece en correos (decisión del código, no un bug).

## 7. Pruebas

### Existentes
| Archivo | Casos |
|---|---|
| `deploy/tests/TaxVision.Notification.Tests/TenantEmailLinksTests.cs` | `SigningLink_uses_office_subdomain_when_host_resolves`, `SigningLink_falls_back_to_fixed_base_when_host_is_null`, `PublicShareDownloadLink_uses_office_subdomain_and_escapes_email` |
| `deploy/tests/TaxVision.Notification.Tests/AuthAccountEmailsTests.cs` | `A_portal_reset_links_to_the_portal_and_names_the_client_account_and_office` (cliente → `https://{host}/portal/client/auth/...`), `A_workspace_reset_keeps_linking_to_the_office_workspace` (empleado → `https://{host}/reset-password`), `Protected_token_bound_to_another_user_is_not_sent`, `Password_reset_email_is_sent_confidential` |
| `deploy/tests/TaxVision.PaymentClient.Tests/Api/PayablePublicUrlsTests.cs` | `Stable_invoice_url_uses_the_tenant_subdomain_host`, `The_tenant_host_follows_the_current_platform_domain`, `A_host_that_is_not_an_office_gives_null`, `A_revoked_link_keeps_the_client_on_the_office_host` |
| `deploy/tests/TaxVision.Notification.Tests/OfficeWispReviewDueConsumerTests.cs` | URL de staff por oficina |
| `deploy/tests/TaxVision.Calendar.Tests/Application/AttendeeRsvpTests.cs` | Usa el resolver de host de Calendar |
| `deploy/tests/TaxVision.BuildingBlocks.Tests/Platform/OAuthReturnRedirectPolicyTests.cs` | `Resolve_HostileOrForeignHost_FallsBackToBase` (anti open-redirect) |

### Las que debe tener un servicio nuevo (o su consumer en Notification)
- Oficina A → URL con host A; oficina B → URL con host B (dos tenants en la misma prueba, `FakeTenantHostResolver`).
- Cliente (`CustomerPortal`) → `https://{host}/portal/...`; empleado → `https://{host}/...`.
- Sin host resuelto → base fija configurada (y log), nunca excepción.
- Host del request manipulado (no de oficina o de otra oficina) → no cambia el host del link.
- Token expirado/revocado → la página del servicio dueño responde su error, el link se queda en el host de la oficina.
- Tras `TenantSubdomainChanged`, el siguiente link usa el host nuevo.

## 8. Dependencias

Auth (`primary-host` M2M, `ServiceOnly`), Tenant (`PlatformSettings`), token M2M (`IServiceTokenAcquirer`), RabbitMQ (eventos de invalidación), config `Portal__*`.

## 9. Material para la guía

1. En el consumer (Notification):
   ```csharp
   var host = await hosts.ResolveHostAsync(evt.TenantId, ct);           // null si no resolvió
   var staffUrl  = $"{TenantEmailLinks.StaffBase(host, portal.Value)}/company/settings#compliance";
   var clientUrl = TenantEmailLinks.ClientDocuments(host, portal.Value); // {host}/portal/client/documents
   ```
2. Destino por actor (patrón real de `PasswordResetRequestedConsumer`):
   ```csharp
   var link = AuthAccountCopy.IsPortal(evt.ActorType)
       ? $"{TenantEmailLinks.ClientBase(tenantHost, portal.Value)}/client/auth/reset-password/new?token={token}"
       : $"{TenantEmailLinks.StaffBase(tenantHost, portal.Value)}/reset-password?token={token}";
   ```
3. En una API que devuelve URL pública: `platform.Current.TenantHost(subDomain.Trim().ToLowerInvariant())` (patrón `PayablePublicUrls`).
4. Correos con token: `render.ToDispatchRequest(...) with { Confidential = true }`.
5. Expiración en la plantilla: `ScribeDates.Instant(expiresAtUtc)`, nunca texto con "UTC".
