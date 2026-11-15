# Cloudflare, Tunnel, Caddy y DNS

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd` salvo indicación. Etiquetas: **[Código verificado]**, **[Doc. histórica]**, **[Recomendación]**, **[Decisión pendiente]**. No se tocó Cloudflare. No se copian IPs, tokens ni IDs de zona.

## 1. Estado actual

- La configuración de Cloudflare (Tunnel, DNS, WAF) **no vive en el repo**: no hay archivo `cloudflared`/`config.yml` del túnel en el backend. La gestiona a mano el senior en el panel. **[Código verificado]** (búsqueda sin resultados) + **[Doc. histórica]**
- Lo que sí está en el repo es el **origen**: Caddy (`deploy/docker/caddy/Caddyfile`, imagen propia con plugin DNS de Cloudflare) y el gateway. **[Código verificado]**
- Cadena documentada: Cliente → Cloudflare (edge) → Cloudflare Tunnel (cloudflared) → Caddy → Gateway YARP → servicio. **[Doc. histórica]** `memory/project_real_client_ip_forwarding_cloudflare_docker_fix.md`, `Implementaciones/DEVS/Guia_Creacion_Microservicio.md:36-43`; y comentario del Caddyfile (15-18).
- Hosts derivados de dos variables: `TAXVISION_BASE_DOMAIN` (apex, default `taxoffix.io`) y `TAXVISION_DOMAIN` (API, default `api.taxoffix.io`). **[Código verificado]** `deploy/docker/docker-compose.yml:4-7`, `PlatformDomainSingleSourceDeployTests.cs:6-12`

## 2. Evidencia

### 2.1 Caddyfile **[Código verificado]** `deploy/docker/caddy/Caddyfile`

| Líneas | Bloque |
|---|---|
| 15-18 | "Cloudflare Tunnel entrega la aplicación publicada a `http://localhost:80`"; el host HTTP de la API se declara explícito para no caer en la redirección HTTP→HTTPS automática de Caddy |
| 48-57 | Snippet `(taxvision_api)`: `/taxvision-storage/*`, `/taxvision-temp/*`, `/taxvision-quarantine/*` → `minio:9000`; todo lo demás → `gateway:8080` |
| 63-111 | Snippet `(taxvision_tenant)` para subdominios de oficina: MinIO primero; `/storage/public/*` y `/payments-client/invoices/*` → gateway **antes** de la SPA; estáticos (`*.js *.css ... /assets/*`) y navegación (`Accept: text/html`) → `https://app.<dominio>` con `header_up Host app.<dominio>`; resto → gateway |
| 116-121 | Orígenes HTTP para el túnel: `http://{$TAXVISION_DOMAIN}` (solo API) y `http://*.{$TAXVISION_BASE_DOMAIN}` (tenant) |
| 125-138 | Entrada TLS directa: apex + API (solo API) y `*.dominio` (tenant), con `tls { dns cloudflare {env.CLOUDFLARE_API_TOKEN} }` |

- Imagen: `deploy/docker/caddy/Dockerfile:14-20` (`xcaddy build --with github.com/caddy-dns/cloudflare`), necesaria para el certificado wildcard por DNS-01.
- Servicio `caddy` en compose: `ports 80:80, 443:443`, `CLOUDFLARE_API_TOKEN: ${CLOUDFLARE_DNS_API_TOKEN:?...}` (token Zone:DNS:Edit **distinto** del que usa Auth para custom hostnames). `deploy/docker/docker-compose.yml:3029-3062`.
- El CRM está atado a `127.0.0.1` en la VM del frontend y no es alcanzable por red interna: por eso Caddy lo pide por `https://app.<dominio>` a través de Cloudflare (`Caddyfile:38-43`).

### 2.2 IP real del cliente

- `CF-Connecting-IP` lo pone Cloudflare en el edge. `AddTaxVisionClientIpForwarding` / `UseTaxVisionClientIp` lo leen en cada host y confían por defecto en redes privadas. El gateway consume el header y lo re-inyecta hacia el backend (`src/Gateway/TaxVision.Gateway/Program.cs:97-112`). **[Código verificado]**
- Guardarraíl: `deploy/tests/TaxVision.BuildingBlocks.Tests/Architecture/ClientIpForwardingFitnessTests.cs` (todo `Program.cs` con `WebApplication.CreateBuilder` registra ambas llamadas). **[Código verificado]** (existencia) + **[Doc. histórica]** memoria citada.

