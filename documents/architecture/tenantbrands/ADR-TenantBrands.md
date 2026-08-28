# ADR — TenantBrands: identidad visual por tenant y superficie

**Estado:** Aceptado (implementado, Fases 1–10).
**Contexto:** El sistema tenía 5 mecanismos de branding desconectados (logo + 4 colores planos en `Tenants`,
logo de email en Scribe, colores hardcodeados en cada front, etc.). TenantBrands los unifica en un solo
modelo owned por el servicio Tenant.

## Decisión 1 — Referenciar assets por `FileId`, no por URL

Un asset (logo/favicon) se guarda como el **`FileId`** de CloudStorage, no como una URL.

- Una URL **presignada** caduca en minutos; guardarla deja referencias muertas.
- Una URL "estable" exigiría servir los bytes desde un dominio público propio y gestionar su ciclo de vida.
- El `FileId` es **content-addressed**: cambia cuando cambia el archivo. La ruta pública
  `/tenants/branding/assets/{fileId}` es su propio cache-busting, y el backend valida `Status=Confirmed`
  antes de servir. El front construye la URL desde el `FileId` que le devuelve el API.

**Consecuencia / trampa aprendida:** el endpoint hace **302 a una presigned de vida corta**. Marcar ese
redirect como `Cache-Control: immutable` (1 año) fue un bug real: el navegador reusa la firma caducada →
403 → imagen rota. Lo cacheable content-addressed son los BYTES, no el redirect. Se acota el cache del
redirect a la vida real de la presigned (`BrandingAssetCachePolicy`), consistente con cómo el resto del
sistema sirve archivos.

## Decisión 2 — Tokens por SUPERFICIE (no un editor de CSS libre)

La marca se modela por `BrandSurface` (`Crm`, `Portal`; el enum deja lugar a `Mobile`/`Email`), y dentro de
cada superficie hay **tokens fijos**: 2 colores (`Primary`, `Accent`) + 2 assets (`Logo`, `Favicon`).

- **Por superficie** porque lo que ve el staff (CRM) y lo que ven los clientes (Portal) son audiencias
  distintas y una oficina puede querer identidades distintas. El TenantAdmin las configura por separado
  (toggle en `company-settings`); si una superficie no está configurada, hereda por cascada (ver Decisión 5).
- **Tokens fijos, no CSS libre**, para que el resultado sea siempre legible y mapee 1:1 a las variables CSS
  que ya consumen los frontends (rampa generada desde un hex por color). Un editor libre rompería el
  contraste y multiplicaría la superficie de prueba.

## Decisión 3 — Sin `Background`/`Text` en v1

Se excluyen fondo y texto a propósito: cambiarlos arriesga UI ilegible (contraste) y tocaría ~1.400 clases
de gris repartidas por las apps. El alcance v1 es primary + accent + logo + favicon, que cubre la identidad
sin ese riesgo. Queda como posible fase futura con tooling de contraste.

## Decisión 4 — El logo de EMAIL sale de la marca `Crm`

Los correos siguen embebiendo el logo vía Scribe (`LogoResolver`/`TenantLogoRef`), alimentado por
`TenantLogoUpdatedIntegrationEvent`/`...Removed`. Ese contrato de eventos **no cambió**: el consumer de
escaneo publica el evento solo para `Crm`+`Logo`. Así el correo reutiliza el logo canónico de la oficina
sin una superficie `Email` separada (que sería redundante para v1) y **Scribe no se tocó** — mínima
superficie de cambio en un camino sensible (envío de correo).

## Decisión 5 — Cascada de defaults de 3 niveles

`token del tenant → marca del sistema (tenant de plataforma, misma superficie) → constante compilada`.
El tenant de plataforma actúa como default global editable **solo por PlatformAdmin**
(`platform.branding.manage`, PlatformOnly, no asignable por tenant). Los endpoints anónimos de pre-login
devuelven la marca del sistema con **200** ante un slug desconocido (anti-enumeración, nunca 404).

## Alternativas descartadas

- **Guardar el branding en Auth / proyectarlo a cada servicio:** rompe el ownership (el branding es un
  atributo del tenant, vive en Tenant). Se retiró el campo muerto `TenantResolutionResponse.LogoUrl`.
- **Proxear los bytes del asset desde el endpoint (same-origin):** técnicamente válido y con cache immutable
  correcto, pero introduce un HttpClient a storage y se aparta del patrón redirect→presigned que usa todo el
  sistema. Se prefirió consistencia; el cache-buster del front resuelve el envenenamiento heredado.
- **Fondo/texto configurables:** ver Decisión 3.
