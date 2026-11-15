# Checklist — Enlaces por oficina y resolución de tenant por host

> Propósito: que todo enlace que sale de la plataforma (correo, push, API pública) apunte al host de la oficina correcta, al destino correcto según el actor, y que ningún servicio confíe en el host como fuente de tenant.
> Ningún consumer arma links con el host del request: el host sale de Auth (`primary-host`), del slug del evento o de `PlatformSettings`.

**Cuándo aplica:** el servicio publica eventos que terminan en un aviso con link, devuelve una URL pública en su API, expone una ruta pública con token bajo el host de una oficina, o necesita saber si un host es de oficina.

**Formato:** *Verificación* = comando, prueba o archivo; *Origen* = investigación. Condicionales entre corchetes.

---

## 1. Tenant: nunca por host

- [ ] **El servicio no resuelve tenant por host**: `tenant_id` del JWT (`JwtTenantContextMiddleware`) y, en consumers, el `TenantId` del evento (`IntegrationEventTenantMiddleware`).
  - Verificación: pruebas "JWT de la oficina A con datos de B → 0 filas / 404"; "consumer con `TenantId = Guid.Empty` → falla cerrado".
  - Origen: [Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §5 y §7.
- [ ] **Nunca leer `X-Forwarded-Host`, `X-Tenant-Id` ni `X-Resolved-Tenant` sin pasar por el Gateway**; nunca aceptar `TenantId` del body ni caer a un tenant por defecto.
  - Verificación: prueba "header enviado por el cliente → ignorado"; revisión.
  - Origen: [Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §5 y §9.5.
- [ ] **"¿Es host de oficina?"** con `IPlatformSettings.Current.TryGetTenantSlug(host, out var slug)`; host de oficina con `TenantHost(slug)`. No parsear sufijos a mano.
  - Verificación: revisión; patrón `PayablePublicUrlsTests.A_host_that_is_not_an_office_gives_null`.
  - Origen: [Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §5 y §9.3.
- [ ] [si el servicio necesita dominio o marca] `AddPlatformSettings(builder.Configuration)` + `options.ListenForPlatformSettings()`; en compose el ancla `x-platform-settings-env` (`PlatformSettings__TenantBaseUrl`).
  - Verificación: `Program.cs` y bloque del compose; regenerar codegen (cambia el código generado).
  - Origen: [Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §9.1; [Roslyn_Produccion](../Investigaciones/Roslyn_Produccion.md) §10 "Cuándo regenerar".
- [ ] `EnforceHostResolution=false` solo en local; no copiarlo a otros ambientes.
  - Verificación: compose de producción.
  - Origen: [Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §6.

## 2. Enlaces en correos y push (consumer en Notification)

- [ ] **Host con `ITenantHostResolver.ResolveHostAsync(evt.TenantId, ct)`** (nunca lanza; `null` si no resolvió) o, si el evento trae el slug, `IPlatformSettings.Current.TenantHost(slug)`.
  - Verificación: prueba "oficina A → host A; oficina B → host B" en la misma prueba con `FakeTenantHostResolver`.
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §1, §7 y §9.1.
- [ ] **Destino por actor**: `CustomerPortal` → `TenantEmailLinks.ClientBase` (`https://{host}/portal/...`); staff → `TenantEmailLinks.StaffBase` (`https://{host}/...`).
  - Verificación: prueba "cliente → `/portal/...`; empleado → raíz" (patrón `AuthAccountEmailsTests`).
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §2.3, §5 y §9.2.
- [ ] **Helpers nuevos en `TenantEmailLinks`** en vez de concatenar URLs en el consumer.
  - Verificación: revisión del consumer; prueba del helper en `TenantEmailLinksTests`.
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §5.
- [ ] **Sin host resuelto → base fija configurada y log**, nunca excepción.
  - Verificación: patrón `SigningLink_falls_back_to_fixed_base_when_host_is_null`.
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §7.
- [ ] **`PORTAL_BASE_URL` / `CLIENT_PORTAL_BASE_URL` configurados en producción** (el default de compose de `PORTAL_BASE_URL` es `http://localhost:4200`).
  - Verificación: compose `deploy/docker/docker-compose.yml:940-945` y `.env` de `deploy.yml`.
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §3.2 y §6.
- [ ] [si el servicio cachea el host] Invalidar ante `TenantSubdomainChangedIntegrationEvent` (y `PlatformSettingsChanged`).
  - Verificación: prueba "tras `TenantSubdomainChanged`, el siguiente link usa el host nuevo".
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §4 y §7.

## 3. URL pública devuelta por la API del servicio

- [ ] **`platform.Current.TenantHost(subDomain.Trim().ToLowerInvariant())`** (patrón `PayablePublicUrls`); el host del request solo cuando no hay entidad de la que sacar el tenant.
  - Verificación: patrón `PayablePublicUrlsTests` (`Stable_invoice_url_uses_the_tenant_subdomain_host`, `The_tenant_host_follows_the_current_platform_domain`).
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §5 y §9.3.
- [ ] **Host del request manipulado** (no de oficina o de otra oficina) no cambia el host del link.
  - Verificación: prueba del servicio.
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §7.

## 4. Tokens en enlaces

- [ ] **Emitidos por el servicio dueño**, cifrados/ligados al tenant y al destinatario; Notification solo los inserta.
  - Verificación: patrón `Protected_token_bound_to_another_user_is_not_sent`.
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §2.4 y §5.
- [ ] **Fragmento `#token=` cuando la página puede leerlo en el cliente** (patrón RSVP); **nunca el correo en la query** (`?email=`).
  - Verificación: revisión de la forma de la URL.
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §3.3 y §5.
- [ ] **Correo con token `Confidential = true`** y expiración tipada (`ScribeDates.Instant`).
  - Verificación: patrón `Password_reset_email_is_sent_confidential`.
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §9.4-9.5.
- [ ] **Token expirado o revocado** → la página del servicio dueño responde su error y el link se queda en el host de la oficina.
  - Verificación: patrón `A_revoked_link_keeps_the_client_on_the_office_host`.
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §7.

## 5. Ruta pública con token bajo el host de una oficina

- [ ] [si existe] **Validar Host↔token con `X-Resolved-Tenant`** (patrón `PublicSignatureHostGuardMiddleware`); el tenant de escritura sale del token.
  - Verificación: pruebas "host A + token B → 403", "sin header → valida solo el token", "token inválido → su propio error, no 403".
  - Origen: [Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §5, §7 y §9.4.
- [ ] [si el usuario la abre en el navegador] **Regla en Caddy** antes de `@spa_nav` y caso en `CaddyPublicNavigationRouteTests` (si no, cae en la SPA con 404 de Angular). Ver [YARP_Cloudflare](YARP_Cloudflare.md).
  - Origen: [Cloudflare](../Investigaciones/Cloudflare.md) §5 y §9.1.

## Decisiones pendientes

- Qué hacer si no hay host (base fija hoy vs reintentar/fallar) y si `client.{dominio}` sigue siendo destino válido ([Tenant_Links](../Investigaciones/Tenant_Links.md) §5).
- Cerrar el fail-open del guard del Gateway para rutas sensibles o pieza BB para validación Host↔token ([Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §5).
- Soporte de custom hostnames en Gateway/CORS ([Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §5).