### 2.3 Procedimientos manuales documentados (del senior/dueño)

- **Hostname nuevo de una app** (ej. portal cliente): "el senior agrega el hostname `client.<dominio>` al tunnel de Cloudflare → `http://localhost:8082`". **[Doc. histórica]** `Implementaciones/documents/Portal_Cliente_Guia_Frontend.md:144,342`
- **Registro DNS no-HTTP** (TURN): registro `A` en **DNS only** (nube gris), porque el proxy de Cloudflare solo pasa HTTP/HTTPS; verificación con `dig +short <host>` que debe devolver la IP del servidor y no IPs de Cloudflare. Token de API con `Zone:Zone:Read` **y** `Zone:DNS:Edit` para DNS-01. **[Doc. histórica]** `Implementaciones/GITHUBSCT/COTURN_turn.taxoffix.io_PASO_A_PASO.txt` (PARTE 1, 1.1 y 1.2)
- **Turnstile**: widget creado en Cloudflare → Turnstile con los hostnames del landing; site key y secret como secrets de GitHub. **[Doc. histórica]** `Implementaciones/CommunicationCorrecciones/Proyectos/PublicDemoScheduling/Evidencias/E-14_*.md:43`
- **Wildcard DNS** `*.<dominio>` proxied para subdominios de oficina (sin tocar DNS por oficina) y Cloudflare for SaaS para dominios propios futuros. **[Doc. histórica]** `Implementaciones/Auth_y_CloudStorage_Plan_Completitud_v2.md:29,219-221`
- **SQL Server** en otra VM, "conexión por túnel de Cloudflare". **[Doc. histórica]** `CommunicationCorrecciones/Evidencias/Registro_Evidencias.md` E-196.

## 3. Problemas

1. **Contradicción sobre dónde corre `cloudflared`**: la guía DEVS dice "cloudflared TUNNEL (vm-frontend, remotely-managed)" (`DEVS/Guia_Creacion_Microservicio.md:39`), pero el Caddyfile dice que el túnel entrega a `http://localhost:80` (puerto de Caddy en el backend) (15). Ambas cosas solo cuadran si hay un conector en la VM del backend o si "localhost" se refiere a otra máquina. **[Decisión pendiente]**: confirmar con el senior.
2. **Sin evidencia escrita del procedimiento de solicitud** al senior (canal, plantilla, quién verifica). Solo hay frases sueltas ("el senior agrega..."). **[Decisión pendiente]**
3. **Sin evidencia de reglas WAF, caché o Page Rules** de Cloudflare sobre `api.*`. Si existieran, un path nuevo podría verse afectado (ej. caché de un GET público). **[Decisión pendiente]**
4. La documentación vieja usa dominios anteriores (`taxprocore.com`, `taxproffice.com`); el código ya deriva todo de variables y usa `taxoffix.io` como default. **[Doc. histórica]** vs **[Código verificado]**

## 4. Buenas prácticas ya vigentes

- Cero hosts literales en Caddy (probado por `PlatformDomainSingleSourceDeployTests.Caddyfile_has_no_literal_hosts_outside_the_domain_variables`).
- Wildcard por DNS-01: dar de alta una oficina no requiere tocar DNS ni Caddy.
- Token de DNS de Caddy separado del de Auth y con permisos mínimos.
- Rutas públicas abiertas desde el navegador probadas por `CaddyPublicNavigationRouteTests` (orden antes de `@spa_nav`).

## 5. Recomendación — cuándo hace falta el senior

Basado en la cadena verificada (el túnel publica **hosts completos** hacia Caddy, y Caddy manda todo lo que no es bucket ni SPA al gateway):

| Cambio del servicio nuevo | ¿Cambio en Cloudflare? | ¿Cambio en repo? |
|---|---|---|
| Path nuevo bajo `api.<dominio>` o un subdominio de oficina (`/<prefijo>/...`) consumido por XHR | **No** (**[Recomendación]** inferida del Caddyfile; confirmar que no hay reglas WAF/caché por path) | Ruta YARP + compose (ver `Gateway_YARP.md`) |
| Endpoint público que el usuario **abre en el navegador** en un host de oficina (link de correo, QR) | No | **Sí**: regla en `(taxvision_tenant)` antes de `@spa_nav` y caso en `CaddyPublicNavigationRouteTests` |
| Prefijo que choque con `/taxvision-storage`, `/taxvision-temp`, `/taxvision-quarantine` | No | Elegir otro prefijo |
| Hostname nuevo (otra app, otro subdominio fijo, TURN, webhook dedicado) | **Sí**: el senior agrega el public hostname al túnel o el registro DNS | Posible bloque en Caddy y CORS |
| Puerto no HTTP expuesto a internet (UDP, TURN, SFU) | **Sí**: DNS only (nube gris) y reenvío en router | `ports:` justificado + excepción en `DockerComposeNoPublicApiPortsFitnessTests` |
| Turnstile u otro producto de Cloudflare | **Sí** (widget/claves) | Secrets en GitHub |

- **[Decisión pendiente]** Plantilla de solicitud al senior: hostname, servicio destino (`http://localhost:<puerto>`), proxied o DNS only, fecha, y cómo se verifica (`dig`, `curl -I https://<host>/health/live`).

## 6. Riesgos

- Un endpoint público abierto desde el navegador sin regla en Caddy cae en la SPA y muestra 404 de Angular; el servicio no ve la petición y sus logs salen limpios (pasó dos veces: `CaddyPublicNavigationRouteTests.cs:6-18`). **[Código verificado]**
- Un registro proxied (nube naranja) para un protocolo no HTTP: el cliente nunca llega al servidor, sin error visible. **[Doc. histórica]**
- Quitar el transform de `CF-Connecting-IP` del gateway vuelve a guardar la IP de Docker en auditoría, sesiones, certificados de firma y rate limiter. **[Doc. histórica]**
- Buffering de Cloudflare rompe socket.io en long-polling: por eso `UseWebSockets()` es obligatorio en el gateway (`Program.cs:186-190`). **[Código verificado]**

## 7. Pruebas

- `CaddyPublicNavigationRouteTests`, `PlatformDomainSingleSourceDeployTests`, `ClientIpForwardingFitnessTests`, `GatewayWebSocketPipelineTests`. Todas en el gate local. **[Código verificado]**
- No hay prueba automatizada de la configuración de Cloudflare (está fuera del repo).

## 8. Dependencias

- Caddy ↔ Gateway ↔ Auth (resolución de tenant por Host).
- Secrets `TAXVISION_BASE_DOMAIN`, `TAXVISION_DOMAIN`, `CLOUDFLARE_DNS_API_TOKEN` (ver `CI_CD_Secrets.md`).
- VM del frontend (CRM en `app.*`) para el split SPA+API.

## 9. Material para la guía

### 9.1 Regla de Caddy para un endpoint público abierto desde el navegador (patrón real)

```caddy
	@invoice_link path /payments-client/invoices/*
	handle @invoice_link {
		reverse_proxy gateway:8080
	}
```
Debe ir dentro de `(taxvision_tenant)`, **después** de `@minio` y **antes** de `@spa_assets` y `@spa_nav` (`Caddyfile:76-92`). Agregar el prefijo a los dos `[InlineData]` de `CaddyPublicNavigationRouteTests` (22-23 y 51-52).

### 9.2 Verificación tras un cambio de Cloudflare (pasos documentados)

1. `dig +short <host>`: proxied → IPs de Cloudflare; DNS only → la IP del servidor. **[Doc. histórica]**
2. `curl -I https://<host>/health/live` debe responder 200 del gateway. **[Recomendación]**
3. Para la API, el workflow ya consulta `https://${TAXVISION_DOMAIN}/health/ready` al final del deploy (`.github/workflows/deploy.yml:786-819`). **[Código verificado]**

### 9.3 Qué NO hacer

- No poner dominios literales en Caddy ni en el compose (rompe `PlatformDomainSingleSourceDeployTests`).
- No reutilizar el token de DNS de Caddy para otros usos.
- No publicar un servicio directo a internet para "probar": todo entra por Caddy → Gateway.
